using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;
using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

[Collection("DiagnosticsSink")]
public sealed class AlertStoreAndServiceTests : IDisposable
{
    private static readonly DateTimeOffset May = new(2031, 5, 10, 12, 0, 0, TimeSpan.Zero);

    // Other tests running in parallel can log through the shared static sink, so access is locked and assertions use Has().
    private sealed class RecordingSink : IDiagnosticsSink
    {
        private readonly List<(string Component, string Code)> entries = new();
        public void Write(DiagnosticLevel level, string component, string code, string? exceptionDetail)
        {
            lock (entries) entries.Add((component, code));
        }
        public bool Has(string component, string code)
        {
            lock (entries) return entries.Contains((component, code));
        }
        public bool Any
        {
            get { lock (entries) return entries.Count > 0; }
        }
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), "cum-alert-tests-" + Guid.NewGuid().ToString("N"));
    private readonly IDiagnosticsSink? previousSink = Diagnostics.Sink;
    private readonly RecordingSink sink = new();
    private DateTimeOffset now = May;

    public AlertStoreAndServiceTests() => Diagnostics.Sink = sink;

    public void Dispose()
    {
        Diagnostics.Sink = previousSink;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private string SettingsPath => Path.Combine(directory, "alerts.json");
    private string StatePath => Path.Combine(directory, "alert-state.json");

    private ThresholdAlertService Service(List<ThresholdAlert> shown, Action<ThresholdAlert>? present = null)
    {
        var service = new ThresholdAlertService(new AlertSettingsStore(SettingsPath), new AlertStateStore(StatePath), present ?? shown.Add, () => now);
        service.Load();
        return service;
    }

    private AccountUsage Usage(decimal consumed) => new() { Status = "available", Consumed = consumed, Allowance = 100, AsOfUtc = now };

    [Fact]
    public void MissingFiles_GiveDefaultsWithoutLogging()
    {
        var shown = new List<ThresholdAlert>();

        var service = Service(shown);

        Assert.False(service.Settings.Enabled);
        Assert.Equal(new[] { 80, 100 }, service.Settings.Levels.Select(l => l.Percent));
        Assert.False(sink.Has("alert_settings", "load-failed") || sink.Has("alert_state", "load-failed"));
    }

    [Fact]
    public void Settings_RoundTripThroughTheStore()
    {
        var store = new AlertSettingsStore(SettingsPath);

        store.Save(new AlertSettings { Enabled = true, Levels = { new AlertLevel(50, AlertChannel.MessageWindow) } });
        var loaded = store.Load();

        Assert.True(loaded.Enabled);
        Assert.Equal(new[] { 50, 80, 100 }, loaded.Levels.Select(l => l.Percent));
        Assert.Equal(AlertChannel.MessageWindow, loaded.Levels[0].Channel);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"Enabled\":\"yes\"}")]
    public void CorruptSettingsFile_FallsBackToDefaultsAndLogsOnlyACode(string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, content);

        var loaded = new AlertSettingsStore(SettingsPath).Load();

        Assert.False(loaded.Enabled);
        Assert.Equal(2, loaded.Levels.Count);
        Assert.True(sink.Has("alert_settings", "load-failed"));
    }

    [Fact]
    public void OversizedFile_IsRejectedWithoutReadingIt()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(StatePath, new string(' ', AlertFile.MaximumBytes + 1));

        var state = new AlertStateStore(StatePath).Load();

        Assert.Empty(state.Fired);
        Assert.True(sink.Has("alert_state", "load-failed"));
    }

    [Fact]
    public void HandEditedSettings_AreNormalizedOnLoad()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, "{\"Enabled\":true,\"Levels\":[{\"Percent\":900,\"Channel\":1},{\"Percent\":70,\"Channel\":2},{\"Percent\":70,\"Channel\":1}]}");

        var loaded = new AlertSettingsStore(SettingsPath).Load();

        Assert.Single(loaded.Levels);
        Assert.Equal(70, loaded.Levels[0].Percent);
        Assert.Equal(AlertChannel.MessageWindow, loaded.Levels[0].Channel);
    }

    [Fact]
    public void EnabledService_PresentsACrossingOnceEvenAfterARestart()
    {
        var shown = new List<ThresholdAlert>();
        var service = Service(shown);
        service.Update(new AlertSettings { Enabled = true });

        service.OnAccount(Usage(85));
        service.OnAccount(Usage(86));
        var restarted = Service(shown);
        restarted.OnAccount(Usage(87));

        Assert.Single(shown);
        Assert.Equal(80, shown[0].Level);
        Assert.True(restarted.Settings.Enabled);
        Assert.True(sink.Has("alerts", "threshold-fired-80"));
    }

    [Fact]
    public void DisabledService_NeverPresents()
    {
        var shown = new List<ThresholdAlert>();
        var service = Service(shown);

        service.OnAccount(Usage(120));

        Assert.Empty(shown);
    }

    [Fact]
    public void AddingACustomLevel_AppliesToTheLatestUsage()
    {
        var shown = new List<ThresholdAlert>();
        var service = Service(shown);
        service.OnAccount(Usage(60));

        service.Update(new AlertSettings { Enabled = true, Levels = { new AlertLevel(50, AlertChannel.Notification), new AlertLevel(70, AlertChannel.MessageWindow) } });
        service.OnAccount(Usage(61));
        service.OnAccount(Usage(71));

        Assert.Single(shown);
        Assert.Equal(70, shown[0].Level);
        Assert.Equal(AlertChannel.MessageWindow, shown[0].Channel);
    }

    [Fact]
    public void ALevelAddedDuringAFailedRead_IsJudgedAgainstTheLastUsableReading()
    {
        var shown = new List<ThresholdAlert>();
        var service = Service(shown);
        service.Update(new AlertSettings { Enabled = true, Levels = { new AlertLevel(80, AlertChannel.Notification) } });
        service.OnAccount(Usage(60));
        service.OnAccount(new AccountUsage { Status = "error", ErrorCode = "github-request-failed" });

        service.Update(new AlertSettings { Enabled = true, Levels = { new AlertLevel(50, AlertChannel.Notification), new AlertLevel(80, AlertChannel.Notification) } });
        service.OnAccount(Usage(61));

        Assert.Empty(shown);
    }

    [Fact]
    public void ANewMonth_AlertsAgain()
    {
        var shown = new List<ThresholdAlert>();
        var service = Service(shown);
        service.Update(new AlertSettings { Enabled = true });

        service.OnAccount(Usage(90));
        now = now.AddMonths(1);
        service.OnAccount(Usage(90));

        Assert.Equal(2, shown.Count);
    }

    [Fact]
    public void FailingPresenter_IsLoggedAndDoesNotThrow()
    {
        var shown = new List<ThresholdAlert>();
        var service = Service(shown, _ => throw new InvalidOperationException("invented-secret-77"));
        service.Update(new AlertSettings { Enabled = true });

        service.OnAccount(Usage(90));

        Assert.True(sink.Has("alerts", "present-failed"));
    }

    [Fact]
    public void UnwritableSettings_ApplyInMemoryAndReportFailure()
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(SettingsPath);
        var shown = new List<ThresholdAlert>();
        var service = Service(shown);

        var saved = service.Update(new AlertSettings { Enabled = true, Levels = { new AlertLevel(40, AlertChannel.Notification) } });

        Assert.False(saved);
        Assert.True(service.Settings.Enabled);
        Assert.True(sink.Has("alert_settings", "save-failed"));
    }

    [Fact]
    public void SendTest_UsesTheChosenChannelAndNeverTouchesState()
    {
        var shown = new List<ThresholdAlert>();
        var service = Service(shown);

        service.SendTest(AlertChannel.MessageWindow);

        Assert.Equal(AlertChannel.MessageWindow, Assert.Single(shown).Channel);
        Assert.False(File.Exists(StatePath));
        Assert.True(sink.Has("alerts", "test-shown-80"));
    }
}
