using System;
using System.Collections.Generic;
using System.Linq;

namespace CopilotUsageMeter.Core
{
    public enum AlertChannel
    {
        Off = 0,
        Notification = 1,
        MessageWindow = 2,
    }

    // One user-chosen threshold, as a whole percentage of the GitHub-reported monthly allowance.
    public sealed class AlertLevel
    {
        public AlertLevel() { }

        public AlertLevel(int percent, AlertChannel channel)
        {
            Percent = percent;
            Channel = channel;
        }

        public int Percent { get; set; }
        public AlertChannel Channel { get; set; }
    }

    public sealed class AlertSettings
    {
        public const int MinimumPercent = 1;
        public const int MaximumPercent = 200;
        public const int MaximumLevels = 10;

        // Off by default, matching the documented decision that alerts are opt-in.
        public bool Enabled { get; set; }
        // Problem notifications (sign-in needed, GitHub or local read failures) are independent of the usage levels and on by default.
        public bool NotifyProblems { get; set; } = true;
        public List<AlertLevel> Levels { get; set; } = DefaultLevels();

        public static List<AlertLevel> DefaultLevels() => new List<AlertLevel>
        {
            new AlertLevel(80, AlertChannel.Notification),
            new AlertLevel(100, AlertChannel.Notification),
        };

        public static bool IsValidPercent(int percent) => percent >= MinimumPercent && percent <= MaximumPercent;

        // Drops invalid levels and duplicates (the first one wins), sorts ascending and caps the list.
        public static List<AlertLevel> NormalizeLevels(IEnumerable<AlertLevel>? levels)
        {
            var result = new List<AlertLevel>();
            if (levels == null) return result;
            foreach (var level in levels)
            {
                if (level == null || !IsValidPercent(level.Percent)) continue;
                if (!Enum.IsDefined(typeof(AlertChannel), level.Channel)) continue;
                if (result.Any(existing => existing.Percent == level.Percent)) continue;
                result.Add(new AlertLevel(level.Percent, level.Channel));
            }
            return result.OrderBy(level => level.Percent).Take(MaximumLevels).ToList();
        }

        public AlertSettings Normalized() => new AlertSettings { Enabled = Enabled, NotifyProblems = NotifyProblems, Levels = NormalizeLevels(Levels) };
    }
}
