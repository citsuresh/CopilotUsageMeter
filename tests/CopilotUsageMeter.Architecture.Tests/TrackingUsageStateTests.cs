using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class TrackingUsageStateTests
{
    [Fact]
    public void StartsUnavailableAndNeverInventsLocalOrAccountCreditZeros()
    {
        var state = new TrackingUsageState("host-001");
        var snapshot = state.GetSnapshot();
        Assert.False(snapshot.IsDemo);
        Assert.Equal("signed-out", snapshot.Account.Status);
        Assert.Null(snapshot.Account.Consumed);
        Assert.Equal("unavailable", snapshot.Local.Status);
        Assert.Null(snapshot.Local.OverallCredits);
        Assert.Null(snapshot.Local.RepositoryCredits);
        Assert.Empty(snapshot.Local.CurrentChat.Turns);
    }

    [Fact]
    public void ProjectsOnlyRequestedChartAndDetachesMutableValues()
    {
        var state = new TrackingUsageState("host-001");
        state.UpdateAccount(new AccountUsage { Status = "available", Allowance = 100, Consumed = 39 });
        var local = new LocalUsage { Status = "available", OverallCredits = 400 };
        var chart = new CurrentChatUsage
        {
            Status = "available", AsOfUtc = DateTimeOffset.UnixEpoch,
            Turns = new[] { new CurrentChatTurnUsage { TurnIndex = 7, Credits = 1.25m } }
        };
        state.UpdateLocal(local, new Dictionary<string, CurrentChatUsage> { ["session-001"] = chart });
        chart.Turns[0].Credits = 100;
        local.OverallCredits = 900;
        var selected = state.ProjectSnapshot(null, "session-001");
        Assert.Equal(1.25m, Assert.Single(selected.Local.CurrentChat.Turns).Credits);
        Assert.Equal(400, selected.Local.OverallCredits);
        selected.Account.Consumed = 999;
        selected.Local.CurrentChat.Turns[0].Credits = 999;
        Assert.Equal(39, state.GetSnapshot().Account.Consumed);
        Assert.Equal(1.25m, state.ProjectSnapshot(null, "session-001").Local.CurrentChat.Turns[0].Credits);
        Assert.Empty(state.ProjectSnapshot(null, "session-002").Local.CurrentChat.Turns);
        Assert.Empty(state.GetSnapshot().Local.CurrentChat.Turns);
        state.RetainContexts(new[] { "session-002" });
        Assert.Empty(state.ProjectSnapshot(null, "session-001").Local.CurrentChat.Turns);
    }

    [Fact]
    public void RepositoryProjectionUsesExactRecordedGroupsWithoutChangingGlobalTotals()
    {
        var state = new TrackingUsageState("host-001");
        state.UpdateLocal(new LocalUsage { Status = "available", OverallCredits = 200 },
            new Dictionary<string, CurrentChatUsage>(), new LocalRecordedRepositoryMap(
                new Dictionary<string, LocalUsageTotals> { ["001/002"] = new LocalUsageTotals { Credits = 25 } },
                true, new LocalUsageTotals { Credits = 200 }));
        var first = state.ProjectContext(null, null, "001/002");
        Assert.Equal(25, first.Local.RepositoryCredits);
        Assert.Equal(200, first.Local.OverallCredits);
        first.Local.RepositoryCredits = 999;
        Assert.Equal(25, state.ProjectContext(null, null, "001/002").Local.RepositoryCredits);
        Assert.Null(state.ProjectContext(null, null, "003/004").Local.RepositoryCredits);
        Assert.Null(state.GetSnapshot().Local.RepositoryCredits);
    }
}