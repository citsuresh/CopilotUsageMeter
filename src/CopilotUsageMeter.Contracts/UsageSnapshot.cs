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
    }

    [DataContract]
    public sealed class LocalUsage
    {
        [DataMember] public string Status { get; set; } = "demo";
        [DataMember] public string RepositoryId { get; set; } = "repo-001";
        [DataMember] public decimal OverallCredits { get; set; }
        [DataMember] public decimal RepositoryCredits { get; set; }
        [DataMember] public long InputTokens { get; set; }
        [DataMember] public long OutputTokens { get; set; }
        [DataMember] public DateTimeOffset? AsOfUtc { get; set; }
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