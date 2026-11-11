using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Signals.FileWatcher
{
    public struct LocalStoreMetadata : IEquatable<LocalStoreMetadata>
    {
        public bool DirectoryExists { get; set; }
        public bool DatabaseExists { get; set; }
        public long DatabaseLength { get; set; }
        public long DatabaseWriteTicks { get; set; }
        public bool WalExists { get; set; }
        public long WalLength { get; set; }
        public long WalWriteTicks { get; set; }

        public bool Equals(LocalStoreMetadata other) => DirectoryExists == other.DirectoryExists
            && DatabaseExists == other.DatabaseExists && DatabaseLength == other.DatabaseLength && DatabaseWriteTicks == other.DatabaseWriteTicks
            && WalExists == other.WalExists && WalLength == other.WalLength && WalWriteTicks == other.WalWriteTicks;
        public override bool Equals(object? obj) => obj is LocalStoreMetadata other && Equals(other);
        public override int GetHashCode() => DatabaseWriteTicks.GetHashCode() ^ WalWriteTicks.GetHashCode() ^ DirectoryExists.GetHashCode();
    }

    public interface ILocalStoreWatchBackend
    {
        LocalStoreMetadata ReadMetadata(string root);
        IDisposable Watch(string root, Action<bool> changedOrError);
    }

    public sealed class LocalStoreChangeSignal : IChangeSignal
    {
        public static readonly TimeSpan ReconciliationInterval = TimeSpan.FromSeconds(60);
        private readonly object gate = new object();
        private readonly string root;
        private readonly ILocalStoreWatchBackend backend;
        private readonly IClock clock;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly Task reconciliation;
        private LocalStoreMetadata metadata;
        private IDisposable? watcher;
        private bool recoveryNeeded;
        private bool disposed;

        public LocalStoreChangeSignal(string storeRoot, IClock? clock = null, ILocalStoreWatchBackend? backend = null)
        {
            if (string.IsNullOrWhiteSpace(storeRoot) || !Path.IsPathRooted(storeRoot))
                throw new ArgumentException("An absolute store root is required.", nameof(storeRoot));
            root = Path.GetFullPath(storeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            this.clock = clock ?? new SystemClock();
            this.backend = backend ?? new LocalStoreWatchBackend();
            var metadataAvailable = true;
            try { metadata = this.backend.ReadMetadata(root); }
            catch (Exception error) when (IsExpectedWatchFailure(error))
            {
                metadataAvailable = false;
                recoveryNeeded = true;
                LastErrorCode = "history_watch_metadata_failed";
                Diagnostics.Warning("watch.signal", "history_watch_metadata_failed", error);
            }
            BindWatcher();
            if (metadataAvailable && !recoveryNeeded)
                LastErrorCode = metadata.DirectoryExists ? null : "history_watch_directory_unavailable";
            reconciliation = Task.Run(ReconcileAsync);
            _ = reconciliation.ContinueWith(failed =>
            {
                lock (gate) LastErrorCode = "history_watch_faulted";
                Diagnostics.Error("watch.signal", "history_watch_faulted", failed.Exception?.GetBaseException());
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public event EventHandler? Changed;
        public string? LastErrorCode { get; private set; }

        public async Task StopAsync()
        {
            lock (gate)
            {
                if (!disposed)
                {
                    disposed = true;
                    stop.Cancel();
                    watcher?.Dispose();
                    watcher = null;
                    Changed = null;
                }
            }
            await reconciliation.ConfigureAwait(false);
        }

        public void Dispose() => StopAsync().GetAwaiter().GetResult();

        private void BindWatcher()
        {
            try
            {
                watcher?.Dispose();
                watcher = backend.Watch(root, OnChange) ?? throw new InvalidOperationException();
                recoveryNeeded = false;
            }
            catch (Exception error) when (IsExpectedWatchFailure(error))
            {
                watcher = null;
                recoveryNeeded = true;
                LastErrorCode = "history_watch_unavailable";
                Diagnostics.Warning("watch.signal", "history_watch_unavailable", error);
            }
        }

        private void OnChange(bool error)
        {
            EventHandler? handler;
            lock (gate)
            {
                if (disposed) return;
                if (error)
                {
                    recoveryNeeded = true;
                    LastErrorCode = "history_watch_error";
                }
                handler = Changed;
            }
            // A subscriber fault must not end the watcher or the reconciliation loop.
            try { handler?.Invoke(this, EventArgs.Empty); }
            catch (Exception exception) { Diagnostics.Error("watch.signal", "change_handler_failed", exception); }
        }

        private async Task ReconcileAsync()
        {
            try
            {
                while (true)
                {
                    await clock.Delay(ReconciliationInterval, stop.Token).ConfigureAwait(false);
                    bool changed;
                    lock (gate)
                    {
                        if (disposed) return;
                        var previousError = LastErrorCode;
                        try
                        {
                            var current = backend.ReadMetadata(root);
                            changed = !current.Equals(metadata);
                            var directoryChanged = current.DirectoryExists != metadata.DirectoryExists;
                            metadata = current;
                            if (recoveryNeeded || directoryChanged) BindWatcher();
                            if (!recoveryNeeded) LastErrorCode = metadata.DirectoryExists ? null : "history_watch_directory_unavailable";
                            changed |= !string.Equals(previousError, LastErrorCode, StringComparison.Ordinal);
                        }
                        catch (Exception error) when (IsExpectedWatchFailure(error))
                        {
                            changed = true;
                            recoveryNeeded = true;
                            LastErrorCode = "history_watch_metadata_failed";
                            Diagnostics.Warning("watch.signal", "history_watch_metadata_failed", error);
                        }
                    }
                    if (changed) OnChange(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        private static bool IsExpectedWatchFailure(Exception error) => error is IOException || error is UnauthorizedAccessException
            || error is System.ComponentModel.Win32Exception || error is System.Security.SecurityException;
    }

    public sealed class LocalStoreWatchBackend : ILocalStoreWatchBackend
    {
        public LocalStoreMetadata ReadMetadata(string root)
        {
            // Metadata only: neither the database nor WAL is opened.
            var database = new FileInfo(Path.Combine(root, "session-store.db"));
            var wal = new FileInfo(Path.Combine(root, "session-store.db-wal"));
            return new LocalStoreMetadata
            {
                DirectoryExists = Directory.Exists(root),
                DatabaseExists = database.Exists,
                DatabaseLength = database.Exists ? database.Length : 0,
                DatabaseWriteTicks = database.Exists ? database.LastWriteTimeUtc.Ticks : 0,
                WalExists = wal.Exists,
                WalLength = wal.Exists ? wal.Length : 0,
                WalWriteTicks = wal.Exists ? wal.LastWriteTimeUtc.Ticks : 0
            };
        }

        public IDisposable Watch(string root, Action<bool> changedOrError)
        {
            var directoryExists = Directory.Exists(root);
            var ancestor = new DirectoryInfo(root);
            while (!ancestor.Exists)
                ancestor = ancestor.Parent ?? throw new IOException("history_watch_unavailable");
            var watcher = new FileSystemWatcher(ancestor.FullName)
            {
                IncludeSubdirectories = !directoryExists,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                Filter = directoryExists ? "session-store.db*" : "*"
            };
            bool Relevant(string path)
            {
                if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
                    || root.StartsWith(path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
                return string.Equals(path, Path.Combine(root, "session-store.db"), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, Path.Combine(root, "session-store.db-wal"), StringComparison.OrdinalIgnoreCase);
            }
            FileSystemEventHandler changed = (_, args) => { if (Relevant(args.FullPath)) changedOrError(false); };
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += (_, args) => { if (Relevant(args.FullPath) || Relevant(args.OldFullPath)) changedOrError(false); };
            watcher.Error += (_, __) => changedOrError(true);
            var enabled = false;
            try { watcher.EnableRaisingEvents = true; enabled = true; return watcher; }
            finally { if (!enabled) watcher.Dispose(); }
        }
    }
}