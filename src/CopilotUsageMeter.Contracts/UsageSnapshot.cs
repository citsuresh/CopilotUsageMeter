using System;
using System.Runtime.Serialization;

namespace CopilotUsageMeter.Contracts
{
    [DataContract]
    public sealed class AccountUsage
    {
        [DataMember] public string Status { get; set; } = "demo";
        [DataMember] public string Unit { get; set; } = "demo-account-credits";
        [DataMember] public decimal? Allowance { get; set; }
        [DataMember] public decimal? Consumed { get; set; }
        [DataMember] public decimal? Remaining { get; set; }
        [DataMember] public DateTimeOffset? AsOfUtc { get; set; }
        [DataMember] public string? Login { get; set; }
        [DataMember] public string? Source { get; set; }
        [DataMember] public string? ErrorCode { get; set; }
        [DataMember] public DateTimeOffset? NextRefreshUtc { get; set; }
        [DataMember] public DateTimeOffset? ResetUtc { get; set; }
        [DataMember] public bool IsUnlimited { get; set; }
    }

    [DataContract]
    public sealed class CurrentChatTurnUsage
    {
        [DataMember] public int TurnIndex { get; set; }
        [DataMember] public decimal? Credits { get; set; }
        [DataMember] public DateTimeOffset? TimestampUtc { get; set; }
    }

    [DataContract]
    public sealed class CurrentChatUsage
    {
        [DataMember] public string Status { get; set; } = "unavailable";
        [DataMember] public DateTimeOffset? AsOfUtc { get; set; }
        [DataMember] public CurrentChatTurnUsage[] Turns { get; set; } = Array.Empty<CurrentChatTurnUsage>();
        // Whole-chat credits from the history tool; Turns holds only the newest 20, so this can exceed their sum. Null means unknown.
        [DataMember] public decimal? TotalCredits { get; set; }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Status = string.IsNullOrWhiteSpace(Status) ? "unavailable" : Status;
            Turns ??= Array.Empty<CurrentChatTurnUsage>();

            if (!string.Equals(Status, "available", StringComparison.Ordinal) || AsOfUtc is null || Turns.Length == 0)
            {
                ClearUsage();
                return;
            }

            var turnIndexes = new System.Collections.Generic.HashSet<int>();
            foreach (var turn in Turns)
            {
                if (turn is null || turn.TurnIndex < 0 || turn.Credits is < 0 || !turnIndexes.Add(turn.TurnIndex))
                {
                    ClearUsage();
                    return;
                }
            }

            Array.Sort(Turns, (left, right) => left.TurnIndex.CompareTo(right.TurnIndex));
            AsOfUtc = AsOfUtc.Value.ToUniversalTime();
            if (TotalCredits is < 0) TotalCredits = null;
        }

        private void ClearUsage()
        {
            if (string.Equals(Status, "available", StringComparison.Ordinal)) Status = "unavailable";
            AsOfUtc = null;
            TotalCredits = null;
            Turns = Array.Empty<CurrentChatTurnUsage>();
        }
    }

    [DataContract]
    public sealed class LocalUsage
    {
        [DataMember] public string Status { get; set; } = "demo";
        [DataMember] public string RepositoryId { get; set; } = "repo-001";
        [DataMember] public decimal? OverallCredits { get; set; }
        [DataMember] public decimal? RepositoryCredits { get; set; }
        [DataMember] public long InputTokens { get; set; }
        [DataMember] public long OutputTokens { get; set; }
        [DataMember] public DateTimeOffset? AsOfUtc { get; set; }
        [DataMember] public CurrentChatUsage CurrentChat { get; set; } = new CurrentChatUsage();
        [DataMember] public string? ErrorCode { get; set; }
        [DataMember] public DateTimeOffset? WindowStartUtc { get; set; }
        [DataMember] public DateTimeOffset? WindowEndUtc { get; set; }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            CurrentChat ??= new CurrentChatUsage();
            if (string.IsNullOrWhiteSpace(Status)) Status = "unavailable";
        }
    }

    [DataContract]
    public sealed class UsageSettings
    {
        [DataMember] public int DemoRefreshSeconds { get; set; } = 15;
    }

    [DataContract]
    public sealed class UsageSnapshot
    {
        [DataMember] public string HostInstanceId { get; set; } = "";
        [DataMember] public long Revision { get; set; }
        [DataMember] public bool IsDemo { get; set; } = true;
        [DataMember] public bool IsPaused { get; set; }
        [DataMember] public long RefreshCount { get; set; }
        [DataMember] public AccountUsage Account { get; set; } = new AccountUsage();
        [DataMember] public LocalUsage Local { get; set; } = new LocalUsage();
        [DataMember] public UsageSettings Settings { get; set; } = new UsageSettings();
    }
}