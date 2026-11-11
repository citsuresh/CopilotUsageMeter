using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;
using CopilotUsageMeter.Panel.Core;
using CopilotUsageMeter.Readers.ChatHistoryCli;
using CopilotUsageMeter.Readers.GitHub;
using CopilotUsageMeter.Signals.FileWatcher;
using CopilotUsageMeter.Transport.Pipes;
using Forms = System.Windows.Forms;

namespace CopilotUsageMeter.Tray;

internal sealed class TrayHost : IDisposable
{
    private readonly TrackingUsageState state = new(Guid.NewGuid().ToString("N"));
    private readonly UsagePipeServer server;
    private readonly ChatHistoryReader history;
    private readonly LocalStoreChangeSignal changes;
    private readonly RefreshSignal refreshSignal = new();
    private readonly LocalRefreshScheduler local;
    private readonly GitHubApiClient api = new();
    private readonly GitHubAccountReader account;
    private readonly AccountRefreshCoordinator accountRefresh;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<bool> publications = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly List<Task> workers = new();
    private readonly object pauseGate = new();
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem pause;
    private Icon? currentIcon;
    private MainWindow? window;
    private bool disposed;
    private int utcMonth = DateTimeOffset.UtcNow.Year * 12 + DateTimeOffset.UtcNow.Month;

    public TrayHost(SecurityIdentifier user, Func<Task> exit)
    {
        var clock = new TrackingClock();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "VisualStudio", "CopilotCli");
        history = new ChatHistoryReader(AppContext.BaseDirectory, storeRoot: root, clock: clock);
        changes = new LocalStoreChangeSignal(root, clock);
        account = new GitHubAccountReader(api, new WindowsAccountCredentialStore(), clock);
        accountRefresh = new AccountRefreshCoordinator(account, clock);
        local = new LocalRefreshScheduler(refreshSignal, clock, ReadLocalAsync);
        changes.Changed += DatabaseChanged;
        accountRefresh.StateChanged += AccountChanged;
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        server = new UsagePipeServer(
            () => NamedPipeServerStreamAcl.Create(UsageProtocol.PipePrefix + user.Value, PipeDirection.InOut, 16,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security),
            state.GetSnapshot, ProcessAsync, projectContext: state.ProjectContext);
        server.ContextsChanged += ContextsChanged;
        server.Faulted += QueueFault;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Status / Settings...", null, (_, _) => ShowWindow());
        menu.Items.Add("Refresh now", null, (_, _) => RunCommand(new ProtocolMessage { Type = "Refresh" }));
        pause = new Forms.ToolStripMenuItem("Pause updates", null,
            (_, _) => RunCommand(new ProtocolMessage { Type = "SetPaused", Paused = !state.GetSnapshot().IsPaused }));
        menu.Items.Add(pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await exit());
        icon = new Forms.NotifyIcon { ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => ShowWindow();
        UpdatePresentation(state.GetSnapshot());
        icon.Visible = true;
    }

    public void Start()
    {
        server.Start();
        workers.Add(ObserveAsync(RunPublicationsAsync()));
        workers.Add(ObserveAsync(RunCountdownAsync()));
        workers.Add(ObserveAsync(RunAccountAsync()));
        workers.Add(ObserveAsync(local.ManualRefreshAsync(lifetime.Token)));
    }

    public void ShowWindow()
    {
        if (window == null)
        {
            window = new MainWindow(state.GetSnapshot, ProcessAsync, account.ListCliLogins);
            window.Closed += (_, _) => window = null;
        }
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        ForegroundWindow.Bring(window);
    }

    private async Task RunAccountAsync()
    {
        await account.RestoreAsync().ConfigureAwait(false);
        AccountChanged(this, EventArgs.Empty);
        await accountRefresh.RunAsync(lifetime.Token).ConfigureAwait(false);
    }

    private void DatabaseChanged(object? sender, EventArgs args)
    {
        accountRefresh.NotifyDatabaseActivity();
        refreshSignal.Raise();
    }

    private void AccountChanged(object? sender, EventArgs args)
    {
        state.UpdateAccount(accountRefresh.Current.Usage);
        RequestPublication();
    }

    private void ContextsChanged()
    {
        if (lifetime.IsCancellationRequested) return;
        state.RetainContexts(server.GetActiveSessionIds());
        refreshSignal.Raise();
    }

    private async Task ReadLocalAsync(CancellationToken token)
    {
        var request = new LocalHistoryRequest(DateTimeOffset.UtcNow, includeDays: false);
        var monthly = await history.ReadAsync(request, token).ConfigureAwait(false);
        var charts = new Dictionary<string, CurrentChatUsage>(StringComparer.Ordinal);
        foreach (var id in server.GetActiveSessionIds())
        {
            var current = await history.ReadCurrentChatAsync(id, token).ConfigureAwait(false);
            if (current.CurrentChat.Status == "available") current.CurrentChat.TotalCredits = current.Total?.Credits;
            charts[id] = current.CurrentChat.Status == "available" ? current.CurrentChat
                : new CurrentChatUsage { Status = current.ErrorCode ?? "unavailable" };
        }
        state.UpdateLocal(new LocalUsage
        {
            Status = monthly.Status, ErrorCode = monthly.ErrorCode ?? changes.LastErrorCode, AsOfUtc = monthly.AsOfUtc,
            WindowStartUtc = request.FromUtc, WindowEndUtc = request.ToUtc, RepositoryId = "unavailable",
            OverallCredits = monthly.Overall?.Credits, RepositoryCredits = null,
            InputTokens = monthly.Overall?.InputTokens ?? 0, OutputTokens = monthly.Overall?.OutputTokens ?? 0
        }, charts, monthly.RecordedRepositories);
        state.RetainContexts(server.GetActiveSessionIds());
        RequestPublication();
    }

    internal async Task<ProtocolMessage> ProcessAsync(ProtocolMessage request, CancellationToken token)
    {
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = requestLifetime.Token;
        switch (request.Type)
        {
            case "Refresh":
                await Task.WhenAll(local.ManualRefreshAsync(token), accountRefresh.RefreshAsync(manual: true, cancellationToken: token)).ConfigureAwait(false);
                return new ProtocolMessage { Type = "Ack" };
            case "SetPaused":
                if (!request.Paused.HasValue) return UsagePipeServer.Error(request, "invalid_pause", "An explicit paused boolean is required.");
                lock (pauseGate)
                {
                    local.SetPaused(request.Paused.Value);
                    accountRefresh.SetPaused(request.Paused.Value);
                    state.SetPaused(request.Paused.Value);
                    RequestPublication();
                }
                return new ProtocolMessage { Type = "Ack" };
            case "SignIn":
                // Panel-initiated sign-in: connect the single secure gh login, start browser sign-in when there is none, otherwise let the user choose in Status.
                if (!account.IsAuthorized)
                {
                    var attempt = await account.SwitchAccountAsync("").ConfigureAwait(false);
                    if (account.IsAuthorized)
                    {
                        RequestPublication();
                        return new ProtocolMessage { Type = "Ack", Message = "Secure credential imported. Profile and quota verification is scheduled separately." };
                    }
                    RequestPublication();
                    if (attempt.ErrorCode == "cli-login-not-found") return StartBrowserSignIn(request);
                    if (attempt.ErrorCode == "multiple-cli-logins")
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => { ShowWindow(); window?.ShowAccountChoices(); });
                        return new ProtocolMessage { Type = "Ack", Message = "Several GitHub accounts are signed in to GitHub CLI. Choose one in the Full details window." };
                    }
                }
                await Application.Current.Dispatcher.InvokeAsync(() => { ShowWindow(); if (account.IsAuthorized) window?.ShowAccountChoices(); });
                return new ProtocolMessage { Type = "Ack" };
            case "OpenStatus":
                await Application.Current.Dispatcher.InvokeAsync(ShowWindow);
                return new ProtocolMessage { Type = "Ack" };
            case "BeginSignIn":
                return StartBrowserSignIn(request);
            case "ConnectAccount":
                var connected = await account.SwitchAccountAsync(request.AccountLogin ?? "").ConfigureAwait(false);
                if (!account.IsAuthorized) return UsagePipeServer.Error(request, connected.ErrorCode ?? "sign_in_failed", connected.ErrorMessage ?? "A secure GitHub credential is unavailable.");
                RequestPublication();
                return new ProtocolMessage { Type = "Ack", Message = "Secure credential imported. Profile and quota verification is scheduled separately." };
            case "SignOut":
            case "SkipAccount":
                var signedOut = request.Type == "SkipAccount" ? await account.SkipAsync().ConfigureAwait(false) : await account.SignOutAsync().ConfigureAwait(false);
                if (signedOut.Status == "error") return UsagePipeServer.Error(request, signedOut.ErrorCode ?? "sign_out_failed", signedOut.ErrorMessage ?? "Unable to clear the application credential.");
                return new ProtocolMessage { Type = "Ack", Message = "Application authorization cleared. Local tracking continues; GitHub CLI's own login is unchanged." };
            default: return UsagePipeServer.Error(request, "unknown_command", "Command is not supported by the tracking host.");
        }
    }

    private void RequestPublication() => publications.Writer.TryWrite(true);

    private ProtocolMessage StartBrowserSignIn(ProtocolMessage request)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo("gh", "auth login --web --hostname github.com --git-protocol https") { UseShellExecute = true });
            if (process != null) _ = ConnectAfterBrowserSignInAsync(process);
            return new ProtocolMessage { Type = "Ack", Message = "Complete GitHub CLI sign-in; the account connects automatically when it finishes. You can also choose Connect." };
        }
        catch (Exception exception) when (exception is Win32Exception || exception is FileNotFoundException)
        {
            return UsagePipeServer.Error(request, "github_cli_unavailable", "Install GitHub CLI and run gh auth login --web --hostname github.com, then Connect. Plaintext credential fallback is not accepted.");
        }
    }

    // Waits for the owned gh login process, then imports the single secure gh login without reading any token output.
    private async Task ConnectAfterBrowserSignInAsync(Process process)
    {
        try
        {
            using (process)
            {
                await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
                if (process.ExitCode != 0) return;
            }
            await account.SwitchAccountAsync("").ConfigureAwait(false);
            RequestPublication();
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private async Task RunPublicationsAsync()
    {
        var last = Stopwatch.StartNew();
        while (await publications.Reader.WaitToReadAsync(lifetime.Token).ConfigureAwait(false))
        {
            var wait = TimeSpan.FromSeconds(2) - last.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, lifetime.Token).ConfigureAwait(false);
            while (publications.Reader.TryRead(out _)) { }
            var snapshot = state.GetSnapshot();
            await Application.Current.Dispatcher.InvokeAsync(() => UpdatePresentation(snapshot));
            await server.BroadcastAsync(snapshot, lifetime.Token).ConfigureAwait(false);
            last.Restart();
        }
    }

    private async Task RunCountdownAsync()
    {
        using var ticks = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await ticks.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var month = now.Year * 12 + now.Month;
            if (month != utcMonth) { utcMonth = month; refreshSignal.Raise(); }
            await Application.Current.Dispatcher.InvokeAsync(() => window?.RefreshSnapshot(state.GetSnapshot()));
        }
    }

    private void UpdatePresentation(UsageSnapshot snapshot)
    {
        var accountUsage = snapshot.Account;
        var available = accountUsage.Status == "available" && accountUsage.Allowance > 0 && accountUsage.Consumed.HasValue;
        var fraction = available ? accountUsage.Consumed!.Value >= accountUsage.Allowance!.Value ? 1f : (float)(accountUsage.Consumed.Value / accountUsage.Allowance.Value) : 0f;
        var next = DrawIcon(snapshot.IsPaused, available, fraction);
        icon.Icon = next;
        currentIcon?.Dispose();
        currentIcon = next;
        icon.Text = UsageDisplay.TrayTooltip(snapshot, DateTimeOffset.UtcNow);
        pause.Checked = snapshot.IsPaused;
        pause.Text = snapshot.IsPaused ? "Resume updates" : "Pause updates";
        window?.RefreshSnapshot(snapshot);
    }

    private static Icon DrawIcon(bool paused, bool available, float fraction)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new Pen(Color.Gray, 4);
        graphics.DrawEllipse(background, 4, 4, 24, 24);
        using var progress = new Pen(paused ? Color.Gray : fraction >= 1 ? Color.Red : fraction >= 0.8f ? Color.DarkOrange : Color.DodgerBlue, 4);
        if (available) graphics.DrawArc(progress, 4, 4, 24, 24, -90, Math.Clamp(fraction, 0, 1) * 360);
        if (!available && !paused) graphics.DrawLine(background, 6, 26, 26, 6);
        if (available && fraction >= 1 && !paused) graphics.FillEllipse(Brushes.Red, 22, 1, 9, 9);
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

    private async void RunCommand(ProtocolMessage command)
    {
        try
        {
            var reply = await ProcessAsync(command, lifetime.Token);
            if (reply.Type == "Error") QueueFault(reply.Message ?? "Command failed.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            QueueFault("Tracking failed. Check source availability and restart the tray.");
        }
    }

    private void QueueFault(string message)
    {
        if (!lifetime.IsCancellationRequested)
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (lifetime.IsCancellationRequested) return;
                // A failure is shown as a banner in the details window instead of a blocking message box.
                ShowWindow();
                window?.SetFault(message);
            }));
    }

    public async Task StopAsync()
    {
        lifetime.Cancel();
        changes.Changed -= DatabaseChanged;
        server.ContextsChanged -= ContextsChanged;
        await Task.WhenAll(server.StopAsync(), local.StopAsync(), changes.StopAsync());
        await Task.WhenAll(workers);
        window?.Close();
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        server.Dispose();
        local.Dispose();
        changes.Dispose();
        accountRefresh.Dispose();
        account.Dispose();
        api.Dispose();
        icon.Visible = false;
        icon.Dispose();
        currentIcon?.Dispose();
    }

    private sealed class TrackingClock : IClock, IAccountClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public Task Delay(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token);
        public Task DelayAsync(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token);
    }

    private sealed class RefreshSignal : IChangeSignal
    {
        public event EventHandler? Changed;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() => Changed = null;
    }
}