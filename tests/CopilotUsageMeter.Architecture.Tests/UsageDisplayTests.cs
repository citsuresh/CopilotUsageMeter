using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class UsageDisplayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 16, 51, 0, TimeSpan.Zero);

    private static UsageSnapshot Snapshot(decimal consumed = 6339, decimal allowance = 100000, int turns = 3, DateTimeOffset? asOf = null) => new()
    {
        HostInstanceId = "0123456789abcdef", Revision = 7, RefreshCount = 3, IsDemo = false,
        Account = new AccountUsage
        {
            Status = "available", Unit = "github-premium-interactions", Allowance = allowance, Consumed = consumed, Remaining = allowance - consumed,
            AsOfUtc = asOf ?? Now.AddMinutes(-5), NextRefreshUtc = Now.AddMinutes(10), ResetUtc = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            Login = "octo", Source = "GitHub"
        },
        Local = new LocalUsage
        {
            Status = "available", OverallCredits = 2688, RepositoryCredits = 1378, InputTokens = 1234567, OutputTokens = 4321,
            AsOfUtc = Now, WindowStartUtc = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            CurrentChat = new CurrentChatUsage
            {
                Status = "available", AsOfUtc = Now, TotalCredits = 58,
                Turns = Enumerable.Range(0, turns).Select(i => new CurrentChatTurnUsage { TurnIndex = i, Credits = i + 1, TimestampUtc = Now.AddMinutes(-i) }).ToArray()
            }
        }
    };

    private static UsageDisplay Create(UsageSnapshot? snapshot, bool connected = true) => UsageDisplay.Create(snapshot, connected, Now, TimeZoneInfo.Utc);

    [Theory]
    [InlineData(6339, GitHubLevel.Normal)]
    [InlineData(84999, GitHubLevel.Normal)]
    [InlineData(85000, GitHubLevel.Warning)]
    [InlineData(99999, GitHubLevel.Warning)]
    [InlineData(100000, GitHubLevel.Over)]
    [InlineData(112000, GitHubLevel.Over)]
    public void ClassifiesAllowanceUse(int consumed, GitHubLevel level)
    {
        var display = Create(Snapshot(consumed));
        Assert.Equal(level, display.Level);
        Assert.InRange(display.UsedFraction!.Value, 0, 1);
    }

    [Fact]
    public void FormatsFiguresWithSeparatorsAndPlainWording()
    {
        var display = Create(Snapshot());
        Assert.Equal("6,339 / 100,000", display.GitHubHeadline);
        Assert.Equal("6,339 of 100,000 (6.3%)", display.GitHubSummary);
        Assert.Equal("GITHUB ACCOUNT · NOVEMBER", display.GitHubTitle);
        Assert.Equal("Resets 1 Dec 2026 · Synced 4:46 PM · Next 5:01 PM", display.ScheduleText);
        Assert.Equal("Month 39% elapsed", display.PaceText);
        Assert.DoesNotContain(display.AccountRows.Concat(display.LocalRows), row => row.Value.Contains("0000000") || row.Value.Contains("revision"));
    }

    [Fact]
    public void SplitsLocalUseWithoutAddingItToGitHub()
    {
        var display = Create(Snapshot());
        Assert.Equal("2,688 total", display.LocalTotalText);
        Assert.Equal(new[] { 58m, 1320m, 1310m }, display.Segments.Select(s => s.Credits));
        Assert.Equal(new[] { "This chat", "Rest of this repo", "Other repos" }, display.Segments.Select(s => s.Label));
        Assert.Equal(2688m, display.Segments.Sum(s => s.Credits));
        Assert.Contains("includes this chat", display.LocalNote);
        Assert.Contains("not added to the GitHub total", display.LocalNote);
        Assert.Equal("Chat 58", display.ChatText);
    }

    [Fact]
    public void WholeChatTotalIsNotLimitedToTheNewestTwentyTurns()
    {
        var snapshot = Snapshot(turns: 25);
        snapshot.Local.CurrentChat.TotalCredits = 900;
        var display = Create(snapshot);
        Assert.Equal(20, display.Bars.Length);
        Assert.Equal("Chat 900", display.ChatText);
        snapshot.Local.CurrentChat.TotalCredits = null;
        Assert.Equal("Chat " + Enumerable.Range(1, 25).Sum(), Create(snapshot).ChatText);
    }

    [Fact]
    public void ClampsInconsistentSegmentsAndNeverGoesNegative()
    {
        var snapshot = Snapshot();
        snapshot.Local.RepositoryCredits = 10;
        snapshot.Local.CurrentChat.TotalCredits = 58;
        var display = Create(snapshot);
        Assert.All(display.Segments, s => Assert.True(s.Credits >= 0));
        Assert.Equal(2688m, display.Segments.Sum(s => s.Credits));
    }

    [Fact]
    public void NoActiveChatKeepsRepositorySplitAndHidesChatParts()
    {
        var snapshot = Snapshot();
        snapshot.Local.CurrentChat = new CurrentChatUsage();
        var display = Create(snapshot);
        Assert.False(display.HasChat);
        Assert.Empty(display.Bars);
        Assert.Equal("No active chat", display.ChatText);
        Assert.Equal(new[] { LocalSegmentKind.RepositoryRest, LocalSegmentKind.Other }, display.Segments.Select(s => s.Kind));
        Assert.Contains("Repo total includes any chats", display.LocalNote);
    }

    [Fact]
    public void UnknownRepositoryDoesNotInventARepositoryShare()
    {
        var snapshot = Snapshot();
        snapshot.Local.RepositoryCredits = null;
        var display = Create(snapshot);
        Assert.DoesNotContain(display.Segments, s => s.Kind == LocalSegmentKind.RepositoryRest);
        Assert.Equal("Rest of local use", display.Segments.Last().Label);
        var row = display.LocalRows.Single(r => r.Label == "This repository");
        Assert.StartsWith("unavailable (", row.Value);
        Assert.DoesNotContain("unavailable credits", row.Value);
    }

    [Fact]
    public void UnavailableLocalAndAccountAreExplicitNotZero()
    {
        var snapshot = Snapshot();
        snapshot.Local.Status = "unavailable";
        snapshot.Account.Status = "unavailable";
        var display = Create(snapshot);
        Assert.Equal("unavailable", display.LocalTotalText);
        Assert.Empty(display.Segments);
        Assert.Equal(GitHubLevel.Unavailable, display.Level);
        Assert.Null(display.UsedFraction);
        Assert.Equal("GitHub credits unavailable", display.GitHubHeadline);
        snapshot.Account.Status = "signed-out";
        Assert.Equal("Not signed in", Create(snapshot).GitHubHeadline);
        Assert.Equal(GitHubLevel.SignedOut, Create(snapshot).Level);
    }

    [Fact]
    public void StaleOfflineAndPausedStatesAreDistinct()
    {
        Assert.False(Create(Snapshot()).IsStale);
        var stale = Create(Snapshot(asOf: Now.AddMinutes(-31)));
        Assert.True(stale.IsStale);
        Assert.Equal("Stale since 4:20 PM", stale.StateNote);
        var offline = Create(Snapshot(), connected: false);
        Assert.Equal("Usage host offline", offline.StateNote);
        Assert.False(offline.IsConnected);
        var pausedSnapshot = Snapshot(asOf: Now.AddMinutes(-31));
        pausedSnapshot.IsPaused = true;
        var paused = Create(pausedSnapshot);
        Assert.False(paused.IsStale);
        Assert.Equal("Updates paused", paused.StateNote);
        Assert.Contains("Paused", paused.ScheduleText);
        Assert.Equal("Usage host offline", Create(null, false).StateNote);
    }

    [Theory]
    [InlineData(200, 20, 20)]
    [InlineData(100, 20, 16)]
    [InlineData(36, 20, 6)]
    [InlineData(23, 20, 0)]
    [InlineData(0, 20, 0)]
    [InlineData(200, 7, 7)]
    [InlineData(200, 3, 0)]
    public void BarCountShowsAsManyAsFitAndHidesBelowFour(double width, int available, int expected) =>
        Assert.Equal(expected, UsageDisplay.BarCountFor(width, available));

    [Theory]
    [InlineData(0, 24, 4, 4)]
    [InlineData(1, 24, 4, 24)]
    [InlineData(0.25, 24, 4, 14)]
    [InlineData(2, 24, 4, 24)]
    [InlineData(-1, 24, 4, 4)]
    public void BarHeightUsesSquareRootScaleWithinLimits(double fraction, double max, double min, double expected) =>
        Assert.Equal(expected, UsageDisplay.BarHeightFor(fraction, max, min), 6);

    [Fact]
    public void SmallTurnsStayVisibleNextToALargeOne()
    {
        // 6 of 60 credits: linear scaling gave about 2 px of 24; the square-root scale gives well over 10.
        Assert.True(UsageDisplay.BarHeightFor(6d / 60d, 24, 4) > 10);
    }

    [Theory]
    [InlineData(200, 20, 10)]
    [InlineData(100, 20, 6)]
    [InlineData(1000, 5, 16)]
    [InlineData(60, 5, 12)]
    [InlineData(10, 5, 6)]
    [InlineData(0, 5, 6)]
    [InlineData(100, 0, 6)]
    public void BarSlotGrowsWithSpareRoomWithinLimits(double width, int count, double expected) =>
        Assert.Equal(expected, UsageDisplay.BarSlotFor(width, count));

    [Fact]
    public void BarsAreOrderedScaledAndLabelledWithoutZeroForUnknown()
    {
        var snapshot = Snapshot(turns: 4);
        snapshot.Local.CurrentChat.Turns[1].Credits = null;
        var display = Create(snapshot);
        Assert.Equal(new[] { 0, 1, 2, 3 }, display.Bars.Select(b => b.TurnIndex));
        Assert.Equal(1.0, display.Bars[3].Fraction);
        Assert.True(display.Bars[3].IsNewest);
        Assert.Contains("unavailable credits", display.Bars[1].Tooltip);
        Assert.Equal("LAST 4 TURNS", display.BarsCaption);
        Assert.Equal("last 4", display.LastText);
        Assert.Equal("Last 4 · Avg 2.7 · Max 4", display.BarsSummary);
    }

    [Fact]
    public void DetailRowsKeepEveryFieldTheOldWindowShowed()
    {
        var display = Create(Snapshot());
        var account = display.AccountRows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("octo", account["Account"]);
        Assert.Equal("6,339 (6.3% of allowance)", account["Used"]);
        Assert.Equal("100,000", account["Allowance"]);
        Assert.Equal("93,661", account["Reported remaining"]);
        Assert.Equal("github-premium-interactions", account["Unit"]);
        Assert.Equal("GitHub", account["Source"]);
        Assert.Contains("1 Dec 2026", account["Resets"]);
        Assert.True(account.ContainsKey("Last sync") && account.ContainsKey("Next sync"));
        var local = display.LocalRows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("2026-11", local["Month (UTC)"]);
        Assert.Equal("input 1,234,567 · output 4,321", local["Tokens processed"]);
        Assert.Equal("2,688 credits", local["All local use"]);
        Assert.Contains("1,378 credits", local["This repository"]);
        Assert.True(local.ContainsKey("As of"));
        Assert.Equal("0123456789abcdef".Substring(0, 8), display.DiagnosticRows[0].Value);
    }

    [Fact]
    public void ErrorCodesAppearOnlyAsProblemRows()
    {
        var snapshot = Snapshot();
        snapshot.Account.ErrorCode = "unsupported-billing";
        snapshot.Local.ErrorCode = "history_tool_missing";
        var display = Create(snapshot);
        Assert.Contains(display.AccountRows, r => r.Label == "Problem" && r.Value == "unsupported-billing");
        Assert.Contains(display.LocalRows, r => r.Label == "Problem" && r.Value == "history_tool_missing");
    }

    private static string Tip(UsageSnapshot? snapshot) => UsageDisplay.TrayTooltip(snapshot, Now, TimeZoneInfo.Utc);

    [Fact]
    public void TrayTooltipShowsPercentAndSyncTimes()
    {
        var tip = Tip(Snapshot());
        Assert.Contains("Copilot 6.3% used (6,339 of 100,000)", tip);
        Assert.Contains("Last synced 4:46 PM · next 5:01 PM", tip);
        Assert.DoesNotContain("Updates paused", tip);
    }

    [Fact]
    public void TrayTooltipExplainsSignedOutAndPausedAndErrors()
    {
        var signedOut = Snapshot();
        signedOut.Account = new AccountUsage { Status = "signed-out" };
        Assert.Contains("Not signed in to GitHub", Tip(signedOut));
        Assert.Contains("Status / Settings", Tip(signedOut));

        var paused = Snapshot();
        paused.IsPaused = true;
        var pausedTip = Tip(paused);
        Assert.Contains("Updates paused", pausedTip);
        Assert.DoesNotContain("next", pausedTip);

        var failed = Snapshot();
        failed.Account.Status = "error";
        failed.Account.ErrorCode = "github-request-timeout";
        Assert.Contains("GitHub error: timed out", Tip(failed));
        Assert.Contains("retry 5:01 PM", Tip(failed));

        var expired = Snapshot();
        expired.Account = new AccountUsage { Status = "signed-out", ErrorCode = "authorization-expired" };
        Assert.Contains("sign-in expired", Tip(expired));
        Assert.Contains("Starting", Tip(null));
    }

    [Fact]
    public void TrayTooltipNeverExceedsTheWindowsLimitOrSplitsLines()
    {
        var snapshot = Snapshot(112000);
        snapshot.IsPaused = true;
        snapshot.Local.Status = "unavailable";
        var tip = Tip(snapshot);
        Assert.InRange(tip.Length, 1, UsageDisplay.TrayTooltipLimit);
        Assert.Contains("over allowance", tip);
    }
}
