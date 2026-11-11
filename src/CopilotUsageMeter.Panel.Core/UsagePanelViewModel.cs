using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Client;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Panel.Core
{
    public sealed class UsagePanelViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IUsageClient client;
        private readonly Action<Action> dispatch;
        private UsageSnapshot? snapshot;
        private bool disposed;
        private string status;
        private string notice = "Demo only: no GitHub/history reads or chat embedding.";
        private string interval = "15";

        public event PropertyChangedEventHandler? PropertyChanged;
        public string Status => snapshot != null && !CanSend && !status.Contains("stale")
            ? status + " - cached demo values are stale" : status;
        public string Notice => notice;
        public bool CanSend => status == "Connected - demo" || status == "Paused - demo" || status == "Connected" || status == "Paused";
        public double DataOpacity => CanSend ? 1 : 0.45;
        public string PauseLabel => snapshot?.IsPaused == true ? "Resume updates" : "Pause updates";
        public string Identity => snapshot == null ? "No demo snapshot received." :
            $"Host {snapshot.HostInstanceId}; revision {snapshot.Revision}; shared demo refreshes {snapshot.RefreshCount}";
        public string AccountText => snapshot == null ? "Unavailable - tray not connected." : !snapshot.IsDemo
            ? CompactUsagePresentation.Create(snapshot, CanSend, DateTimeOffset.UtcNow).Account :
            $"Consumed {snapshot.Account.Consumed} / {snapshot.Account.Allowance} {snapshot.Account.Unit}; remaining {snapshot.Account.Remaining}. As of {snapshot.Account.AsOfUtc:HH:mm:ss} UTC.";
        public string LocalText => snapshot == null ? "Unavailable - tray not connected." : !snapshot.IsDemo
            ? CompactUsagePresentation.Create(snapshot, CanSend, DateTimeOffset.UtcNow).Local :
            $"Overall {snapshot.Local.OverallCredits} local credits; {snapshot.Local.RepositoryId}: {snapshot.Local.RepositoryCredits}. Input {snapshot.Local.InputTokens}; output {snapshot.Local.OutputTokens}. As of {snapshot.Local.AsOfUtc:HH:mm:ss} UTC.";
        public string DemoIntervalText
        {
            get => interval;
            set { interval = value; Notify(); }
        }

        public UsagePanelViewModel(IUsageClient client, Action<Action> dispatch)
        {
            this.client = client;
            this.dispatch = dispatch;
            status = client.Status;
            client.SnapshotChanged += SnapshotChanged;
            client.StatusChanged += StatusChanged;
        }

        public void Start() => client.Start();

        private void SnapshotChanged(UsageSnapshot value) => dispatch(() =>
        {
            if (disposed) return;
            if (snapshot?.Settings.DemoRefreshSeconds != value.Settings.DemoRefreshSeconds)
                interval = value.Settings.DemoRefreshSeconds.ToString(CultureInfo.InvariantCulture);
            snapshot = value;
            Notify();
        });

        private void StatusChanged(string value) => dispatch(() =>
        {
            if (disposed) return;
            status = value;
            Notify();
        });

        public Task RefreshAsync() => SendAsync(new ProtocolMessage { Type = "Refresh" });
        public Task TogglePauseAsync() => SendAsync(new ProtocolMessage { Type = "SetPaused", Paused = snapshot?.IsPaused != true });
        public Task OpenStatusAsync() => SendAsync(new ProtocolMessage { Type = "OpenStatus" });

        public Task SaveSettingsAsync()
        {
            if (!int.TryParse(interval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < 5 || seconds > 300)
            {
                notice = "Enter a whole number from 5 through 300 seconds. Settings were not sent.";
                Notify();
                return Task.CompletedTask;
            }
            return SendAsync(new ProtocolMessage { Type = "SetSettings", Settings = new UsageSettings { DemoRefreshSeconds = seconds } });
        }

        private async Task SendAsync(ProtocolMessage command)
        {
            try
            {
                if (!CanSend) throw new IOException("Tray is not connected.");
                var response = await client.SendAsync(command, CancellationToken.None);
                notice = response.Type == "Error" ? response.Message ?? "Demo command was rejected." : "Demo command acknowledged.";
            }
            catch (CommandTimeoutException) when (command.Type == "Refresh")
            {
                notice = "Refresh is taking longer than usual; values update when it finishes.";
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is OperationCanceledException)
            {
                notice = "Command not acknowledged; check connection/state before retrying.";
            }
            catch (Exception exception)
            {
                Diagnostics.Error("panel.viewmodel", "command_failed", exception);
                notice = "Command failed unexpectedly; details are in the activity log.";
            }
            Notify();
        }

        private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client.SnapshotChanged -= SnapshotChanged;
            client.StatusChanged -= StatusChanged;
            client.Dispose();
        }
    }
}