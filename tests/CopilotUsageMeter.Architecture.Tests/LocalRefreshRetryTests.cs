using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class LocalRefreshRetryTests
{
    [Fact]
    public void RetryDelaysAreFiveFifteenFortyFiveSecondsThenFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), LocalRefreshScheduler.RetryDelayAfter(1));
        Assert.Equal(TimeSpan.FromSeconds(15), LocalRefreshScheduler.RetryDelayAfter(2));
        Assert.Equal(TimeSpan.FromSeconds(45), LocalRefreshScheduler.RetryDelayAfter(3));
        Assert.Equal(TimeSpan.FromMinutes(5), LocalRefreshScheduler.RetryDelayAfter(4));
        Assert.Equal(TimeSpan.FromMinutes(5), LocalRefreshScheduler.RetryDelayAfter(500));
    }

    [Fact]
    public async Task ReportedFailureRetriesOnItsOwnWithBackoffUntilItSucceeds()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var count = 0;
        var failing = true;
        using var scheduler = new LocalRefreshScheduler(signal, clock,
            _ => { Interlocked.Increment(ref count); return Task.CompletedTask; }, () => Volatile.Read(ref failing));
        await scheduler.ManualRefreshAsync();
        Assert.Equal(1, scheduler.ConsecutiveFailures);
        Assert.Equal("local_refresh_failed", scheduler.LastErrorCode);
        foreach (var seconds in new[] { 5, 15, 45, 300, 300 })
        {
            await clock.WaitForDelayAsync(TimeSpan.FromSeconds(seconds));
            clock.Advance(TimeSpan.FromSeconds(seconds));
        }
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(300));
        Assert.Equal(6, Volatile.Read(ref count));
        Assert.Equal(6, scheduler.ConsecutiveFailures);

        Volatile.Write(ref failing, false);
        clock.Advance(TimeSpan.FromSeconds(300));
        await WaitUntilAsync(() => Volatile.Read(ref count) == 7 && scheduler.ConsecutiveFailures == 0);
        await clock.WaitForNoDelaysAsync();
        Assert.Null(scheduler.LastErrorCode);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(7, Volatile.Read(ref count));
    }

    [Fact]
    public async Task ThrownIoFailureAlsoRetriesWithoutAnyOtherEvent()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var count = 0;
        using var scheduler = new LocalRefreshScheduler(signal, clock, _ =>
        {
            if (Interlocked.Increment(ref count) == 1) throw new IOException("7654321");
            return Task.CompletedTask;
        });
        await scheduler.ManualRefreshAsync();
        Assert.Equal("local_refresh_failed", scheduler.LastErrorCode);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => Volatile.Read(ref count) == 2 && scheduler.LastErrorCode == null);
        await clock.WaitForNoDelaysAsync();
        Assert.Equal(0, scheduler.ConsecutiveFailures);
    }

    [Fact]
    public async Task ChangeEventDoesNotWaitForTheRetryDelay()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var count = 0;
        using var scheduler = new LocalRefreshScheduler(signal, clock,
            _ => { Interlocked.Increment(ref count); return Task.CompletedTask; }, () => true);
        await scheduler.ManualRefreshAsync();
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, Volatile.Read(ref count));
        // The 5-second throttle still applies, but the 15-second retry delay does not.
        signal.Fire();
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(45));
        Assert.Equal(3, Volatile.Read(ref count));
    }

    [Fact]
    public async Task PausedSchedulerStopsRetrying()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var count = 0;
        using var scheduler = new LocalRefreshScheduler(signal, clock,
            _ => { Interlocked.Increment(ref count); return Task.CompletedTask; }, () => true);
        await scheduler.ManualRefreshAsync();
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(5));
        scheduler.SetPaused(true);
        await clock.WaitForNoDelaysAsync();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, Volatile.Read(ref count));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
            await Task.Delay(5);
        }
    }
}
