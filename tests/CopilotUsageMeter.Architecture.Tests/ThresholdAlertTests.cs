using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class AlertSettingsTests
{
    [Fact]
    public void Defaults_AreOffWith80And100Notifications()
    {
        var settings = new AlertSettings();

        Assert.False(settings.Enabled);
        Assert.Equal(new[] { 80, 100 }, settings.Levels.Select(l => l.Percent));
        Assert.All(settings.Levels, l => Assert.Equal(AlertChannel.Notification, l.Channel));
    }

    [Fact]
    public void Normalize_DropsInvalidAndDuplicateLevelsAndSorts()
    {
        var result = AlertSettings.NormalizeLevels(new[]
        {
            new AlertLevel(100, AlertChannel.MessageWindow), new AlertLevel(0, AlertChannel.Off), new AlertLevel(201, AlertChannel.Off),
            new AlertLevel(-5, AlertChannel.Off), new AlertLevel(50, AlertChannel.Notification), new AlertLevel(100, AlertChannel.Off),
            new AlertLevel(60, (AlertChannel)99),
        });

        Assert.Equal(new[] { 50, 100 }, result.Select(l => l.Percent));
        Assert.Equal(AlertChannel.MessageWindow, result[1].Channel);
    }

    [Fact]
    public void Normalize_CapsTheNumberOfLevels()
    {
        var many = Enumerable.Range(1, 30).Select(p => new AlertLevel(p, AlertChannel.Notification));

        Assert.Equal(AlertSettings.MaximumLevels, AlertSettings.NormalizeLevels(many).Count);
    }

    [Fact]
    public void Normalize_NullGivesEmptyList() => Assert.Empty(AlertSettings.NormalizeLevels(null));

    [Theory]
    [InlineData(1, true)]
    [InlineData(200, true)]
    [InlineData(0, false)]
    [InlineData(201, false)]
    public void IsValidPercent_UsesTheDocumentedRange(int percent, bool expected) => Assert.Equal(expected, AlertSettings.IsValidPercent(percent));
}

public sealed class ThresholdAlertTrackerTests
{
    private static readonly DateTimeOffset May = new(2031, 5, 10, 12, 0, 0, TimeSpan.Zero);

    private static AccountUsage Usage(decimal consumed, decimal allowance = 100, string status = "available", DateTimeOffset? asOf = null) =>
        new() { Status = status, Consumed = consumed, Allowance = allowance, AsOfUtc = asOf ?? May };

    private static AlertSettings Enabled(params (int Percent, AlertChannel Channel)[] levels) =>
        new() { Enabled = true, Levels = levels.Select(l => new AlertLevel(l.Percent, l.Channel)).ToList() };

    private static readonly AlertSettings Default = new() { Enabled = true };

    [Fact]
    public void CrossingALevel_FiresOnceAndNotAgain()
    {
        var tracker = new ThresholdAlertTracker();

        Assert.Null(tracker.Evaluate(Usage(79), May, Default));
        var alert = tracker.Evaluate(Usage(80), May, Default);

        Assert.NotNull(alert);
        Assert.Equal(80, alert!.Level);
        Assert.Null(tracker.Evaluate(Usage(85), May, Default));
    }

    [Fact]
    public void AJumpAcrossSeveralLevels_FiresOnlyTheHighestAndMarksAllFired()
    {
        var tracker = new ThresholdAlertTracker();
        var settings = Enabled((50, AlertChannel.Notification), (80, AlertChannel.MessageWindow));

        Assert.Null(tracker.Evaluate(Usage(45), May, settings));
        var alert = tracker.Evaluate(Usage(85), May, settings);

        Assert.Equal(80, alert!.Level);
        Assert.Equal(AlertChannel.MessageWindow, alert.Channel);
        Assert.Equal(new[] { 50, 80 }, tracker.Export().Fired);
    }

    [Fact]
    public void AJumpPrefersTheHighestLevelThatIsNotOff()
    {
        var tracker = new ThresholdAlertTracker();
        var settings = Enabled((50, AlertChannel.Notification), (80, AlertChannel.Off));

        Assert.Equal(50, tracker.Evaluate(Usage(85), May, settings)!.Level);
    }

    [Fact]
    public void ALevelWithChannelOff_NeverAlertsButIsRecorded()
    {
        var tracker = new ThresholdAlertTracker();

        Assert.Null(tracker.Evaluate(Usage(60), May, Enabled((50, AlertChannel.Off))));
        Assert.Equal(new[] { 50 }, tracker.Export().Fired);
    }

    [Fact]
    public void WhileDisabled_CrossingsAreRecordedSoEnablingLaterIsQuiet()
    {
        var tracker = new ThresholdAlertTracker();
        var settings = new AlertSettings { Enabled = false };

        Assert.Null(tracker.Evaluate(Usage(90), May, settings));
        settings.Enabled = true;

        Assert.Null(tracker.Evaluate(Usage(91), May, settings));
    }

    [Fact]
    public void FallingBelowALevel_ReArmsIt()
    {
        var tracker = new ThresholdAlertTracker();

        Assert.NotNull(tracker.Evaluate(Usage(85), May, Default));
        Assert.Null(tracker.Evaluate(Usage(10), May, Default));
        Assert.NotNull(tracker.Evaluate(Usage(82), May, Default));
    }

    [Fact]
    public void ANewMonth_ForgetsFiredLevels()
    {
        var tracker = new ThresholdAlertTracker();

        Assert.NotNull(tracker.Evaluate(Usage(90), May, Default));
        Assert.NotNull(tracker.Evaluate(Usage(90, asOf: May.AddMonths(1)), May.AddMonths(1), Default));
    }

    [Fact]
    public void LastMonthsResultRightAfterRollover_IsIgnoredUntilAFreshReadArrives()
    {
        var tracker = new ThresholdAlertTracker();
        var june = May.AddMonths(1);
        Assert.NotNull(tracker.Evaluate(Usage(90), May, Default));

        Assert.Null(tracker.Evaluate(Usage(90), june, Default));
        Assert.Null(tracker.Evaluate(Usage(10, asOf: june), june, Default));
        Assert.Equal(80, tracker.Evaluate(Usage(85, asOf: june), june, Default)!.Level);
    }

    [Fact]
    public void AResultWithoutATimestamp_IsNotUsed()
    {
        var tracker = new ThresholdAlertTracker();

        Assert.Null(tracker.Evaluate(new AccountUsage { Status = "available", Consumed = 95, Allowance = 100 }, May, Default));
    }

    [Fact]
    public void MonthIsDecidedInUtc()
    {
        var late = new DateTimeOffset(2031, 5, 31, 23, 30, 0, TimeSpan.FromHours(-5));

        Assert.Equal(ThresholdAlertTracker.MonthKeyOf(new DateTimeOffset(2031, 6, 1, 4, 30, 0, TimeSpan.Zero)), ThresholdAlertTracker.MonthKeyOf(late));
    }

    [Theory]
    [InlineData("signed-out")]
    [InlineData("skipped")]
    [InlineData("unavailable")]
    [InlineData("error")]
    [InlineData("rate-limited")]
    public void UnusableStatuses_NeverAlertOrChangeState(string status)
    {
        var tracker = new ThresholdAlertTracker();

        Assert.Null(tracker.Evaluate(Usage(95, 100, status), May, Default));
        Assert.Empty(tracker.Export().Fired);
    }

    [Fact]
    public void MissingNumbersUnlimitedAndZeroAllowance_NeverAlert()
    {
        var tracker = new ThresholdAlertTracker();

        Assert.Null(tracker.Evaluate(null, May, Default));
        Assert.Null(tracker.Evaluate(new AccountUsage { Status = "available", AsOfUtc = May, Allowance = 100 }, May, Default));
        Assert.Null(tracker.Evaluate(new AccountUsage { Status = "available", AsOfUtc = May, Consumed = 90 }, May, Default));
        Assert.Null(tracker.Evaluate(new AccountUsage { Status = "available", AsOfUtc = May, Consumed = 90, Allowance = 0 }, May, Default));
        Assert.Null(tracker.Evaluate(new AccountUsage { Status = "available", AsOfUtc = May, Consumed = 90, Allowance = 100, IsUnlimited = true }, May, Default));
        Assert.Null(tracker.Evaluate(Usage(-1), May, Default));
    }

    [Fact]
    public void UsageAboveTheAllowance_StillAlertsAtOneHundred()
    {
        var tracker = new ThresholdAlertTracker();

        var alert = tracker.Evaluate(Usage(130), May, Enabled((100, AlertChannel.Notification), (150, AlertChannel.Notification)));

        Assert.Equal(100, alert!.Level);
        Assert.Equal(130, alert.ActualPercent, 6);
    }

    [Fact]
    public void AddingALevelAtOrBelowCurrentUsage_DoesNotFireRetroactively()
    {
        var tracker = new ThresholdAlertTracker();
        Assert.Null(tracker.Evaluate(Usage(60), May, Enabled((80, AlertChannel.Notification))));
        var widened = Enabled((50, AlertChannel.Notification), (60, AlertChannel.Notification), (80, AlertChannel.Notification));

        tracker.ApplyLevels(Enabled((80, AlertChannel.Notification)), widened, Usage(60), May);

        Assert.Null(tracker.Evaluate(Usage(61), May, widened));
        Assert.Equal(new[] { 50, 60 }, tracker.Export().Fired);
    }

    [Fact]
    public void AddingALevelAboveCurrentUsage_FiresWhenCrossed()
    {
        var tracker = new ThresholdAlertTracker();
        var widened = Enabled((50, AlertChannel.Notification), (70, AlertChannel.Notification));
        tracker.Evaluate(Usage(55), May, Enabled((50, AlertChannel.Notification)));

        tracker.ApplyLevels(Enabled((50, AlertChannel.Notification)), widened, Usage(55), May);

        Assert.Equal(70, tracker.Evaluate(Usage(72), May, widened)!.Level);
    }

    [Fact]
    public void ALevelAddedWithNoUsableReading_IsSilentlyPassedOnTheNextReading()
    {
        var tracker = new ThresholdAlertTracker();
        var before = Enabled((80, AlertChannel.Notification));
        var widened = Enabled((50, AlertChannel.Notification), (80, AlertChannel.Notification), (95, AlertChannel.Notification));

        tracker.ApplyLevels(before, widened, null, May);

        Assert.Null(tracker.Evaluate(Usage(60), May, widened));
        Assert.Equal(new[] { 50 }, tracker.Export().Fired);
        Assert.Equal(95, tracker.Evaluate(Usage(96), May, widened)!.Level);
    }

    [Fact]
    public void RemovingALevel_ForgetsItSoReAddingStartsFresh()
    {
        var tracker = new ThresholdAlertTracker();
        var both = Enabled((50, AlertChannel.Notification), (80, AlertChannel.Notification));
        tracker.Evaluate(Usage(55), May, both);

        tracker.ApplyLevels(both, Enabled((80, AlertChannel.Notification)), Usage(55), May);

        Assert.Empty(tracker.Export().Fired);
    }

    [Fact]
    public void ExportAndLoad_RoundTripAndIgnoreInvalidPercentages()
    {
        var first = new ThresholdAlertTracker();
        first.Evaluate(Usage(85), May, Default);
        var state = first.Export();
        state.Fired.Add(5000);
        var second = new ThresholdAlertTracker();

        second.Load(state);

        Assert.Equal(new[] { 80 }, second.Export().Fired);
        Assert.Null(second.Evaluate(Usage(86), May, Default));
    }

    [Fact]
    public void LoadingNull_ClearsState()
    {
        var tracker = new ThresholdAlertTracker();
        tracker.Evaluate(Usage(85), May, Default);

        tracker.Load(null);

        Assert.Empty(tracker.Export().Fired);
    }

    [Fact]
    public void AlertText_IsFixedWordingWithNumbersOnly()
    {
        var alert = new ThresholdAlert { Level = 50, ActualPercent = 52.9, Consumed = 264.5, Allowance = 500, Channel = AlertChannel.Notification };

        Assert.Equal("Copilot usage: 50% of monthly allowance", ThresholdAlertText.Title(alert));
        Assert.Equal("GitHub reports 264.50 of 500 used this month (52%).", ThresholdAlertText.Body(alert));
    }
}
