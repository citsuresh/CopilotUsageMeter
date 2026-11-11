using System;
using System.Collections.Generic;
using System.Linq;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Core
{
    public sealed class TrackingUsageState
    {
        private readonly object gate = new object();
        private readonly string hostId;
        private long revision = 1;
        private long refreshCount;
        private bool paused;
        private bool refreshing;
        private AccountUsage account = new AccountUsage { Status = "signed-out", Unit = "github-premium-interactions" };
        private LocalUsage local = new LocalUsage { Status = "unavailable", RepositoryId = "unavailable" };
        private Dictionary<string, CurrentChatUsage> charts = new Dictionary<string, CurrentChatUsage>(StringComparer.Ordinal);
        private LocalRecordedRepositoryMap? repositories;

        public TrackingUsageState(string hostId) => this.hostId = hostId;

        public void SetPaused(bool value)
        {
            lock (gate) { if (paused != value) { paused = value; revision++; } }
        }

        // Returns true when the flag changed, so the caller publishes only real transitions.
        public bool SetRefreshing(bool value)
        {
            lock (gate)
            {
                if (refreshing == value) return false;
                refreshing = value;
                revision++;
                return true;
            }
        }

        public void UpdateAccount(AccountUsage value)
        {
            lock (gate) { account = CloneAccount(value); revision++; }
        }

        public void UpdateLocal(LocalUsage value, IDictionary<string, CurrentChatUsage> currentCharts, LocalRecordedRepositoryMap? recordedRepositories = null)
        {
            lock (gate)
            {
                local = CloneLocal(value);
                repositories = recordedRepositories;
                charts = currentCharts.ToDictionary(pair => pair.Key, pair => CloneChat(pair.Value), StringComparer.Ordinal);
                revision++;
                refreshCount++;
            }
        }

        public void RetainContexts(IEnumerable<string> activeIds)
        {
            lock (gate)
            {
                var ids = new HashSet<string>(activeIds, StringComparer.Ordinal);
                foreach (var id in charts.Keys.Where(id => !ids.Contains(id)).ToArray()) charts.Remove(id);
            }
        }

        public UsageSnapshot GetSnapshot() => ProjectSnapshot(null, null);

        public UsageSnapshot ProjectSnapshot(UsageSnapshot? ignored, string? sessionId) => ProjectContext(ignored, sessionId, null);

        public UsageSnapshot ProjectContext(UsageSnapshot? ignored, string? sessionId, string? repositoryKey)
        {
            lock (gate)
            {
                var result = new UsageSnapshot
                {
                    HostInstanceId = hostId, Revision = revision, IsDemo = false, IsPaused = paused, IsRefreshing = refreshing,
                    RefreshCount = refreshCount, Account = CloneAccount(account), Local = CloneLocal(local)
                };
                result.Local.CurrentChat = sessionId != null && charts.TryGetValue(sessionId, out var chat)
                    ? CloneChat(chat) : new CurrentChatUsage();
                if (repositoryKey != null)
                {
                    result.Local.RepositoryId = repositoryKey;
                    result.Local.RepositoryCredits = repositories?.Lookup(repositoryKey).Totals?.Credits;
                }
                return result;
            }
        }

        private static AccountUsage CloneAccount(AccountUsage value) => new AccountUsage
        {
            Status = value.Status, Unit = value.Unit, Allowance = value.Allowance, Consumed = value.Consumed,
            Remaining = value.Remaining, AsOfUtc = value.AsOfUtc, Login = value.Login, Source = value.Source,
            ErrorCode = value.ErrorCode, NextRefreshUtc = value.NextRefreshUtc, ResetUtc = value.ResetUtc, IsUnlimited = value.IsUnlimited
        };

        private static LocalUsage CloneLocal(LocalUsage value) => new LocalUsage
        {
            Status = value.Status, RepositoryId = value.RepositoryId, OverallCredits = value.OverallCredits,
            RepositoryCredits = value.RepositoryCredits, InputTokens = value.InputTokens, OutputTokens = value.OutputTokens,
            AsOfUtc = value.AsOfUtc, ErrorCode = value.ErrorCode, WindowStartUtc = value.WindowStartUtc, WindowEndUtc = value.WindowEndUtc
        };

        private static CurrentChatUsage CloneChat(CurrentChatUsage value) => new CurrentChatUsage
        {
            Status = value.Status, AsOfUtc = value.AsOfUtc, TotalCredits = value.TotalCredits,
            Turns = value.Turns.Select(turn => new CurrentChatTurnUsage
            {
                TurnIndex = turn.TurnIndex, Credits = turn.Credits, TimestampUtc = turn.TimestampUtc
            }).ToArray()
        };
    }
}