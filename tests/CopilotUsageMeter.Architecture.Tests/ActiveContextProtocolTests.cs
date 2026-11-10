using System.IO.Pipes;
using System.Text;
using CopilotUsageMeter.Client;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Transport.Pipes;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class ActiveContextProtocolTests
{
    [Fact]
    public async Task ClientsKeepDifferentChartsAndResendContextAfterReconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        var firstId = "00000000-0000-0000-0000-000000000001";
        var secondId = "00000000-0000-0000-0000-000000000002";
        UsagePipeServer Create(string host) => new(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => new UsageSnapshot { HostInstanceId = host, Revision = 1, IsDemo = false },
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }),
            (snapshot, id) => new UsageSnapshot
            {
                HostInstanceId = snapshot.HostInstanceId, Revision = snapshot.Revision, IsDemo = false,
                Local = new LocalUsage { OverallCredits = id == firstId ? 1 : id == secondId ? 2 : null }
            });
        using var first = new PipeUsageClient(name);
        using var second = new PipeUsageClient(name);
        using var server = Create("host-001");
        server.Start(); first.Start(); second.Start();
        await Until(() => first.Snapshot != null && second.Snapshot != null, timeout.Token);
        await first.SendAsync(new ProtocolMessage { Type = "SetActiveSession", ActiveSessionId = firstId }, timeout.Token);
        await second.SendAsync(new ProtocolMessage { Type = "SetActiveSession", ActiveSessionId = secondId }, timeout.Token);
        Assert.Equal(1, first.Snapshot!.Local.OverallCredits);
        Assert.Equal(2, second.Snapshot!.Local.OverallCredits);
        Assert.Equal(2, server.GetActiveSessionIds().Length);
        await server.StopAsync();
        using var restarted = Create("host-002"); restarted.Start();
        await Until(() => first.Snapshot?.HostInstanceId == "host-002" && second.Snapshot?.HostInstanceId == "host-002", timeout.Token);
        Assert.Equal(1, first.Snapshot!.Local.OverallCredits);
        Assert.Equal(2, second.Snapshot!.Local.OverallCredits);
        await first.StopAsync(); await second.StopAsync(); await restarted.StopAsync();
    }

    [Fact]
    public async Task ContextIdentifiersCannotAppearInSnapshotEnvelopes()
    {
        using var stream = new MemoryStream();
        using var connection = new PipeConnection(stream);
        await connection.WriteAsync(new ProtocolMessage { Type = "Snapshot", Snapshot = new UsageSnapshot() }, CancellationToken.None);
        var json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("activeSessionId", json);
        Assert.DoesNotContain("SessionId", json);
        Assert.DoesNotContain("repositoryKey", json);
        Assert.DoesNotContain("accountLogin", json);
        await Assert.ThrowsAsync<InvalidDataException>(() => connection.WriteAsync(new ProtocolMessage
        {
            Type = "Snapshot", Snapshot = new UsageSnapshot(), ActiveSessionId = "00000000-0000-0000-0000-000000000001"
        }, CancellationToken.None));
    }

    [Theory]
    [InlineData(-1, null, "invalid_context")]
    [InlineData(0, "001/002/003", "invalid_context")]
    [InlineData(0, "C:\\001\\002", "invalid_context")]
    public async Task InvalidContextsAreRejectedWithoutEchoingInput(long version, string? repository, string code)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        using var server = new UsagePipeServer(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => new UsageSnapshot { HostInstanceId = "host-001", Revision = 1 },
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }));
        server.Start();
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var connection = new PipeConnection(pipe);
        await connection.WriteAsync(new ProtocolMessage { Type = "Hello" }, timeout.Token);
        Assert.Equal("HelloAck", (await connection.ReadAsync(timeout.Token))!.Type);
        await connection.WriteAsync(new ProtocolMessage { Type = "Subscribe", ContextVersion = version, RepositoryKey = repository }, timeout.Token);
        var error = await connection.ReadAsync(timeout.Token);
        Assert.Equal("Error", error!.Type);
        Assert.Equal(code, error.Code);
        Assert.Null(error.RepositoryKey);
        Assert.Null(error.ActiveSessionId);
        await server.StopAsync();
    }

    [Fact]
    public async Task ContextSelectedBeforeConnectionIsRetainedByInitialSubscription()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        using var client = new PipeUsageClient(name);
        const string id = "00000000-0000-0000-0000-000000000001";
        await Assert.ThrowsAsync<IOException>(() => client.SendAsync(new ProtocolMessage
        { Type = "SetActiveSession", ActiveSessionId = id, RepositoryKey = "001/002" }, timeout.Token));
        using var server = new UsagePipeServer(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => new UsageSnapshot { HostInstanceId = "host-001", Revision = 1, IsDemo = false },
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }),
            projectContext: (snapshot, session, repository) => new UsageSnapshot
            {
                HostInstanceId = snapshot.HostInstanceId, Revision = 1, IsDemo = false,
                Local = new LocalUsage { RepositoryId = repository!, OverallCredits = session == id ? 3 : null }
            });
        server.Start(); client.Start();
        await Until(() => client.Status == "Connected", timeout.Token);
        Assert.Equal(3, client.Snapshot!.Local.OverallCredits);
        Assert.Equal("001/002", client.Snapshot.Local.RepositoryId);
        await client.StopAsync(); await server.StopAsync();
    }

    private static async Task Until(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(20, token);
    }
}