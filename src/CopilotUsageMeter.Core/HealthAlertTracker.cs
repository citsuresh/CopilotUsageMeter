using System;
using System.Collections.Generic;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Core
{
    // Problems the tray can tell the user about. Names are written to the log, so they never carry account or chat data.
    public enum HealthCondition
    {
        SignInNeeded,
        AuthorizationExpired,
        AccountUsageUnavailable,
        GitHubUnreachable,
        GitHubError,
        LocalReadFailing,
        LocalSetupProblem,
        TrackingFault,
    }

    public sealed class HealthAlert
    {
        public HealthAlert(HealthCondition condition) => Condition = condition;
        public HealthCondition Condition { get; }
    }

    // Fixed wording only. Nothing from GitHub, the local history or exceptions is ever put into a notification.
    public static class HealthAlertText
    {
        public static string Title(HealthCondition condition)
        {
            switch (condition)
            {
                case HealthCondition.SignInNeeded: return "Copilot Usage Meter: sign in to GitHub";
                case HealthCondition.AuthorizationExpired: return "Copilot Usage Meter: sign in again";
                case HealthCondition.AccountUsageUnavailable: return "Copilot Usage Meter: usage not available";
                case HealthCondition.GitHubUnreachable: return "Copilot Usage Meter: GitHub not reachable";
                case HealthCondition.GitHubError: return "Copilot Usage Meter: GitHub usage failed";
                case HealthCondition.LocalReadFailing: return "Copilot Usage Meter: local usage failed";
                case HealthCondition.LocalSetupProblem: return "Copilot Usage Meter: local tracking not set up";
                default: return "Copilot Usage Meter: tracking problem";
            }
        }

        public static string Body(HealthCondition condition)
        {
            switch (condition)
            {
                case HealthCondition.SignInNeeded:
                    return "Sign in to see your GitHub-reported usage. Click to open Full details; local tracking works without signing in.";
                case HealthCondition.AuthorizationExpired:
                    return "GitHub no longer accepts the saved sign-in, so account usage is not updating. Click to open Full details and sign in again.";
                case HealthCondition.AccountUsageUnavailable:
                    return "GitHub is not reporting Copilot usage for this account. It may have no supported Copilot plan or restricted access. Click for details.";
                case HealthCondition.GitHubUnreachable:
                    return "GitHub could not be reached for a while, so account usage is out of date. The tray keeps retrying.";
                case HealthCondition.GitHubError:
                    return "GitHub usage could not be read several times in a row. Click for details; the tray keeps retrying.";
                case HealthCondition.LocalReadFailing:
                    return "Local usage could not be read several times in a row, so local figures are out of date. The tray keeps retrying.";
                case HealthCondition.LocalSetupProblem:
                    return "Local usage tracking needs the Copilot chat history tool, which is missing or misconfigured. Click for details.";
                default:
                    return "A background task failed and is restarting. Click for details; details were written to the tray log.";
            }
        }
    }

    // Decides when a problem is worth a notification: only once it has persisted, only once per problem, and not again
    // for the same problem within the cool-down, so a flapping connection cannot flood the user.
    public sealed class HealthAlertTracker
    {
        public static readonly TimeSpan Cooldown = TimeSpan.FromHours(6);

        private sealed class Rule
        {
            public Rule(int observations, TimeSpan duration) { Observations = observations; Duration = duration; }
            public int Observations { get; }
            public TimeSpan Duration { get; }
        }

        private sealed class Progress
        {
            public int Count;
            public DateTimeOffset? FirstBad;
            public bool Notified;
            public DateTimeOffset? LastNotified;
        }

        private static readonly Dictionary<HealthCondition, Rule> Rules = new Dictionary<HealthCondition, Rule>
        {
            { HealthCondition.SignInNeeded, new Rule(1, TimeSpan.Zero) },
            { HealthCondition.AuthorizationExpired, new Rule(1, TimeSpan.Zero) },
            { HealthCondition.AccountUsageUnavailable, new Rule(2, TimeSpan.Zero) },
            // A dropped connection is expected on laptops; only a sustained outage is worth a notification.
            { HealthCondition.GitHubUnreachable, new Rule(3, TimeSpan.FromMinutes(30)) },
            { HealthCondition.GitHubError, new Rule(3, TimeSpan.Zero) },
            { HealthCondition.LocalReadFailing, new Rule(2, TimeSpan.Zero) },
            { HealthCondition.LocalSetupProblem, new Rule(1, TimeSpan.Zero) },
            { HealthCondition.TrackingFault, new Rule(1, TimeSpan.Zero) },
        };

        private readonly object gate = new object();
        private readonly Dictionary<HealthCondition, Progress> progress = new Dictionary<HealthCondition, Progress>();

        // A good observation re-arms the condition. While notifications are off a problem is still remembered as
        // already seen, so switching them on later stays quiet until the problem recurs.
        public HealthAlert? Observe(HealthCondition condition, bool bad, DateTimeOffset now, bool enabled)
        {
            lock (gate)
            {
                if (!progress.TryGetValue(condition, out var entry)) progress[condition] = entry = new Progress();
                if (!bad)
                {
                    entry.Count = 0;
                    entry.FirstBad = null;
                    entry.Notified = false;
                    return null;
                }
                entry.Count++;
                if (!entry.FirstBad.HasValue) entry.FirstBad = now;
                if (entry.Notified) return null;
                var rule = Rules[condition];
                if (entry.Count < rule.Observations || now - entry.FirstBad.Value < rule.Duration) return null;
                entry.Notified = true;
                if (!enabled) return null;
                if (entry.LastNotified.HasValue && now - entry.LastNotified.Value < Cooldown) return null;
                entry.LastNotified = now;
                return new HealthAlert(condition);
            }
        }

        // Re-arms every condition in the list, used when the state they describe no longer applies (for example after signing out).
        public void Clear(IEnumerable<HealthCondition> conditions, DateTimeOffset now)
        {
            foreach (var condition in conditions) Observe(condition, bad: false, now, enabled: false);
        }
    }

    // Maps raw read results to health observations. Kept free of UI so the rules can be tested directly.
    public static class HealthSignals
    {
        public static readonly HealthCondition[] AccountConditions =
        {
            HealthCondition.AuthorizationExpired,
            HealthCondition.AccountUsageUnavailable,
            HealthCondition.GitHubUnreachable,
            HealthCondition.GitHubError,
        };

        // True when the result stands for a problem, with the condition it maps to. Rate limiting is temporary and says
        // nothing about the account, so it sets ignore and leaves the earlier observations untouched.
        public static bool TryClassifyAccount(AccountReadResult? result, out HealthCondition condition, out bool ignore)
        {
            condition = HealthCondition.GitHubError;
            ignore = false;
            if (result == null) return false;
            var code = result.ErrorCode;
            if (result.Status == "rate-limited") { ignore = true; return false; }
            if (result.Status == "signed-out" && code == "authorization-expired") { condition = HealthCondition.AuthorizationExpired; return true; }
            if (result.Status == "unavailable" && (code == "quota-unavailable" || code == "unsupported-billing" || code == "unsupported-plan"))
            {
                condition = HealthCondition.AccountUsageUnavailable;
                return true;
            }
            if (result.Status == "error")
            {
                condition = code == "github-request-failed" || code == "github-request-timeout" ? HealthCondition.GitHubUnreachable : HealthCondition.GitHubError;
                return true;
            }
            return false;
        }

        // A local read is a problem unless it is available or one of the normal "nothing recorded yet" states.
        public static bool TryClassifyLocal(string? status, string? code, out HealthCondition condition)
        {
            condition = HealthCondition.LocalReadFailing;
            if (status == "available" || LocalHistoryErrorCodes.IsInformational(code)) return false;
            if (LocalHistoryErrorCodes.IsPermanent(code)) condition = HealthCondition.LocalSetupProblem;
            return true;
        }
    }
}
