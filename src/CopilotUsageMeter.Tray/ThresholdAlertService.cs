using System;
using System.Linq;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Tray;

// Ties the tracker to its stored settings and state. Presentation is a callback, so this class has no UI types.
internal sealed class ThresholdAlertService
{
    private readonly object gate = new();
    private readonly AlertSettingsStore settingsStore;
    private readonly AlertStateStore stateStore;
    private readonly Action<ThresholdAlert> present;
    private readonly Func<DateTimeOffset> clock;
    private readonly ThresholdAlertTracker tracker = new();
    private AlertSettings settings = new();
    private AccountUsage? latest;
    private string persistedState = "";

    public ThresholdAlertService(AlertSettingsStore settingsStore, AlertStateStore stateStore, Action<ThresholdAlert> present, Func<DateTimeOffset>? clock = null)
    {
        this.settingsStore = settingsStore;
        this.stateStore = stateStore;
        this.present = present;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public AlertSettings Settings
    {
        get { lock (gate) return settings.Normalized(); }
    }

    public void Load()
    {
        lock (gate)
        {
            settings = settingsStore.Load();
            tracker.Load(stateStore.Load());
            persistedState = Serialize(tracker.Export());
        }
    }

    public void OnAccount(AccountUsage? usage)
    {
        ThresholdAlert? alert;
        lock (gate)
        {
            var now = clock();
            // Keep the last usable reading so levels added during a failed or signed-out read are still judged against it;
            // signing out or skipping means the reading no longer belongs to the account in use.
            if (ThresholdAlertTracker.TryGetPercent(usage, now, out _)) latest = usage;
            else if (usage == null || usage.Status == "signed-out" || usage.Status == "skipped") latest = null;
            alert = tracker.Evaluate(usage, now, settings);
            PersistState();
        }
        if (alert != null) Present(alert);
    }

    // Returns false when the settings could not be written; they still apply until the tray exits.
    public bool Update(AlertSettings update)
    {
        var saved = true;
        lock (gate)
        {
            var previous = settings;
            settings = update.Normalized();
            tracker.ApplyLevels(previous, settings, latest, clock());
            try { settingsStore.Save(settings); }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
            {
                Diagnostics.Warning("alert_settings", "save-failed", exception);
                saved = false;
            }
            PersistState();
        }
        return saved;
    }

    public void SendTest(AlertChannel channel)
    {
        var sample = ThresholdAlertText.Sample();
        sample.Channel = channel;
        Present(sample, test: true);
    }

    private void Present(ThresholdAlert alert, bool test = false)
    {
        Diagnostics.Warning("alerts", (test ? "test-shown-" : "threshold-fired-") + alert.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try { present(alert); }
        catch (Exception exception) { Diagnostics.Error("alerts", "present-failed", exception); }
    }

    private void PersistState()
    {
        var state = tracker.Export();
        var text = Serialize(state);
        if (text == persistedState) return;
        try
        {
            stateStore.Save(state);
            persistedState = text;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            Diagnostics.Warning("alert_state", "save-failed", exception);
        }
    }

    private static string Serialize(ThresholdAlertState state) => state.MonthKey + ":" + string.Join(",", state.Fired.OrderBy(p => p));
}
