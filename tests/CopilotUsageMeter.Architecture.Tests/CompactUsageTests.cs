using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class CompactUsageTests
{
    [Fact]
    public void ScalesNewestTwentyAndPreservesExactTooltipValues()
    {
        var snapshot = new UsageSnapshot
        {
            Local = new LocalUsage
            {
                CurrentChat = new CurrentChatUsage
                {
                    Status = "available", AsOfUtc = DateTimeOffset.UnixEpoch,
                    Turns = Enumerable.Range(0, 25).Reverse().Select(index => new CurrentChatTurnUsage
                    {
                        TurnIndex = index, Credits = index + 0.125m, TimestampUtc = DateTimeOffset.UnixEpoch.AddMinutes(index)
                    }).ToArray()
                }
            }
        };
        var presentation = CompactUsagePresentation.Create(snapshot, true, DateTimeOffset.UnixEpoch);
        Assert.Equal(Enumerable.Range(5, 20), presentation.Bars.Select(bar => bar.TurnIndex));
        Assert.Equal(22, presentation.Bars[^1].Height);
        Assert.True(presentation.Bars[^1].IsNewest);
        Assert.All(presentation.Bars[..^1], bar => Assert.False(bar.IsNewest));
        Assert.Contains("24.125 local credits", presentation.Bars[^1].Tooltip);
        Assert.Contains("1970-01-01T00:24:00.0000000+00:00", presentation.Bars[^1].Tooltip);
    }

    [Fact]
    public void UnknownCreditsAndTimeAreNotPresentedAsZero()
    {
        var snapshot = new UsageSnapshot
        {
            Account = new AccountUsage { Status = "unavailable" },
            Local = new LocalUsage
            {
                Status = "available",
                CurrentChat = new CurrentChatUsage
                {
                    Status = "available", AsOfUtc = DateTimeOffset.UnixEpoch,
                    Turns = new[] { new CurrentChatTurnUsage { TurnIndex = 7 } }
                }
            }
        };
        var result = CompactUsagePresentation.Create(snapshot, true, DateTimeOffset.UnixEpoch);
        Assert.Contains("GitHub unavailable", result.Header);
        Assert.Contains("This Repo unavailable", result.Header);
        Assert.Contains("unavailable local credits", Assert.Single(result.Bars).Tooltip);
        Assert.Contains("last usage event unknown", result.Bars[0].Tooltip);
    }

    [Fact]
    public void CountdownBecomesPausedOrOfflineWithoutChangingSourceValues()
    {
        var snapshot = new UsageSnapshot
        {
            Account = new AccountUsage
            {
                Status = "available", Allowance = 100, Consumed = 39, Remaining = 60,
                NextRefreshUtc = DateTimeOffset.UnixEpoch.AddSeconds(42)
            },
            Local = new LocalUsage { OverallCredits = 400, RepositoryCredits = 125 }
        };
        var running = CompactUsagePresentation.Create(snapshot, true, DateTimeOffset.UnixEpoch);
        Assert.Contains("GitHub 39%", running.Header);
        Assert.Equal("Next GitHub sync in 42s", running.Refresh);
        Assert.Contains("reported remaining 60", running.Account);
        Assert.Contains("overall 400 local credits", running.Local);
        snapshot.IsPaused = true;
        Assert.StartsWith("Paused", CompactUsagePresentation.Create(snapshot, true, DateTimeOffset.UnixEpoch).Refresh);
        Assert.StartsWith("Offline", CompactUsagePresentation.Create(snapshot, false, DateTimeOffset.UnixEpoch).Refresh);
    }
}