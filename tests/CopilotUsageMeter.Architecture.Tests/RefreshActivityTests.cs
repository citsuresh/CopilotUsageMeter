using System.Runtime.Serialization.Json;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class RefreshActivityTests
{
    private static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void ShortReadsNeverCountAsRefreshing()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var activity = new RefreshActivity(Minimum, () => now);
        Assert.True(activity.Set("local", true));
        now += TimeSpan.FromMilliseconds(499);
        Assert.False(activity.IsRefreshing);
        activity.Set("local", false);
        now += TimeSpan.FromSeconds(5);
        Assert.False(activity.IsRefreshing);
    }

    [Fact]
    public void LongReadsCountUntilTheyFinishAndRepeatedStartsKeepTheOriginalTime()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var activity = new RefreshActivity(Minimum, () => now);
        Assert.True(activity.Set("account", true));
        now += TimeSpan.FromMilliseconds(300);
        Assert.False(activity.Set("account", true));
        now += TimeSpan.FromMilliseconds(200);
        Assert.True(activity.IsRefreshing);
        activity.Set("account", false);
        Assert.False(activity.IsRefreshing);
    }

    [Fact]
    public void AnyVisibleSourceKeepsTheIndicatorOn()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var activity = new RefreshActivity(Minimum, () => now);
        activity.Set("local", true);
        now += TimeSpan.FromSeconds(1);
        activity.Set("account", true);
        Assert.True(activity.IsRefreshing);
        activity.Set("local", false);
        Assert.False(activity.IsRefreshing);
        now += TimeSpan.FromSeconds(1);
        Assert.True(activity.IsRefreshing);
    }

    [Fact]
    public void StateProjectsTheRefreshingFlagAndReportsOnlyRealTransitions()
    {
        var state = new TrackingUsageState("host-001");
        Assert.False(state.GetSnapshot().IsRefreshing);
        var revision = state.GetSnapshot().Revision;
        Assert.True(state.SetRefreshing(true));
        Assert.True(state.GetSnapshot().IsRefreshing);
        Assert.True(state.GetSnapshot().Revision > revision);
        Assert.False(state.SetRefreshing(true));
        Assert.True(state.SetRefreshing(false));
        Assert.False(state.GetSnapshot().IsRefreshing);
    }

    [Fact]
    public void RefreshingFlagSurvivesSerializationAndOlderPayloadsDefaultToFalse()
    {
        var serializer = new DataContractJsonSerializer(typeof(UsageSnapshot));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, new UsageSnapshot { IsRefreshing = true });
        stream.Position = 0;
        Assert.True(((UsageSnapshot)serializer.ReadObject(stream)!).IsRefreshing);

        using var older = new MemoryStream("{\"Revision\":3}"u8.ToArray());
        Assert.False(((UsageSnapshot)serializer.ReadObject(older)!).IsRefreshing);
    }
}
