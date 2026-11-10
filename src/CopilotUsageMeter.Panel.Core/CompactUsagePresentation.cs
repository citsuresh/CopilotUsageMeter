using System;
using System.Globalization;
using System.Linq;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Panel.Core
{
    public sealed class TurnBar
    {
        public int TurnIndex { get; set; }
        public double Height { get; set; }
        public bool IsNewest { get; set; }
        public string Tooltip { get; set; } = "";
    }

    public sealed class CompactUsagePresentation
    {
        public string Header { get; private set; } = "Copilot Usage - unavailable";
        public string Account { get; private set; } = "GitHub account: unavailable";
        public string Local { get; private set; } = "Local analytics (this machine only): unavailable";
        public string Refresh { get; private set; } = "Offline";
        public string Chat { get; private set; } = "Current chat: unavailable";
        public TurnBar[] Bars { get; private set; } = Array.Empty<TurnBar>();

        public static CompactUsagePresentation Create(UsageSnapshot? snapshot, bool connected, DateTimeOffset now)
        {
            var result = new CompactUsagePresentation();
            if (snapshot == null) return result;
            var account = snapshot.Account;
            var local = snapshot.Local;
            var percentage = account.Status == "available" && account.Allowance > 0 && account.Consumed.HasValue
                ? account.Consumed > account.Allowance ? "100%+" : (account.Consumed.Value / account.Allowance.Value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"
                : account.Status == "available" && account.IsUnlimited ? "unlimited" : account.Status;
            result.Header = "Copilot Usage: GitHub " + percentage + " | This Repo " + Number(local.RepositoryCredits) + " local credits";
            result.Account = "GitHub account " + (account.Login ?? "(not signed in)") + ": " + account.Status
                + "\n" + (account.Source ?? "Account source unavailable")
                + "\nConsumed " + Number(account.Consumed) + " / " + Number(account.Allowance) + " " + account.Unit
                + "; reported remaining " + Number(account.Remaining)
                + ". Last sync: " + Time(account.AsOfUtc) + ". Reset: " + Time(account.ResetUtc)
                + (account.ErrorCode == null ? "" : "\n" + account.ErrorCode);
            result.Local = "Local analytics (this machine only): " + local.Status
                + "\nUTC month " + (local.WindowStartUtc?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "unknown")
                + "; overall " + Number(local.OverallCredits) + " local credits; recorded repository " + Number(local.RepositoryCredits)
                + " (exact GitHub origin key; not canonical folder attribution)"
                + ". Tokens processed: input " + (local.Status == "available" ? local.InputTokens.ToString(CultureInfo.InvariantCulture) : "unavailable")
                + "; output " + (local.Status == "available" ? local.OutputTokens.ToString(CultureInfo.InvariantCulture) : "unavailable")
                + ". As of " + Time(local.AsOfUtc)
                + (local.ErrorCode == null ? "" : "\n" + local.ErrorCode);
            result.Refresh = !connected ? "Offline - cached values are stale" : snapshot.IsPaused ? "Paused - automatic refresh suspended"
                : account.NextRefreshUtc.HasValue
                    ? "Next GitHub sync in " + Math.Max(0, Math.Ceiling((account.NextRefreshUtc.Value - now).TotalSeconds)).ToString(CultureInfo.InvariantCulture) + "s"
                    : "GitHub sync: " + account.Status;
            var chat = local.CurrentChat;
            result.Chat = "Current chat: " + chat.Status;
            if (chat.Status != "available" || chat.Turns == null || chat.Turns.Length == 0) return result;
            var turns = chat.Turns.Where(turn => turn != null && turn.TurnIndex >= 0)
                .OrderBy(turn => turn.TurnIndex).ToArray();
            turns = turns.Skip(Math.Max(0, turns.Length - 20)).ToArray();
            var maximum = turns.Where(turn => turn.Credits.HasValue).Select(turn => turn.Credits!.Value).DefaultIfEmpty(0).Max();
            result.Bars = turns.Select((turn, index) => new TurnBar
            {
                TurnIndex = turn.TurnIndex,
                Height = maximum > 0 && turn.Credits.HasValue ? Math.Max(2, (double)(turn.Credits.Value / maximum) * 22) : 2,
                IsNewest = index == turns.Length - 1,
                Tooltip = "Turn " + turn.TurnIndex.ToString(CultureInfo.InvariantCulture) + ": " + Number(turn.Credits)
                    + " local credits; last usage event " + Time(turn.TimestampUtc)
            }).ToArray();
            result.Chat = "Current chat - newest " + turns.Length.ToString(CultureInfo.InvariantCulture)
                + " recorded turns, including subagents; shared visible-window scale. Last " + Number(turns.LastOrDefault()?.Credits) + " credits.";
            result.Header += " | Last " + Number(turns.LastOrDefault()?.Credits) + " local credits";
            return result;
        }

        private static string Number(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture) ?? "unavailable";
        private static string Time(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "unknown";
    }
}