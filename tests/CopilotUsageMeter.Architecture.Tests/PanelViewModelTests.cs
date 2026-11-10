using CopilotUsageMeter.Client;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class PanelViewModelTests
{
    [Fact]
    public void DisconnectRetainsSeparatelyLabelledValuesAndDimsThem()
    {
        using var client = new FakeClient();
        using var model = new UsagePanelViewModel(client, action => action());
        client.Publish(new UsageSnapshot
        {
            HostInstanceId = "host-021", Revision = 1,
            Account = new AccountUsage { Allowance = 100, Consumed = 39, Remaining = 61 },
            Local = new LocalUsage { OverallCredits = 400, RepositoryCredits = 125 }
        });
        Assert.Contains("39 / 100 demo-account-credits", model.AccountText);
        Assert.Contains("400 local credits", model.LocalText);
        Assert.Equal(1, model.DataOpacity);
        client.ChangeStatus("Tray unavailable - cached demo values are stale");
        Assert.Equal(0.45, model.DataOpacity);
        Assert.False(model.CanSend);
        Assert.Contains("39 / 100", model.AccountText);
        Assert.Contains("400 local credits", model.LocalText);
        Assert.Contains("stale", model.Status);
        client.ChangeStatus("Connecting");
        Assert.Equal("Connecting - cached demo values are stale", model.Status);
        Assert.Equal(0.45, model.DataOpacity);
        Assert.False(model.CanSend);
    }

    [Fact]
    public async Task SettingsValidationAndPauseCommandsHaveExplicitOutcomes()
    {
        using var client = new FakeClient();
        using var model = new UsagePanelViewModel(client, action => action());
        client.Publish(new UsageSnapshot { HostInstanceId = "host-022", Revision = 1, IsPaused = true });
        model.DemoIntervalText = "4";
        await model.SaveSettingsAsync();
        Assert.Empty(client.Commands);
        Assert.Contains("not sent", model.Notice);
        model.DemoIntervalText = "5";
        await model.SaveSettingsAsync();
        Assert.Equal(5, client.Commands.Single().Settings!.DemoRefreshSeconds);
        Assert.Equal("SetSettings", client.Commands.Single().Type);
        await model.TogglePauseAsync();
        Assert.False(client.Commands.Last().Paused);
        Assert.Equal("SetPaused", client.Commands.Last().Type);
        Assert.Equal("Resume updates", model.PauseLabel);
        Assert.Contains("acknowledged", model.Notice);
    }

    [Fact]
    public void DisposingUnsubscribesFromClientUpdates()
    {
        using var client = new FakeClient();
        var model = new UsagePanelViewModel(client, action => action());
        client.Publish(new UsageSnapshot { HostInstanceId = "host-023", Revision = 1 });
        model.Dispose();
        client.ChangeStatus("Stopped");
        Assert.Equal("Connected - demo", model.Status);
        Assert.True(client.Disposed);
    }

    private sealed class FakeClient : IUsageClient
    {
        public event Action<UsageSnapshot>? SnapshotChanged;
        public event Action<string>? StatusChanged;
        public UsageSnapshot? Snapshot { get; private set; }
        public string Status { get; private set; } = "Disconnected";
        public List<ProtocolMessage> Commands { get; } = [];
        public bool Disposed { get; private set; }
        public void Start() { }
        public void ChangeStatus(string status) { Status = status; StatusChanged?.Invoke(status); }
        public void Publish(UsageSnapshot value)
        {
            Snapshot = value;
            ChangeStatus(value.IsPaused ? "Paused - demo" : "Connected - demo");
            SnapshotChanged?.Invoke(value);
        }
        public Task<ProtocolMessage> SendAsync(ProtocolMessage command, CancellationToken token)
        {
            Commands.Add(command);
            return Task.FromResult(new ProtocolMessage { Type = "Ack" });
        }
        public Task StopAsync() { ChangeStatus("Stopped"); return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }
}