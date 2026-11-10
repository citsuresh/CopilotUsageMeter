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
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Threading;

namespace CopilotUsageMeter.Panel.Vsix
{
    internal sealed class ChatUsageIntegration : IDisposable
    {
        private readonly PipeUsageClient client;
        private readonly CompactUsageView view;
        private readonly DispatcherTimer timer;
        private readonly IVsStatusbar statusBar;
        private readonly WritableSettingsStore settings;
        private readonly JoinableTaskFactory tasks;
        private readonly IVsSolution? solution;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
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
        private const string SettingsCollection = "CopilotUsageMeter";

        public ChatUsageIntegration(IServiceProvider services, IVsStatusbar statusBar, JoinableTaskFactory tasks)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            this.statusBar = statusBar;
            this.tasks = tasks;
            solution = services.GetService(typeof(SVsSolution)) as IVsSolution;
            settings = new ShellSettingsManager(services).GetWritableSettingsStore(SettingsScope.UserSettings);
            if (!settings.CollectionExists(SettingsCollection)) settings.CreateCollection(SettingsCollection);
            var expanded = settings.PropertyExists(SettingsCollection, "Expanded") && settings.GetBoolean(SettingsCollection, "Expanded");
            using (var identity = WindowsIdentity.GetCurrent())
                client = new PipeUsageClient(UsageProtocol.PipePrefix + (identity.User?.Value ?? throw new InvalidOperationException("Windows user SID unavailable.")));
            view = new CompactUsageView(SendCommand, expanded);
            view.ExpansionChanged += value => settings.SetBoolean(SettingsCollection, "Expanded", value);
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
            forcedTier = tier;
            Detach();
            Tick(this, EventArgs.Empty);
        }

        private void SnapshotChanged(UsageSnapshot value) => Dispatch(Render);
        private void StatusChanged(string value) => Dispatch(Render);
        private void Dispatch(Action action) => _ = tasks.RunAsync(async () =>
        {
            await tasks.SwitchToMainThreadAsync(lifetime.Token);
            if (!disposed) action();
        });

        private void Tick(object? sender, EventArgs args)
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
                activeId = nextId;
                repositoryKey = nextRepository;
                SendCommand(new ProtocolMessage { Type = "SetActiveSession", ActiveSessionId = activeId, RepositoryKey = repositoryKey });
            }
            Render();
        }

        private void Attach()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
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
                catch (Exception exception) when (exception is TargetInvocationException || exception is AmbiguousMatchException || exception is MemberAccessException)
                {
                    ActivityLog.LogWarning(nameof(ChatUsageIntegration), "Active-chat identity is unavailable after a Copilot internal change.");
                    return null;
                }
            }
            return null;
        }

        private void Render()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var connected = client.Status == "Connected" || client.Status == "Paused";
            view.Update(client.Snapshot, connected, client.Status, placement + (activeId == null ? " | Active-chat identity unavailable" : " | Active-chat identity available"));
            if (parent == null) statusBar.SetText("Copilot Usage Meter: " + client.Status + "; chat placement unavailable. Tools > Copilot Usage Meter.");
        }

        private void SendCommand(ProtocolMessage command) => _ = tasks.RunAsync(() => SendCommandAsync(command));

        private async Task SendCommandAsync(ProtocolMessage command)
        {
            if (command.Type == "TogglePause")
            {
                command.Type = "SetPaused";
                command.Paused = client.Snapshot?.IsPaused != true;
            }
            try
            {
                var reply = await client.SendAsync(command, lifetime.Token);
                await tasks.SwitchToMainThreadAsync(lifetime.Token);
                if (reply.Type == "Error") statusBar.SetText(reply.Message ?? "Copilot Usage Meter command rejected.");
            }
            catch (Exception exception) when (exception is IOException || exception is OperationCanceledException)
            {
                await tasks.SwitchToMainThreadAsync();
                if (!disposed) statusBar.SetText("Copilot Usage Meter: tray unavailable; context will be resent on reconnect.");
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
            Detach();
        }
    }
}