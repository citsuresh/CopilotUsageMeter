using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Core
{
    public sealed class ThresholdAlert
    {
        public int Level { get; set; }
        public double ActualPercent { get; set; }
        public double Consumed { get; set; }
        public double Allowance { get; set; }
        public AlertChannel Channel { get; set; }
    }

    // Persisted part of the tracker: which levels already fired in which UTC month.
    public sealed class ThresholdAlertState
    {
        public int MonthKey { get; set; }
        public List<int> Fired { get; set; } = new List<int>();
    }

    // Decides when GitHub-reported account usage has crossed a configured level. A level fires once per UTC month and per crossing:
    // it is re-armed when a fresh reading drops below it (for example after switching to an account with lower usage).
    // Only "available" account data read in the current UTC month counts; local credits are never used.
    public sealed class ThresholdAlertTracker
    {
        private readonly object gate = new object();
        private readonly HashSet<int> fired = new HashSet<int>();
        // Levels added while there was no usable reading; they are silently marked passed on the next usable reading.
        private readonly HashSet<int> pendingSilent = new HashSet<int>();
        private int monthKey;

        public static int MonthKeyOf(DateTimeOffset utc) => utc.UtcDateTime.Year * 12 + utc.UtcDateTime.Month;

        public static bool TryGetPercent(AccountUsage? usage, DateTimeOffset nowUtc, out double percent)
        {
            percent = 0;
            if (usage == null || usage.Status != "available" || usage.IsUnlimited) return false;
            // A result kept from last month is not this month's usage, even though it still says "available".
            if (!usage.AsOfUtc.HasValue || MonthKeyOf(usage.AsOfUtc.Value) != MonthKeyOf(nowUtc)) return false;
            if (!usage.Allowance.HasValue || usage.Allowance.Value <= 0 || !usage.Consumed.HasValue || usage.Consumed.Value < 0) return false;
            percent = (double)(usage.Consumed.Value / usage.Allowance.Value) * 100.0;
            return !double.IsNaN(percent) && !double.IsInfinity(percent);
        }

        public ThresholdAlertState Export()
        {
            lock (gate) return new ThresholdAlertState { MonthKey = monthKey, Fired = fired.OrderBy(p => p).ToList() };
        }

        public void Load(ThresholdAlertState? state)
        {
            lock (gate)
            {
                fired.Clear();
                monthKey = 0;
                if (state == null) return;
                monthKey = state.MonthKey;
                foreach (var percent in state.Fired ?? new List<int>())
                    if (AlertSettings.IsValidPercent(percent)) fired.Add(percent);
            }
        }

        // Levels added while usage is already at or above them are treated as passed so they do not fire retroactively.
        // Removed levels are forgotten, so re-adding one later starts fresh.
        public void ApplyLevels(AlertSettings previous, AlertSettings settings, AccountUsage? usage, DateTimeOffset nowUtc)
        {
            lock (gate)
            {
                RollMonth(nowUtc);
                var before = AlertSettings.NormalizeLevels(previous.Levels).Select(level => level.Percent).ToList();
                var levels = AlertSettings.NormalizeLevels(settings.Levels);
                fired.RemoveWhere(percent => levels.All(level => level.Percent != percent));
                pendingSilent.RemoveWhere(percent => levels.All(level => level.Percent != percent));
                var added = levels.Where(level => !before.Contains(level.Percent)).Select(level => level.Percent).ToList();
                if (!TryGetPercent(usage, nowUtc, out var current))
                {
                    foreach (var percent in added) pendingSilent.Add(percent);
                    return;
                }
                foreach (var percent in added)
                    if (current >= percent) fired.Add(percent);
            }
        }

        public ThresholdAlert? Evaluate(AccountUsage? usage, DateTimeOffset nowUtc, AlertSettings settings)
        {
            lock (gate)
            {
                if (!TryGetPercent(usage, nowUtc, out var current)) return null;
                RollMonth(nowUtc);
                var levels = AlertSettings.NormalizeLevels(settings.Levels);

                foreach (var percent in pendingSilent)
                    if (current >= percent) fired.Add(percent);
                pendingSilent.Clear();
                fired.RemoveWhere(percent => levels.All(level => level.Percent != percent) || current < percent);

                var crossed = levels.Where(level => current >= level.Percent && !fired.Contains(level.Percent)).ToList();
                if (crossed.Count == 0) return null;
                foreach (var level in crossed) fired.Add(level.Percent);

                // While alerts are off the crossing is still recorded, so switching them on later does not report old news.
                if (!settings.Enabled) return null;
                var chosen = crossed.LastOrDefault(level => level.Channel != AlertChannel.Off);
                if (chosen == null) return null;
                return new ThresholdAlert
                {
                    Level = chosen.Percent,
                    ActualPercent = current,
                    Consumed = (double)usage!.Consumed!.Value,
                    Allowance = (double)usage.Allowance!.Value,
                    Channel = chosen.Channel,
                };
            }
        }

        private void RollMonth(DateTimeOffset nowUtc)
        {
            var key = MonthKeyOf(nowUtc);
            if (key == monthKey) return;
            monthKey = key;
            fired.Clear();
        }
    }

    // Fixed wording with numbers only: no login, title, path or chat data ever appears in an alert.
    public static class ThresholdAlertText
    {
        public static string Title(ThresholdAlert alert) =>
            "Copilot usage: " + alert.Level.ToString(CultureInfo.InvariantCulture) + "% of monthly allowance";

        public static string Body(ThresholdAlert alert) =>
            "GitHub reports " + Number(alert.Consumed) + " of " + Number(alert.Allowance) + " used this month ("
            + Math.Floor(alert.ActualPercent).ToString("0", CultureInfo.InvariantCulture) + "%).";

        public static ThresholdAlert Sample() =>
            new ThresholdAlert { Level = 80, ActualPercent = 80, Consumed = 240, Allowance = 300, Channel = AlertChannel.Notification };

        private static string Number(double value) =>
            Math.Abs(value - Math.Round(value)) < 0.005
                ? Math.Round(value).ToString("N0", CultureInfo.InvariantCulture)
                : value.ToString("N2", CultureInfo.InvariantCulture);
    }
}
