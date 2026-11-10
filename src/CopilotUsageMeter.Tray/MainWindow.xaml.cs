using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Tray;

public partial class MainWindow : Window
{
    private readonly Func<UsageSnapshot> getSnapshot;
    private readonly Func<ProtocolMessage, CancellationToken, Task<ProtocolMessage>> send;

    internal MainWindow(Func<UsageSnapshot> getSnapshot, Func<ProtocolMessage, CancellationToken, Task<ProtocolMessage>> send)
    {
        this.getSnapshot = getSnapshot;
        this.send = send;
        InitializeComponent();
        RefreshSnapshot(getSnapshot());
    }

    internal void RefreshSnapshot(UsageSnapshot snapshot)
    {
        StatusText.Text = $"{(snapshot.IsPaused ? "Paused" : "Running")} - revision {snapshot.Revision}; shared local refreshes {snapshot.RefreshCount}";
        var presentation = CompactUsagePresentation.Create(snapshot, true, DateTimeOffset.UtcNow);
        AccountText.Text = presentation.Account;
        AccountRefreshText.Text = presentation.Refresh;
        LocalText.Text = presentation.Local;
        PauseButton.Content = snapshot.IsPaused ? "Resume updates" : "Pause updates";
    }

    private async void RefreshClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "Refresh" });
    private async void PauseClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "SetPaused", Paused = !getSnapshot().IsPaused });

    private async void SignInClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "BeginSignIn" });
    private async void SignOutClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "SignOut" });
    private async void SkipClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "SkipAccount" });
    private async void ConnectClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "ConnectAccount", AccountLogin = AccountLogin.Text.Trim() });

    private async Task ExecuteAsync(ProtocolMessage command)
    {
        try
        {
            var reply = await send(command, CancellationToken.None);
            NoticeText.Text = reply.Message ?? "Command acknowledged.";
        }
        catch (Exception exception) when (exception is IOException || exception is OperationCanceledException)
        {
            NoticeText.Text = "Command did not complete; check the tray status before retrying.";
        }
    }
}