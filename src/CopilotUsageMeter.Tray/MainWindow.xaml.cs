using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CopilotUsageMeter.Contracts;

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
        StatusText.Text = $"{(snapshot.IsPaused ? "Paused" : "Running")} - revision {snapshot.Revision}; shared demo refreshes {snapshot.RefreshCount}";
        AccountText.Text = $"Consumed {snapshot.Account.Consumed} / {snapshot.Account.Allowance} {snapshot.Account.Unit}; remaining {snapshot.Account.Remaining}. As of {snapshot.Account.AsOfUtc:HH:mm:ss} UTC.";
        LocalText.Text = $"Overall {snapshot.Local.OverallCredits} local credits; {snapshot.Local.RepositoryId}: {snapshot.Local.RepositoryCredits}. Input {snapshot.Local.InputTokens}; output {snapshot.Local.OutputTokens}. As of {snapshot.Local.AsOfUtc:HH:mm:ss} UTC.";
        PauseButton.Content = snapshot.IsPaused ? "Resume updates" : "Pause updates";
        if (!IntervalText.IsKeyboardFocusWithin) IntervalText.Text = snapshot.Settings.DemoRefreshSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private async void RefreshClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "Refresh" });
    private async void PauseClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "SetPaused", Paused = !getSnapshot().IsPaused });

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < 5 || seconds > 300)
        {
            NoticeText.Text = "Enter a whole number from 5 through 300 seconds. Settings were not changed.";
            return;
        }
        await ExecuteAsync(new ProtocolMessage { Type = "SetSettings", Settings = new UsageSettings { DemoRefreshSeconds = seconds } });
    }

    private async Task ExecuteAsync(ProtocolMessage command)
    {
        try
        {
            var reply = await send(command, CancellationToken.None);
            NoticeText.Text = reply.Type == "Error" ? reply.Message : "Demo command acknowledged.";
        }
        catch (Exception exception) when (exception is IOException || exception is OperationCanceledException)
        {
            NoticeText.Text = "Demo command did not complete; check the tray status before retrying.";
        }
    }
}