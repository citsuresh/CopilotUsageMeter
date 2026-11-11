using System;
using System.Collections.Generic;
using CopilotUsageMeter.Client;

namespace CopilotUsageMeter.Panel.Core
{
    // Problems the extension tells the user about with a Visual Studio infobar. Names are written to the log, so they never carry chat data.
    public enum ExtensionNotice
    {
        TrayUnavailable,
        VersionMismatch,
        ChatLayoutUnrecognised,
    }

    public enum NoticeAction
    {
        None,
        Show,
        Close,
    }

    public sealed class NoticeChange
    {
        public NoticeChange(ExtensionNotice notice, NoticeAction action)
        {
            Notice = notice;
            Action = action;
        }

        public ExtensionNotice Notice { get; }
        public NoticeAction Action { get; }
    }

    // Fixed wording only. Nothing from the tray, the chat or exceptions is ever put into an infobar.
    public static class ExtensionNoticeText
    {
        public static string Message(ExtensionNotice notice)
        {
            switch (notice)
            {
                case ExtensionNotice.TrayUnavailable:
                    return "Copilot Usage Meter cannot reach the tray app, so usage figures are not updating. Start Copilot Usage Meter from the Windows tray; this notice closes when it connects.";
                case ExtensionNotice.VersionMismatch:
                    return "Copilot Usage Meter: the tray app and this extension are different versions. Update both to the same version.";
                default:
                    return "Copilot Usage Meter does not recognise this version of the Copilot Chat layout, so the usage row is shown in a fallback position. Tools > Copilot Usage Meter has placement options.";
            }
        }
    }

    // What the pipe client's status text says about the tray connection.
    public enum TrayConnection
    {
        Connected,
        Unavailable,
        VersionMismatch,
        Transitional,
    }

    public static class ExtensionSignals
    {
        public static TrayConnection Classify(string? status)
        {
            switch (status)
            {
                case "Connected":
                case "Paused":
                case "Connected - demo":
                case "Paused - demo":
                    return TrayConnection.Connected;
                case PipeUsageClient.VersionMismatchStatus:
                    return TrayConnection.VersionMismatch;
                // The client cycles through Connecting while the tray is down, so these all count as an outage.
                case "Disconnected":
                case "Connecting":
                case "Tray unavailable - cached values are stale":
                case "Protocol error - reconnecting":
                case "Internal error - reconnecting":
                    return TrayConnection.Unavailable;
                default:
                    // Resynchronizing, Stopped and anything unknown neither start nor end an outage.
                    return TrayConnection.Transitional;
            }
        }
    }

    // Decides when a problem is worth an infobar: only once it has persisted, only once per problem, and not again for the
    // same problem within the cool-down, so a flapping connection cannot flood the user. A good observation closes the
    // infobar and re-arms the problem.
    public sealed class ExtensionNoticeTracker
    {
        public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        private sealed class Progress
        {
            public DateTimeOffset? FirstBad;
            public bool Handled;
            public bool Visible;
            public DateTimeOffset? LastShown;
            public DateTimeOffset? RetryAfter;
        }

        private static readonly Dictionary<ExtensionNotice, TimeSpan> Delays = new Dictionary<ExtensionNotice, TimeSpan>
        {
            // Visual Studio can start before the tray, and the client reconnects within seconds when the tray restarts.
            { ExtensionNotice.TrayUnavailable, TimeSpan.FromSeconds(20) },
            { ExtensionNotice.VersionMismatch, TimeSpan.FromSeconds(5) },
            // The layout is looked up on a timer, so allow for the chat pane still loading.
            { ExtensionNotice.ChatLayoutUnrecognised, TimeSpan.FromSeconds(30) },
        };

        private readonly object gate = new object();
        private readonly Dictionary<ExtensionNotice, Progress> progress = new Dictionary<ExtensionNotice, Progress>();

        public NoticeAction Observe(ExtensionNotice notice, bool bad, DateTimeOffset now)
        {
            lock (gate)
            {
                if (!progress.TryGetValue(notice, out var entry)) progress[notice] = entry = new Progress();
                if (!bad)
                {
                    var wasVisible = entry.Visible;
                    entry.FirstBad = null;
                    entry.Handled = false;
                    entry.Visible = false;
                    entry.RetryAfter = null;
                    return wasVisible ? NoticeAction.Close : NoticeAction.None;
                }
                if (!entry.FirstBad.HasValue) entry.FirstBad = now;
                if (entry.Handled || now - entry.FirstBad.Value < Delays[notice]) return NoticeAction.None;
                if (entry.RetryAfter.HasValue && now < entry.RetryAfter.Value) return NoticeAction.None;
                // Inside the cool-down the notice stays pending, so a long outage is still reported once the cool-down has passed.
                if (entry.LastShown.HasValue && now - entry.LastShown.Value < Cooldown) return NoticeAction.None;
                entry.Handled = true;
                entry.LastShown = now;
                entry.Visible = true;
                return NoticeAction.Show;
            }
        }

        // Visual Studio could not show the infobar (for example its host is not ready yet), so the notice is tried again after a short wait.
        public void ShowFailed(ExtensionNotice notice, DateTimeOffset now)
        {
            lock (gate)
            {
                if (!progress.TryGetValue(notice, out var entry)) return;
                entry.Handled = false;
                entry.Visible = false;
                entry.LastShown = null;
                entry.RetryAfter = now + RetryDelay;
            }
        }

        // The user closed the infobar, so there is nothing left for the tracker to close.
        public void Dismissed(ExtensionNotice notice)
        {
            lock (gate)
            {
                if (progress.TryGetValue(notice, out var entry)) entry.Visible = false;
            }
        }

        // Evaluates all notices for one tick; only the ones with something to do are returned.
        public IReadOnlyList<NoticeChange> Evaluate(string? trayStatus, bool layoutUnrecognised, DateTimeOffset now)
        {
            var changes = new List<NoticeChange>();
            var connection = ExtensionSignals.Classify(trayStatus);
            if (connection != TrayConnection.Transitional)
            {
                Add(changes, ExtensionNotice.TrayUnavailable, connection == TrayConnection.Unavailable, now);
                Add(changes, ExtensionNotice.VersionMismatch, connection == TrayConnection.VersionMismatch, now);
            }
            Add(changes, ExtensionNotice.ChatLayoutUnrecognised, layoutUnrecognised, now);
            return changes;
        }

        private void Add(List<NoticeChange> changes, ExtensionNotice notice, bool bad, DateTimeOffset now)
        {
            var action = Observe(notice, bad, now);
            if (action != NoticeAction.None) changes.Add(new NoticeChange(notice, action));
        }
    }
}
