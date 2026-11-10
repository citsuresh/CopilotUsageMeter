using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;
using CopilotUsageMeter.Transport.Pipes;
using Forms = System.Windows.Forms;

namespace CopilotUsageMeter.Tray;

internal sealed class TrayHost : IDisposable
{
    private readonly DemoUsageEngine engine;
    private readonly DemoSettingsStore settingsStore;
    private readonly UsagePipeServer server;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim commands = new(1, 1);
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem pause;
    private Icon? currentIcon;
    private MainWindow? window;
    private Task? timer;
    private bool disposed;

    public TrayHost(SecurityIdentifier user, DemoSettingsStore settingsStore, Func<Task> exit)
    {
        this.settingsStore = settingsStore;
        engine = new DemoUsageEngine(Guid.NewGuid().ToString("N"), () => DateTimeOffset.UtcNow, settingsStore.Load().DemoRefreshSeconds);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        server = new UsagePipeServer(
            () => NamedPipeServerStreamAcl.Create(UsageProtocol.PipePrefix + user.Value, PipeDirection.InOut, 16,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security),
            engine.GetSnapshot, ProcessAsync);
        server.Faulted += message =>
        {
            if (!lifetime.IsCancellationRequested)
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!lifetime.IsCancellationRequested) ShowFault(message);
                }));
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("PHASE 2 DEMO - invented data only") { Enabled = false });
        menu.Items.Add("Status / Settings...", null, (_, _) => ShowWindow());
        menu.Items.Add("Refresh demo now", null, (_, _) => RunCommand(new ProtocolMessage { Type = "Refresh" }));
        pause = new Forms.ToolStripMenuItem("Pause updates", null,
            (_, _) => RunCommand(new ProtocolMessage { Type = "SetPaused", Paused = !engine.GetSnapshot().IsPaused }));
        menu.Items.Add(pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await RunActionAsync(exit));
        icon = new Forms.NotifyIcon { ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => ShowWindow();
        UpdatePresentation(engine.GetSnapshot());
        icon.Visible = true;
    }

    public void Start()
    {
        server.Start();
        timer = RunTimerAsync();
    }

    public void ShowWindow()
    {
        if (window == null)
        {
            window = new MainWindow(engine.GetSnapshot, ProcessAsync);
            window.Closed += (_, _) => window = null;
        }
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private async Task RunTimerAsync()
    {
        try
        {
            using var ticks = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await ticks.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
            {
                await commands.WaitAsync(lifetime.Token).ConfigureAwait(false);
                try { if (engine.Refresh(automatic: true)) await PublishAsync().ConfigureAwait(false); }
                finally { commands.Release(); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    internal async Task<ProtocolMessage> ProcessAsync(ProtocolMessage request, CancellationToken token)
    {
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = requestLifetime.Token;
        await commands.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            bool changed;
            switch (request.Type)
            {
                case "Refresh": changed = engine.Refresh(automatic: false); break;
                case "SetPaused":
                    if (!request.Paused.HasValue) return UsagePipeServer.Error(request, "invalid_pause", "An explicit paused boolean is required.");
                    changed = engine.SetPaused(request.Paused.Value);
                    break;
                case "SetSettings":
                    var interval = request.Settings?.DemoRefreshSeconds;
                    if (!interval.HasValue || interval < 5 || interval > 300)
                        return UsagePipeServer.Error(request, "invalid_settings", "Demo refresh interval must be 5-300 seconds.");
                    try { settingsStore.Save(interval.Value); }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        return UsagePipeServer.Error(request, "settings_write_failed", "Unable to save demo settings; no change was applied.");
                    }
                    changed = engine.SetInterval(interval.Value);
                    break;
                case "OpenStatus":
                    await Application.Current.Dispatcher.InvokeAsync(ShowWindow);
                    return new ProtocolMessage { Type = "Ack" };
                default: return UsagePipeServer.Error(request, "unknown_command", "Command is not supported by the Phase 2 demo host.");
            }
            if (changed) await PublishAsync().ConfigureAwait(false);
            return new ProtocolMessage { Type = "Ack", Code = changed ? "updated" : "unchanged" };
        }
        finally { commands.Release(); }
    }

    private async Task PublishAsync()
    {
        var snapshot = engine.GetSnapshot();
        await Application.Current.Dispatcher.InvokeAsync(() => UpdatePresentation(snapshot));
        await server.BroadcastAsync(snapshot, lifetime.Token).ConfigureAwait(false);
    }

    private void UpdatePresentation(UsageSnapshot snapshot)
    {
        var next = DrawIcon(snapshot.IsPaused, (float)(snapshot.Account.Consumed!.Value / snapshot.Account.Allowance!.Value));
        icon.Icon = next;
        currentIcon?.Dispose();
        currentIcon = next;
        icon.Text = snapshot.IsPaused ? "Copilot Usage Meter - DEMO paused; invented data only" : "Copilot Usage Meter - DEMO account/local separate; invented data only";
        pause.Checked = snapshot.IsPaused;
        pause.Text = snapshot.IsPaused ? "Resume updates" : "Pause updates";
        window?.RefreshSnapshot(snapshot);
    }

    private static Icon DrawIcon(bool paused, float fraction)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new Pen(Color.Gray, 4);
        graphics.DrawEllipse(background, 4, 4, 24, 24);
        using var progress = new Pen(paused ? Color.Gray : Color.DodgerBlue, 4);
        graphics.DrawArc(progress, 4, 4, 24, 24, -90, Math.Clamp(fraction, 0, 1) * 360);
        if (paused)
        {
            graphics.FillRectangle(Brushes.White, 12, 10, 3, 12);
            graphics.FillRectangle(Brushes.White, 18, 10, 3, 12);
        }
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private async void RunCommand(ProtocolMessage command) => await RunActionAsync(async () =>
    {
        var reply = await ProcessAsync(command, lifetime.Token);
        if (reply.Type == "Error") ShowFault(reply.Message ?? "Demo command failed.");
    });

    private async Task RunActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
        {
            ShowFault("The demo action failed. Check tray status and retry.");
        }
    }

    private static void ShowFault(string message) => MessageBox.Show(message, "Copilot Usage Meter - demo", MessageBoxButton.OK, MessageBoxImage.Warning);

    public async Task StopAsync()
    {
        lifetime.Cancel();
        await server.StopAsync();
        if (timer != null) await timer;
        await commands.WaitAsync();
        commands.Release();
        window?.Close();
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        server.Dispose();
        icon.Visible = false;
        icon.Dispose();
        currentIcon?.Dispose();
    }
}