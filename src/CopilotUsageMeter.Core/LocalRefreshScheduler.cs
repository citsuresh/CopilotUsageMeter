using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotUsageMeter.Core
{
    public sealed class LocalRefreshScheduler : IDisposable
    {
        public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(1500);
        public static readonly TimeSpan Throttle = TimeSpan.FromSeconds(5);
        // Automatic retries after a failed refresh: 5 s, 15 s, 45 s, then every 5 minutes until one succeeds.
        public static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45) };
        public static readonly TimeSpan LongRetryDelay = TimeSpan.FromMinutes(5);
        // A failure is shown to the user only once this many consecutive attempts have failed.
        public const int NotifyAfterFailures = 3;
        private readonly object gate = new object();
        private readonly IChangeSignal signal;
        private readonly IClock clock;
        private readonly Func<CancellationToken, Task> refresh;
        private readonly Func<bool>? attemptFailed;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource? delayCancellation;
        private TaskCompletionSource<bool>? manual;
        private TaskCompletionSource<bool>? inFlight;
        private Task worker = Task.CompletedTask;
        private DateTimeOffset? lastStart;
        private DateTimeOffset lastEvent;
        private DateTimeOffset? retryAt;
        private int consecutiveFailures;
        private bool pending;
        private bool paused;
        private bool running;
        private bool disposed;

        // attemptFailed lets a refresh that reports failure through its result (instead of throwing) trigger the retry schedule.
        public LocalRefreshScheduler(IChangeSignal signal, IClock clock, Func<CancellationToken, Task> refresh, Func<bool>? attemptFailed = null)
        {
            this.signal = signal ?? throw new ArgumentNullException(nameof(signal));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            this.attemptFailed = attemptFailed;
            signal.Changed += OnChanged;
        }

        public string? LastErrorCode { get; private set; }
        public int ConsecutiveFailures { get { lock (gate) return consecutiveFailures; } }

        public static TimeSpan RetryDelayAfter(int failures) =>
            failures >= 1 && failures <= RetryDelays.Length ? RetryDelays[failures - 1] : LongRetryDelay;
        public bool IsPaused { get { lock (gate) return paused; } }

        public Task ManualRefreshAsync(CancellationToken cancellationToken = default)
        {
            Task shared;
            lock (gate)
            {
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                if (inFlight != null) shared = inFlight.Task;
                else
                {
                    if (manual == null) manual = Completion();
                    shared = manual.Task;
                    WakeLocked();
                }
            }
            return WaitForCallerAsync(shared, cancellationToken);
        }

        public void SetPaused(bool value)
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (paused == value) return;
                paused = value;
                if (!paused)
                {
                    pending = true;
                    lastEvent = clock.UtcNow - Debounce;
                }
                WakeLocked();
            }
        }

        public async Task StopAsync()
        {
            Task drain;
            lock (gate)
            {
                if (!disposed)
                {
                    disposed = true;
                    signal.Changed -= OnChanged;
                    lifetime.Cancel();
                    delayCancellation?.Cancel();
                    manual?.TrySetCanceled();
                    manual = null;
                }
                drain = worker;
            }
            await drain.ConfigureAwait(false);
        }

        // The composition root owns the signal; stopping the scheduler only unsubscribes it.
        public void Dispose() => StopAsync().GetAwaiter().GetResult();

        private void OnChanged(object? sender, EventArgs args)
        {
            lock (gate)
            {
                if (disposed) return;
                pending = true;
                lastEvent = clock.UtcNow;
                WakeLocked();
            }
        }

        private void WakeLocked()
        {
            delayCancellation?.Cancel();
            if (running) return;
            running = true;
            worker = Task.Run(RunAsync);
            _ = worker.ContinueWith(OnWorkerFaulted, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void OnWorkerFaulted(Task failed)
        {
            TaskCompletionSource<bool>? active;
            TaskCompletionSource<bool>? queued;
            lock (gate)
            {
                if (!ReferenceEquals(worker, failed)) return;
                LastErrorCode = "local_refresh_faulted";
                active = inFlight;
                queued = manual;
                inFlight = null;
                manual = null;
                pending = false;
                running = false;
            }
            active?.TrySetException(failed.Exception!.InnerExceptions);
            queued?.TrySetException(failed.Exception!.InnerExceptions);
        }

        private async Task RunAsync()
        {
            while (true)
            {
                TimeSpan wait;
                CancellationTokenSource? delay = null;
                TaskCompletionSource<bool>? completion = null;
                lock (gate)
                {
                    if (disposed || (manual == null && (paused || (!pending && !retryAt.HasValue))))
                    {
                        running = false;
                        return;
                    }
                    var now = clock.UtcNow;
                    // A new change event or a manual refresh does not wait for the retry delay: the change may be the fix.
                    var due = manual != null ? now : pending ? lastEvent + Debounce : retryAt!.Value;
                    if (lastStart.HasValue && due < lastStart.Value + Throttle) due = lastStart.Value + Throttle;
                    wait = due - now;
                    if (wait > TimeSpan.Zero)
                    {
                        delay = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        delayCancellation = delay;
                    }
                    else
                    {
                        completion = manual ?? Completion();
                        manual = null;
                        inFlight = completion;
                        pending = false;
                        retryAt = null;
                        lastStart = now;
                    }
                }
                if (delay != null)
                {
                    try { await clock.Delay(wait, delay.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (delay.IsCancellationRequested) { }
                    finally
                    {
                        lock (gate) { if (ReferenceEquals(delayCancellation, delay)) delayCancellation = null; }
                        delay.Dispose();
                    }
                    continue;
                }

                var canceled = false;
                var failed = false;
                try
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    await refresh(lifetime.Token).ConfigureAwait(false);
                    failed = attemptFailed?.Invoke() == true;
                    LastErrorCode = failed ? "local_refresh_failed" : null;
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { canceled = true; }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is TimeoutException)
                {
                    failed = true;
                    LastErrorCode = "local_refresh_failed";
                }
                lock (gate)
                {
                    inFlight = null;
                    if (!canceled)
                    {
                        if (failed)
                        {
                            consecutiveFailures = Math.Min(consecutiveFailures + 1, 1000);
                            retryAt = clock.UtcNow + RetryDelayAfter(consecutiveFailures);
                        }
                        else
                        {
                            consecutiveFailures = 0;
                            retryAt = null;
                        }
                    }
                }
                if (canceled) completion!.TrySetCanceled();
                else completion!.TrySetResult(LastErrorCode == null);
            }
        }

        private static async Task WaitForCallerAsync(Task shared, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) { await shared.ConfigureAwait(false); return; }
            var cancelled = Completion();
            using (cancellationToken.Register(() => cancelled.TrySetCanceled()))
            {
                var finished = await Task.WhenAny(shared, cancelled.Task).ConfigureAwait(false);
                await finished.ConfigureAwait(false);
            }
        }

        private static TaskCompletionSource<bool> Completion() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(LocalRefreshScheduler)); }
    }
}