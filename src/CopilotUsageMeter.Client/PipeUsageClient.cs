using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Transport.Pipes;

namespace CopilotUsageMeter.Client
{
    public sealed class PipeUsageClient : IUsageClient
    {
        private readonly string pipeName;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ProtocolMessage>> pending =
            new ConcurrentDictionary<string, TaskCompletionSource<ProtocolMessage>>();
        private PipeConnection? connection;
        private string? lastLoggedFailure;
        private Task? worker;
        private long sequence;
        private string hostInstance = "";
        private string? activeSessionId;
        private string? repositoryKey;
        private long contextVersion;
        private readonly object contextGate = new object();
        private readonly SemaphoreSlim contextDeliveryGate = new SemaphoreSlim(1, 1);

        public event Action<UsageSnapshot>? SnapshotChanged;
        public event Action<string>? StatusChanged;
        public UsageSnapshot? Snapshot { get; private set; }
        public const string VersionMismatchStatus = "Version mismatch: update tray and extension";
        private readonly TimeSpan mismatchRetryDelay;
        private readonly TimeSpan heartbeatTimeout;
        private readonly TimeSpan commandTimeout;
        private readonly TimeSpan refreshTimeout;
        // Refresh may wait on the history CLI (15 s) and GitHub (30 s), so it gets longer than other commands.
        public static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultRefreshTimeout = TimeSpan.FromSeconds(45);
        public string Status { get; private set; } = "Disconnected";

        private sealed class VersionMismatchException : IOException
        {
            public VersionMismatchException() : base("Tray and extension protocol versions differ.") { }
        }

        public PipeUsageClient(string pipeName, TimeSpan? mismatchRetryDelay = null, TimeSpan? heartbeatTimeout = null,
            TimeSpan? commandTimeout = null, TimeSpan? refreshTimeout = null)
        {
            this.pipeName = pipeName;
            this.mismatchRetryDelay = mismatchRetryDelay ?? TimeSpan.FromSeconds(10);
            this.heartbeatTimeout = heartbeatTimeout ?? DefaultHeartbeatTimeout;
            this.commandTimeout = commandTimeout ?? DefaultCommandTimeout;
            this.refreshTimeout = refreshTimeout ?? DefaultRefreshTimeout;
        }

        public void Start()
        {
            if (worker != null) throw new InvalidOperationException("Usage client has already started.");
            worker = RunAsync();
        }

        private void SetStatus(string status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }

        // The reconnect loop repeats a failure every few seconds, so each distinct failure is logged once until a connection succeeds.
        private void LogOnce(string code, Exception exception)
        {
            var key = code + "|" + exception.GetType().FullName;
            if (key == lastLoggedFailure) return;
            lastLoggedFailure = key;
            Diagnostics.Warning("client.pipe", code, exception);
        }

        private async Task RunAsync()
        {
            int retrySeconds = 1;
            while (!lifetime.IsCancellationRequested)
            {
                TimeSpan? delayOverride = null;
                try
                {
                    if (Status != VersionMismatchStatus) SetStatus("Connecting");
                    using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                    using (lifetime.Token.Register(() => pipe.Dispose()))
                    {
                        await pipe.ConnectAsync(2000, lifetime.Token).ConfigureAwait(false);
                        using (var active = new PipeConnection(pipe))
                        {
                            await active.WriteAsync(new ProtocolMessage { Type = "Hello" }, lifetime.Token).ConfigureAwait(false);
                            ProtocolMessage? ack;
                            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                            {
                                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                                using (handshake.Token.Register(() => pipe.Dispose()))
                                    ack = await active.ReadAsync(handshake.Token).ConfigureAwait(false);
                            }
                            if (ack?.Type == "Error" && ack.Code == "version_mismatch")
                                throw new VersionMismatchException();
                            if (ack?.Type == "HelloAck" && ack.Protocol != UsageProtocol.Version)
                                throw new VersionMismatchException();
                            if (ack?.Type != "HelloAck")
                                throw new InvalidDataException("Invalid tray handshake.");
                            sequence = 0;
                            hostInstance = "";
                            await SubscribeAsync(active, promote: true).ConfigureAwait(false);
                            // The watchdog arms only once this tray has sent a heartbeat, so an older tray that sends none is not dropped.
                            var heartbeatSeen = false;
                            while (!lifetime.IsCancellationRequested)
                            {
                                ProtocolMessage? message;
                                if (!heartbeatSeen) message = await active.ReadAsync(lifetime.Token).ConfigureAwait(false);
                                else
                                    using (var watchdog = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                                    {
                                        watchdog.CancelAfter(heartbeatTimeout);
                                        using (watchdog.Token.Register(() => pipe.Dispose()))
                                            message = await active.ReadAsync(watchdog.Token).ConfigureAwait(false);
                                    }
                                if (message == null) throw new EndOfStreamException("Tray disconnected.");
                                if (message.Protocol != UsageProtocol.Version)
                                    throw new VersionMismatchException();
                                if (message.Type == "Heartbeat")
                                {
                                    heartbeatSeen = true;
                                    continue;
                                }
                                if (message.Type == "Snapshot" || message.Type == "Update")
                                {
                                    var snapshot = message.Snapshot;
                                    if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.HostInstanceId)
                                        || snapshot.Revision < 1 || snapshot.Account == null || snapshot.Local == null || snapshot.Settings == null
                                        || snapshot.Settings.DemoRefreshSeconds < 5 || snapshot.Settings.DemoRefreshSeconds > 300)
                                        throw new InvalidDataException("Invalid usage snapshot.");
                                    if (message.Type != "Snapshot" && (message.Sequence != sequence + 1 || snapshot.HostInstanceId != hostInstance))
                                    {
                                        await SubscribeAsync(active, promote: false).ConfigureAwait(false);
                                        SetStatus("Resynchronizing");
                                        continue;
                                    }
                                    if (message.Sequence < 1) throw new InvalidDataException("Invalid delivery sequence.");
                                    sequence = message.Sequence;
                                    hostInstance = snapshot.HostInstanceId;
                                    lock (contextGate)
                                    {
                                        if (message.ContextVersion != contextVersion) continue;
                                        Snapshot = snapshot;
                                        retrySeconds = 1;
                                        lastLoggedFailure = null;
                                        SetStatus(snapshot.IsDemo ? (snapshot.IsPaused ? "Paused - demo" : "Connected - demo") : (snapshot.IsPaused ? "Paused" : "Connected"));
                                        if (message.ContextVersion == contextVersion) SnapshotChanged?.Invoke(snapshot);
                                    }
                                }
                                else if (message.RequestId != null && pending.TryRemove(message.RequestId, out var completion))
                                    completion.TrySetResult(message);
                                // An error for a request that already timed out is not a protocol failure.
                                else if (message.Type == "Error" && message.RequestId == null)
                                    throw new InvalidDataException(message.Message ?? "Tray protocol error.");
                            }
                        }
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is TimeoutException
                    || exception is UnauthorizedAccessException || exception is ObjectDisposedException || exception is OperationCanceledException)
                {
                    if (!lifetime.IsCancellationRequested)
                    {
                        if (exception is VersionMismatchException)
                        {
                            LogOnce("version_mismatch", exception);
                            SetStatus(VersionMismatchStatus);
                            delayOverride = mismatchRetryDelay;
                        }
                        else
                        {
                            // A tray that is simply not running is routine; only protocol faults are recorded.
                            if (exception is InvalidDataException) LogOnce("protocol_error", exception);
                            SetStatus(exception is InvalidDataException ? "Protocol error - reconnecting" : "Tray unavailable - cached values are stale");
                        }
                    }
                }
                catch (Exception exception) when (!lifetime.IsCancellationRequested)
                {
                    // A fault in a subscriber or in this loop must not end reconnecting and leave the status stuck.
                    LogOnce("client_failed", exception);
                    SetStatus("Internal error - reconnecting");
                }
                finally
                {
                    connection = null;
                    foreach (var request in pending)
                        if (pending.TryRemove(request.Key, out var completion))
                            completion.TrySetException(new IOException("Tray disconnected before command acknowledgement."));
                }
                try { await Task.Delay(delayOverride ?? TimeSpan.FromSeconds(retrySeconds), lifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                retrySeconds = Math.Min(10, retrySeconds * 2);
            }
            SetStatus("Stopped");
        }

        public async Task<ProtocolMessage> SendAsync(ProtocolMessage command, CancellationToken cancellationToken)
        {
            var version = Interlocked.Read(ref contextVersion);
            if (command.Type == "SetActiveSession")
            {
                lock (contextGate)
                {
                    activeSessionId = command.ActiveSessionId;
                    repositoryKey = command.RepositoryKey;
                    version = Interlocked.Increment(ref contextVersion);
                    var previous = Snapshot;
                    if (previous != null)
                    {
                        Snapshot = new UsageSnapshot
                        {
                            HostInstanceId = previous.HostInstanceId, Revision = previous.Revision, IsDemo = previous.IsDemo,
                            IsPaused = previous.IsPaused, RefreshCount = previous.RefreshCount, Account = previous.Account, Settings = previous.Settings,
                            Local = new LocalUsage
                            {
                                Status = previous.Local.Status, RepositoryId = command.RepositoryKey ?? "unavailable",
                                OverallCredits = previous.Local.OverallCredits, RepositoryCredits = null,
                                InputTokens = previous.Local.InputTokens, OutputTokens = previous.Local.OutputTokens,
                                AsOfUtc = previous.Local.AsOfUtc, ErrorCode = previous.Local.ErrorCode,
                                WindowStartUtc = previous.Local.WindowStartUtc, WindowEndUtc = previous.Local.WindowEndUtc
                            }
                        };
                    }
                }
                if (Snapshot != null) SnapshotChanged?.Invoke(Snapshot);
            }
            var active = connection ?? throw new IOException("Tray is not connected.");
            var requestId = Guid.NewGuid().ToString("N");
            var request = new ProtocolMessage
            {
                Protocol = command.Protocol, Type = command.Type, RequestId = requestId,
                Sequence = command.Sequence, Snapshot = command.Snapshot,
                Settings = command.Settings, Paused = command.Paused,
                ActiveSessionId = command.ActiveSessionId,
                RepositoryKey = command.RepositoryKey, AccountLogin = command.AccountLogin,
                ContextVersion = version,
                Code = command.Code, Message = command.Message
            };
            var completion = new TaskCompletionSource<ProtocolMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[requestId] = completion;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                timeout.CancelAfter(command.Type == "Refresh" ? refreshTimeout : commandTimeout);
                using (timeout.Token.Register(() => completion.TrySetCanceled()))
                {
                    try
                    {
                        await contextDeliveryGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                        try { await active.WriteAsync(request, timeout.Token).ConfigureAwait(false); }
                        finally { contextDeliveryGate.Release(); }
                        var reply = await completion.Task.ConfigureAwait(false);
                        if (reply.Type != "Ack" && reply.Type != "Error") throw new InvalidDataException("Invalid command response.");
                        return reply;
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !lifetime.IsCancellationRequested)
                    {
                        throw new CommandTimeoutException(command.Type);
                    }
                    catch (ObjectDisposedException exception)
                    {
                        throw new IOException("Tray disconnected before command acknowledgement.", exception);
                    }
                    finally { pending.TryRemove(requestId, out _); }
                }
            }
        }

        public async Task StopAsync()
        {
            lifetime.Cancel();
            connection?.Dispose();
            if (worker != null) await worker.ConfigureAwait(false);
        }

        private async Task SubscribeAsync(PipeConnection active, bool promote)
        {
            await contextDeliveryGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                if (promote) connection = active;
                await active.WriteAsync(CreateSubscription(), lifetime.Token).ConfigureAwait(false);
            }
            finally { contextDeliveryGate.Release(); }
        }

        private ProtocolMessage CreateSubscription()
        {
            lock (contextGate) return new ProtocolMessage
            {
                Type = "Subscribe", ActiveSessionId = activeSessionId, RepositoryKey = repositoryKey, ContextVersion = contextVersion
            };
        }

        public void Dispose()
        {
            lifetime.Cancel();
            activeSessionId = null;
            repositoryKey = null;
            connection?.Dispose();
        }
    }
}