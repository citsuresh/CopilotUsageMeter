using System.Threading.Channels;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class LocalRefreshSchedulerTests
{
    [Fact]
    public async Task DebounceIsMeasuredFromLastEventAtExactThreshold()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe();
        using var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        signal.Fire();
        await clock.WaitForDelayAsync(TimeSpan.FromMilliseconds(1500));
        clock.Advance(TimeSpan.FromMilliseconds(1000));
        signal.Fire();
        await clock.WaitForDelayAsync(TimeSpan.FromMilliseconds(1500));
        clock.Advance(TimeSpan.FromMilliseconds(1499));
        Assert.Equal(0, probe.Count);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await probe.Started(1);
        Assert.Equal(1, probe.Count);
    }

    [Fact]
    public async Task ThrottleIsFiveSecondsBetweenStartsNotCompletions()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe { Hold = true };
        using var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        var first = scheduler.ManualRefreshAsync();
        await probe.Started(1);
        clock.Advance(TimeSpan.FromSeconds(3));
        probe.Release();
        await first;
        var second = scheduler.ManualRefreshAsync();
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(1, probe.Count);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await probe.Started(2);
        probe.Release();
        await second;
        Assert.Equal(1, probe.MaxActive);
    }

    [Fact]
    public async Task ManualAndAutomaticRefreshesShareSingleFlightAndTrailingEventIsNotLost()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe { Hold = true };
        using var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        var first = scheduler.ManualRefreshAsync();
        await probe.Started(1);
        var joined = scheduler.ManualRefreshAsync();
        for (var index = 0; index < 100; index++) signal.Fire();
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, probe.Count);
        probe.Release();
        await Task.WhenAll(first, joined);
        await probe.Started(2);
        Assert.Equal(2, probe.Count);
        Assert.Equal(1, probe.MaxActive);
        probe.Release();
    }

    [Fact]
    public async Task EventDuringInflightUsesLastEventDebounceEvenAfterThrottleExpired()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe { Hold = true };
        using var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        var manual = scheduler.ManualRefreshAsync();
        await probe.Started(1);
        clock.Advance(TimeSpan.FromSeconds(10));
        signal.Fire();
        probe.Release();
        await manual;
        await clock.WaitForDelayAsync(TimeSpan.FromMilliseconds(1500));
        clock.Advance(TimeSpan.FromMilliseconds(1499));
        Assert.Equal(1, probe.Count);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await probe.Started(2);
        probe.Release();
    }

    [Fact]
    public async Task QueuedManualRequestsCoalesceAndBypassDebounceButNotThrottle()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe();
        using var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        await scheduler.ManualRefreshAsync();
        await probe.Started(1);
        signal.Fire();
        var queued = Enumerable.Range(0, 20).Select(_ => scheduler.ManualRefreshAsync()).ToArray();
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.WhenAll(queued);
        await probe.Started(2);
        Assert.Equal(2, probe.Count);
    }

    [Fact]
    public async Task PauseCancelsPendingAutomaticDelayManualWorksAndResumeCatchesUpOnce()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe();
        using var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        signal.Fire();
        await clock.WaitForDelayAsync(TimeSpan.FromMilliseconds(1500));
        scheduler.SetPaused(true);
        await clock.WaitForNoDelaysAsync();
        for (var index = 0; index < 100; index++) signal.Fire();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, probe.Count);
        await scheduler.ManualRefreshAsync();
        await probe.Started(1);
        Assert.True(scheduler.IsPaused);
        scheduler.SetPaused(false);
        scheduler.SetPaused(false);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await probe.Started(2);
        await scheduler.StopAsync();
        Assert.Equal(2, probe.Count);
        Assert.False(signal.Disposed);
    }

    [Fact]
    public async Task PauseDoesNotStartTrailingAutomaticWorkUntilResume()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe { Hold = true };
        using var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        var first = scheduler.ManualRefreshAsync();
        await probe.Started(1);
        signal.Fire();
        scheduler.SetPaused(true);
        probe.Release();
        await first;
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, probe.Count);
        scheduler.SetPaused(false);
        await probe.Started(2);
        probe.Release();
    }

    [Fact]
    public async Task CallerCancellationDoesNotCancelSharedRefresh()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe { Hold = true };
        using var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        using var caller = new CancellationTokenSource();
        var canceled = scheduler.ManualRefreshAsync(caller.Token);
        await probe.Started(1);
        var retained = scheduler.ManualRefreshAsync();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(retained.IsCompleted);
        probe.Release();
        await retained;
        Assert.Equal(1, probe.Count);
    }

    [Fact]
    public async Task StopCancelsAndDrainsInflightRefreshAndRejectsFutureWork()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe { Hold = true };
        var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        var current = scheduler.ManualRefreshAsync();
        await probe.Started(1);
        await scheduler.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => current);
        signal.Fire();
        Assert.Equal(1, probe.Count);
        Assert.Equal(0, probe.Active);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => scheduler.ManualRefreshAsync());
        Assert.Throws<ObjectDisposedException>(() => scheduler.SetPaused(false));
        await scheduler.StopAsync();
        scheduler.Dispose();
    }

    [Fact]
    public async Task StopCancelsThrottleWaitAndPendingManualCalls()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var probe = new Probe();
        using var scheduler = new LocalRefreshScheduler(signal, clock, probe.Refresh);
        await scheduler.ManualRefreshAsync();
        var pending = scheduler.ManualRefreshAsync();
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(5));
        await scheduler.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await clock.WaitForNoDelaysAsync();
        Assert.Equal(1, probe.Count);
    }

    [Fact]
    public async Task FailedRefreshIsSanitizedAndNextManualCanRecover()
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
        var retry = scheduler.ManualRefreshAsync();
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await retry;
        Assert.Null(scheduler.LastErrorCode);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task UnexpectedRefreshBugFaultsEveryJoinedCallerAndAllowsFreshWork()
    {
        var clock = new LocalFakeClock();
        using var signal = new LocalFakeSignal();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var bug = new InvalidOperationException("7654321");
        var scheduler = new LocalRefreshScheduler(signal, clock, async _ =>
        {
            if (Interlocked.Increment(ref count) != 1) return;
            entered.TrySetResult(true);
            await release.Task;
            throw bug;
        });
        var first = scheduler.ManualRefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var joined = scheduler.ManualRefreshAsync();
        release.TrySetResult(true);
        Assert.Same(bug, await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(TimeSpan.FromSeconds(10))));
        Assert.Same(bug, await Assert.ThrowsAsync<InvalidOperationException>(() => joined.WaitAsync(TimeSpan.FromSeconds(10))));
        Assert.Equal("local_refresh_faulted", scheduler.LastErrorCode);
        var retry = scheduler.ManualRefreshAsync();
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await retry.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(scheduler.LastErrorCode);
        Assert.Equal(2, count);
        await scheduler.StopAsync();
        scheduler.Dispose();
    }

    [Fact]
    public async Task ClockProgrammingBugFaultsQueuedManualInsteadOfLeavingOrphanTask()
    {
        var clock = new FaultClock();
        using var signal = new LocalFakeSignal();
        var scheduler = new LocalRefreshScheduler(signal, clock, _ => Task.CompletedTask);
        var pending = scheduler.ManualRefreshAsync();
        Assert.Same(clock.Fault, await Assert.ThrowsAsync<NullReferenceException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10))));
        Assert.Equal("local_refresh_faulted", scheduler.LastErrorCode);
        Assert.Same(clock.Fault, await Assert.ThrowsAsync<NullReferenceException>(() => scheduler.StopAsync()));
    }

    [Fact]
    public async Task DelayProgrammingBugFaultsThrottledManualAndDoesNotHideDuringShutdown()
    {
        var clock = new FaultDelayClock();
        using var signal = new LocalFakeSignal();
        var scheduler = new LocalRefreshScheduler(signal, clock, _ => Task.CompletedTask);
        await scheduler.ManualRefreshAsync();
        var throttled = scheduler.ManualRefreshAsync();
        Assert.Same(clock.Fault, await Assert.ThrowsAsync<ArgumentException>(() => throttled.WaitAsync(TimeSpan.FromSeconds(10))));
        Assert.Equal("local_refresh_faulted", scheduler.LastErrorCode);
        Assert.Same(clock.Fault, await Assert.ThrowsAsync<ArgumentException>(() => scheduler.StopAsync()));
    }

    private sealed class FaultDelayClock : IClock
    {
        public ArgumentException Fault { get; } = new("7654321");
        public DateTimeOffset UtcNow => new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
        public Task Delay(TimeSpan delay, CancellationToken token) => throw Fault;
    }

    private sealed class FaultClock : IClock
    {
        public NullReferenceException Fault { get; } = new("7654321");
        public DateTimeOffset UtcNow => throw Fault;
        public Task Delay(TimeSpan delay, CancellationToken token) => throw Fault;
    }

    private sealed class Probe
    {
        private readonly Channel<int> starts = Channel.CreateUnbounded<int>();
        private readonly Queue<TaskCompletionSource<bool>> releases = new();
        private int count;
        private int active;
        public int Count => Volatile.Read(ref count);
        public int Active => Volatile.Read(ref active);
        public int MaxActive { get; private set; }
        public bool Hold { get; set; }

        public async Task Refresh(CancellationToken token)
        {
            TaskCompletionSource<bool>? completion = null;
            lock (releases)
            {
                MaxActive = Math.Max(MaxActive, Interlocked.Increment(ref active));
                if (Hold) { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); releases.Enqueue(completion); }
            }
            starts.Writer.TryWrite(Interlocked.Increment(ref count));
            try { if (completion != null) await completion.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref active); }
        }

        public async Task Started(int expected) => Assert.Equal(expected, await starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        public void Release() { lock (releases) releases.Dequeue().TrySetResult(true); }
    }
}

internal sealed class LocalFakeSignal : IChangeSignal
{
    public event EventHandler? Changed;
    public bool Disposed { get; private set; }
    public void Fire() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() { Disposed = true; Changed = null; }
}

internal sealed class LocalFakeClock : IClock
{
    private readonly object gate = new();
    private readonly List<DelayEntry> delays = [];
    private TaskCompletionSource<bool> changed = NewCompletion();
    private DateTimeOffset now;

    public LocalFakeClock(DateTimeOffset? now = null) => this.now = now ?? new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset UtcNow { get { lock (gate) return now; } }

    public Task Delay(TimeSpan delay, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DelayEntry entry;
        lock (gate)
        {
            entry = new DelayEntry(now + delay);
            delays.Add(entry);
            PulseLocked();
        }
        var registration = token.Register(() =>
        {
            lock (gate) { delays.Remove(entry); PulseLocked(); }
            entry.Completion.TrySetCanceled(token);
        });
        return Drain(entry.Completion.Task, registration);
    }

    public void Advance(TimeSpan by)
    {
        DelayEntry[] ready;
        lock (gate)
        {
            now += by;
            ready = delays.Where(delay => delay.Due <= now).ToArray();
            foreach (var delay in ready) delays.Remove(delay);
            PulseLocked();
        }
        foreach (var delay in ready) delay.Completion.TrySetResult(true);
    }

    public Task WaitForDelayAsync(TimeSpan delay) => WaitForAsync(() => delays.Any(entry => entry.Due == now + delay));
    public Task WaitForNoDelaysAsync() => WaitForAsync(() => delays.Count == 0);

    private async Task WaitForAsync(Func<bool> predicate)
    {
        while (true)
        {
            Task wait;
            lock (gate) { if (predicate()) return; wait = changed.Task; }
            await wait.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private void PulseLocked() { var previous = changed; changed = NewCompletion(); previous.TrySetResult(true); }
    private static async Task Drain(Task task, CancellationTokenRegistration registration) { using (registration) await task; }
    private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed record DelayEntry(DateTimeOffset Due)
    {
        public TaskCompletionSource<bool> Completion { get; } = NewCompletion();
    }
}