using CopilotUsageMeter.Client;
using CopilotUsageMeter.Panel.Core;
using Xunit;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class ExtensionNoticeTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private const string Lost = "Tray unavailable - cached values are stale";

    private static NoticeAction For(IReadOnlyList<NoticeChange> changes, ExtensionNotice notice) =>
        changes.FirstOrDefault(change => change.Notice == notice)?.Action ?? NoticeAction.None;

    [Theory]
    [InlineData("Connected", TrayConnection.Connected)]
    [InlineData("Paused", TrayConnection.Connected)]
    [InlineData("Connected - demo", TrayConnection.Connected)]
    [InlineData("Paused - demo", TrayConnection.Connected)]
    [InlineData("Disconnected", TrayConnection.Unavailable)]
    [InlineData("Connecting", TrayConnection.Unavailable)]
    [InlineData(Lost, TrayConnection.Unavailable)]
    [InlineData("Protocol error - reconnecting", TrayConnection.Unavailable)]
    [InlineData("Internal error - reconnecting", TrayConnection.Unavailable)]
    [InlineData(PipeUsageClient.VersionMismatchStatus, TrayConnection.VersionMismatch)]
    [InlineData("Resynchronizing", TrayConnection.Transitional)]
    [InlineData("Stopped", TrayConnection.Transitional)]
    [InlineData("something new", TrayConnection.Transitional)]
    [InlineData(null, TrayConnection.Transitional)]
    public void ClassifiesClientStatus(string? status, TrayConnection expected) =>
        Assert.Equal(expected, ExtensionSignals.Classify(status));

    [Fact]
    public void ClientStatusTextsStillClassifyAsExpected()
    {
        // The classifier matches the client's literal status texts; this guards against the client renaming one without the extension noticing.
        var source = File.ReadAllText(FindFile(Path.Combine("src", "CopilotUsageMeter.Client", "PipeUsageClient.cs")));
        foreach (var status in new[] { "Disconnected", "Connecting", "Resynchronizing", Lost, "Protocol error - reconnecting", "Internal error - reconnecting", "Connected - demo", "Paused - demo" })
            Assert.Contains("\"" + status + "\"", source);
    }

    [Fact]
    public void ShortOutageNeverShowsAnInfobar()
    {
        var tracker = new ExtensionNoticeTracker();
        Assert.Empty(tracker.Evaluate(Lost, false, Start));
        Assert.Empty(tracker.Evaluate("Connecting", false, Start.AddSeconds(10)));
        Assert.Empty(tracker.Evaluate("Connected", false, Start.AddSeconds(15)));
    }

    [Fact]
    public void SustainedOutageShowsOnceThenClosesOnReconnect()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate("Connecting", false, Start);
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, Start.AddSeconds(21)), ExtensionNotice.TrayUnavailable));
        Assert.Empty(tracker.Evaluate(Lost, false, Start.AddMinutes(5)));
        Assert.Empty(tracker.Evaluate("Connecting", false, Start.AddMinutes(6)));
        Assert.Equal(NoticeAction.Close, For(tracker.Evaluate("Connected", false, Start.AddMinutes(7)), ExtensionNotice.TrayUnavailable));
        Assert.Empty(tracker.Evaluate("Connected", false, Start.AddMinutes(8)));
    }

    [Fact]
    public void ConnectingAndOutageStatusesShareOneOutage()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate("Connecting", false, Start);
        tracker.Evaluate(Lost, false, Start.AddSeconds(5));
        tracker.Evaluate("Connecting", false, Start.AddSeconds(10));
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, Start.AddSeconds(20)), ExtensionNotice.TrayUnavailable));
    }

    [Fact]
    public void TransitionalStatusDoesNotResetOrStartAnOutage()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate(Lost, false, Start);
        Assert.Empty(tracker.Evaluate("Resynchronizing", false, Start.AddSeconds(10)));
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, Start.AddSeconds(25)), ExtensionNotice.TrayUnavailable));
    }

    [Fact]
    public void FlappingConnectionIsCooledDown()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate(Lost, false, Start);
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, Start.AddSeconds(30)), ExtensionNotice.TrayUnavailable));
        tracker.Evaluate("Connected", false, Start.AddMinutes(1));
        tracker.Evaluate(Lost, false, Start.AddMinutes(2));
        Assert.Empty(tracker.Evaluate(Lost, false, Start.AddMinutes(3)));
        tracker.Evaluate("Connected", false, Start.AddMinutes(4));
        tracker.Evaluate(Lost, false, Start.AddMinutes(40));
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, Start.AddMinutes(41)), ExtensionNotice.TrayUnavailable));
    }

    [Fact]
    public void OutageInsideTheCooldownIsStillReportedOnceTheCooldownPasses()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate(Lost, false, Start);
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, Start.AddSeconds(30)), ExtensionNotice.TrayUnavailable));
        tracker.Evaluate("Connected", false, Start.AddMinutes(1));
        tracker.Evaluate(Lost, false, Start.AddMinutes(10));
        Assert.Empty(tracker.Evaluate(Lost, false, Start.AddMinutes(20)));
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, Start.AddMinutes(31)), ExtensionNotice.TrayUnavailable));
        Assert.Empty(tracker.Evaluate(Lost, false, Start.AddMinutes(45)));
    }

    [Fact]
    public void FailedShowIsRetriedAfterTheRetryDelayAndOnlyOnce()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate(Lost, false, Start);
        var shownAt = Start.AddSeconds(30);
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, shownAt), ExtensionNotice.TrayUnavailable));
        tracker.ShowFailed(ExtensionNotice.TrayUnavailable, shownAt);
        Assert.Empty(tracker.Evaluate(Lost, false, shownAt.AddSeconds(10)));
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, shownAt.AddSeconds(31)), ExtensionNotice.TrayUnavailable));
        Assert.Empty(tracker.Evaluate(Lost, false, shownAt.AddMinutes(5)));
    }

    [Fact]
    public void FailedShowThenRecoveryClosesNothing()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate(Lost, false, Start);
        var shownAt = Start.AddSeconds(30);
        tracker.Evaluate(Lost, false, shownAt);
        tracker.ShowFailed(ExtensionNotice.TrayUnavailable, shownAt);
        Assert.Empty(tracker.Evaluate("Connected", false, shownAt.AddSeconds(5)));
    }

    [Fact]
    public void DismissedInfobarIsNotShownAgainDuringTheSameOutageAndNothingIsClosed()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate(Lost, false, Start);
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, Start.AddSeconds(30)), ExtensionNotice.TrayUnavailable));
        tracker.Dismissed(ExtensionNotice.TrayUnavailable);
        Assert.Empty(tracker.Evaluate(Lost, false, Start.AddHours(2)));
        Assert.Empty(tracker.Evaluate("Connected", false, Start.AddHours(3)));
    }

    [Fact]
    public void VersionMismatchShowsQuicklyAndReplacesTheOutageNotice()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate(Lost, false, Start);
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(Lost, false, Start.AddSeconds(25)), ExtensionNotice.TrayUnavailable));
        var first = tracker.Evaluate(PipeUsageClient.VersionMismatchStatus, false, Start.AddSeconds(26));
        Assert.Equal(NoticeAction.Close, For(first, ExtensionNotice.TrayUnavailable));
        Assert.DoesNotContain(first, change => change.Notice == ExtensionNotice.VersionMismatch);
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate(PipeUsageClient.VersionMismatchStatus, false, Start.AddSeconds(32)), ExtensionNotice.VersionMismatch));
    }

    [Fact]
    public void UnrecognisedLayoutShowsAfterItPersistsAndClosesWhenRecognised()
    {
        var tracker = new ExtensionNoticeTracker();
        Assert.Empty(tracker.Evaluate("Connected", true, Start));
        Assert.Empty(tracker.Evaluate("Connected", true, Start.AddSeconds(20)));
        Assert.Equal(NoticeAction.Show, For(tracker.Evaluate("Connected", true, Start.AddSeconds(31)), ExtensionNotice.ChatLayoutUnrecognised));
        Assert.Empty(tracker.Evaluate("Connected", true, Start.AddMinutes(10)));
        Assert.Equal(NoticeAction.Close, For(tracker.Evaluate("Connected", false, Start.AddMinutes(11)), ExtensionNotice.ChatLayoutUnrecognised));
    }

    [Fact]
    public void BriefLayoutGlitchNeverShows()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate("Connected", true, Start);
        tracker.Evaluate("Connected", false, Start.AddSeconds(10));
        Assert.Empty(tracker.Evaluate("Connected", true, Start.AddSeconds(35)));
    }

    [Fact]
    public void NoticesAreIndependent()
    {
        var tracker = new ExtensionNoticeTracker();
        tracker.Evaluate(Lost, true, Start);
        var changes = tracker.Evaluate(Lost, true, Start.AddSeconds(31));
        Assert.Equal(NoticeAction.Show, For(changes, ExtensionNotice.TrayUnavailable));
        Assert.Equal(NoticeAction.Show, For(changes, ExtensionNotice.ChatLayoutUnrecognised));
    }

    [Fact]
    public void WordingIsFixedAndFreeOfPrivateDetails()
    {
        foreach (ExtensionNotice notice in Enum.GetValues(typeof(ExtensionNotice)))
        {
            var text = ExtensionNoticeText.Message(notice);
            Assert.StartsWith("Copilot Usage Meter", text);
            Assert.DoesNotContain(":\\", text);
            Assert.DoesNotContain("@", text);
        }
    }

    private static string FindFile(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relative);
    }
}
