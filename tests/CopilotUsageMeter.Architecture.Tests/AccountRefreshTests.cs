using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class AccountRefreshTests
{
    [Fact]
    public async Task AuthorizedStartupIsDueAndIdleScheduleUsesLastSuccess()
    {
        var clock = new Clock();
        var reader = new Reader();
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        Assert.Equal(clock.UtcNow, scheduler.NextSyncUtc);
        await scheduler.RefreshAsync();
        Assert.Equal(1, reader.Calls);
        Assert.Equal(clock.UtcNow, scheduler.LastSuccessfulSyncUtc);
        Assert.Equal(clock.UtcNow.AddMinutes(15), scheduler.NextSyncUtc);
        clock.Advance(TimeSpan.FromMinutes(14));
        await scheduler.RefreshAsync();
        Assert.Equal(1, reader.Calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.RefreshAsync();
        Assert.Equal(2, reader.Calls);
    }

    [Fact]
    public async Task DatabaseActivityPublishesEarlierNextRefreshWithoutAnAccountRead()
    {
        var clock = new Clock();
        var reader = new Reader();
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        await scheduler.RefreshAsync();
        var success = clock.UtcNow;
        Assert.Equal(success.AddMinutes(15), scheduler.Current.Usage.NextRefreshUtc);
        var published = new List<DateTimeOffset?>();
        scheduler.StateChanged += (_, _) => published.Add(scheduler.Current.Usage.NextRefreshUtc);

        clock.Advance(TimeSpan.FromMinutes(1));
        scheduler.NotifyDatabaseActivity();
        scheduler.NotifyDatabaseActivity();

        Assert.Equal(new DateTimeOffset?[] { success.AddMinutes(5) }, published);
        Assert.Equal(success.AddMinutes(5), scheduler.NextSyncUtc);
        Assert.Equal(success.AddMinutes(5), scheduler.Current.Usage.NextRefreshUtc);
        Assert.Equal(1, reader.Calls);
        Assert.Equal(success, scheduler.LastSuccessfulSyncUtc);
    }

    [Fact]
    public async Task ActiveExpiryPublishesPostponedNextRefreshWithoutAnAccountRead()
    {
        var clock = new AdvancingClock();
        var reader = new Reader();
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        scheduler.NotifyDatabaseActivity();
        clock.Advance(TimeSpan.FromMinutes(4));
        await scheduler.RefreshAsync();
        var success = clock.UtcNow;
        Assert.Equal(success.AddMinutes(5), scheduler.Current.Usage.NextRefreshUtc);

        var changed = new TaskCompletionSource<DateTimeOffset?>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.StateChanged += (_, _) => changed.TrySetResult(scheduler.Current.Usage.NextRefreshUtc);
        using var cancellation = new CancellationTokenSource();
        var run = scheduler.RunAsync(cancellation.Token);
        try
        {
            await clock.WaitForDelayAsync(TimeSpan.FromMinutes(1)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(changed.Task.IsCompleted);
            clock.Advance(TimeSpan.FromMinutes(1));

            Assert.Equal(success.AddMinutes(15), await changed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            await clock.WaitForDelayAsync(TimeSpan.FromMinutes(14)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(success.AddMinutes(15), scheduler.NextSyncUtc);
            Assert.Equal(success.AddMinutes(15), scheduler.Current.Usage.NextRefreshUtc);
            Assert.Equal(1, reader.Calls);
            Assert.Equal(success, scheduler.LastSuccessfulSyncUtc);
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }
    }

    [Fact]
    public async Task RepeatedActivityNeverSlidesFiveMinuteDueWork()
    {
        var clock = new Clock();
        var reader = new Reader();
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        await scheduler.RefreshAsync();
        var first = clock.UtcNow;
        for (var index = 0; index < 6; index++)
        {
            scheduler.NotifyDatabaseActivity();
            Assert.Equal(first.AddMinutes(5), scheduler.NextSyncUtc);
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        await scheduler.RefreshAsync();
        Assert.Equal(2, reader.Calls);
    }

    [Fact]
    public async Task ActivityOutsidePrecedingFiveMinutesDoesNotCausePrematureRefresh()
    {
        var clock = new Clock();
        var reader = new Reader();
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        scheduler.NotifyDatabaseActivity();
        clock.Advance(TimeSpan.FromMinutes(4));
        await scheduler.RefreshAsync();
        var success = clock.UtcNow;
        Assert.Equal(success.AddMinutes(5), scheduler.NextSyncUtc);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(success.AddMinutes(15), scheduler.NextSyncUtc);
        await scheduler.RefreshAsync();
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task DueWhileActiveRemainsDueWhenActivityExpires()
    {
        var clock = new Clock();
        using var scheduler = new AccountRefreshCoordinator(new Reader(), clock);
        await scheduler.RefreshAsync();
        scheduler.NotifyDatabaseActivity();
        var due = clock.UtcNow.AddMinutes(5);
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(due, scheduler.NextSyncUtc);
    }

    [Fact]
    public async Task ManualWorksDuringPauseWithFiveSecondCooldownAndResumeCatchup()
    {
        var clock = new Clock();
        var reader = new Reader();
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        scheduler.SetPaused(true);
        await scheduler.RefreshAsync();
        Assert.Equal(0, reader.Calls);
        await scheduler.RefreshAsync(true);
        await scheduler.RefreshAsync(true);
        Assert.Equal(1, reader.Calls);
        clock.Advance(TimeSpan.FromSeconds(5));
        await scheduler.RefreshAsync(true);
        Assert.Equal(2, reader.Calls);
        clock.Advance(TimeSpan.FromMinutes(16));
        await scheduler.RefreshAsync();
        Assert.Equal(2, reader.Calls);
        scheduler.SetPaused(false);
        await scheduler.RefreshAsync();
        Assert.Equal(3, reader.Calls);
    }

    [Fact]
    public async Task ManualAndAutomaticRefreshShareSingleFlight()
    {
        var clock = new Clock();
        var pending = new TaskCompletionSource<AccountReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader { Pending = pending.Task };
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        var first = scheduler.RefreshAsync();
        Assert.Same(first, scheduler.RefreshAsync(true));
        Assert.True(scheduler.IsRefreshing);
        Assert.Equal(1, reader.Calls);
        pending.SetResult(new AccountReadResult { Status = "available" });
        await first;
        Assert.False(scheduler.IsRefreshing);
    }

    [Fact]
    public async Task RetryIsExponentialBoundedAndManualCannotBypassServerCooldown()
    {
        var clock = new Clock();
        var reader = new Reader { Result = AccountReadResult.Empty("error") };
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        foreach (var seconds in new[] { 15, 30, 60, 120, 240, 480, 900, 900 })
        {
            await scheduler.RefreshAsync();
            Assert.Equal(clock.UtcNow.AddSeconds(seconds), scheduler.NextSyncUtc);
            clock.Advance(TimeSpan.FromSeconds(seconds));
        }
        reader.Result = AccountReadResult.Empty("rate-limited");
        reader.Result.RetryNotBeforeUtc = clock.UtcNow.AddHours(1);
        await scheduler.RefreshAsync();
        Assert.Equal(clock.UtcNow.AddHours(1), scheduler.NextSyncUtc);
        var calls = reader.Calls;
        await scheduler.RefreshAsync(true);
        Assert.Equal(calls, reader.Calls);
        clock.Advance(TimeSpan.FromHours(1));
        reader.Result = new AccountReadResult { Status = "available" };
        await scheduler.RefreshAsync();
        Assert.Equal(clock.UtcNow, scheduler.LastSuccessfulSyncUtc);
        Assert.Equal(clock.UtcNow.AddMinutes(15), scheduler.NextSyncUtc);
    }

    [Fact]
    public async Task LogoutImmediatelyClearsSchedulerCacheAndOldReadCannotPublish()
    {
        var clock = new Clock();
        var reader = new Reader();
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        await scheduler.RefreshAsync();
        var pending = new TaskCompletionSource<AccountReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = pending.Task;
        var old = scheduler.RefreshAsync(true);
        reader.SignOut();
        Assert.Equal("signed-out", scheduler.Current.Status);
        Assert.Null(scheduler.LastSuccessfulSyncUtc);
        Assert.Null(scheduler.NextSyncUtc);
        pending.SetResult(new AccountReadResult { Status = "available", Login = "account-001" });
        await old;
        Assert.Equal("signed-out", scheduler.Current.Status);
        Assert.Null(scheduler.Current.Login);
    }

    [Fact]
    public async Task ProgrammingFaultPropagatesWithoutInventedErrorResultOrHungSingleFlight()
    {
        var reader = new Reader { Fault = new InvalidOperationException("invented-reader-fault") };
        using var scheduler = new AccountRefreshCoordinator(reader, new Clock());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.RefreshAsync());
        Assert.Same(reader.Fault, failure);
        Assert.False(scheduler.IsRefreshing);
        Assert.Null(scheduler.LastSuccessfulSyncUtc);
        reader.Fault = null;
        Assert.Equal("available", (await scheduler.RefreshAsync()).Status);
        Assert.Equal(2, reader.Calls);
    }

    [Fact]
    public async Task RunLoopUsesInjectedDelayAndCancelsWithoutRealTimers()
    {
        var clock = new Clock();
        var reader = new Reader();
        using var scheduler = new AccountRefreshCoordinator(reader, clock);
        using var cancellation = new CancellationTokenSource();
        var run = scheduler.RunAsync(cancellation.Token);
        await clock.DelayEntered.Task;
        Assert.Equal(1, reader.Calls);
        Assert.Equal(TimeSpan.FromMinutes(15), clock.LastDelay);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private sealed class AdvancingClock : IAccountClock
    {
        private readonly object gate = new();
        private readonly List<(DateTimeOffset Due, TaskCompletionSource<bool> Completion)> timers = new();
        private readonly Queue<TimeSpan> delays = new();
        private readonly SemaphoreSlim delayEntered = new(0);
        private DateTimeOffset now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public DateTimeOffset UtcNow { get { lock (gate) return now; } }

        public void Advance(TimeSpan value)
        {
            List<TaskCompletionSource<bool>> elapsed;
            lock (gate)
            {
                now += value;
                elapsed = timers.Where(timer => timer.Due <= now).Select(timer => timer.Completion).ToList();
                timers.RemoveAll(timer => timer.Due <= now || timer.Completion.Task.IsCompleted);
            }
            foreach (var timer in elapsed) timer.TrySetResult(true);
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                timers.Add((now + delay, completion));
                delays.Enqueue(delay);
            }
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            delayEntered.Release();
            return completion.Task;
        }

        public async Task WaitForDelayAsync(TimeSpan expected)
        {
            while (true)
            {
                await delayEntered.WaitAsync();
                lock (gate) if (delays.Dequeue() == expected) return;
            }
        }
    }

    private sealed class Clock : IAccountClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public readonly TaskCompletionSource<bool> DelayEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan LastDelay;
        public void Advance(TimeSpan value) => UtcNow += value;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            LastDelay = delay;
            DelayEntered.TrySetResult(true);
            return Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
    private sealed class Reader : IAccountReader
    {
        public bool IsAuthorized { get; private set; } = true;
        public AccountReadResult Result = new() { Status = "available" };
        public Task<AccountReadResult>? Pending;
        public Exception? Fault;
        public AccountReadResult Current => IsAuthorized ? Result.Copy() : AccountReadResult.Empty("signed-out");
        public event EventHandler? AuthorizationChanged;
        public int Calls;
        public Task<AccountReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Fault != null) throw Fault;
            return Pending ?? Task.FromResult(Result.Copy());
        }
        public void SignOut() { IsAuthorized = false; AuthorizationChanged?.Invoke(this, EventArgs.Empty); }
    }
}