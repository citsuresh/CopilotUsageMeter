using System;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Core
{
    public sealed class AccountRefreshCoordinator : IDisposable
    {
        // Interval after the last successful read while local chat activity was seen in that window, and otherwise.
        public static readonly TimeSpan ActiveInterval = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(15);

        private readonly object gate = new object();
        private readonly IAccountReader reader;
        private readonly IAccountClock clock;
        private readonly SemaphoreSlim wake = new SemaphoreSlim(0, 1);
        private Task<AccountReadResult>? flight;
        private AccountReadResult current;
        private DateTimeOffset? lastSuccess, lastActivity, lastManual, retryAt, firstDue, lastPublishedNextSync;
        private int failures, generation, readSequence;
        private bool paused, disposed, running;

        public AccountRefreshCoordinator(IAccountReader reader, IAccountClock clock)
        {
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            current = reader.Current.Copy();
            firstDue = reader.IsAuthorized ? clock.UtcNow : (DateTimeOffset?)null;
            lastPublishedNextSync = NextDue();
            reader.AuthorizationChanged += OnAuthorizationChanged;
        }

        public AccountReadResult Current
        {
            get
            {
                lock (gate)
                {
                    var copy = current.Copy();
                    copy.Usage.NextRefreshUtc = NextDue();
                    return copy;
                }
            }
        }
        public DateTimeOffset? LastSuccessfulSyncUtc { get { lock (gate) return lastSuccess; } }
        public DateTimeOffset? NextSyncUtc { get { lock (gate) return NextDue(); } }
        public bool IsPaused { get { lock (gate) return paused; } }
        public bool IsRefreshing { get { lock (gate) return flight != null; } }
        // Counts completed reads, so a host can tell a new result from a republished one.
        public int ReadSequence { get { lock (gate) return readSequence; } }
        public event EventHandler? StateChanged;

        public void NotifyDatabaseActivity()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                // Latch already-due work before a changing activity window can move it later.
                var previous = NextDue();
                if (previous.HasValue && previous.Value <= clock.UtcNow && !retryAt.HasValue)
                    firstDue = previous;
                lastActivity = clock.UtcNow;
            }
            Signal();
            NotifyScheduleChanged();
        }

        public void SetPaused(bool value)
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (value && !paused)
                {
                    var due = NextDue();
                    if (due.HasValue && due.Value <= clock.UtcNow) firstDue = due;
                }
                paused = value;
            }
            Signal();
            PublishStateChanged();
        }

        public Task<AccountReadResult> RefreshAsync(bool manual = false, CancellationToken cancellationToken = default)
        {
            var task = StartRefresh(manual, cancellationToken, out var started);
            // Lets the host show that a read is in progress; the completion publishes again.
            if (started) StateChanged?.Invoke(this, EventArgs.Empty);
            return task;
        }

        private Task<AccountReadResult> StartRefresh(bool manual, CancellationToken cancellationToken, out bool started)
        {
            started = false;
            NotifyScheduleChanged();
            lock (gate)
            {
                ThrowIfDisposed();
                if (flight != null) return flight;
                if (!reader.IsAuthorized) return Task.FromResult(current.Copy());
                var now = clock.UtcNow;
                if (retryAt.HasValue && now < retryAt.Value) return Task.FromResult(current.Copy());
                if (manual)
                {
                    if (lastManual.HasValue && now - lastManual.Value < TimeSpan.FromSeconds(5))
                        return Task.FromResult(current.Copy());
                    lastManual = now;
                }
                else if (paused || !NextDue().HasValue || now < NextDue()!.Value)
                    return Task.FromResult(current.Copy());

                var completion = new TaskCompletionSource<AccountReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                flight = completion.Task;
                started = true;
                var epoch = generation;
                var operation = ReadAndPublishAsync(epoch, completion, cancellationToken);
                _ = operation.ContinueWith(finished =>
                {
                    if (!finished.IsFaulted && !finished.IsCanceled) return;
                    lock (gate) if (epoch == generation) flight = null;
                    if (finished.IsFaulted) completion.TrySetException(finished.Exception!.InnerExceptions);
                    else completion.TrySetCanceled();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return completion.Task;
            }
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (running) throw new InvalidOperationException("Account schedule is already running.");
                running = true;
            }
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RefreshAsync(false, cancellationToken).ConfigureAwait(false);
                    TimeSpan delay;
                    lock (gate)
                    {
                        var next = NextDue();
                        delay = paused || !next.HasValue ? TimeSpan.FromDays(1) : next.Value - clock.UtcNow;
                        if (delay < TimeSpan.FromMilliseconds(1)) delay = TimeSpan.FromMilliseconds(1);
                        // Re-evaluate the active window exactly when it expires, unless work is due.
                        if (!paused && lastActivity.HasValue && lastActivity.Value + ActiveInterval > clock.UtcNow)
                        {
                            var expiry = lastActivity.Value + ActiveInterval - clock.UtcNow;
                            if (expiry < delay) delay = expiry;
                        }
                    }
                    using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        var timer = clock.DelayAsync(delay, wait.Token);
                        var changed = wake.WaitAsync(wait.Token);
                        var finished = await Task.WhenAny(timer, changed).ConfigureAwait(false);
                        wait.Cancel();
                        try { await finished.ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                    }
                }
            }
            finally { lock (gate) running = false; }
        }

        private DateTimeOffset? NextDue()
        {
            if (!reader.IsAuthorized) return null;
            if (retryAt.HasValue) return retryAt;
            if (firstDue.HasValue) return firstDue;
            if (!lastSuccess.HasValue) return clock.UtcNow;
            var activeDue = lastSuccess.Value + ActiveInterval;
            // Once due while active, do not slide the due time forward as activity ages out.
            if (lastActivity.HasValue && lastActivity.Value + ActiveInterval >= activeDue && clock.UtcNow >= activeDue)
                return activeDue;
            var active = lastActivity.HasValue && clock.UtcNow - lastActivity.Value < ActiveInterval;
            return lastSuccess.Value + (active ? ActiveInterval : IdleInterval);
        }

        private async Task ReadAndPublishAsync(int epoch, TaskCompletionSource<AccountReadResult> completion, CancellationToken token)
        {
            AccountReadResult result;
            try { result = await reader.ReadAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                lock (gate) if (epoch == generation) flight = null;
                completion.TrySetCanceled();
                Signal();
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                // A programming fault still propagates to the caller; it is recorded first.
                Diagnostics.Error("account.refresh", "refresh_faulted", exception);
                throw;
            }
            AccountReadResult published;
            lock (gate)
            {
                if (disposed || epoch != generation)
                {
                    completion.TrySetResult(current.Copy());
                    return;
                }
                current = result.Copy();
                readSequence++;
                flight = null;
                firstDue = null;
                if (result.Status == "available")
                {
                    lastSuccess = clock.UtcNow;
                    failures = 0;
                    retryAt = null;
                }
                else if (reader.IsAuthorized)
                {
                    failures = Math.Min(failures + 1, 7);
                    retryAt = clock.UtcNow.AddSeconds(Math.Min(900, 15 * Math.Pow(2, failures - 1)));
                    if (result.RetryNotBeforeUtc.HasValue && result.RetryNotBeforeUtc > retryAt)
                        retryAt = result.RetryNotBeforeUtc;
                }
                published = current.Copy();
            }
            Signal();
            PublishStateChanged();
            completion.TrySetResult(published);
        }

        private void OnAuthorizationChanged(object? sender, EventArgs args)
        {
            lock (gate)
            {
                generation++;
                flight = null;
                current = reader.Current.Copy();
                lastSuccess = lastManual = retryAt = lastActivity = null;
                failures = 0;
                firstDue = reader.IsAuthorized ? clock.UtcNow : (DateTimeOffset?)null;
            }
            Signal();
            PublishStateChanged();
        }

        private void NotifyScheduleChanged()
        {
            lock (gate)
            {
                if (disposed) return;
                var next = NextDue();
                if (next == lastPublishedNextSync) return;
                lastPublishedNextSync = next;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void PublishStateChanged()
        {
            lock (gate) lastPublishedNextSync = NextDue();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Signal()
        {
            try { wake.Release(); }
            catch (SemaphoreFullException) { }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(AccountRefreshCoordinator));
        }

        public void Dispose()
        {
            lock (gate) { disposed = true; generation++; }
            reader.AuthorizationChanged -= OnAuthorizationChanged;
            Signal();
        }
    }
}