using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class DemoEngineTests
{
    [Fact]
    public void PauseSuspendsAutomaticRefreshButAllowsManualAndResumeCatchesUp()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var engine = new DemoUsageEngine("host-010", () => now, 5);
        Assert.True(engine.SetPaused(true));
        now = now.AddSeconds(20);
        Assert.False(engine.Refresh(automatic: true));
        Assert.Equal(0, engine.GetSnapshot().RefreshCount);
        Assert.True(engine.Refresh(automatic: false));
        Assert.True(engine.GetSnapshot().IsPaused);
        Assert.Equal(1, engine.GetSnapshot().RefreshCount);
        Assert.True(engine.SetPaused(false));
        Assert.Equal(2, engine.GetSnapshot().RefreshCount);
        Assert.False(engine.SetPaused(false));
        Assert.Equal(2, engine.GetSnapshot().RefreshCount);
    }

    [Fact]
    public void MultipleConsumersDoNotMultiplyRefreshesOrMutateEngineState()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var engine = new DemoUsageEngine("host-011", () => now, 5);
        var first = engine.GetSnapshot();
        first.Local.OverallCredits = 99999;
        first.Settings.DemoRefreshSeconds = 300;
        var second = engine.GetSnapshot();
        Assert.Equal(125, second.Local.OverallCredits);
        Assert.Equal(5, second.Settings.DemoRefreshSeconds);
        Assert.Equal(0, second.RefreshCount);
        now = now.AddSeconds(5);
        Parallel.For(0, 20, _ => engine.Refresh(automatic: false));
        var updated = engine.GetSnapshot();
        Assert.Equal(1, updated.RefreshCount);
        Assert.Equal(301, updated.Account.Consumed);
        Assert.Equal(127, updated.Local.OverallCredits);
        Assert.Equal(13, updated.Local.RepositoryCredits);
        Assert.Equal(2, updated.Revision);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void IntervalLimitsAreExactAndInvalidSettingsDoNotMutateState(int seconds, bool valid)
    {
        var engine = new DemoUsageEngine("host-012", () => DateTimeOffset.UnixEpoch);
        if (valid)
        {
            engine.SetInterval(seconds);
            Assert.Equal(seconds, engine.GetSnapshot().Settings.DemoRefreshSeconds);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.SetInterval(seconds));
            Assert.Equal(15, engine.GetSnapshot().Settings.DemoRefreshSeconds);
            Assert.Equal(1, engine.GetSnapshot().Revision);
        }
    }

    [Fact]
    public void AutomaticRefreshHonorsIntervalWithoutSubscriptionWork()
    {
        var now = DateTimeOffset.UnixEpoch;
        var engine = new DemoUsageEngine("host-013", () => now, 5);
        now = now.AddSeconds(4);
        Assert.False(engine.Refresh(true));
        now = now.AddSeconds(1);
        Assert.True(engine.Refresh(true));
        for (int i = 0; i < 20; i++) Assert.Equal(1, engine.GetSnapshot().RefreshCount);
        Assert.False(engine.Refresh(false));
        Assert.Equal(now, engine.GetSnapshot().Local.AsOfUtc);
    }
}