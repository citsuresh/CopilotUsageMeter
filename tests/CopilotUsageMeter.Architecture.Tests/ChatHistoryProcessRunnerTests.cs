using CopilotUsageMeter.Readers.ChatHistoryCli;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class ChatHistoryProcessRunnerTests
{
    [Theory]
    [InlineData("sql", "1")]
    [InlineData("store", "1")]
    [InlineData("turns", "id-001")]
    [InlineData("usage", "id-001")]
    [InlineData("usage-turns", "--help")]
    [InlineData("usage-turns", "id 001")]
    public async Task ForbiddenOrInvalidVerbsCannotStartAProcess(string verb, string id)
    {
        var result = await new ChatHistoryProcessRunner().RunAsync(Path.Combine(AppContext.BaseDirectory, "missing-001.exe"),
            new[] { verb, id, "--limit", "20" }, default);
        Assert.Equal("history_invalid_configuration", result.ErrorCode);
        Assert.Equal(-1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
    }

    [Theory]
    [InlineData("--text")]
    [InlineData("--help")]
    [InlineData("--force")]
    public async Task ArbitraryExtraOptionsAreRejected(string option)
    {
        var result = await new ChatHistoryProcessRunner().RunAsync(Path.Combine(AppContext.BaseDirectory, "missing-001.exe"),
            new[] { "usage-turns", "id-001", "--limit", "20", option }, default);
        Assert.Equal("history_invalid_configuration", result.ErrorCode);
    }

    [Fact]
    public async Task StartFailuresAreSanitizedAndNeverEchoTheExecutableOrIdentity()
    {
        var result = await new ChatHistoryProcessRunner().RunAsync(Path.Combine(AppContext.BaseDirectory, "missing-7654321.exe"),
            new[] { "usage-turns", "id-7654321", "--limit", "20" }, default);
        Assert.Equal("history_process_failed", result.ErrorCode);
        Assert.Empty(result.StandardOutput);
        Assert.DoesNotContain("7654321", result.ErrorCode);
    }

    [Fact]
    public async Task CancellationBeforeStartRemainsCancellation()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ChatHistoryProcessRunner().RunAsync(
            Path.Combine(AppContext.BaseDirectory, "missing-001.exe"), new[] { "usage-turns", "id-001", "--limit", "20" }, stop.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void TimeoutConfigurationIsBounded(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChatHistoryProcessRunner(TimeSpan.FromSeconds(seconds)));
    }
}