using CopilotUsageMeter.Signals.FileWatcher;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class LocalStoreChangeSignalTests
{
    [Fact]
    public async Task AbsentDirectoryCreationIsDetectedByMetadataAndRebindsWatcher()
    {
        var clock = new LocalFakeClock();
        var backend = new Backend();
        using var signal = new LocalStoreChangeSignal(AppContext.BaseDirectory, clock, backend);
        var changes = 0;
        signal.Changed += (_, _) => Interlocked.Increment(ref changes);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        backend.Metadata = new LocalStoreMetadata { DirectoryExists = true, DatabaseExists = true, DatabaseLength = 100 };
        clock.Advance(TimeSpan.FromSeconds(60));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(1, changes);
        Assert.Equal(2, backend.BindCount);
        Assert.Equal(1, backend.DisposedWatches);
        Assert.Equal(2, backend.MetadataReads);
    }

    [Fact]
    public async Task UnchangedMetadataDoesNotCausePeriodicCliRefreshesButWalGrowthDoes()
    {
        var clock = new LocalFakeClock();
        var backend = new Backend { Metadata = new LocalStoreMetadata { DirectoryExists = true, WalExists = true, WalLength = 100 } };
        using var signal = new LocalStoreChangeSignal(AppContext.BaseDirectory, clock, backend);
        var changes = 0;
        signal.Changed += (_, _) => Interlocked.Increment(ref changes);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        clock.Advance(TimeSpan.FromSeconds(60));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(0, changes);
        backend.Metadata = new LocalStoreMetadata { DirectoryExists = true, WalExists = true, WalLength = 101 };
        clock.Advance(TimeSpan.FromSeconds(60));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(1, changes);
        Assert.Equal(1, backend.BindCount);
    }

    [Fact]
    public async Task RootDeletionAndRecreationRebindWatchingAndRestoreAvailableState()
    {
        var clock = new LocalFakeClock();
        var backend = new Backend { Metadata = new LocalStoreMetadata { DirectoryExists = true, DatabaseExists = true } };
        using var signal = new LocalStoreChangeSignal(AppContext.BaseDirectory, clock, backend);
        Assert.Null(signal.LastErrorCode);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        backend.Metadata = default;
        clock.Advance(TimeSpan.FromSeconds(60));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        Assert.Equal("history_watch_directory_unavailable", signal.LastErrorCode);
        Assert.Equal(2, backend.BindCount);
        backend.Metadata = new LocalStoreMetadata { DirectoryExists = true, DatabaseExists = true };
        clock.Advance(TimeSpan.FromSeconds(60));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        Assert.Null(signal.LastErrorCode);
        Assert.Equal(3, backend.BindCount);
        Assert.Equal(2, backend.DisposedWatches);
    }

    [Fact]
    public async Task ErrorsSignalImmediateRefreshAndRecoverWatcherAtReconciliation()
    {
        var clock = new LocalFakeClock();
        var backend = new Backend();
        using var signal = new LocalStoreChangeSignal(AppContext.BaseDirectory, clock, backend);
        var changes = 0;
        signal.Changed += (_, _) => Interlocked.Increment(ref changes);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        backend.Fire(true);
        Assert.Equal(1, changes);
        Assert.Equal("history_watch_error", signal.LastErrorCode);
        clock.Advance(TimeSpan.FromSeconds(60));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(2, backend.BindCount);
        Assert.Equal(2, changes);
        Assert.Equal("history_watch_directory_unavailable", signal.LastErrorCode);
    }

    [Fact]
    public async Task WatchAndMetadataFailuresAreRetriedWithoutLeakingExceptions()
    {
        var clock = new LocalFakeClock();
        var backend = new Backend { FailBind = true, FailRead = true };
        using var signal = new LocalStoreChangeSignal(AppContext.BaseDirectory, clock, backend);
        Assert.Equal("history_watch_unavailable", signal.LastErrorCode);
        var changes = 0;
        signal.Changed += (_, _) => Interlocked.Increment(ref changes);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        clock.Advance(TimeSpan.FromSeconds(60));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(1, changes);
        Assert.Equal("history_watch_metadata_failed", signal.LastErrorCode);
        backend.FailBind = false;
        backend.FailRead = false;
        clock.Advance(TimeSpan.FromSeconds(60));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        backend.Fire(false);
        Assert.Equal(3, changes);
        Assert.Equal(2, backend.BindCount);
        Assert.Equal("history_watch_directory_unavailable", signal.LastErrorCode);
    }

    [Fact]
    public void ProgrammingFaultDuringInitialMetadataReadOrWatchBindingPropagates()
    {
        var metadataFault = new InvalidOperationException("7654321");
        var backend = new Backend { ReadException = metadataFault };
        Assert.Same(metadataFault, Assert.Throws<InvalidOperationException>(() => new LocalStoreChangeSignal(AppContext.BaseDirectory, new LocalFakeClock(), backend)));
        var watchFault = new NullReferenceException("7654321");
        backend = new Backend { BindException = watchFault };
        Assert.Same(watchFault, Assert.Throws<NullReferenceException>(() => new LocalStoreChangeSignal(AppContext.BaseDirectory, new LocalFakeClock(), backend)));
    }

    [Fact]
    public async Task ReconciliationProgrammingFaultSurfacesDuringShutdownAndReleasesWatcher()
    {
        var clock = new LocalFakeClock();
        var backend = new Backend { Metadata = new LocalStoreMetadata { DirectoryExists = true } };
        var signal = new LocalStoreChangeSignal(AppContext.BaseDirectory, clock, backend);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        var fault = new InvalidOperationException("7654321");
        backend.ReadException = fault;
        clock.Advance(TimeSpan.FromSeconds(60));
        await backend.ReadFailed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var surfaced = await Assert.ThrowsAsync<InvalidOperationException>(() => signal.StopAsync());
        Assert.Same(fault, surfaced);
        Assert.Equal(1, backend.DisposedWatches);
        await clock.WaitForNoDelaysAsync();
    }

    [Fact]
    public async Task SuccessfulRecoveryClearsFailureStateAndNotifiesHost()
    {
        var clock = new LocalFakeClock();
        var backend = new Backend { Metadata = new LocalStoreMetadata { DirectoryExists = true }, FailBind = true };
        using var signal = new LocalStoreChangeSignal(AppContext.BaseDirectory, clock, backend);
        Assert.Equal("history_watch_unavailable", signal.LastErrorCode);
        var changes = 0;
        signal.Changed += (_, _) => Interlocked.Increment(ref changes);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        backend.FailBind = false;
        clock.Advance(TimeSpan.FromSeconds(60));
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        Assert.Null(signal.LastErrorCode);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void NativeBackendWatchesExistingRootNonrecursivelyWithNarrowFilter()
    {
        using var handle = new LocalStoreWatchBackend().Watch(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), _ => { });
        var watcher = Assert.IsType<FileSystemWatcher>(handle);
        Assert.Equal(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), watcher.Path);
        Assert.False(watcher.IncludeSubdirectories);
        Assert.Equal("session-store.db*", watcher.Filter);
    }

    [Fact]
    public void NativeBackendUsesAncestorRecursionOnlyWhileStoreRootIsAbsent()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "absent-" + Guid.NewGuid().ToString("N"));
        using var handle = new LocalStoreWatchBackend().Watch(root, _ => { });
        var watcher = Assert.IsType<FileSystemWatcher>(handle);
        Assert.Equal(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), watcher.Path);
        Assert.True(watcher.IncludeSubdirectories);
        Assert.Equal("*", watcher.Filter);
    }

    [Fact]
    public async Task StopReleasesOwnedWatcherCancelsReconciliationAndSuppressesEvents()
    {
        var clock = new LocalFakeClock();
        var backend = new Backend();
        var signal = new LocalStoreChangeSignal(AppContext.BaseDirectory, clock, backend);
        var changes = 0;
        signal.Changed += (_, _) => Interlocked.Increment(ref changes);
        await clock.WaitForDelayAsync(TimeSpan.FromSeconds(60));
        var retainedCallback = backend.Callback!;
        await signal.StopAsync();
        retainedCallback(false);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(0, changes);
        Assert.Equal(1, backend.DisposedWatches);
        Assert.Equal(1, backend.MetadataReads);
        await clock.WaitForNoDelaysAsync();
        await signal.StopAsync();
        signal.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MetadataComparisonCoversDatabaseWalAndTimestampChanges(int variant)
    {
        var first = new LocalStoreMetadata { DirectoryExists = true, DatabaseExists = true, WalExists = true };
        var second = first;
        if (variant == 0) second.DatabaseLength = 10;
        if (variant == 1) second.DatabaseWriteTicks = 10;
        if (variant == 2) second.WalWriteTicks = 10;
        Assert.False(first.Equals(second));
        Assert.True(first.Equals(first));
    }

    private sealed class Backend : ILocalStoreWatchBackend
    {
        public LocalStoreMetadata Metadata { get; set; }
        public int BindCount { get; private set; }
        public int MetadataReads { get; private set; }
        public int DisposedWatches { get; private set; }
        public bool FailBind { get; set; }
        public bool FailRead { get; set; }
        public Exception? ReadException { get; set; }
        public Exception? BindException { get; set; }
        public TaskCompletionSource<bool> ReadFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<bool>? Callback { get; private set; }
        public LocalStoreMetadata ReadMetadata(string root)
        {
            MetadataReads++;
            if (ReadException != null) { ReadFailed.TrySetResult(true); throw ReadException; }
            if (FailRead) throw new IOException("7654321");
            return Metadata;
        }
        public IDisposable Watch(string root, Action<bool> changedOrError)
        {
            BindCount++;
            if (BindException != null) throw BindException;
            if (FailBind) throw new IOException("7654321");
            Callback = changedOrError;
            return new WatchHandle(() => DisposedWatches++);
        }
        public void Fire(bool error) => Callback?.Invoke(error);
        private sealed class WatchHandle(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}