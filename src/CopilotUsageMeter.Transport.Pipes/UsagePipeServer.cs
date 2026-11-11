using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Transport.Pipes
{
    public sealed class UsagePipeServer : IDisposable
    {
        private sealed class Session
        {
            public readonly PipeConnection Connection;
            public readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
            public bool Subscribed;
            public long Sequence;
            public long Revision;
            public string? ActiveSessionId;
            public string? RepositoryKey;
            public long ContextVersion;
            public Session(PipeConnection connection) => Connection = connection;
        }

        private readonly Func<NamedPipeServerStream> createPipe;
        private readonly Func<UsageSnapshot> getSnapshot;
        private readonly Func<ProtocolMessage, CancellationToken, Task<ProtocolMessage>> handleCommand;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly ConcurrentDictionary<int, Session> sessions = new ConcurrentDictionary<int, Session>();
        private readonly ConcurrentDictionary<int, Task> workers = new ConcurrentDictionary<int, Task>();
        private readonly SemaphoreSlim slots = new SemaphoreSlim(16, 16);
        private Task? listener;
        private int nextSession;
        private readonly Func<UsageSnapshot, string?, UsageSnapshot>? projectSnapshot;
        private readonly Func<UsageSnapshot, string?, string?, UsageSnapshot>? projectContext;

        public event Action<string>? Faulted;
        // Raised with the earlier fault message once the listener accepts a connection again.
        public event Action<string>? Recovered;
        public event Action? ContextsChanged;
        public const int ListenerFaultNotifyAfter = 3;
        public const string ListenerFaultMessage = "Pipe listener failed; retrying automatically. Restart the tray if this persists.";
        private readonly Func<int, TimeSpan> listenerRetryDelay;

        public UsagePipeServer(Func<NamedPipeServerStream> createPipe, Func<UsageSnapshot> getSnapshot,
            Func<ProtocolMessage, CancellationToken, Task<ProtocolMessage>> handleCommand,
            Func<UsageSnapshot, string?, UsageSnapshot>? projectSnapshot = null,
            Func<UsageSnapshot, string?, string?, UsageSnapshot>? projectContext = null,
            Func<int, TimeSpan>? listenerRetryDelay = null)
        {
            this.createPipe = createPipe;
            this.getSnapshot = getSnapshot;
            this.handleCommand = handleCommand;
            this.projectSnapshot = projectSnapshot;
            this.projectContext = projectContext;
            this.listenerRetryDelay = listenerRetryDelay ?? DefaultListenerRetryDelay;
        }

        public string[] GetActiveSessionIds() => sessions.Values
            .Select(session => session.ActiveSessionId).Where(id => id != null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();

        public void Start()
        {
            if (listener != null) throw new InvalidOperationException("Pipe server has already started.");
            listener = ListenAsync();
        }

        private async Task ListenAsync()
        {
            var failures = 0;
            while (!lifetime.IsCancellationRequested)
            {
                var slotHeld = false;
                try
                {
                    await slots.WaitAsync(lifetime.Token).ConfigureAwait(false);
                    slotHeld = true;
                    var pipe = createPipe();
                    try
                    {
                        using (lifetime.Token.Register(() => pipe.Dispose()))
                            await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                    }
                    catch { pipe.Dispose(); throw; }
                    if (failures >= ListenerFaultNotifyAfter) Recovered?.Invoke(ListenerFaultMessage);
                    failures = 0;
                    slotHeld = false;
                    int id = Interlocked.Increment(ref nextSession);
                    var task = ServeAsync(id, pipe);
                    workers[id] = task;
                    _ = task.ContinueWith(completed =>
                    {
                        workers.TryRemove(id, out _);
                        slots.Release();
                        if (completed.IsFaulted) Faulted?.Invoke("Pipe session failed unexpectedly.");
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (ObjectDisposedException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    if (slotHeld) slots.Release();
                    failures++;
                    // The listener recreates the pipe on its own; the user is told only if it keeps failing.
                    if (failures == ListenerFaultNotifyAfter) Faulted?.Invoke(ListenerFaultMessage);
                    try { await Task.Delay(listenerRetryDelay(failures), lifetime.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        // 1 s, 2 s, 4 s, 8 s, 16 s, then every 30 s.
        public static TimeSpan DefaultListenerRetryDelay(int failures) =>
            TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Max(0, Math.Min(failures, 10) - 1))));

        private async Task ServeAsync(int id, NamedPipeServerStream pipe)
        {
            using (var connection = new PipeConnection(pipe))
            using (lifetime.Token.Register(() => pipe.Dispose()))
            {
                var session = new Session(connection);
                sessions[id] = session;
                try
                {
                    ProtocolMessage? hello;
                    using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        handshake.CancelAfter(TimeSpan.FromSeconds(10));
                        using (handshake.Token.Register(() => pipe.Dispose()))
                            hello = await connection.ReadAsync(handshake.Token).ConfigureAwait(false);
                    }
                    if (hello == null) return;
                    if (hello.Protocol != UsageProtocol.Version)
                    {
                        await connection.WriteAsync(Error(hello, "version_mismatch", "Update the tray and extension to matching protocol versions."), lifetime.Token).ConfigureAwait(false);
                        return;
                    }
                    if (hello.Type != "Hello")
                    {
                        await connection.WriteAsync(Error(hello, "handshake_required", "Send Hello first."), lifetime.Token).ConfigureAwait(false);
                        return;
                    }
                    await connection.WriteAsync(new ProtocolMessage { Type = "HelloAck", RequestId = hello.RequestId }, lifetime.Token).ConfigureAwait(false);
                    while (!lifetime.IsCancellationRequested)
                    {
                        var request = await connection.ReadAsync(lifetime.Token).ConfigureAwait(false);
                        if (request == null || request.Type == "Bye") return;
                        if (request.Protocol != UsageProtocol.Version)
                        {
                            await connection.WriteAsync(Error(request, "version_mismatch", "Update the tray and extension to matching protocol versions."), lifetime.Token).ConfigureAwait(false);
                            return;
                        }
                        if (request.Type == "Subscribe" || request.Type == "SetActiveSession")
                        {
                            if (request.ContextVersion < 0 || (request.ActiveSessionId != null && !Guid.TryParseExact(request.ActiveSessionId, "D", out _))
                                || (request.RepositoryKey != null && (request.RepositoryKey.Length > 201
                                    || !Regex.IsMatch(request.RepositoryKey, @"\A[A-Za-z0-9._-]{1,100}/[A-Za-z0-9._-]{1,100}\z"))))
                            {
                                await connection.WriteAsync(Error(request, "invalid_context", "A valid opaque session identifier or null is required."), lifetime.Token).ConfigureAwait(false);
                                continue;
                            }
                            await session.Gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                            try
                            {
                                if (request.ContextVersion < session.ContextVersion)
                                {
                                    await connection.WriteAsync(Error(request, "stale_context", "A newer active-chat context has already been selected."), lifetime.Token).ConfigureAwait(false);
                                    continue;
                                }
                                var canonicalId = request.ActiveSessionId == null ? null : Guid.Parse(request.ActiveSessionId).ToString("D");
                                var changed = !string.Equals(session.ActiveSessionId, canonicalId, StringComparison.Ordinal)
                                    || !string.Equals(session.RepositoryKey, request.RepositoryKey, StringComparison.OrdinalIgnoreCase);
                                session.ActiveSessionId = canonicalId;
                                session.RepositoryKey = request.RepositoryKey;
                                session.ContextVersion = request.ContextVersion;
                                var snapshot = getSnapshot();
                                await SendSnapshotAsync(session, snapshot, "Snapshot", lifetime.Token).ConfigureAwait(false);
                                session.Subscribed = true;
                                if (request.Type == "SetActiveSession")
                                    await connection.WriteAsync(new ProtocolMessage { Type = "Ack", RequestId = request.RequestId }, lifetime.Token).ConfigureAwait(false);
                                if (changed) ContextsChanged?.Invoke();
                            }
                            finally { session.Gate.Release(); }
                        }
                        else if (!session.Subscribed)
                            await connection.WriteAsync(Error(request, "subscription_required", "Subscribe before issuing commands."), lifetime.Token).ConfigureAwait(false);
                        else
                        {
                            var reply = await handleCommand(request, lifetime.Token).ConfigureAwait(false);
                            reply.RequestId = request.RequestId;
                            await connection.WriteAsync(reply, lifetime.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (InvalidDataException)
                {
                    Faulted?.Invoke("Rejected an invalid or oversized pipe frame.");
                }
                catch (IOException) { /* A client closed its connection. */ }
                // A command handler may be cancelled by its owner's token just before this server's own lifetime is cancelled.
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException)
                {
                    if (!lifetime.IsCancellationRequested) Faulted?.Invoke("Pipe session closed or handshake timed out.");
                }
                finally
                {
                    sessions.TryRemove(id, out _);
                    ContextsChanged?.Invoke();
                }
            }
        }

        private async Task SendSnapshotAsync(Session session, UsageSnapshot snapshot, string type, CancellationToken token)
        {
            snapshot = projectContext?.Invoke(snapshot, session.ActiveSessionId, session.RepositoryKey)
                ?? projectSnapshot?.Invoke(snapshot, session.ActiveSessionId) ?? snapshot;
            long nextSequence = session.Sequence + 1;
            await session.Connection.WriteAsync(new ProtocolMessage
            {
                Type = type, Snapshot = snapshot, Sequence = nextSequence, ContextVersion = session.ContextVersion
            }, token).ConfigureAwait(false);
            session.Sequence = nextSequence;
            session.Revision = snapshot.Revision;
        }

        public async Task BroadcastAsync(UsageSnapshot snapshot, CancellationToken cancellationToken)
        {
            await Task.WhenAll(sessions.Values.Select(session => PublishAsync(session, snapshot, cancellationToken))).ConfigureAwait(false);
        }

        private async Task PublishAsync(Session session, UsageSnapshot snapshot, CancellationToken token)
        {
            await session.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (session.Subscribed && snapshot.Revision > session.Revision)
                    await SendSnapshotAsync(session, snapshot, "Update", token).ConfigureAwait(false);
            }
            catch (IOException) { session.Connection.Dispose(); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                session.Connection.Dispose();
                Faulted?.Invoke("Disconnected a slow pipe client.");
            }
            catch (ObjectDisposedException) { /* The disconnected session is being removed. */ }
            finally { session.Gate.Release(); }
        }

        public static ProtocolMessage Error(ProtocolMessage request, string code, string message) =>
            new ProtocolMessage { Type = "Error", RequestId = request.RequestId, Code = code, Message = message };

        public async Task StopAsync()
        {
            lifetime.Cancel();
            foreach (var session in sessions.Values) session.Connection.Dispose();
            if (listener != null) await listener.ConfigureAwait(false);
            try { await Task.WhenAll(workers.Values.ToArray()).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* Workers were stopped by shutdown. */ }
        }

        public void Dispose()
        {
            lifetime.Cancel();
            foreach (var session in sessions.Values) session.Connection.Dispose();
        }
    }
}