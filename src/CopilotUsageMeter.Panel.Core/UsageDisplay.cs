using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Panel.Core
{
    public enum GitHubLevel { Normal, Warning, Over, Unlimited, SignedOut, Unavailable }

    public enum LocalSegmentKind { Chat, RepositoryRest, Other }

    public sealed class DisplayRow
    {
        public DisplayRow(string label, string value) { Label = label; Value = value; }
        public string Label { get; }
        public string Value { get; }
    }

    public sealed class LocalSegment
    {
        public LocalSegmentKind Kind { get; set; }
        public string Label { get; set; } = "";
        public decimal Credits { get; set; }
        public string Text { get; set; } = "";
    }

    public sealed class DisplayBar
    {
        public int TurnIndex { get; set; }
        public double Fraction { get; set; }
        public bool IsNewest { get; set; }
        public string Tooltip { get; set; } = "";
    }

    // Presentation rules shared by the Visual Studio panel and the tray details window. No UI types.
    public sealed class UsageDisplay
    {
        public const decimal WarningFraction = 0.85m;
        public const int MaximumBars = 20;
        public const int MinimumVisibleBars = 4;
        public const double BarSlotWidth = 6;
        public const double MaximumBarSlotWidth = 16;
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

        public bool IsConnected { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsStale { get; private set; }

        public GitHubLevel Level { get; private set; } = GitHubLevel.Unavailable;
        public double? UsedFraction { get; private set; }
        public double? PaceFraction { get; private set; }
        public string GitHubTitle { get; private set; } = "GITHUB ACCOUNT";
        public string GitHubHeadline { get; private set; } = "GitHub credits unavailable";
        public string GitHubSummary { get; private set; } = "GitHub credits unavailable";
        public string PercentText { get; private set; } = "";
        public string PaceText { get; private set; } = "";
        public string ScheduleText { get; private set; } = "";
        public string StateNote { get; private set; } = "";

        public string LocalTitle { get; private set; } = "LOCAL · THIS MACHINE";
        public bool LocalAvailable { get; private set; }
        public string LocalTotalText { get; private set; } = "unavailable";
        public string LocalNote { get; private set; } = "Local figures are not added to the GitHub total.";
        public LocalSegment[] Segments { get; private set; } = Array.Empty<LocalSegment>();

        public bool HasChat { get; private set; }
        public string ChatText { get; private set; } = "No active chat";
        public string BarsCaption { get; private set; } = "";
        public string BarsSummary { get; private set; } = "";
        public string LastText { get; private set; } = "";
        public DisplayBar[] Bars { get; private set; } = Array.Empty<DisplayBar>();

        public DisplayRow[] AccountRows { get; private set; } = Array.Empty<DisplayRow>();
        public DisplayRow[] LocalRows { get; private set; } = Array.Empty<DisplayRow>();
        public DisplayRow[] DiagnosticRows { get; private set; } = Array.Empty<DisplayRow>();

        public string AccessibleSummary { get; private set; } = "";

        // Square-root scale: one very large turn no longer flattens the smaller ones into slivers. Exact values stay in the tooltips.
        public static double BarHeightFor(double fraction, double maxHeight, double minHeight)
        {
            var clamped = Math.Max(0, Math.Min(1, fraction));
            return Math.Min(maxHeight, minHeight + (maxHeight - minHeight) * Math.Sqrt(clamped));
        }

        // Spare room makes the bars wider (never narrower than the minimum slot, never wider than the maximum) so a roomy row is not a row of slivers.
        public static double BarSlotFor(double availableWidth, int count)
        {
            if (count <= 0) return BarSlotWidth;
            return Math.Max(BarSlotWidth, Math.Min(MaximumBarSlotWidth, Math.Floor(Math.Max(0, availableWidth) / count)));
        }

        // How many of the newest bars fit; fewer than the minimum is not a readable chart, so none are shown.
        public static int BarCountFor(double availableWidth, int available)
        {
            var fit = (int)Math.Floor(Math.Max(0, availableWidth) / BarSlotWidth);
            var count = Math.Min(Math.Min(MaximumBars, available), fit);
            return count < MinimumVisibleBars ? 0 : count;
        }

        public static string Number(decimal? value, string unknown = "unavailable") =>
            value.HasValue ? value.Value.ToString("#,##0.###", CultureInfo.InvariantCulture) : unknown;

        public static string Credits(decimal? value, string unknown = "unavailable") =>
            value.HasValue ? value.Value.ToString("#,##0.#", CultureInfo.InvariantCulture) : unknown;

        public static UsageDisplay Create(UsageSnapshot? snapshot, bool connected, DateTimeOffset now, TimeZoneInfo? zone = null)
        {
            zone ??= TimeZoneInfo.Local;
            var result = new UsageDisplay { IsConnected = connected };
            if (snapshot == null)
            {
                result.StateNote = connected ? "Waiting for the first usage update" : "Usage host offline";
                result.AccessibleSummary = "Copilot usage: " + result.StateNote;
                return result;
            }
            result.IsPaused = snapshot.IsPaused;
            result.BuildAccount(snapshot.Account, connected, now, zone);
            result.BuildLocal(snapshot.Local, snapshot.Account);
            result.BuildChat(snapshot.Local, zone);
            result.BuildRows(snapshot, zone);
            result.AccessibleSummary = "Copilot usage: GitHub " + result.GitHubSummary
                + (result.HasChat ? "; " + result.ChatText : "") + (result.StateNote.Length > 0 ? "; " + result.StateNote : "");
            return result;
        }

        private void BuildAccount(AccountUsage account, bool connected, DateTimeOffset now, TimeZoneInfo zone)
        {
            var period = PeriodStart(account.ResetUtc);
            GitHubTitle = "GITHUB ACCOUNT" + (period.HasValue ? " · " + MonthName(period.Value) : "");
            if (account.ResetUtc.HasValue && period.HasValue)
            {
                var span = (account.ResetUtc.Value - period.Value).TotalSeconds;
                if (span > 0) PaceFraction = Math.Max(0, Math.Min(1, (now - period.Value).TotalSeconds / span));
            }
            var hasFigures = account.Status == "available" && account.Allowance > 0 && account.Consumed.HasValue;
            if (account.Status == "signed-out")
            {
                Level = GitHubLevel.SignedOut;
                GitHubHeadline = GitHubSummary = "Not signed in";
                PaceFraction = null;
            }
            else if (account.Status == "available" && account.IsUnlimited)
            {
                Level = GitHubLevel.Unlimited;
                GitHubHeadline = GitHubSummary = "Unlimited";
                PaceFraction = null;
            }
            else if (!hasFigures)
            {
                Level = GitHubLevel.Unavailable;
                GitHubHeadline = GitHubSummary = "GitHub credits unavailable";
                PaceFraction = null;
            }
            else
            {
                var ratio = account.Consumed!.Value / account.Allowance!.Value;
                UsedFraction = (double)Math.Max(0m, Math.Min(1m, ratio));
                Level = ratio >= 1m ? GitHubLevel.Over : ratio >= WarningFraction ? GitHubLevel.Warning : GitHubLevel.Normal;
                PercentText = (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";
                GitHubHeadline = Number(account.Consumed) + " / " + Number(account.Allowance);
                GitHubSummary = Number(account.Consumed) + " of " + Number(account.Allowance) + " (" + PercentText + ")";
                if (PaceFraction.HasValue)
                    PaceText = "Month " + Math.Round(PaceFraction.Value * 100, 0).ToString("0", CultureInfo.InvariantCulture) + "% elapsed";
                if (Level == GitHubLevel.Over) StateNote = "Over the allowance";
                else if (Level == GitHubLevel.Warning) StateNote = "Close to the allowance";
            }
            var parts = new List<string>();
            if (account.ResetUtc.HasValue) parts.Add("Resets " + Day(account.ResetUtc.Value, zone));
            if (account.AsOfUtc.HasValue) parts.Add("Synced " + Clock(account.AsOfUtc.Value, zone));
            if (IsPaused) parts.Add("Paused");
            else if (account.NextRefreshUtc.HasValue) parts.Add("Next " + Clock(account.NextRefreshUtc.Value, zone));
            ScheduleText = string.Join(" · ", parts);
            if (!connected)
            {
                StateNote = "Usage host offline";
            }
            else if (!IsPaused && account.Status == "available" && account.AsOfUtc.HasValue && now - account.AsOfUtc.Value > StaleAfter)
            {
                IsStale = true;
                StateNote = "Stale since " + Clock(account.AsOfUtc.Value, zone);
            }
            else if (IsPaused)
            {
                StateNote = "Updates paused";
            }
        }

        private void BuildLocal(LocalUsage local, AccountUsage account)
        {
            var month = local.WindowStartUtc;
            LocalTitle = "LOCAL · THIS MACHINE" + (month.HasValue ? " · " + MonthName(month.Value) : "");
            LocalAvailable = local.Status == "available" && local.OverallCredits.HasValue;
            var chatTotal = ChatTotal(local.CurrentChat);
            var repository = local.RepositoryCredits;
            var overall = local.OverallCredits;
            var segments = new List<LocalSegment>();
            if (LocalAvailable)
            {
                LocalTotalText = Credits(overall) + " total";
                var chat = Math.Min(chatTotal ?? 0m, overall!.Value);
                var knownRepository = repository.HasValue ? Math.Min(Math.Max(repository.Value, chat), overall.Value) : (decimal?)null;
                if (HasChatTotal(local)) segments.Add(Segment(LocalSegmentKind.Chat, "This chat", chat));
                if (knownRepository.HasValue)
                    segments.Add(Segment(LocalSegmentKind.RepositoryRest, "Rest of this repo", knownRepository.Value - chat));
                var other = overall.Value - (knownRepository ?? chat);
                segments.Add(Segment(LocalSegmentKind.Other, knownRepository.HasValue ? "Other repos" : "Rest of local use", other));
                LocalNote = knownRepository.HasValue
                    ? (HasChatTotal(local) ? "This repo totals " + Credits(knownRepository) + " (includes this chat). " : "Repo total includes any chats. ") + "Local figures are not added to the GitHub total."
                    : "Local figures are not added to the GitHub total.";
            }
            else
            {
                LocalTotalText = "unavailable";
                LocalNote = "Local history is unavailable. It is never shown as zero.";
            }
            Segments = segments.ToArray();
        }

        private static bool HasChatTotal(LocalUsage local) => local.CurrentChat.Status == "available" && ChatTotal(local.CurrentChat).HasValue;

        private static LocalSegment Segment(LocalSegmentKind kind, string label, decimal credits)
        {
            credits = Math.Max(0m, credits);
            return new LocalSegment { Kind = kind, Label = label, Credits = credits, Text = Credits(credits) };
        }

        // The tool's whole-chat total wins; older payloads only have the newest turns to sum.
        private static decimal? ChatTotal(CurrentChatUsage chat)
        {
            if (chat.Status != "available" || chat.Turns == null || chat.Turns.Length == 0) return null;
            if (chat.TotalCredits.HasValue) return chat.TotalCredits;
            var known = chat.Turns.Where(turn => turn != null && turn.Credits.HasValue).ToArray();
            return known.Length == 0 ? (decimal?)null : known.Sum(turn => turn.Credits!.Value);
        }

        private void BuildChat(LocalUsage local, TimeZoneInfo zone)
        {
            var chat = local.CurrentChat;
            if (chat.Status != "available" || chat.Turns == null || chat.Turns.Length == 0)
            {
                ChatText = "No active chat";
                return;
            }
            var turns = chat.Turns.Where(turn => turn != null && turn.TurnIndex >= 0).OrderBy(turn => turn.TurnIndex).ToArray();
            turns = turns.Skip(Math.Max(0, turns.Length - MaximumBars)).ToArray();
            if (turns.Length == 0)
            {
                ChatText = "No active chat";
                return;
            }
            HasChat = true;
            var values = turns.Where(turn => turn.Credits.HasValue).Select(turn => turn.Credits!.Value).ToArray();
            var maximum = values.Length == 0 ? 0m : values.Max();
            Bars = turns.Select((turn, index) => new DisplayBar
            {
                TurnIndex = turn.TurnIndex,
                Fraction = maximum > 0 && turn.Credits.HasValue ? (double)(turn.Credits.Value / maximum) : 0,
                IsNewest = index == turns.Length - 1,
                Tooltip = "Turn " + turn.TurnIndex.ToString(CultureInfo.InvariantCulture) + ": " + Number(turn.Credits) + " credits"
                    + (turn.TimestampUtc.HasValue ? " · " + Day(turn.TimestampUtc.Value, zone) + ", " + Clock(turn.TimestampUtc.Value, zone) : "")
            }).ToArray();
            var total = ChatTotal(chat);
            ChatText = "Chat " + Credits(total);
            BarsCaption = "LAST " + turns.Length.ToString(CultureInfo.InvariantCulture) + " TURNS";
            var last = turns[turns.Length - 1].Credits;
            LastText = "last " + Credits(last, "n/a");
            BarsSummary = values.Length == 0 ? "" : "Last " + Credits(last, "n/a") + " · Avg " + Credits(values.Average()) + " · Max " + Credits(maximum);
        }

        private void BuildRows(UsageSnapshot snapshot, TimeZoneInfo zone)
        {
            var account = snapshot.Account;
            var local = snapshot.Local;
            var accountRows = new List<DisplayRow>
            {
                new DisplayRow("Status", AccountStatus(account.Status)),
                new DisplayRow("Account", account.Login ?? "Not signed in"),
                new DisplayRow("Used", account.Consumed.HasValue ? Number(account.Consumed) + (PercentText.Length > 0 ? " (" + PercentText + " of allowance)" : "") : "unavailable"),
                new DisplayRow("Allowance", account.IsUnlimited ? "Unlimited" : Number(account.Allowance)),
                new DisplayRow("Reported remaining", Number(account.Remaining)),
                new DisplayRow("Unit", account.Unit),
                new DisplayRow("Resets", account.ResetUtc.HasValue ? Day(account.ResetUtc.Value, zone) + ", " + Clock(account.ResetUtc.Value, zone) : "unknown"),
                new DisplayRow("Last sync", account.AsOfUtc.HasValue ? Day(account.AsOfUtc.Value, zone) + ", " + Clock(account.AsOfUtc.Value, zone) : "never"),
                new DisplayRow("Next sync", IsPaused ? "Paused" : account.NextRefreshUtc.HasValue ? Clock(account.NextRefreshUtc.Value, zone) : "not scheduled"),
                new DisplayRow("Source", account.Source ?? "Account source unavailable (GitHub, all machines)")
            };
            if (account.ErrorCode != null) accountRows.Add(new DisplayRow("Problem", account.ErrorCode));
            AccountRows = accountRows.ToArray();
            var localRows = new List<DisplayRow>
            {
                new DisplayRow("Status", AccountStatus(local.Status)),
                new DisplayRow("Month (UTC)", local.WindowStartUtc.HasValue ? local.WindowStartUtc.Value.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture) : "unknown"),
                new DisplayRow("All local use", local.OverallCredits.HasValue ? Credits(local.OverallCredits) + " credits" : "unavailable"),
                new DisplayRow("This repository", (local.RepositoryCredits.HasValue ? Credits(local.RepositoryCredits) + " credits" : "unavailable") + " (exact recorded GitHub origin, not folder attribution)"),
                new DisplayRow("Tokens processed", local.Status == "available"
                    ? "input " + local.InputTokens.ToString("#,##0", CultureInfo.InvariantCulture) + " · output " + local.OutputTokens.ToString("#,##0", CultureInfo.InvariantCulture)
                    : "unavailable"),
                new DisplayRow("As of", local.AsOfUtc.HasValue ? Day(local.AsOfUtc.Value, zone) + ", " + Clock(local.AsOfUtc.Value, zone) : "unknown")
            };
            if (local.ErrorCode != null) localRows.Add(new DisplayRow("Problem", local.ErrorCode));
            LocalRows = localRows.ToArray();
            DiagnosticRows = new[]
            {
                new DisplayRow("Host", snapshot.HostInstanceId.Length > 8 ? snapshot.HostInstanceId.Substring(0, 8) : snapshot.HostInstanceId),
                new DisplayRow("Update number", snapshot.Revision.ToString(CultureInfo.InvariantCulture)),
                new DisplayRow("Local refreshes", snapshot.RefreshCount.ToString(CultureInfo.InvariantCulture))
            };
        }

        private static string AccountStatus(string status)
        {
            switch (status)
            {
                case "available": return "Available";
                case "signed-out": return "Not signed in";
                case "unavailable": return "Unavailable";
                case "error": return "Error";
                default: return status;
            }
        }

        private static DateTimeOffset? PeriodStart(DateTimeOffset? reset) => reset?.ToUniversalTime().AddMonths(-1);
        private static string MonthName(DateTimeOffset value) => value.ToUniversalTime().ToString("MMMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        private static string Day(DateTimeOffset value, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(value, zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        private static string Clock(DateTimeOffset value, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(value, zone).ToString("h:mm tt", CultureInfo.InvariantCulture);
    }
}
