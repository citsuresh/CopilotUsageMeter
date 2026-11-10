using System;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Core
{
    public interface IHistoryReader
    {
        Task<LocalReadResult> ReadAsync(LocalHistoryRequest request, CancellationToken cancellationToken);
        Task<LocalCurrentChatReadResult> ReadCurrentChatAsync(string? activeSessionId, CancellationToken cancellationToken);
    }

    public interface IChangeSignal : IDisposable
    {
        event EventHandler? Changed;
    }

    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
        Task Delay(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }

    // Request-only identity is never included in results or persisted.
    public sealed class LocalHistoryRequest
    {
        public LocalHistoryRequest(DateTimeOffset month, string? activeSessionId = null, string? canonicalRepositoryKey = null,
            string? recordedRepositoryKey = null, bool includeDays = true)
        {
            var utc = month.ToUniversalTime();
            FromUtc = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
            ToUtc = FromUtc.AddMonths(1);
            ActiveSessionId = activeSessionId;
            CanonicalRepositoryKey = canonicalRepositoryKey;
            RecordedRepositoryKey = recordedRepositoryKey;
            IncludeDays = includeDays;
        }

        public DateTimeOffset FromUtc { get; }
        public DateTimeOffset ToUtc { get; }
        public string? ActiveSessionId { get; }
        public string? CanonicalRepositoryKey { get; }
        public string? RecordedRepositoryKey { get; }
        public bool IncludeDays { get; }
    }

    public sealed class LocalUsageTotals
    {
        public decimal? Credits { get; set; }
        public long? Requests { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long? CacheReadTokens { get; set; }
        public long? CacheWriteTokens { get; set; }
        public long? ReasoningTokens { get; set; }
    }

    public sealed class LocalDailyUsage
    {
        public DateTimeOffset DayUtc { get; set; }
        public LocalUsageTotals Totals { get; set; } = new LocalUsageTotals();
    }

    public sealed class LocalCurrentChatReadResult
    {
        public CurrentChatUsage CurrentChat { get; set; } = new CurrentChatUsage();
        public LocalUsageTotals? Total { get; set; }
        public string? ErrorCode { get; set; }
    }

    // Not a wire DTO: the host explicitly maps this separate local source into a snapshot.
    public sealed class LocalReadResult
    {
        public string Status { get; set; } = "unavailable";
        public string? ErrorCode { get; set; }
        public DateTimeOffset? AsOfUtc { get; set; }
        public LocalUsageTotals? Overall { get; set; }
        public LocalDailyUsage[] Days { get; set; } = Array.Empty<LocalDailyUsage>();
        public bool DaysCapped { get; set; }
        public LocalUsageTotals? Repository { get; set; }
        public string RepositoryStatus { get; set; } = "unavailable";
        public string RepositoryErrorCode { get; set; } = "repository_key_unsupported";
        public LocalRecordedRepositoryMap? RecordedRepositories { get; set; }
        public CurrentChatUsage CurrentChat { get; set; } = new CurrentChatUsage();
        public LocalUsageTotals? CurrentChatTotal { get; set; }
        public string? CurrentChatErrorCode { get; set; }
    }
}