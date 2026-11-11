using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CopilotUsageMeter.Client;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace CopilotUsageMeter.Panel.Vsix
{
    internal sealed class ChatUsageIntegration : IDisposable
    {
        private readonly PipeUsageClient client;
        private readonly CompactUsageView view;
        private readonly DispatcherTimer timer;
        private readonly IVsStatusbar statusBar;
        private readonly JoinableTaskFactory tasks;
        private readonly IVsSolution? solution;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly ExtensionNoticeTracker notices = new ExtensionNoticeTracker();
        private readonly InfoBarPresenter infoBars;
        private bool layoutUnrecognised;
        private System.Windows.Controls.Panel? parent;
        private FrameworkElement? anchor;
        private Grid? fallbackGrid;
        private RowDefinition? fallbackRow;
        private readonly Dictionary<UIElement, int> originalRows = new Dictionary<UIElement, int>();
        private string placement = "Chat placement unavailable - status bar fallback";
        private string? activeId;
        private string? repositoryKey;
        private DateTimeOffset repositoryChecked;
        private bool disposed;
        private int forcedTier;
        private int manualRefreshes;
        private long? awaitedRefreshCount;
        private DateTime awaitUntil;
        private string? faultNote;
        private long lastFaultAnnounced;
        private string? lastFaultKey;
        private static readonly System.Diagnostics.Stopwatch FaultClock = System.Diagnostics.Stopwatch.StartNew();
        private static bool identityFaultLogged;
        private const string FaultNoteText = "The usage display hit an unexpected error and is retrying. Details are in the Visual Studio activity log.";

        public ChatUsageIntegration(IServiceProvider services, IVsStatusbar statusBar, JoinableTaskFactory tasks)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            this.statusBar = statusBar;
            this.tasks = tasks;
            solution = services.GetService(typeof(SVsSolution)) as IVsSolution;
            using (var identity = WindowsIdentity.GetCurrent())
                client = new PipeUsageClient(UsageProtocol.PipePrefix + (identity.User?.Value ?? throw new InvalidOperationException("Windows user SID unavailable.")));
            view = new CompactUsageView(SendCommand);
            infoBars = new InfoBarPresenter(services, notices.Dismissed);
            client.SnapshotChanged += SnapshotChanged;
            client.StatusChanged += StatusChanged;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += Tick;
            client.Start();
            timer.Start();
            Tick(this, EventArgs.Empty);
        }

        public void OpenStatus() => SendCommand(new ProtocolMessage { Type = "OpenStatus" });

        public void Recheck(int tier = 0)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                forcedTier = tier;
                Detach();
                Tick(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                ReportFault("recheck_failed", exception);
            }
        }

        private void SnapshotChanged(UsageSnapshot value) => Dispatch(Render);
        private void StatusChanged(string value) => Dispatch(Render);
        private void Dispatch(Action action) => _ = tasks.RunAsync(async () =>
        {
            try
            {
                await tasks.SwitchToMainThreadAsync(lifetime.Token);
                if (!disposed) action();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                ReportFault("render_failed", exception);
            }
        });

        // Logs the failure (type only), shows a note in the usage row and tells the status bar at most once a minute. It never throws.
        private void ReportFault(string code, Exception exception)
        {
            try
            {
                // The tick runs every 2 s, so a persistent fault is logged once per distinct code and exception type until a tick succeeds.
                var key = code + "|" + exception.GetType().FullName;
                if (key != lastFaultKey)
                {
                    lastFaultKey = key;
                    Diagnostics.Error("vsix.integration", code, exception);
                }
                faultNote = FaultNoteText;
                var now = FaultClock.ElapsedMilliseconds + 1;
                if (lastFaultAnnounced == 0 || now - lastFaultAnnounced > 60_000)
                {
                    lastFaultAnnounced = now;
                    _ = tasks.RunAsync(async () =>
                    {
                        try
                        {
                            await tasks.SwitchToMainThreadAsync();
                            if (!disposed) statusBar.SetText("Copilot Usage Meter: unexpected error; details are in the Visual Studio activity log.");
                        }
                        catch (Exception)
                        {
                        }
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        private void Tick(object? sender, EventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                TickCore();
                faultNote = null;
                lastFaultKey = null;
            }
            catch (Exception exception)
            {
                ReportFault("tick_failed", exception);
                try { Render(); }
                catch (Exception) { }
            }
        }

        private void TickCore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposed) return;
            if (parent == null || !view.IsLoaded || (anchor != null && !anchor.IsLoaded))
            {
                Detach();
                Attach();
            }
            else if (fallbackGrid != null && forcedTier == 0)
            {
                FrameworkElement? preferred = null;
                Grid? grid = null;
                var count = 0;
                foreach (Window window in Application.Current.Windows)
                    if (window.IsVisible) FindAnchors(window, ref preferred, ref grid, ref count);
                if (preferred != null && VisualTreeHelper.GetParent(preferred) is StackPanel) { Detach(); Attach(); }
            }
            var nextId = ReadActiveId(anchor);
            var nextRepository = repositoryKey;
            if (DateTimeOffset.UtcNow - repositoryChecked > TimeSpan.FromSeconds(30))
            {
                string? file = null;
                if (solution != null) solution.GetSolutionInfo(out _, out file, out _);
                nextRepository = RepositoryContextReader.Read(file);
                repositoryChecked = DateTimeOffset.UtcNow;
            }
            if (!string.Equals(nextId, activeId, StringComparison.Ordinal) || !string.Equals(nextRepository, repositoryKey, StringComparison.Ordinal))
            {
                // The tray re-reads the newly active chat after its debounce; show loading until a read newer than this one is published (or 12 s pass).
                if (nextId != null && !string.Equals(nextId, activeId, StringComparison.Ordinal))
                {
                    awaitedRefreshCount = client.Snapshot?.RefreshCount ?? 0;
                    awaitUntil = DateTime.UtcNow.AddSeconds(12);
                }
                activeId = nextId;
                repositoryKey = nextRepository;
                SendCommand(new ProtocolMessage { Type = "SetActiveSession", ActiveSessionId = activeId, RepositoryKey = repositoryKey });
            }
            Render();
            EvaluateNotices();
        }

        // Never throws: a failure here must not stop the usage row from updating.
        private void EvaluateNotices()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                // The session list has no conversation (and no working-set anchor), so a missing anchor only counts as an unrecognised layout while a chat is open.
                var layoutProblem = layoutUnrecognised && activeId != null;
                foreach (var change in notices.Evaluate(client.Status, layoutProblem, DateTimeOffset.UtcNow))
                {
                    if (change.Action == NoticeAction.Close) infoBars.Close(change.Notice);
                    else if (!infoBars.Show(change.Notice))
                    {
                        notices.ShowFailed(change.Notice, DateTimeOffset.UtcNow);
                        statusBar.SetText(ExtensionNoticeText.Message(change.Notice));
                    }
                }
            }
            catch (Exception exception)
            {
                Diagnostics.Error("vsix.integration", "notices_failed", exception);
            }
        }

        private void Attach()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            layoutUnrecognised = false;
            FrameworkElement? workingSet = null;
            Grid? paneGrid = null;
            var count = 0;
            foreach (Window window in Application.Current.Windows)
            {
                if (!window.IsVisible) continue;
                FindAnchors(window, ref workingSet, ref paneGrid, ref count);
                if (workingSet != null) break;
            }
            if (forcedTier == 0 && workingSet != null && VisualTreeHelper.GetParent(workingSet) is StackPanel stack)
            {
                parent = stack;
                anchor = workingSet;
                stack.Children.Insert(stack.Children.IndexOf(workingSet), view);
                placement = "Above Total changes (internal Copilot integration)";
                return;
            }
            if (forcedTier < 2 && paneGrid != null && paneGrid.RowDefinitions.Count > 0)
            {
                parent = paneGrid;
                anchor = paneGrid;
                fallbackGrid = paneGrid;
                fallbackRow = new RowDefinition { Height = GridLength.Auto };
                paneGrid.RowDefinitions.Insert(0, fallbackRow);
                foreach (UIElement child in paneGrid.Children)
                {
                    originalRows[child] = Grid.GetRow(child);
                    Grid.SetRow(child, Grid.GetRow(child) + 1);
                }
                Grid.SetRow(view, 0);
                Grid.SetColumnSpan(view, Math.Max(1, paneGrid.ColumnDefinitions.Count));
                paneGrid.Children.Add(view);
                placement = "Degraded: top-of-chat fallback (internal Copilot integration)";
                // The chat pane was found but its usual anchor was not. A forced fallback from the Tools menu is deliberate, so it is not reported.
                layoutUnrecognised = forcedTier == 0;
                return;
            }
            anchor = workingSet ?? (FrameworkElement?)paneGrid;
            placement = "Degraded: chat placement unavailable - status bar fallback";
        }

        private static void FindAnchors(DependencyObject root, ref FrameworkElement? workingSet, ref Grid? paneGrid, ref int count)
        {
            if (++count > 12000) return;
            if (root is FrameworkElement element)
            {
                if (element.GetType().Name == "ChatWorkingSet") workingSet = element;
                if (element is Grid grid && grid.Name == "mainContentGrid"
                    && grid.Children.OfType<FrameworkElement>().Any(child => child.GetType().Name == "HarnessChatHeader")) paneGrid = grid;
                if (element.Name == "CopilotUsageMeterPanel") return;
            }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                FindAnchors(VisualTreeHelper.GetChild(root, index), ref workingSet, ref paneGrid, ref count);
                if (workingSet != null) return;
            }
        }

        private static string? ReadActiveId(FrameworkElement? element)
        {
            for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (!(current is FrameworkElement framework) || framework.DataContext == null) continue;
                var context = framework.DataContext;
                try
                {
                    var value = ActiveChatIdentity.Read(context);
                    if (value != null) return value;
                }
                catch (Exception exception)
                {
                    // Copilot's internals are not a contract: any failure reading them is a degraded state, logged once, not a crash.
                    if (!identityFaultLogged)
                    {
                        identityFaultLogged = true;
                        Diagnostics.Warning("vsix.integration", "active_chat_identity_unavailable", exception);
                    }
                    return null;
                }
            }
            return null;
        }

        private void Render()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var connected = client.Status == "Connected" || client.Status == "Paused";
            var notes = new List<string>();
            if (placement.StartsWith("Degraded", StringComparison.Ordinal)) notes.Add("The usage row is shown in a fallback position because the Copilot Chat layout was not recognised.");
            if (activeId == null) notes.Add("Active chat not detected; chat figures are unavailable.");
            if (faultNote != null) notes.Add(faultNote);
            var snapshot = client.Snapshot;
            var waiting = awaitedRefreshCount.HasValue && snapshot != null && !snapshot.IsPaused && snapshot.RefreshCount <= awaitedRefreshCount.Value && DateTime.UtcNow < awaitUntil;
            if (!waiting) awaitedRefreshCount = null;
            view.Update(snapshot, connected, client.Status, notes.Count == 0 ? null : string.Join(" ", notes), manualRefreshes > 0 || waiting || snapshot?.IsRefreshing == true);
            if (parent == null) statusBar.SetText("Copilot Usage Meter: " + client.Status + "; chat placement unavailable. Tools > Copilot Usage Meter.");
        }

        private void SendCommand(ProtocolMessage command) => _ = tasks.RunAsync(async () =>
        {
            try { await SendCommandAsync(command); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                ReportFault("command_failed." + command.Type, exception);
            }
        });

        private async Task SendCommandAsync(ProtocolMessage command)
        {
            if (command.Type == "TogglePause")
            {
                command.Type = "SetPaused";
                command.Paused = client.Snapshot?.IsPaused != true;
            }
            if (command.Type == "Refresh")
            {
                await tasks.SwitchToMainThreadAsync(lifetime.Token);
                manualRefreshes++;
                Render();
            }
            try
            {
                var reply = await client.SendAsync(command, lifetime.Token);
                await tasks.SwitchToMainThreadAsync(lifetime.Token);
                if (reply.Type == "Error")
                {
                    Diagnostics.Warning("vsix.command", "command_rejected." + command.Type + "." + reply.Code);
                    statusBar.SetText(reply.Message ?? "Copilot Usage Meter command rejected.");
                }
            }
            catch (CommandTimeoutException) when (command.Type == "Refresh")
            {
                await tasks.SwitchToMainThreadAsync();
                if (!disposed) statusBar.SetText("Copilot Usage Meter: refresh is taking longer than usual; values update when it finishes.");
            }
            catch (Exception exception) when (exception is IOException || exception is OperationCanceledException)
            {
                await tasks.SwitchToMainThreadAsync();
                if (!disposed) statusBar.SetText("Copilot Usage Meter: tray unavailable; context will be resent on reconnect.");
            }
            finally
            {
                if (command.Type == "Refresh")
                {
                    await tasks.SwitchToMainThreadAsync();
                    manualRefreshes = Math.Max(0, manualRefreshes - 1);
                    if (!disposed) Render();
                }
            }
        }

        private void Detach()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            parent?.Children.Remove(view);
            if (fallbackGrid != null && fallbackRow != null && fallbackGrid.RowDefinitions.Contains(fallbackRow))
            {
                fallbackGrid.RowDefinitions.Remove(fallbackRow);
                foreach (var original in originalRows)
                    if (fallbackGrid.Children.Contains(original.Key) && Grid.GetRow(original.Key) == original.Value + 1)
                        Grid.SetRow(original.Key, original.Value);
            }
            originalRows.Clear();
            fallbackGrid = null;
            fallbackRow = null;
            parent = null;
            anchor = null;
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            disposed = true;
            timer.Stop();
            timer.Tick -= Tick;
            client.SnapshotChanged -= SnapshotChanged;
            client.StatusChanged -= StatusChanged;
            client.Dispose();
            activeId = null;
            repositoryKey = null;
            lifetime.Cancel();
            infoBars.Dispose();
            Detach();
        }
    }
}