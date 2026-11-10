using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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

        public event Action<string>? Faulted;

        public UsagePipeServer(Func<NamedPipeServerStream> createPipe, Func<UsageSnapshot> getSnapshot,
            Func<ProtocolMessage, CancellationToken, Task<ProtocolMessage>> handleCommand)
        {
            this.createPipe = createPipe;
            this.getSnapshot = getSnapshot;
            this.handleCommand = handleCommand;
        }

        public void Start()
        {
            if (listener != null) throw new InvalidOperationException("Pipe server has already started.");
            listener = ListenAsync();
        }

        private async Task ListenAsync()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    await slots.WaitAsync(lifetime.Token).ConfigureAwait(false);
                    var pipe = createPipe();
                    try
                    {
                        using (lifetime.Token.Register(() => pipe.Dispose()))
                            await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                    }
                    catch { pipe.Dispose(); throw; }
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
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (lifetime.IsCancellationRequested) { }
            catch (IOException) when (lifetime.IsCancellationRequested) { }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Faulted?.Invoke("Pipe listener failed. Exit and restart the tray.");
            }
        }

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
                        if (request.Type == "Subscribe")
                        {
                            await session.Gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                            try
                            {
                                var snapshot = getSnapshot();
                                await SendSnapshotAsync(session, snapshot, "Snapshot", lifetime.Token).ConfigureAwait(false);
                                session.Subscribed = true;
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
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (ObjectDisposedException)
                {
                    if (!lifetime.IsCancellationRequested) Faulted?.Invoke("Pipe session closed or handshake timed out.");
                }
                finally { sessions.TryRemove(id, out _); }
            }
        }

        private static async Task SendSnapshotAsync(Session session, UsageSnapshot snapshot, string type, CancellationToken token)
        {
            long nextSequence = session.Sequence + 1;
            await session.Connection.WriteAsync(new ProtocolMessage
            {
                Type = type, Snapshot = snapshot, Sequence = nextSequence
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
            await Task.WhenAll(workers.Values.ToArray()).ConfigureAwait(false);
        }

        public void Dispose()
        {
            lifetime.Cancel();
            foreach (var session in sessions.Values) session.Connection.Dispose();
        }
    }
}