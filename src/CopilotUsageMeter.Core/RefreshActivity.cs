using System;
using System.Collections.Generic;

namespace CopilotUsageMeter.Core
{
    // Tracks which sources (for example "local", "account") are reading data. A source counts as refreshing only after it has
    // been active for a minimum time, so quick reads do not make the progress indicator flash.
    public sealed class RefreshActivity
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, DateTimeOffset> active = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        private readonly Func<DateTimeOffset> clock;

        public RefreshActivity(TimeSpan minimumVisible, Func<DateTimeOffset>? clock = null)
        {
            MinimumVisible = minimumVisible;
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public TimeSpan MinimumVisible { get; }

        // Returns true when the source has just become active.
        public bool Set(string source, bool isActive)
        {
            lock (gate)
            {
                if (!isActive) { active.Remove(source); return false; }
                if (active.ContainsKey(source)) return false;
                active[source] = clock();
                return true;
            }
        }

        public bool IsRefreshing
        {
            get
            {
                lock (gate)
                {
                    var now = clock();
                    foreach (var started in active.Values)
                        if (now - started >= MinimumVisible) return true;
                    return false;
                }
            }
        }
    }
}
