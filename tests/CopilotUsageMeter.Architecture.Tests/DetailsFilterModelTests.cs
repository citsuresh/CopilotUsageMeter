using CopilotUsageMeter.Core;
using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class DetailsFilterModelTests
{
    // 13 November 2026, 15:30 in a +05:30 zone, which is still 12 November in UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 3, 30, 0, TimeSpan.FromHours(5.5));
    private static DateTimeOffset Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DetailsRange.Last7Days, 2026, 11, 6)]
    [InlineData(DetailsRange.Last30Days, 2026, 10, 14)]
    [InlineData(DetailsRange.Last90Days, 2026, 8, 15)]
    [InlineData(DetailsRange.ThisMonth, 2026, 11, 1)]
    public void PresetRangesCountUtcDaysUpToAndIncludingToday(DetailsRange range, int year, int month, int day)
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput { Range = range }, Now);

        Assert.Null(result.Error);
        Assert.Equal(Utc(year, month, day), result.Query!.FromUtc);
        Assert.Equal(Utc(2026, 11, 13), result.Query.ToUtc);
    }

    [Fact]
    public void LastMonthIsTheWholePreviousUtcMonth()
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput { Range = DetailsRange.LastMonth }, Now);

        Assert.Equal(Utc(2026, 10, 1), result.Query!.FromUtc);
        Assert.Equal(Utc(2026, 11, 1), result.Query.ToUtc);
    }

    [Fact]
    public void LastMonthInJanuaryIsDecember()
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput { Range = DetailsRange.LastMonth }, Utc(2027, 1, 20));

        Assert.Equal(Utc(2026, 12, 1), result.Query!.FromUtc);
        Assert.Equal(Utc(2027, 1, 1), result.Query.ToUtc);
    }

    [Fact]
    public void CustomRangeEndDateIsInclusive()
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput
        {
            Range = DetailsRange.Custom, CustomFrom = new DateTime(2026, 11, 2), CustomTo = new DateTime(2026, 11, 4)
        }, Now);

        Assert.Equal(Utc(2026, 11, 2), result.Query!.FromUtc);
        Assert.Equal(Utc(2026, 11, 5), result.Query.ToUtc);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CustomRangeNeedsBothDates(bool hasFrom, bool hasTo)
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput
        {
            Range = DetailsRange.Custom,
            CustomFrom = hasFrom ? new DateTime(2026, 11, 2) : null,
            CustomTo = hasTo ? new DateTime(2026, 11, 4) : null
        }, Now);

        Assert.Null(result.Query);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void CustomRangeRejectsEndDateThatCannotBeExtended()
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput
        { Range = DetailsRange.Custom, CustomFrom = new DateTime(2026, 11, 2), CustomTo = DateTime.MaxValue }, Now);

        Assert.Null(result.Query);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void CustomRangeRejectsReversedAndOverlongRanges()
    {
        var reversed = DetailsFilterBuilder.Build(new DetailsFilterInput
        { Range = DetailsRange.Custom, CustomFrom = new DateTime(2026, 11, 5), CustomTo = new DateTime(2026, 11, 4) }, Now);
        var overlong = DetailsFilterBuilder.Build(new DetailsFilterInput
        { Range = DetailsRange.Custom, CustomFrom = new DateTime(2025, 1, 1), CustomTo = new DateTime(2026, 11, 4) }, Now);
        var longest = DetailsFilterBuilder.Build(new DetailsFilterInput
        { Range = DetailsRange.Custom, CustomFrom = new DateTime(2026, 1, 1), CustomTo = new DateTime(2026, 1, 1).AddDays(365) }, Now);

        Assert.Null(reversed.Query);
        Assert.Null(overlong.Query);
        Assert.Contains("366", overlong.Error);
        Assert.NotNull(longest.Query);
    }

    [Fact]
    public void FiltersForceTheirDimensionIntoTheGrouping()
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput
        { Grouping = DetailsGrouping.Day, Repository = " Octo/Hello-World ", Model = "gpt-4.1" }, Now);

        Assert.Null(result.Error);
        Assert.Equal(DetailsGrouping.Day | DetailsGrouping.Repository | DetailsGrouping.Model, result.Grouping);
        Assert.Equal(result.Grouping, result.Query!.Grouping);
        Assert.Equal("Octo/Hello-World", result.Query.RepositoryFilter);
        Assert.Equal("gpt-4.1", result.Query.ModelFilter);
    }

    [Fact]
    public void BlankFiltersMeanNoFilter()
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput { Repository = "  ", Model = "" }, Now);

        Assert.Null(result.Query!.RepositoryFilter);
        Assert.Null(result.Query.ModelFilter);
    }

    [Fact]
    public void EmptyGroupingIsRejected()
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput { Grouping = DetailsGrouping.None }, Now);

        Assert.Null(result.Query);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("octo", null)]
    [InlineData("a/b/c", null)]
    [InlineData("-x/y", null)]
    [InlineData("octo/..", null)]
    [InlineData("octo/two words", null)]
    [InlineData(null, "two words")]
    [InlineData(null, "-gpt")]
    [InlineData(null, "gpt;rm")]
    public void InvalidFiltersAreRejectedWithAMessage(string? repository, string? model)
    {
        var result = DetailsFilterBuilder.Build(new DetailsFilterInput { Repository = repository, Model = model }, Now);

        Assert.Null(result.Query);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void UnavailableReportShowsAProblemAndNoFigures()
    {
        var display = DetailsDisplayBuilder.Build(new DetailsReport { ErrorCode = "history_tool_missing" });

        Assert.NotNull(display.Problem);
        Assert.Equal("history_tool_missing", display.Problem!.Code);
        Assert.False(display.HasTable);
        Assert.Null(display.Total);
        Assert.Null(display.EmptyMessage);
    }

    [Fact]
    public void NotRecordedIsAnEmptyStateNotAProblem()
    {
        var display = DetailsDisplayBuilder.Build(new DetailsReport { ErrorCode = "history_not_recorded" });

        Assert.Null(display.Problem);
        Assert.NotNull(display.EmptyMessage);
    }

    [Fact]
    public void UnknownFailureStillShowsAProblem()
    {
        var display = DetailsDisplayBuilder.Build(new DetailsReport { ErrorCode = null });

        Assert.NotNull(display.Problem);
    }

    private static DetailsReport Report(DetailsGrouping grouping, params DetailsRow[] rows) => new()
    {
        Status = "available",
        AsOfUtc = Utc(2026, 11, 13),
        Query = new DetailsQuery(Utc(2026, 11, 1), Utc(2026, 11, 8), grouping),
        Rows = rows,
        Total = new DetailsTotals { Requests = 12, Credits = 7.5m, InputTokens = 3000, OutputTokens = 1200, Chats = 4 }
    };

    [Fact]
    public void ColumnsFollowTheGroupingAndRowsAreFormatted()
    {
        var report = Report(DetailsGrouping.Day | DetailsGrouping.Repository | DetailsGrouping.Model,
            new DetailsRow
            {
                DayUtc = Utc(2026, 11, 2), Repository = "octo/hello", Model = "gpt-4.1",
                Totals = new DetailsTotals { Requests = 1234, Credits = 2.5m, InputTokens = 10000, OutputTokens = 20, Chats = 2 }
            });

        var display = DetailsDisplayBuilder.Build(report, TimeZoneInfo.Utc);

        Assert.Equal(new[] { "Day (UTC)", "Repository", "Model", "Requests", "Credits", "Input tokens", "Output tokens", "Chats" }, display.Headers);
        Assert.Equal(new[] { "2026-11-02", "octo/hello", "gpt-4.1", "1,234", "2.5", "10,000", "20", "2" }, display.Rows.Single());
        Assert.Equal(new[] { "Total", "", "", "12", "7.5", "3,000", "1,200", "4" }, display.Total);
        Assert.Contains("1 row · UTC days 2026-11-01 to 2026-11-07 · as of 13 Nov 2026, 12:00 AM", display.Summary);
        Assert.Empty(display.Notes);
    }

    [Fact]
    public void SingleDimensionGroupingHasOneLeadingColumn()
    {
        var report = Report(DetailsGrouping.Model,
            new DetailsRow { Model = "gpt-4.1", Totals = new DetailsTotals { Requests = 1, Credits = 1m, Chats = 1 } });

        var display = DetailsDisplayBuilder.Build(report, TimeZoneInfo.Utc);

        Assert.Equal("Model", display.Headers[0]);
        Assert.Equal("Total", display.Total![0]);
        Assert.Equal(display.Headers.Count, display.Total.Count);
        Assert.Equal(display.Headers.Count, display.Rows[0].Count);
    }

    [Fact]
    public void UnavailableCreditsAreNeverShownAsZero()
    {
        var report = Report(DetailsGrouping.Repository,
            new DetailsRow { Repository = DetailsQuery.UnsupportedOrigin, Totals = new DetailsTotals { Requests = 3, Credits = null, Chats = null } });
        report.Total!.Credits = null;

        var display = DetailsDisplayBuilder.Build(report, TimeZoneInfo.Utc);

        Assert.Equal(DetailsDisplay.Unavailable, display.Rows[0][2]);
        Assert.Equal(DetailsDisplay.NotStated, display.Rows[0][5]);
        Assert.Equal(DetailsDisplay.Unavailable, display.Total![2]);
        Assert.Equal(DetailsQuery.UnsupportedOrigin, display.Rows[0][0]);
        Assert.Contains(display.Notes, note => note.StartsWith("Chats is n/a"));
    }

    [Fact]
    public void UnknownRepositoryAndMissingModelGetReadableLabels()
    {
        var report = Report(DetailsGrouping.Repository | DetailsGrouping.Model,
            new DetailsRow { Repository = "unknown", Model = null, Totals = new DetailsTotals { Requests = 1, Credits = 1m, Chats = 1 } });

        var display = DetailsDisplayBuilder.Build(report, TimeZoneInfo.Utc);

        Assert.Equal("(unknown)", display.Rows[0][0]);
        Assert.Equal("(unknown)", display.Rows[0][1]);
    }

    [Fact]
    public void CappedResultNamesTheLimit()
    {
        var report = Report(DetailsGrouping.Day,
            new DetailsRow { DayUtc = Utc(2026, 11, 2), Totals = new DetailsTotals { Requests = 1, Credits = 1m, Chats = 1 } });
        report.RowsCapped = true;

        var display = DetailsDisplayBuilder.Build(report, TimeZoneInfo.Utc);

        Assert.Contains(display.Notes, note => note.Contains("first 500 rows"));
    }

    [Fact]
    public void MissingTotalIsExplainedNotShownAsZero()
    {
        var report = Report(DetailsGrouping.Day,
            new DetailsRow { DayUtc = Utc(2026, 11, 2), Totals = new DetailsTotals { Requests = 1, Credits = 1m, Chats = 1 } });
        report.Total = null;

        var display = DetailsDisplayBuilder.Build(report, TimeZoneInfo.Utc);

        Assert.Null(display.Total);
        Assert.Contains(display.Notes, note => note.StartsWith("No total is shown"));
    }

    [Fact]
    public void NoRowsGivesAnEmptyMessageAndNoTotal()
    {
        var report = Report(DetailsGrouping.Day);
        report.Total = new DetailsTotals { Credits = 0m, Chats = 0 };

        var display = DetailsDisplayBuilder.Build(report, TimeZoneInfo.Utc);

        Assert.False(display.HasTable);
        Assert.NotNull(display.EmptyMessage);
        Assert.Contains("0 rows", display.Summary);
    }

    [Fact]
    public void AsOfIsShownInTheGivenZone()
    {
        var report = Report(DetailsGrouping.Day,
            new DetailsRow { DayUtc = Utc(2026, 11, 2), Totals = new DetailsTotals { Requests = 1, Credits = 1m, Chats = 1 } });

        var display = DetailsDisplayBuilder.Build(report, TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(5.5), "test", "test"));

        Assert.Contains("as of 13 Nov 2026, 5:30 AM", display.Summary);
    }
}
