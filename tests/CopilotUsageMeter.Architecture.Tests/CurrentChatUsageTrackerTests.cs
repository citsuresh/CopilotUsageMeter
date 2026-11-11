using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class CurrentChatUsageTrackerTests
{
    [Fact]
    public void ReplacesStreamingTurnSnapshotsInsteadOfAddingCredits()
    {
        var tracker = new CurrentChatUsageTracker();
        tracker.SetActiveSession("session-001");
        var firstTime = DateTimeOffset.Parse("2026-01-01T10:00:00+02:00");

        Assert.True(tracker.ReplaceTurns("session-001", new[]
        {
            new CurrentChatTurnUsage { TurnIndex = 0, Credits = 1.25m, TimestampUtc = firstTime },
            new CurrentChatTurnUsage { TurnIndex = 1, Credits = 2m, TimestampUtc = firstTime.AddMinutes(1) }
        }, firstTime));

        var firstSnapshot = tracker.GetSnapshot();
        firstSnapshot.Turns[1].Credits = 200m;
        Assert.Equal(2m, tracker.GetSnapshot().Turns[1].Credits);

        var refreshedAt = firstTime.AddMinutes(2);
        Assert.True(tracker.ReplaceTurns("session-001", new[]
        {
            new CurrentChatTurnUsage { TurnIndex = 0, Credits = 1.25m, TimestampUtc = firstTime },
            new CurrentChatTurnUsage { TurnIndex = 1, Credits = 3.5m, TimestampUtc = refreshedAt }
        }, refreshedAt));

        var refreshed = tracker.GetSnapshot();
        Assert.Equal("available", refreshed.Status);
        Assert.Equal(2, refreshed.Turns.Length);
        Assert.Equal(3.5m, refreshed.Turns[1].Credits);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T08:02:00Z"), refreshed.AsOfUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T08:02:00Z"), refreshed.Turns[1].TimestampUtc);
    }

    [Fact]
    public void SessionChangesAndUnrecordedUsageProduceUnavailableSnapshots()
    {
        var tracker = new CurrentChatUsageTracker();
        tracker.SetActiveSession("session-010");
        Assert.True(tracker.ReplaceTurns("session-010", new[]
        {
            new CurrentChatTurnUsage { TurnIndex = 0, Credits = 1m }
        }, DateTimeOffset.UtcNow));

        tracker.SetActiveSession("session-011");
        var changedSession = tracker.GetSnapshot();
        Assert.Equal("unavailable", changedSession.Status);
        Assert.Empty(changedSession.Turns);
        Assert.Null(changedSession.AsOfUtc);
        Assert.False(tracker.ReplaceTurns("session-010", Array.Empty<CurrentChatTurnUsage>(), DateTimeOffset.UtcNow));

        Assert.True(tracker.ReplaceTurns("session-011", Array.Empty<CurrentChatTurnUsage>(), DateTimeOffset.UtcNow));
        var noUsage = tracker.GetSnapshot();
        Assert.Equal("unavailable", noUsage.Status);
        Assert.Empty(noUsage.Turns);
        Assert.Null(noUsage.AsOfUtc);
    }

    [Fact]
    public void RetainsOnlyTheNewestTwentyTurnsInIndexOrder()
    {
        var tracker = new CurrentChatUsageTracker();
        tracker.SetActiveSession("session-020");
        var turns = Enumerable.Range(0, 25)
            .Reverse()
            .Select(index => new CurrentChatTurnUsage { TurnIndex = index, Credits = index })
            .ToArray();

        Assert.True(tracker.ReplaceTurns("session-020", turns, DateTimeOffset.UnixEpoch));

        var snapshot = tracker.GetSnapshot();
        Assert.Equal(20, snapshot.Turns.Length);
        Assert.Equal(5, snapshot.Turns[0].TurnIndex);
        Assert.Equal(24, snapshot.Turns[^1].TurnIndex);
        Assert.Equal(Enumerable.Range(5, 20), snapshot.Turns.Select(turn => turn.TurnIndex));
    }

    [Fact]
    public void KeepsTheWholeChatTotalSeparateFromTheRetainedTurns()
    {
        var tracker = new CurrentChatUsageTracker();
        tracker.SetActiveSession("session-021");
        var turns = Enumerable.Range(0, 25).Select(index => new CurrentChatTurnUsage { TurnIndex = index, Credits = 1 }).ToArray();

        Assert.True(tracker.ReplaceTurns("session-021", turns, DateTimeOffset.UnixEpoch, 25m));
        var snapshot = tracker.GetSnapshot();
        Assert.Equal(25m, snapshot.TotalCredits);
        Assert.Equal(20, snapshot.Turns.Length);
        snapshot.TotalCredits = 1;
        Assert.Equal(25m, tracker.GetSnapshot().TotalCredits);
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.ReplaceTurns("session-021", turns, DateTimeOffset.UnixEpoch, -1m));
        tracker.SetActiveSession("session-022");
        Assert.Null(tracker.GetSnapshot().TotalCredits);
    }

    [Fact]
    public void PreservesUnknownCreditsAndRejectsInvalidTurnIndexes()
    {
        var tracker = new CurrentChatUsageTracker();
        tracker.SetActiveSession("session-030");

        Assert.True(tracker.ReplaceTurns("session-030", new[]
        {
            new CurrentChatTurnUsage { TurnIndex = 4, Credits = null }
        }, DateTimeOffset.UnixEpoch));
        Assert.Null(Assert.Single(tracker.GetSnapshot().Turns).Credits);

        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.ReplaceTurns("session-030", new[]
        {
            new CurrentChatTurnUsage { TurnIndex = -1, Credits = 0m }
        }, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => tracker.ReplaceTurns("session-030", new[]
        {
            new CurrentChatTurnUsage { TurnIndex = 1, Credits = 1m },
            new CurrentChatTurnUsage { TurnIndex = 1, Credits = 2m }
        }, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.ReplaceTurns("session-030", new[]
        {
            new CurrentChatTurnUsage { TurnIndex = 1, Credits = -1m }
        }, DateTimeOffset.UnixEpoch));
    }
}
