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
        private Task? worker;
        private long sequence;
        private string hostInstance = "";

        public event Action<UsageSnapshot>? SnapshotChanged;
        public event Action<string>? StatusChanged;
        public UsageSnapshot? Snapshot { get; private set; }
        public string Status { get; private set; } = "Disconnected";

        public PipeUsageClient(string pipeName) => this.pipeName = pipeName;

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

        private async Task RunAsync()
        {
            int retrySeconds = 1;
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    SetStatus("Connecting");
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
                            {
                                SetStatus("Version mismatch: update tray and extension");
                                return;
                            }
                            if (ack?.Type != "HelloAck" || ack.Protocol != UsageProtocol.Version)
                                throw new InvalidDataException("Invalid tray handshake.");
                            sequence = 0;
                            hostInstance = "";
                            await active.WriteAsync(new ProtocolMessage { Type = "Subscribe" }, lifetime.Token).ConfigureAwait(false);
                            connection = active;
                            while (!lifetime.IsCancellationRequested)
                            {
                                var message = await active.ReadAsync(lifetime.Token).ConfigureAwait(false);
                                if (message == null) throw new EndOfStreamException("Tray disconnected.");
                                if (message.Protocol != UsageProtocol.Version)
                                    throw new InvalidDataException("Unexpected protocol version.");
                                if (message.Type == "Snapshot" || message.Type == "Update")
                                {
                                    var snapshot = message.Snapshot;
                                    if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.HostInstanceId)
                                        || snapshot.Revision < 1 || snapshot.Account == null || snapshot.Local == null || snapshot.Settings == null
                                        || !snapshot.IsDemo || snapshot.Settings.DemoRefreshSeconds < 5 || snapshot.Settings.DemoRefreshSeconds > 300)
                                        throw new InvalidDataException("Invalid demo snapshot.");
                                    if (message.Type != "Snapshot" && (message.Sequence != sequence + 1 || snapshot.HostInstanceId != hostInstance))
                                    {
                                        await active.WriteAsync(new ProtocolMessage { Type = "Subscribe" }, lifetime.Token).ConfigureAwait(false);
                                        SetStatus("Resynchronizing");
                                        continue;
                                    }
                                    if (message.Sequence < 1) throw new InvalidDataException("Invalid delivery sequence.");
                                    sequence = message.Sequence;
                                    hostInstance = snapshot.HostInstanceId;
                                    Snapshot = snapshot;
                                    retrySeconds = 1;
                                    SetStatus(snapshot.IsPaused ? "Paused - demo" : "Connected - demo");
                                    SnapshotChanged?.Invoke(snapshot);
                                }
                                else if (message.RequestId != null && pending.TryRemove(message.RequestId, out var completion))
                                    completion.TrySetResult(message);
                                else if (message.Type == "Error")
                                    throw new InvalidDataException(message.Message ?? "Tray protocol error.");
                            }
                        }
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is TimeoutException
                    || exception is UnauthorizedAccessException || exception is ObjectDisposedException || exception is OperationCanceledException)
                {
                    if (!lifetime.IsCancellationRequested)
                        SetStatus(exception is InvalidDataException ? "Protocol error - reconnecting" : "Tray unavailable - cached demo values are stale");
                }
                finally
                {
                    connection = null;
                    foreach (var request in pending)
                        if (pending.TryRemove(request.Key, out var completion))
                            completion.TrySetException(new IOException("Tray disconnected before command acknowledgement."));
                }
                try { await Task.Delay(TimeSpan.FromSeconds(retrySeconds), lifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                retrySeconds = Math.Min(10, retrySeconds * 2);
            }
            SetStatus("Stopped");
        }

        public async Task<ProtocolMessage> SendAsync(ProtocolMessage command, CancellationToken cancellationToken)
        {
            var active = connection ?? throw new IOException("Tray is not connected.");
            var requestId = Guid.NewGuid().ToString("N");
            var request = new ProtocolMessage
            {
                Protocol = command.Protocol, Type = command.Type, RequestId = requestId,
                Sequence = command.Sequence, Snapshot = command.Snapshot,
                Settings = command.Settings, Paused = command.Paused,
                Code = command.Code, Message = command.Message
            };
            var completion = new TaskCompletionSource<ProtocolMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[requestId] = completion;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using (timeout.Token.Register(() => completion.TrySetCanceled()))
                {
                    try
                    {
                        await active.WriteAsync(request, timeout.Token).ConfigureAwait(false);
                        var reply = await completion.Task.ConfigureAwait(false);
                        if (reply.Type != "Ack" && reply.Type != "Error") throw new InvalidDataException("Invalid command response.");
                        return reply;
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

        public void Dispose()
        {
            lifetime.Cancel();
            connection?.Dispose();
        }
    }
}