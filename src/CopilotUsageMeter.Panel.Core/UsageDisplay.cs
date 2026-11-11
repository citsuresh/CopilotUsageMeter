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

    // A user-visible problem: a short title for the icon and row, a fuller explanation for the popup, and the machine code for support.
    public enum ProblemSeverity { Warning, Error }

    public sealed class DisplayProblem
    {
        // Errors need the user to act; everything else may recover on its own or does not stop tracking.
        private static readonly HashSet<string> ErrorCodes = new HashSet<string>
        {
            "secure-storage-failed", "profile-mismatch", "invalid-login", "invalid-response",
            "history_tool_missing", "history_invalid_configuration", "history_invalid_data", "history_output_limit"
        };

        public DisplayProblem(string scope, string title, string detail, string? code)
        {
            Scope = scope;
            Title = title;
            Detail = detail;
            Code = code;
            Severity = code != null && ErrorCodes.Contains(code) ? ProblemSeverity.Error : ProblemSeverity.Warning;
        }

        public ProblemSeverity Severity { get; }
        public string Scope { get; }
        public string Title { get; }
        public string Detail { get; }
        public string? Code { get; }
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

        // Set only while the host is reachable. The account problem replaces the meter figures; the local one explains a missing or partial local total.
        public DisplayProblem? AccountProblem { get; private set; }
        public DisplayProblem? LocalProblem { get; private set; }
        public bool HasProblem => AccountProblem != null || LocalProblem != null;
        public ProblemSeverity? Severity =>
            !HasProblem ? (ProblemSeverity?)null
            : AccountProblem?.Severity == ProblemSeverity.Error || LocalProblem?.Severity == ProblemSeverity.Error ? ProblemSeverity.Error
            : ProblemSeverity.Warning;
        public string ProblemTooltip { get; private set; } = "";

        public string LocalTitle { get; private set; } = "LOCAL · THIS MACHINE";
        public bool LocalAvailable { get; private set; }
        public string LocalTotalText { get; private set; } = "unavailable";
        public string LocalNote { get; private set; } = "Local figures are not added to the GitHub total.";
        public LocalSegment[] Segments { get; private set; } = Array.Empty<LocalSegment>();

        public bool IsVersionMismatch { get; private set; }

        public const string VersionMismatchLabel = "Update needed";
        public const string VersionMismatchHeading = "EXTENSION AND TRAY VERSIONS DO NOT MATCH";
        public const string VersionMismatchTitle = "Extension and tray versions do not match";

        public static string VersionMismatchDetail(string extensionVersion) =>
            "This extension" + (extensionVersion.Length > 0 ? " (v" + extensionVersion + ")" : "") + " and the Copilot Usage Meter tray app speak different protocol versions, so figures are not updating. Update both to the same release, then reopen this window.";

        public static string NotConnectedNote(bool versionMismatch) => versionMismatch ? VersionMismatchLabel : "Usage tray not running";

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

        public static UsageDisplay Create(UsageSnapshot? snapshot, bool connected, DateTimeOffset now, TimeZoneInfo? zone = null, bool versionMismatch = false)
        {
            zone ??= TimeZoneInfo.Local;
            var result = new UsageDisplay { IsConnected = connected, IsVersionMismatch = versionMismatch && !connected };
            if (snapshot == null)
            {
                result.StateNote = connected ? "Waiting for the first usage update" : NotConnectedNote(result.IsVersionMismatch);
                result.AccessibleSummary = "Copilot usage: " + result.StateNote;
                return result;
            }
            result.IsPaused = snapshot.IsPaused;
            result.BuildAccount(snapshot.Account, connected, now, zone);
            result.BuildLocal(snapshot.Local, snapshot.Account);
            result.BuildChat(snapshot.Local, zone);
            result.BuildProblems(snapshot, connected, zone);
            result.BuildRows(snapshot, zone);
            result.AccessibleSummary = "Copilot usage: GitHub " + result.GitHubSummary
                + (result.HasChat ? "; " + result.ChatText : "") + (result.StateNote.Length > 0 ? "; " + result.StateNote : "");
            return result;
        }

        private void BuildAccount(AccountUsage account, bool connected, DateTimeOffset now, TimeZoneInfo zone)
        {
            var period = PeriodStart(account.ResetUtc);
            GitHubTitle = "GITHUB ACCOUNT" + (string.IsNullOrEmpty(account.Login) ? "" : " · @" + account.Login) + (period.HasValue ? " · " + MonthName(period.Value) : "");
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
                StateNote = NotConnectedNote(IsVersionMismatch);
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

        // Anything that stops the account or local figures from being real numbers is a problem. Signed out, skipped and "not synced yet" are not.
        private void BuildProblems(UsageSnapshot snapshot, bool connected, TimeZoneInfo zone)
        {
            if (!connected) return;
            AccountProblem = AccountProblemFor(snapshot.Account, zone);
            LocalProblem = LocalProblemFor(snapshot.Local);
            if (AccountProblem != null) GitHubHeadline = GitHubSummary = AccountProblem.Title;
            var lines = new List<string>();
            foreach (var problem in new[] { AccountProblem, LocalProblem })
            {
                if (problem == null) continue;
                lines.Add(problem.Scope + ": " + problem.Title + "\n" + problem.Detail + (problem.Code != null ? "\nError code: " + problem.Code : ""));
            }
            ProblemTooltip = string.Join("\n\n", lines);
        }

        private static DisplayProblem? AccountProblemFor(AccountUsage account, TimeZoneInfo zone)
        {
            var status = account.Status;
            if (status == "signed-out")
            {
                var signedOutDetail = account.ErrorCode == "authorization-expired"
                    ? "GitHub rejected the saved sign-in, so it was removed. Sign in again to see account credits. Local tracking continues."
                    : "Sign in to see account credits. Local tracking works without signing in.";
                return new DisplayProblem("GitHub", "Not signed in", signedOutDetail, account.ErrorCode);
            }
            if (status != "error" && status != "rate-limited" && status != "unavailable") return null;
            var code = account.ErrorCode;
            if (status == "unavailable" && (code == null || code == "not-synced")) return null;
            var who = string.IsNullOrEmpty(account.Login) ? "this account" : "@" + account.Login;
            const string retry = " Use Refresh to try again now.";
            string title, detail;
            switch (code)
            {
                case "github-rate-limited":
                    title = "GitHub is limiting requests";
                    detail = "GitHub temporarily limited account requests." + (account.NextRefreshUtc.HasValue ? " The next attempt is at " + Clock(account.NextRefreshUtc.Value, zone) + "." : " Requests retry automatically.");
                    break;
                case "github-request-timeout":
                    title = "GitHub request timed out";
                    detail = "GitHub did not answer in time. Requests retry automatically." + retry;
                    break;
                case "github-request-failed":
                case "github-http-failed":
                    title = "GitHub request failed";
                    detail = "GitHub could not be reached or rejected the request. Requests retry automatically." + retry;
                    break;
                case "quota-unavailable":
                    title = "No usage figures for " + who;
                    detail = "GitHub returned no Copilot allowance for " + who + ". The account may have no Copilot subscription, or the sign-in may lack permission. Local usage is still tracked.";
                    break;
                case "unsupported-plan":
                    title = "Plan not supported";
                    detail = "GitHub does not expose a premium-interaction quota for the plan of " + who + ". Local usage is still tracked.";
                    break;
                case "unsupported-billing":
                    title = "Billing format not supported";
                    detail = "GitHub reports usage for " + who + " in a billing format this app cannot read. Local usage is still tracked.";
                    break;
                case "secure-storage-failed":
                    title = "Credential storage failed";
                    detail = "The saved GitHub credential could not be read or written. Sign out and sign in again.";
                    break;
                case "profile-mismatch":
                    title = "Account mismatch";
                    detail = "The credential belongs to a different GitHub account than the one selected. Sign in again.";
                    break;
                case "invalid-response":
                    title = "Unexpected GitHub response";
                    detail = "GitHub returned data this app does not understand." + retry;
                    break;
                case "invalid-login":
                    title = "Invalid GitHub login";
                    detail = "The account name is not a valid GitHub login. Choose a saved account or sign in again.";
                    break;
                default:
                    title = status == "rate-limited" ? "GitHub is limiting requests" : status == "error" ? "GitHub problem" : "GitHub usage unavailable";
                    detail = "GitHub account usage could not be read." + retry;
                    break;
            }
            return new DisplayProblem("GitHub", title, detail, code);
        }

        private static DisplayProblem? LocalProblemFor(LocalUsage local)
        {
            var code = local.ErrorCode;
            if (code == null || code == "history_not_recorded" || code == "history_no_turns" || code == "history_session_unavailable") return null;
            string title, detail;
            switch (code)
            {
                case "history_tool_missing":
                    title = "Chat history tool not found";
                    detail = "The Copilot chat history tool is missing, so local usage cannot be read.";
                    break;
                case "history_invalid_configuration":
                    title = "Chat history tool misconfigured";
                    detail = "The chat history tool is not set up correctly, so local usage cannot be read.";
                    break;
                case "history_invalid_data":
                case "history_output_limit":
                    title = "Unreadable local history";
                    detail = "The chat history tool returned data this app could not use. Local figures may be missing.";
                    break;
                case "history_process_failed":
                case "history_command_failed":
                case "local_refresh_failed":
                case "local_refresh_faulted":
                    title = "Local refresh failed";
                    detail = "Reading local chat history failed. It retries on the next change or refresh.";
                    break;
                case "history_watch_unavailable":
                case "history_watch_error":
                case "history_watch_faulted":
                case "history_watch_metadata_failed":
                case "history_watch_directory_unavailable":
                    title = "Local history is not being watched";
                    detail = "Changes to local chat history may not be noticed automatically. Use Refresh to update.";
                    break;
                default:
                    title = "Local tracking problem";
                    detail = "Local usage could not be fully read.";
                    break;
            }
            return new DisplayProblem("Local", title, detail, code);
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
            ChatText = "This chat " + Credits(total);
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
            if (account.ErrorCode != null) accountRows.Add(new DisplayRow("Problem", AccountProblem != null ? AccountProblem.Title + " (" + account.ErrorCode + ")" : account.ErrorCode));
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
            if (local.ErrorCode != null) localRows.Add(new DisplayRow("Problem", LocalProblem != null ? LocalProblem.Title + " (" + local.ErrorCode + ")" : local.ErrorCode));
            LocalRows = localRows.ToArray();
            DiagnosticRows = new[]
            {
                new DisplayRow("Host", snapshot.HostInstanceId.Length > 8 ? snapshot.HostInstanceId.Substring(0, 8) : snapshot.HostInstanceId),
                new DisplayRow("Update number", snapshot.Revision.ToString(CultureInfo.InvariantCulture)),
                new DisplayRow("Local refreshes", snapshot.RefreshCount.ToString(CultureInfo.InvariantCulture))
            };
        }

        public const int TrayTooltipLimit = 127;

        // Tray icon text: usage percent, last and next GitHub sync, and what to do when the account is not usable.
        public static string TrayTooltip(UsageSnapshot? snapshot, DateTimeOffset now, TimeZoneInfo? zone = null)
        {
            zone ??= TimeZoneInfo.Local;
            const string title = "Copilot Usage Meter";
            if (snapshot == null) return title + "\nStarting...";
            var account = snapshot.Account;
            var lines = new List<string> { title };
            var synced = account.AsOfUtc.HasValue ? "Last synced " + Clock(account.AsOfUtc.Value, zone) : "Not synced yet";
            var next = !snapshot.IsPaused && account.NextRefreshUtc.HasValue ? " · next " + Clock(account.NextRefreshUtc.Value, zone) : "";
            switch (account.Status)
            {
                case "available" when account.IsUnlimited:
                    lines.Add("Unlimited Copilot credits");
                    lines.Add(synced + next);
                    break;
                case "available" when account.Allowance > 0 && account.Consumed.HasValue:
                    var ratio = account.Consumed.Value / account.Allowance.Value;
                    lines.Add("Copilot " + (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "% used" + (ratio >= 1m ? " (over allowance)" : " (" + Number(account.Consumed) + " of " + Number(account.Allowance) + ")"));
                    var stale = !snapshot.IsPaused && account.AsOfUtc.HasValue && now - account.AsOfUtc.Value > StaleAfter;
                    lines.Add(synced + (stale ? " (stale)" : "") + next);
                    break;
                case "signed-out":
                case "skipped":
                    lines.Add(account.ErrorCode == "authorization-expired" ? "GitHub sign-in expired" : "Not signed in to GitHub");
                    lines.Add("Open Status / Settings to sign in");
                    break;
                case "rate-limited":
                    lines.Add("GitHub is limiting requests");
                    lines.Add(synced + (next.Length > 0 ? next.Replace("next", "retry") : ""));
                    break;
                case "error":
                    lines.Add("GitHub error: " + ErrorSummary(account.ErrorCode));
                    lines.Add(synced + (next.Length > 0 ? next.Replace("next", "retry") : ""));
                    break;
                default:
                    var problem = AccountProblemFor(account, zone);
                    lines.Add(problem != null ? "GitHub: " + problem.Title : "GitHub credits unavailable");
                    lines.Add(synced + next);
                    break;
            }
            if (snapshot.IsPaused) lines.Add("Updates paused");
            if (snapshot.Local.Status != "available") lines.Add("Local history unavailable");
            // Drop the least important lines first so the 127-character limit never cuts a line in half.
            while (lines.Count > 2 && string.Join("\n", lines).Length > TrayTooltipLimit) lines.RemoveAt(lines.Count - 1);
            var text = string.Join("\n", lines);
            return text.Length > TrayTooltipLimit ? text.Substring(0, TrayTooltipLimit) : text;
        }

        private static string ErrorSummary(string? code)
        {
            switch (code)
            {
                case "github-request-failed":
                case "github-http-failed": return "request failed";
                case "github-request-timeout": return "timed out";
                case "secure-storage-failed": return "credential storage";
                case "profile-mismatch": return "account mismatch";
                case "invalid-response": return "unexpected response";
                case "invalid-login": return "invalid login";
                default: return "see Status";
            }
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
