using System;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Core
{
    public sealed class DemoUsageEngine
    {
        private readonly object gate = new object();
        private readonly Func<DateTimeOffset> utcNow;
        private readonly string hostInstanceId;
        private DateTimeOffset refreshedAt;
        private long revision = 1;
        private long refreshCount;
        private bool paused;
        private int refreshSeconds;

        public DemoUsageEngine(string hostInstanceId, Func<DateTimeOffset> utcNow, int refreshSeconds = 15)
        {
            ValidateInterval(refreshSeconds);
            this.hostInstanceId = hostInstanceId;
            this.utcNow = utcNow;
            this.refreshSeconds = refreshSeconds;
            refreshedAt = utcNow();
        }

        public static void ValidateInterval(int seconds)
        {
            if (seconds < 5 || seconds > 300)
                throw new ArgumentOutOfRangeException(nameof(seconds), "Demo refresh interval must be 5-300 seconds.");
        }

        public UsageSnapshot GetSnapshot()
        {
            lock (gate)
                return new UsageSnapshot
                {
                    HostInstanceId = hostInstanceId, Revision = revision, IsDemo = true,
                    IsPaused = paused, RefreshCount = refreshCount,
                    Settings = new UsageSettings { DemoRefreshSeconds = refreshSeconds },
                    Account = new AccountUsage
                    {
                        Allowance = 1000, Consumed = 300 + refreshCount, Remaining = 700 - refreshCount,
                        AsOfUtc = refreshedAt
                    },
                    Local = new LocalUsage
                    {
                        OverallCredits = 125 + refreshCount * 2, RepositoryCredits = 12.5m + refreshCount * 0.5m,
                        InputTokens = 10000 + refreshCount * 100, OutputTokens = 500 + refreshCount * 10,
                        AsOfUtc = refreshedAt
                    }
                };
        }

        public bool Refresh(bool automatic)
        {
            lock (gate)
            {
                var now = utcNow();
                if (automatic && (paused || now < refreshedAt.AddSeconds(refreshSeconds))) return false;
                if (now < refreshedAt.AddSeconds(1)) return false;
                RefreshCore(now);
                return true;
            }
        }

        public bool SetPaused(bool value)
        {
            lock (gate)
            {
                if (paused == value) return false;
                paused = value;
                revision++;
                if (!paused) RefreshCore(utcNow());
                return true;
            }
        }

        public bool SetInterval(int seconds)
        {
            ValidateInterval(seconds);
            lock (gate)
            {
                if (refreshSeconds == seconds) return false;
                refreshSeconds = seconds;
                revision++;
                return true;
            }
        }

        private void RefreshCore(DateTimeOffset now)
        {
            refreshedAt = now;
            refreshCount++;
            revision++;
        }
    }
}