using System.IO.Pipes;
using CopilotUsageMeter.Client;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Transport.Pipes;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class PipeBusyTrayTests
{
    private static string NewPipeName() => "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");

    private static NamedPipeServerStream CreatePipe(string name) =>
        new(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static UsageSnapshot Snapshot() => new() { HostInstanceId = "host-busy", Revision = 1 };

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> ConnectedSignal(PipeUsageClient client)
    {
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += status => { if (status == "Connected - demo") connected.TrySetResult(true); };
        return connected;
    }

    private static async Task<(NamedPipeClientStream Pipe, PipeConnection Connection)> ConnectRawAsync(string name, CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000, token);
        var connection = new PipeConnection(pipe);
        await connection.WriteAsync(new ProtocolMessage { Type = "Hello" }, token);
        Assert.Equal("HelloAck", (await connection.ReadAsync(token))!.Type);
        await connection.WriteAsync(new ProtocolMessage { Type = "Subscribe" }, token);
        Assert.Equal("Snapshot", (await connection.ReadAsync(token))!.Type);
        return (pipe, connection);
    }

    private static async Task<ProtocolMessage> ReadReplyAsync(PipeConnection connection, CancellationToken token)
    {
        while (true)
        {
            var message = await connection.ReadAsync(token) ?? throw new EndOfStreamException();
            if (message.Type != "Heartbeat") return message;
        }
    }

    [Fact]
    public async Task ServerSendsHeartbeatsToSubscribedClients()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        using var server = new UsagePipeServer(() => CreatePipe(name), Snapshot,
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }), heartbeatInterval: TimeSpan.FromMilliseconds(50));
        server.Start();
        try
        {
            var (pipe, connection) = await ConnectRawAsync(name, timeout.Token);
            using (pipe)
            using (connection)
            {
                var first = await connection.ReadAsync(timeout.Token);
                var second = await connection.ReadAsync(timeout.Token);
                Assert.Equal("Heartbeat", first!.Type);
                Assert.Equal("Heartbeat", second!.Type);
            }
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task ClientStaysConnectedWhileHeartbeatsArrive()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        var statuses = new List<string>();
        using var server = new UsagePipeServer(() => CreatePipe(name), Snapshot,
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }), heartbeatInterval: TimeSpan.FromMilliseconds(100));
        using var client = new PipeUsageClient(name, heartbeatTimeout: TimeSpan.FromMilliseconds(700));
        var connected = ConnectedSignal(client);
        client.StatusChanged += status => { lock (statuses) statuses.Add(status); };
        server.Start();
        client.Start();
        try
        {
            await connected.Task.WaitAsync(timeout.Token);
            await Task.Delay(2500, timeout.Token);
            lock (statuses) Assert.DoesNotContain(statuses, status => status.StartsWith("Tray unavailable", StringComparison.Ordinal));
            Assert.Equal("Connected - demo", client.Status);
        }
        finally
        {
            await client.StopAsync();
            await server.StopAsync();
        }
    }

    // A minimal tray stand-in that completes the handshake and then goes silent, optionally after one heartbeat.
    private static async Task<int> RunSilentTrayAsync(string name, bool sendHeartbeat, TaskCompletionSource secondConnection, CancellationToken token)
    {
        var accepted = 0;
        var open = new List<IDisposable>();
        try
        {
            while (!token.IsCancellationRequested)
            {
                var pipe = CreatePipe(name);
                open.Add(pipe);
                await pipe.WaitForConnectionAsync(token);
                if (Interlocked.Increment(ref accepted) == 2) secondConnection.TrySetResult();
                try
                {
                    var connection = new PipeConnection(pipe);
                    await connection.ReadAsync(token);
                    await connection.WriteAsync(new ProtocolMessage { Type = "HelloAck" }, token);
                    await connection.ReadAsync(token);
                    await connection.WriteAsync(new ProtocolMessage { Type = "Snapshot", Snapshot = Snapshot(), Sequence = 1 }, token);
                    if (sendHeartbeat) await connection.WriteAsync(new ProtocolMessage { Type = "Heartbeat" }, token);
                }
                catch (IOException) { /* The client dropped this connection. */ }
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        finally { foreach (var item in open) item.Dispose(); }
        return Volatile.Read(ref accepted);
    }

    [Fact]
    public async Task ClientReconnectsWhenAHeartbeatingTrayGoesSilent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        var second = Signal();
        var tray = RunSilentTrayAsync(name, sendHeartbeat: true, second, timeout.Token);
        using var client = new PipeUsageClient(name, heartbeatTimeout: TimeSpan.FromMilliseconds(400));
        var sawStale = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += status => { if (status.StartsWith("Tray unavailable", StringComparison.Ordinal)) sawStale.TrySetResult(true); };
        client.Start();
        try
        {
            await sawStale.Task.WaitAsync(timeout.Token);
            await second.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            await client.StopAsync();
            timeout.Cancel();
            await tray;
        }
    }

    [Fact]
    public async Task ClientKeepsTheConnectionToATrayThatNeverSendsHeartbeats()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        var second = Signal();
        var tray = RunSilentTrayAsync(name, sendHeartbeat: false, second, timeout.Token);
        using var client = new PipeUsageClient(name, heartbeatTimeout: TimeSpan.FromMilliseconds(300));
        var connected = ConnectedSignal(client);
        client.Start();
        try
        {
            await connected.Task.WaitAsync(timeout.Token);
            await Task.Delay(1500, timeout.Token);
            Assert.False(second.Task.IsCompleted);
            Assert.Equal("Connected - demo", client.Status);
        }
        finally
        {
            await client.StopAsync();
            timeout.Cancel();
            await tray;
        }
    }

    [Fact]
    public async Task ContextChangeIsAnsweredWhileARefreshIsStuck()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        var entered = Signal();
        var release = Signal();
        using var server = new UsagePipeServer(() => CreatePipe(name), Snapshot, async (request, token) =>
        {
            if (request.Type == "Refresh")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            return new ProtocolMessage { Type = "Ack" };
        });
        using var client = new PipeUsageClient(name, refreshTimeout: TimeSpan.FromSeconds(30));
        var connected = ConnectedSignal(client);
        server.Start();
        client.Start();
        try
        {
            await connected.Task.WaitAsync(timeout.Token);
            var refresh = client.SendAsync(new ProtocolMessage { Type = "Refresh" }, timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);

            var context = await client.SendAsync(
                new ProtocolMessage { Type = "SetActiveSession", ActiveSessionId = Guid.NewGuid().ToString("D") }, timeout.Token);
            Assert.Equal("Ack", context.Type);
            Assert.False(refresh.IsCompleted);

            release.SetResult();
            Assert.Equal("Ack", (await refresh).Type);
        }
        finally
        {
            release.TrySetResult();
            await client.StopAsync();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task CommandsOnOneConnectionRunOneAtATimeInArrivalOrder()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        var running = 0;
        var overlapped = false;
        var order = new List<string>();
        using var server = new UsagePipeServer(() => CreatePipe(name), Snapshot, async (request, token) =>
        {
            if (Interlocked.Increment(ref running) > 1) overlapped = true;
            await Task.Delay(30, token);
            lock (order) order.Add(request.Type);
            Interlocked.Decrement(ref running);
            return new ProtocolMessage { Type = "Ack" };
        });
        server.Start();
        try
        {
            var (pipe, connection) = await ConnectRawAsync(name, timeout.Token);
            using (pipe)
            using (connection)
            {
                var types = new[] { "SetPaused", "Refresh", "OpenStatus", "SetPaused" };
                var ids = new List<string>();
                foreach (var type in types)
                {
                    var id = Guid.NewGuid().ToString("N");
                    ids.Add(id);
                    await connection.WriteAsync(new ProtocolMessage { Type = type, RequestId = id, Paused = true }, timeout.Token);
                }
                foreach (var id in ids)
                {
                    var reply = await ReadReplyAsync(connection, timeout.Token);
                    Assert.Equal("Ack", reply.Type);
                    Assert.Equal(id, reply.RequestId);
                }
            }
            Assert.False(overlapped);
            lock (order) Assert.Equal(new[] { "SetPaused", "Refresh", "OpenStatus", "SetPaused" }, order);
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task FullCommandQueueRepliesBusyThenDrainsInOrder()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        var entered = Signal();
        var release = Signal();
        using var server = new UsagePipeServer(() => CreatePipe(name), Snapshot, async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new ProtocolMessage { Type = "Ack" };
        });
        server.Start();
        try
        {
            var (pipe, connection) = await ConnectRawAsync(name, timeout.Token);
            using (pipe)
            using (connection)
            {
                var accepted = new List<string>();
                async Task<string> SendAsync()
                {
                    var id = Guid.NewGuid().ToString("N");
                    await connection.WriteAsync(new ProtocolMessage { Type = "Refresh", RequestId = id }, timeout.Token);
                    return id;
                }

                accepted.Add(await SendAsync());
                await entered.Task.WaitAsync(timeout.Token);
                for (var index = 0; index < UsagePipeServer.MaxQueuedCommands; index++) accepted.Add(await SendAsync());

                var rejectedId = await SendAsync();
                var busy = await ReadReplyAsync(connection, timeout.Token);
                Assert.Equal("Error", busy.Type);
                Assert.Equal("busy", busy.Code);
                Assert.Equal(rejectedId, busy.RequestId);

                release.SetResult();
                foreach (var id in accepted)
                {
                    var reply = await ReadReplyAsync(connection, timeout.Token);
                    Assert.Equal("Ack", reply.Type);
                    Assert.Equal(id, reply.RequestId);
                }
            }
        }
        finally
        {
            release.TrySetResult();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RefreshGetsALongerTimeoutAndALateErrorDoesNotDropTheConnection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        var statuses = new List<string>();
        using var server = new UsagePipeServer(() => CreatePipe(name), Snapshot, async (request, token) =>
        {
            await Task.Delay(600, token);
            return request.Type == "OpenStatus" ? UsagePipeServer.Error(request, "late_failure", "7654321") : new ProtocolMessage { Type = "Ack" };
        });
        using var client = new PipeUsageClient(name, commandTimeout: TimeSpan.FromMilliseconds(200), refreshTimeout: TimeSpan.FromSeconds(5));
        var connected = ConnectedSignal(client);
        client.StatusChanged += status => { lock (statuses) statuses.Add(status); };
        server.Start();
        client.Start();
        try
        {
            await connected.Task.WaitAsync(timeout.Token);

            var exception = await Assert.ThrowsAsync<CommandTimeoutException>(
                () => client.SendAsync(new ProtocolMessage { Type = "OpenStatus" }, timeout.Token));
            Assert.Equal("OpenStatus", exception.CommandType);

            var refresh = await client.SendAsync(new ProtocolMessage { Type = "Refresh" }, timeout.Token);
            Assert.Equal("Ack", refresh.Type);

            // The error for the timed-out request arrived by now and must have been ignored.
            lock (statuses) Assert.DoesNotContain(statuses, status => status.Contains("rror", StringComparison.Ordinal) || status.StartsWith("Tray unavailable", StringComparison.Ordinal));
            Assert.Equal("Connected - demo", client.Status);
        }
        finally
        {
            await client.StopAsync();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task StopWaitsForACommandHandlerThatIsStillUnwinding()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        var entered = Signal();
        var finished = false;
        using var server = new UsagePipeServer(() => CreatePipe(name), Snapshot, async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally
            {
                await Task.Delay(300);
                Volatile.Write(ref finished, true);
            }
            return new ProtocolMessage { Type = "Ack" };
        });
        server.Start();
        var (pipe, connection) = await ConnectRawAsync(name, timeout.Token);
        using (pipe)
        using (connection)
        {
            await connection.WriteAsync(new ProtocolMessage { Type = "Refresh", RequestId = "r1" }, timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);
            await server.StopAsync();
            Assert.True(Volatile.Read(ref finished));
        }
    }

    [Fact]
    public async Task FailingCommandHandlerEndsTheSessionWithOneFaultReport()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        var faults = new List<string>();
        using var server = new UsagePipeServer(() => CreatePipe(name), Snapshot,
            (_, _) => throw new InvalidOperationException("7654321"));
        server.Faulted += message => { lock (faults) faults.Add(message); };
        server.Start();
        try
        {
            var (pipe, connection) = await ConnectRawAsync(name, timeout.Token);
            using (pipe)
            using (connection)
            {
                await connection.WriteAsync(new ProtocolMessage { Type = "Refresh", RequestId = "r1" }, timeout.Token);
                try
                {
                    while (await connection.ReadAsync(timeout.Token) is { } message)
                        Assert.Equal("Heartbeat", message.Type);
                }
                catch (IOException) { /* The tray closed the connection. */ }
            }
            await Task.Delay(500, timeout.Token);
            lock (faults) Assert.Equal(new[] { "Pipe session failed unexpectedly." }, faults);
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task RefreshTimeoutIsReportedAsATimeoutNotAnOutage()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = NewPipeName();
        using var server = new UsagePipeServer(() => CreatePipe(name), Snapshot, async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new ProtocolMessage { Type = "Ack" };
        });
        using var client = new PipeUsageClient(name, refreshTimeout: TimeSpan.FromMilliseconds(300));
        var connected = ConnectedSignal(client);
        server.Start();
        client.Start();
        try
        {
            await connected.Task.WaitAsync(timeout.Token);
            var exception = await Assert.ThrowsAsync<CommandTimeoutException>(
                () => client.SendAsync(new ProtocolMessage { Type = "Refresh" }, timeout.Token));
            Assert.Equal("Refresh", exception.CommandType);
            Assert.Equal("Connected - demo", client.Status);
        }
        finally
        {
            await client.StopAsync();
            await server.StopAsync();
        }
    }
}
