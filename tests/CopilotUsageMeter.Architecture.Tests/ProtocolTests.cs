using System.IO.Pipes;
using System.Reflection;
using System.Text;
using CopilotUsageMeter.Client;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;
using CopilotUsageMeter.Transport.Pipes;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public async Task FramesRoundTripWithoutConflatingAccountAndLocalUnits()
    {
        using var stream = new MemoryStream();
        using var connection = new PipeConnection(stream);
        await connection.WriteAsync(new ProtocolMessage
        {
            Type = "Snapshot", Sequence = 1,
            Snapshot = new UsageSnapshot
            {
                HostInstanceId = "host-001", Revision = 7,
                Account = new AccountUsage { Allowance = 100, Consumed = 39, Remaining = 61 },
                Local = new LocalUsage { OverallCredits = 400, RepositoryCredits = 125 }
            }
        }, CancellationToken.None);
        stream.Position = 0;
        var result = await connection.ReadAsync(CancellationToken.None);
        Assert.Equal(1, result!.Sequence);
        Assert.Equal(7, result.Snapshot!.Revision);
        Assert.Equal(39, result.Snapshot.Account.Consumed);
        Assert.Equal(400, result.Snapshot.Local.OverallCredits);
        Assert.Equal(125, result.Snapshot.Local.RepositoryCredits);
        Assert.Equal("demo-account-credits", result.Snapshot.Account.Unit);
        Assert.Null(await connection.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("{broken}\n")]
    [InlineData("null\n")]
    [InlineData("{\"type\":\"Hello\"}")]
    public async Task InvalidOrIncompleteFramesAreRejected(string json)
    {
        using var connection = new PipeConnection(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OversizedFramesAreRejectedBeforeDeserialization()
    {
        using var connection = new PipeConnection(new MemoryStream(new byte[UsageProtocol.MaximumMessageBytes + 1]));
        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MalformedUtf8IsRejected()
    {
        using var connection = new PipeConnection(new MemoryStream(new byte[] { 0xff, 10 }));
        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnknownJsonFieldsAreIgnored()
    {
        using var connection = new PipeConnection(new MemoryStream(Encoding.UTF8.GetBytes("{\"protocol\":1,\"type\":\"Hello\",\"future\":123}\n")));
        Assert.Equal("Hello", (await connection.ReadAsync(CancellationToken.None))!.Type);
    }

    [Theory]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    public async Task FrameLimitIsMeasuredInBytesExcludingLineFeed(int bytes, bool accepted)
    {
        const string prefix = "{\"protocol\":1,\"type\":\"Hello\",\"future\":\"";
        const string suffix = "\"}";
        var json = prefix + new string('a', bytes - prefix.Length - suffix.Length) + suffix + "\n";
        using var connection = new PipeConnection(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        if (accepted) Assert.Equal("Hello", (await connection.ReadAsync(CancellationToken.None))!.Type);
        else await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancelledReadTerminates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var connection = new PipeConnection(new MemoryStream(Encoding.UTF8.GetBytes("{}\n")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.ReadAsync(cancellation.Token));
    }

    [Fact]
    public async Task MultipleClientsReceiveIndependentSequencesAndReconnectToANewHost()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        var current = new UsageSnapshot { HostInstanceId = "host-001", Revision = 1 };
        UsagePipeServer CreateServer() => new(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => current,
            (request, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }));
        using var first = new PipeUsageClient(name);
        using var second = new PipeUsageClient(name);
        using var server = CreateServer();
        server.Start();
        first.Start();
        second.Start();
        await UntilAsync(() => first.Snapshot?.HostInstanceId == "host-001" && second.Snapshot?.HostInstanceId == "host-001", deadline.Token);
        current = new UsageSnapshot { HostInstanceId = "host-001", Revision = 20 };
        await server.BroadcastAsync(current, deadline.Token);
        await UntilAsync(() => first.Snapshot?.Revision == 20 && second.Snapshot?.Revision == 20, deadline.Token);
        Assert.Equal("Ack", (await first.SendAsync(new ProtocolMessage { Type = "Refresh" }, deadline.Token)).Type);
        await server.StopAsync();
        await UntilAsync(() => first.Status.Contains("stale") && second.Status.Contains("stale"), deadline.Token);
        Assert.Equal(20, first.Snapshot!.Revision);
        current = new UsageSnapshot { HostInstanceId = "host-002", Revision = 1 };
        using var restarted = CreateServer();
        restarted.Start();
        await UntilAsync(() => first.Snapshot?.HostInstanceId == "host-002" && second.Snapshot?.HostInstanceId == "host-002", deadline.Token);
        Assert.Equal(1, first.Snapshot!.Revision);
        await first.StopAsync();
        await second.StopAsync();
        await restarted.StopAsync();
    }

    [Fact]
    public async Task DisconnectDuringCommandReportsFailureWithoutEscapingThePanel()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var resumeReader = new ManualResetEventSlim();
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        using var server = new UsagePipeServer(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => new UsageSnapshot { HostInstanceId = "host-006", Revision = 1 },
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }));
        using var client = new PipeUsageClient(name);
        using var model = new UsagePanelViewModel(client, action => action());
        client.StatusChanged += status =>
        {
            if (status != "Connected - demo") return;
            connected.TrySetResult(true);
            resumeReader.Wait(timeout.Token);
        };
        server.Start();
        model.Start();
        try
        {
            await connected.Task.WaitAsync(timeout.Token);
            Assert.True(model.CanSend);
            var connection = (PipeConnection)typeof(PipeUsageClient)
                .GetField("connection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
            // Hold the reader before cleanup to reproduce a write racing connection disposal.
            connection.Dispose();
            await Assert.ThrowsAsync<IOException>(() => client.SendAsync(new ProtocolMessage { Type = "Refresh" }, timeout.Token));
            await model.RefreshAsync().WaitAsync(timeout.Token);
            Assert.Equal("Command not acknowledged; check connection/state before retrying.", model.Notice);
        }
        finally
        {
            resumeReader.Set();
            await client.StopAsync();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task OwnerCancelledCommandDuringShutdownDoesNotFaultServerStop()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var ownerCancel = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        using var server = new UsagePipeServer(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => new UsageSnapshot { HostInstanceId = "host-007", Revision = 1 },
            async (_, _) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ownerCancel.Token);
                return new ProtocolMessage { Type = "Ack" };
            });
        using var client = new PipeUsageClient(name);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += status => { if (status == "Connected - demo") connected.TrySetResult(true); };
        server.Start();
        client.Start();
        await connected.Task.WaitAsync(timeout.Token);
        var pending = client.SendAsync(new ProtocolMessage { Type = "Refresh" }, timeout.Token);
        await started.Task.WaitAsync(timeout.Token);
        // The owner's token is cancelled before the server's own lifetime, as the tray does on Exit.
        ownerCancel.Cancel();
        await server.StopAsync();
        await client.StopAsync();
        try { await pending; } catch (Exception) { /* The command legitimately fails during shutdown. */ }
    }

    [Fact]
    public async Task IncompatibleHandshakeReturnsReadableError()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        using var server = new UsagePipeServer(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => new UsageSnapshot(), (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }));
        server.Start();
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var connection = new PipeConnection(pipe);
        await connection.WriteAsync(new ProtocolMessage { Type = "Hello", Protocol = 2 }, timeout.Token);
        var reply = await connection.ReadAsync(timeout.Token);
        Assert.Equal("version_mismatch", reply!.Code);
        Assert.Contains("Update", reply.Message);
        Assert.Null(await connection.ReadAsync(timeout.Token));
        await server.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProtocolFailuresReconnectAndAllowCleanShutdown(bool malformedHandshake)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        using var invalidPeer = new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new PipeUsageClient(name);
        var protocolError = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += status => { if (status == "Protocol error - reconnecting") protocolError.TrySetResult(true); };
        client.Start();
        await invalidPeer.WaitForConnectionAsync(timeout.Token);
        using (var wire = new PipeConnection(invalidPeer))
        {
            Assert.Equal("Hello", (await wire.ReadAsync(timeout.Token))!.Type);
            if (malformedHandshake)
            {
                var invalid = Encoding.UTF8.GetBytes("{broken}\n");
                await invalidPeer.WriteAsync(invalid, timeout.Token);
                await invalidPeer.FlushAsync(timeout.Token);
            }
            else
            {
                await wire.WriteAsync(new ProtocolMessage { Type = "HelloAck" }, timeout.Token);
                Assert.Equal("Subscribe", (await wire.ReadAsync(timeout.Token))!.Type);
                await wire.WriteAsync(new ProtocolMessage { Type = "Snapshot", Sequence = 1 }, timeout.Token);
            }
            await protocolError.Task.WaitAsync(timeout.Token);
        }
        using var goodPeer = new UsagePipeServer(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => new UsageSnapshot { HostInstanceId = "host-003", Revision = 1 },
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }));
        goodPeer.Start();
        await UntilAsync(() => client.Snapshot?.HostInstanceId == "host-003", timeout.Token);
        Assert.Equal("Connected - demo", client.Status);
        await client.StopAsync();
        Assert.Equal("Stopped", client.Status);
        await goodPeer.StopAsync();
    }

    [Fact]
    public async Task ConcurrentReuseOfCommandPreservesBothAcknowledgementsAndCallerIdentity()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        using var peer = new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new PipeUsageClient(name);
        client.Start();
        await peer.WaitForConnectionAsync(timeout.Token);
        using var wire = new PipeConnection(peer);
        await wire.ReadAsync(timeout.Token);
        await wire.WriteAsync(new ProtocolMessage { Type = "HelloAck" }, timeout.Token);
        await wire.ReadAsync(timeout.Token);
        await wire.WriteAsync(new ProtocolMessage { Type = "Snapshot", Sequence = 1,
            Snapshot = new UsageSnapshot { HostInstanceId = "host-004", Revision = 1 } }, timeout.Token);
        await UntilAsync(() => client.Snapshot != null, timeout.Token);
        var command = new ProtocolMessage { Type = "Refresh", RequestId = "caller-001" };
        var first = client.SendAsync(command, timeout.Token);
        var firstWire = await wire.ReadAsync(timeout.Token);
        var second = client.SendAsync(command, timeout.Token);
        var secondWire = await wire.ReadAsync(timeout.Token);
        Assert.NotEqual(firstWire!.RequestId, secondWire!.RequestId);
        await wire.WriteAsync(new ProtocolMessage { Type = "Ack", RequestId = firstWire.RequestId }, timeout.Token);
        Assert.Equal(firstWire.RequestId, (await first.WaitAsync(timeout.Token)).RequestId);
        await wire.WriteAsync(new ProtocolMessage { Type = "Ack", RequestId = secondWire.RequestId }, timeout.Token);
        Assert.Equal(secondWire.RequestId, (await second.WaitAsync(timeout.Token)).RequestId);
        Assert.Equal("caller-001", command.RequestId);
        await client.StopAsync();
    }

    [Fact]
    public async Task CancellationBeforeWriteDoesNotConsumeDeliverySequence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        var snapshot = new UsageSnapshot { HostInstanceId = "host-005", Revision = 1 };
        using var server = new UsagePipeServer(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => snapshot, (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }));
        server.Start();
        using var peer = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await peer.ConnectAsync(timeout.Token);
        using var wire = new PipeConnection(peer);
        await wire.WriteAsync(new ProtocolMessage { Type = "Hello" }, timeout.Token);
        await wire.ReadAsync(timeout.Token);
        await wire.WriteAsync(new ProtocolMessage { Type = "Subscribe" }, timeout.Token);
        Assert.Equal(1, (await wire.ReadAsync(timeout.Token))!.Sequence);
        await wire.WriteAsync(new ProtocolMessage { Type = "Refresh", RequestId = "request-005" }, timeout.Token);
        Assert.Equal("Ack", (await wire.ReadAsync(timeout.Token))!.Type);
        var sessions = typeof(UsagePipeServer).GetField("sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(server)!;
        var session = ((System.Collections.IEnumerable)sessions.GetType().GetProperty("Values")!.GetValue(sessions)!).Cast<object>().Single();
        var serverConnection = (PipeConnection)session.GetType().GetField("Connection")!.GetValue(session)!;
        var writeGate = (SemaphoreSlim)typeof(PipeConnection).GetField("writeGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(serverConnection)!;
        // Force cancellation before any frame bytes can be written, without timing a pipe-buffer fill.
        await writeGate.WaitAsync(timeout.Token);
        try
        {
            using var cancel = new CancellationTokenSource();
            var cancelled = server.BroadcastAsync(new UsageSnapshot { HostInstanceId = "host-005", Revision = 2 }, cancel.Token);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        }
        finally { writeGate.Release(); }
        var receiving = wire.ReadAsync(timeout.Token);
        await server.BroadcastAsync(new UsageSnapshot { HostInstanceId = "host-005", Revision = 3 }, timeout.Token);
        var delivered = await receiving;
        Assert.Equal(2, delivered!.Sequence);
        Assert.Equal(3, delivered.Snapshot!.Revision);
        await server.StopAsync();
    }

    private static async Task UntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(25, cancellationToken);
    }
}