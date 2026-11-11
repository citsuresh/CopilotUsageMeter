using System.IO.Pipes;
using CopilotUsageMeter.Client;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Transport.Pipes;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class PipeListenerRecoveryTests
{
    [Fact]
    public void ListenerRetryDelayGrowsThenCaps()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), UsagePipeServer.DefaultListenerRetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), UsagePipeServer.DefaultListenerRetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(16), UsagePipeServer.DefaultListenerRetryDelay(5));
        Assert.Equal(TimeSpan.FromSeconds(30), UsagePipeServer.DefaultListenerRetryDelay(6));
        Assert.Equal(TimeSpan.FromSeconds(30), UsagePipeServer.DefaultListenerRetryDelay(1000));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public async Task ListenerRecreatesThePipeAndOnlyReportsRepeatedFailure(int failuresBeforeRecovery, int expectedFaultReports)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        var attempts = 0;
        var faults = new List<string>();
        var recoveries = new List<string>();
        using var server = new UsagePipeServer(
            () =>
            {
                if (Interlocked.Increment(ref attempts) <= failuresBeforeRecovery) throw new IOException("7654321");
                return new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            },
            () => new UsageSnapshot { HostInstanceId = "host-recover", Revision = 1 },
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }),
            listenerRetryDelay: _ => TimeSpan.FromMilliseconds(10));
        server.Faulted += message => { lock (faults) faults.Add(message); };
        server.Recovered += message => { lock (faults) recoveries.Add(message); };
        using var client = new PipeUsageClient(name);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += status => { if (status == "Connected - demo") connected.TrySetResult(true); };
        server.Start();
        client.Start();
        try
        {
            await connected.Task.WaitAsync(timeout.Token);
            Assert.True(Volatile.Read(ref attempts) > failuresBeforeRecovery);
            lock (faults)
            {
                Assert.Equal(expectedFaultReports, faults.Count);
                Assert.Equal(expectedFaultReports, recoveries.Count);
                if (expectedFaultReports > 0)
                {
                    Assert.Contains("retrying automatically", faults[0]);
                    Assert.Equal(faults[0], recoveries[0]);
                }
            }
        }
        finally
        {
            await client.StopAsync();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ListenerAlsoSurvivesAnUnexpectedExceptionType()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        var attempts = 0;
        using var server = new UsagePipeServer(
            () =>
            {
                if (Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException("7654321");
                return new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            },
            () => new UsageSnapshot { HostInstanceId = "host-other", Revision = 1 },
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }),
            listenerRetryDelay: _ => TimeSpan.FromMilliseconds(10));
        using var client = new PipeUsageClient(name);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += status => { if (status == "Connected - demo") connected.TrySetResult(true); };
        server.Start();
        client.Start();
        try { await connected.Task.WaitAsync(timeout.Token); }
        finally
        {
            await client.StopAsync();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task SeventeenthClientWaitsForAFreeSlotInsteadOfFailing()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var name = "CopilotUsageMeter.Test." + Guid.NewGuid().ToString("N");
        using var server = new UsagePipeServer(
            () => new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            () => new UsageSnapshot { HostInstanceId = "host-load", Revision = 1 },
            (_, _) => Task.FromResult(new ProtocolMessage { Type = "Ack" }));
        var clients = new List<PipeUsageClient>();
        var flags = new List<TaskCompletionSource<bool>>();
        server.Start();
        try
        {
            for (var index = 0; index < 17; index++)
            {
                var client = new PipeUsageClient(name);
                var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                client.StatusChanged += status => { if (status == "Connected - demo") connected.TrySetResult(true); };
                clients.Add(client);
                flags.Add(connected);
                client.Start();
                if (index < 16) await connected.Task.WaitAsync(timeout.Token);
            }

            // All 16 slots are in use, so the last client must still be waiting.
            await Task.Delay(1500, timeout.Token);
            Assert.False(flags[16].Task.IsCompleted);

            await clients[0].StopAsync();
            await flags[16].Task.WaitAsync(timeout.Token);
        }
        finally
        {
            foreach (var client in clients) { await client.StopAsync(); client.Dispose(); }
            await server.StopAsync();
        }
    }
}

public sealed class LocalHistoryErrorCodeTests
{
    [Theory]
    [InlineData("unavailable", "history_not_recorded", false)]
    [InlineData("unavailable", "history_no_turns", false)]
    [InlineData("unavailable", "history_tool_missing", false)]
    [InlineData("unavailable", "history_invalid_configuration", false)]
    [InlineData("available", null, false)]
    [InlineData("unavailable", "history_timeout", true)]
    [InlineData("unavailable", "history_process_failed", true)]
    [InlineData("unavailable", "history_invalid_data", true)]
    [InlineData("unavailable", null, true)]
    public void OnlyTransientFailuresAreRetried(string status, string? code, bool expected) =>
        Assert.Equal(expected, CopilotUsageMeter.Contracts.LocalHistoryErrorCodes.IsRetryableFailure(status, code));
}
