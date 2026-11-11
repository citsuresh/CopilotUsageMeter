using System;
using System.Collections.Generic;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Tray;

// Turns read results into problem notifications. Presentation is a callback, so this class has no UI types.
// Only fixed condition names are logged; account names, error text and paths never are.
internal sealed class HealthAlertService
{
    private readonly object gate = new();
    private readonly HealthAlertTracker tracker = new();
    private readonly Func<bool> enabled;
    private readonly Action<HealthAlert> present;
    private readonly Func<DateTimeOffset> clock;
    private int lastReadSequence = -1;

    public HealthAlertService(Func<bool> enabled, Action<HealthAlert> present, Func<DateTimeOffset>? clock = null)
    {
        this.enabled = enabled;
        this.present = present;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    // launch is true only for the first account state after the tray starts; signing out later is the user's choice, not a problem.
    // The account state is republished for many reasons (schedule changes, progress), so a failure counts only when readSequence shows a new read.
    public void OnAccount(AccountReadResult? result, int readSequence, bool launch)
    {
        if (result == null) return;
        var alerts = new List<HealthAlert>();
        lock (gate)
        {
            var now = clock();
            var on = enabled();
            var newRead = readSequence != lastReadSequence;
            lastReadSequence = readSequence;
            if (launch && result.Status == "signed-out" && result.ErrorCode != "authorization-expired")
                Collect(alerts, tracker.Observe(HealthCondition.SignInNeeded, bad: true, now, on));
            else if (result.Status != "signed-out")
                tracker.Observe(HealthCondition.SignInNeeded, bad: false, now, on);

            if (HealthSignals.TryClassifyAccount(result, out var condition, out var ignore))
            {
                // An expired sign-in clears the credential, so no further read follows and the sequence never advances;
                // it is a state rather than a failing read, and repeating the observation is harmless (it notifies once).
                if (newRead || condition == HealthCondition.AuthorizationExpired)
                {
                    foreach (var other in HealthSignals.AccountConditions)
                        if (other != condition) tracker.Observe(other, bad: false, now, on);
                    Collect(alerts, tracker.Observe(condition, bad: true, now, on));
                }
            }
            else if (!ignore) tracker.Clear(HealthSignals.AccountConditions, now);
        }
        Present(alerts);
    }

    public void OnLocal(string? status, string? code)
    {
        var alerts = new List<HealthAlert>();
        lock (gate)
        {
            var now = clock();
            var on = enabled();
            if (HealthSignals.TryClassifyLocal(status, code, out var condition))
            {
                var other = condition == HealthCondition.LocalReadFailing ? HealthCondition.LocalSetupProblem : HealthCondition.LocalReadFailing;
                tracker.Observe(other, bad: false, now, on);
                Collect(alerts, tracker.Observe(condition, bad: true, now, on));
            }
            else tracker.Clear(new[] { HealthCondition.LocalReadFailing, HealthCondition.LocalSetupProblem }, now);
        }
        Present(alerts);
    }

    // A background task fault. The tracker's cool-down keeps a task that keeps failing from notifying again and again.
    public void OnFault()
    {
        var alerts = new List<HealthAlert>();
        lock (gate)
        {
            var now = clock();
            Collect(alerts, tracker.Observe(HealthCondition.TrackingFault, bad: true, now, enabled()));
            tracker.Observe(HealthCondition.TrackingFault, bad: false, now, enabled());
        }
        Present(alerts);
    }

    private static void Collect(List<HealthAlert> alerts, HealthAlert? alert)
    {
        if (alert != null) alerts.Add(alert);
    }

    private void Present(List<HealthAlert> alerts)
    {
        foreach (var alert in alerts)
        {
            Diagnostics.Warning("health_alerts", "shown-" + alert.Condition);
            try { present(alert); }
            catch (Exception exception) { Diagnostics.Error("health_alerts", "present-failed", exception); }
        }
    }
}
