using System;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Core
{
    public sealed class AccountReadResult
    {
        public const string UnsupportedSource = "GitHub copilot_internal/user (unsupported; may change)";
        public string Status { get; set; } = "unavailable";
        public string? Login { get; set; }
        public string? AccountId { get; set; }
        public string Source { get; set; } = UnsupportedSource;
        public string? Plan { get; set; }
        public bool? TokenBasedBilling { get; set; }
        public bool IsUnlimited { get; set; }
        public DateTimeOffset? ResetUtc { get; set; }
        public DateTimeOffset? RetryNotBeforeUtc { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public AccountUsage Usage { get; set; } = new AccountUsage { Status = "unavailable", Unit = "github-premium-interactions" };

        public AccountReadResult Copy() => new AccountReadResult
        {
            Status = Status, Login = Login, AccountId = AccountId, Source = Source, Plan = Plan,
            TokenBasedBilling = TokenBasedBilling, IsUnlimited = IsUnlimited, ResetUtc = ResetUtc,
            RetryNotBeforeUtc = RetryNotBeforeUtc, ErrorCode = ErrorCode, ErrorMessage = ErrorMessage,
            Usage = new AccountUsage
            {
                Status = Usage.Status, Unit = Usage.Unit, Allowance = Usage.Allowance,
                Consumed = Usage.Consumed, Remaining = Usage.Remaining, AsOfUtc = Usage.AsOfUtc,
                Login = Usage.Login, Source = Usage.Source, ErrorCode = Usage.ErrorCode,
                NextRefreshUtc = Usage.NextRefreshUtc, ResetUtc = Usage.ResetUtc, IsUnlimited = Usage.IsUnlimited
            }
        };

        public static AccountReadResult Empty(string status, string? code = null, string? message = null) =>
            new AccountReadResult
            {
                Status = status, ErrorCode = code, ErrorMessage = message,
                Usage = new AccountUsage { Status = status, Unit = "github-premium-interactions" }
            };
    }

    public interface IAccountReader
    {
        bool IsAuthorized { get; }
        AccountReadResult Current { get; }
        // Raised on identity changes, not ordinary read completion.
        event EventHandler? AuthorizationChanged;
        Task<AccountReadResult> ReadAsync(CancellationToken cancellationToken);
    }

    public interface IAccountClock
    {
        DateTimeOffset UtcNow { get; }
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }
}