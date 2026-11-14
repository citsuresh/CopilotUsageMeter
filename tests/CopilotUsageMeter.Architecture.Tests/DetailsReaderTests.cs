using CopilotUsageMeter.Core;
using CopilotUsageMeter.Readers.ChatHistoryCli;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class DetailsReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
    private const string Metrics = "\"requests\":3,\"input_tokens\":21,\"output_tokens\":7,\"credits\":2.5,\"chats\":2";

    private static string Envelope(string groupBy, string rows, int count, string total = "{" + Metrics + "}", string tail = "") =>
        "{\"ok\":true,\"command\":\"usage-breakdown\",\"recorded\":true,\"group_by\":[" + groupBy + "],\"limit\":500,\"count\":" + count
        + ",\"total\":" + total + ",\"rows\":[" + rows + "]" + tail + "}";

    [Fact]
    public async Task DetailsReadPassesExactlyTheValidatedArgumentsAndReturnsSortedRows()
    {
        var runner = new Runner(Envelope("\"day\",\"repo\",\"model\"",
            "{\"day\":\"2026-11-03\",\"repo\":\"org/b\",\"model\":\"m-2\"," + Metrics + "},{\"day\":\"2026-11-02\",\"repo\":\"org/a\",\"model\":\"m-1\"," + Metrics + "}", 2));
        var query = new DetailsQuery(From, To, DetailsGrouping.Day | DetailsGrouping.Repository | DetailsGrouping.Model);
        var report = await Reader(runner).ReadDetailsAsync(query, default);
        Assert.Equal("available", report.Status);
        Assert.Null(report.ErrorCode);
        Assert.Equal(Now, report.AsOfUtc);
        Assert.Equal(new[] { "usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z",
            "--group-by", "day,repo,model", "--limit", "500" }, Assert.Single(runner.Calls));
        Assert.Equal(new[] { "org/a", "org/b" }, report.Rows.Select(row => row.Repository).ToArray());
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), report.Rows[0].DayUtc);
        Assert.Equal(3L, report.Total!.Requests);
        Assert.Equal(2L, report.Total.Chats);
        Assert.False(report.RowsCapped);
    }

    [Fact]
    public async Task FiltersAreSentInTheFixedOrderAndMatchedExactly()
    {
        // The tool matches filters as text, so a longer repository that merely contains the filter must be dropped.
        var runner = new Runner(Envelope("\"repo\",\"model\"",
            "{\"repo\":\"org/alpha\",\"model\":\"m-1\"," + Metrics + "},{\"repo\":\"org/alpha-2\",\"model\":\"m-1\"," + Metrics + "}", 2,
            "{\"requests\":6,\"input_tokens\":42,\"output_tokens\":14,\"credits\":5,\"chats\":3}"));
        var query = new DetailsQuery(From, To, DetailsGrouping.Repository | DetailsGrouping.Model, "ORG/alpha", "m-1");
        var report = await Reader(runner).ReadDetailsAsync(query, default);
        Assert.Equal(new[] { "usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z",
            "--repo", "ORG/alpha", "--model", "m-1", "--group-by", "repo,model", "--limit", "500" }, Assert.Single(runner.Calls));
        var row = Assert.Single(report.Rows);
        Assert.Equal("org/alpha", row.Repository);
        Assert.Equal(3L, report.Total!.Requests);
        Assert.Equal(2.5m, report.Total.Credits);
        Assert.Equal(2L, report.Total.Chats);
    }

    [Fact]
    public async Task ToolTotalIsKeptWhenNothingIsDroppedAndChatsAreNotAddedAcrossGroups()
    {
        var report = await Reader(new Runner(Envelope("\"model\"",
            "{\"model\":\"m-1\",\"requests\":1,\"input_tokens\":1,\"output_tokens\":1,\"credits\":1,\"chats\":2},{\"model\":\"m-2\",\"requests\":1,\"input_tokens\":1,\"output_tokens\":1,\"credits\":null,\"chats\":2}", 2,
            "{\"requests\":2,\"input_tokens\":2,\"output_tokens\":2,\"credits\":1,\"chats\":2}")))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Model), default);
        Assert.Equal(2L, report.Total!.Chats);
        Assert.Null(report.Rows[1].Totals.Credits);
    }

    [Fact]
    public async Task DroppedRowsRecomputeTheTotalWithUnknownCreditsAndChats()
    {
        var report = await Reader(new Runner(Envelope("\"repo\"",
            "{\"repo\":\"org/a\",\"requests\":1,\"input_tokens\":1,\"output_tokens\":1,\"credits\":null,\"chats\":1},{\"repo\":\"org/ab\",\"requests\":5,\"input_tokens\":5,\"output_tokens\":5,\"credits\":5,\"chats\":5}", 2,
            "{\"requests\":6,\"input_tokens\":6,\"output_tokens\":6,\"credits\":5,\"chats\":6}")))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Repository, "org/a"), default);
        Assert.Equal("available", report.Status);
        Assert.Equal("org/a", Assert.Single(report.Rows).Repository);
        Assert.Equal(1L, report.Total!.Requests);
        Assert.Null(report.Total.Credits);
        Assert.Equal(1L, report.Total.Chats);
    }

    [Fact]
    public async Task DroppedRowsLeaveChatsUnknownWhenMoreThanOneRowRemains()
    {
        var report = await Reader(new Runner(Envelope("\"model\",\"repo\"",
            "{\"repo\":\"org/a\",\"model\":\"m-1\"," + Metrics + "},{\"repo\":\"org/a\",\"model\":\"m-2\"," + Metrics + "},{\"repo\":\"org/ab\",\"model\":\"m-1\"," + Metrics + "}", 3)))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Repository | DetailsGrouping.Model, "org/a"), default);
        Assert.Equal(2, report.Rows.Length);
        Assert.Equal(6L, report.Total!.Requests);
        Assert.Null(report.Total.Chats);
    }

    [Fact]
    public async Task CappedRowsAreFlaggedAndUnrecordedHistoryIsUnavailable()
    {
        var capped = await Reader(new Runner(Envelope("\"model\"", "{\"model\":\"m-1\"," + Metrics + "}", 1, tail: ",\"truncated\":true")))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Model), default);
        Assert.True(capped.RowsCapped);
        var none = await Reader(new Runner("{\"ok\":true,\"command\":\"usage-breakdown\",\"recorded\":false}"))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Model), default);
        Assert.Equal("unavailable", none.Status);
        Assert.Equal("history_not_recorded", none.ErrorCode);
        Assert.Empty(none.Rows);
        Assert.Null(none.Total);
    }

    [Theory]
    [InlineData("\"model\"", "{\"model\":\"m-1\",\"day\":\"2026-11-02\"," + Metrics + "}")]
    [InlineData("\"day\"", "{\"day\":\"2026-11-09\"," + Metrics + "}")]
    [InlineData("\"day\"", "{\"day\":\"2026-10-31\"," + Metrics + "}")]
    [InlineData("\"day\"", "{\"day\":\"2026-11-02T00:00:00Z\"," + Metrics + "}")]
    [InlineData("\"model\"", "{\"model\":\"\"," + Metrics + "}")]
    [InlineData("\"model\"", "{\"model\":\"m\\u0007\"," + Metrics + "}")]
    [InlineData("\"model\"", "{\"model\":\"m-1\",\"requests\":-1,\"input_tokens\":1,\"output_tokens\":1,\"chats\":1}")]
    [InlineData("\"model\"", "{\"model\":\"m-1\",\"requests\":1,\"input_tokens\":1,\"output_tokens\":1}")]
    [InlineData("\"model\"", "{\"model\":\"m-1\",\"requests\":1,\"input_tokens\":1,\"output_tokens\":1,\"credits\":-1,\"chats\":1}")]
    [InlineData("\"repo\"", "{\"model\":\"m-1\"," + Metrics + "}")]
    public async Task InvalidRowsRejectTheWholeReport(string groupBy, string row)
    {
        var grouping = groupBy == "\"day\"" ? DetailsGrouping.Day : groupBy == "\"repo\"" ? DetailsGrouping.Repository : DetailsGrouping.Model;
        var report = await Reader(new Runner(Envelope(groupBy, row, 1))).ReadDetailsAsync(new DetailsQuery(From, To, grouping), default);
        Assert.Equal("unavailable", report.Status);
        Assert.Equal("history_invalid_data", report.ErrorCode);
        Assert.Empty(report.Rows);
        Assert.Null(report.Total);
        Assert.Null(report.AsOfUtc);
    }

    [Fact]
    public async Task DuplicateRowsWrongGroupingLimitAndCountAreRejected()
    {
        var row = "{\"model\":\"m-1\"," + Metrics + "}";
        var bad = new[]
        {
            Envelope("\"model\"", row + "," + row, 2),
            Envelope("\"day\"", row, 1),
            Envelope("\"model\",\"day\"", row, 1),
            Envelope("\"model\"", row, 2),
            Envelope("\"model\"", row, 1).Replace("\"limit\":500", "\"limit\":100"),
            Envelope("\"model\"", row, 1, total: "{\"requests\":1}")
        };
        foreach (var json in bad)
        {
            var report = await Reader(new Runner(json)).ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Model), default);
            Assert.Equal("history_invalid_data", report.ErrorCode);
            Assert.Empty(report.Rows);
        }
    }

    [Fact]
    public async Task FilteredTruncatedReadHasNoTotalEvenWhenAnExactRowIsVisible()
    {
        var visible = await Reader(new Runner(Envelope("\"repo\"", "{\"repo\":\"org/a\"," + Metrics + "}", 1, tail: ",\"truncated\":true")))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Repository, "org/a"), default);
        Assert.Equal("available", visible.Status);
        Assert.Single(visible.Rows);
        Assert.True(visible.RowsCapped);
        Assert.Null(visible.Total);

        // No visible exact row must not become a zero total.
        var none = await Reader(new Runner(Envelope("\"repo\"", "{\"repo\":\"org/ab\"," + Metrics + "}", 1, tail: ",\"truncated\":true")))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Repository, "org/a"), default);
        Assert.Empty(none.Rows);
        Assert.Null(none.Total);

        // Without a filter the tool's total still covers every group, so it is kept.
        var unfiltered = await Reader(new Runner(Envelope("\"repo\"", "{\"repo\":\"org/a\"," + Metrics + "}", 1, tail: ",\"truncated\":true")))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Repository), default);
        Assert.Equal(3L, unfiltered.Total!.Requests);
    }

    [Fact]
    public async Task UnsupportedRecordedOriginsAreRelabelledAndCombinedWithoutExposingTheOriginal()
    {
        var report = await Reader(new Runner(Envelope("\"repo\"",
            "{\"repo\":\"C:\\\\Users\\\\someone\\\\secret-repo\"," + Metrics + "},"
            + "{\"repo\":\"https://user:token@host.example/org/r.git\",\"requests\":4,\"input_tokens\":10,\"output_tokens\":1,\"credits\":1.5,\"chats\":1},"
            + "{\"repo\":\"org/a\"," + Metrics + "},{\"repo\":\"unknown\"," + Metrics + "}", 4,
            "{\"requests\":13,\"input_tokens\":62,\"output_tokens\":22,\"credits\":8.5,\"chats\":5}")))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Repository), default);
        Assert.Equal(new[] { "(unsupported origin)", "org/a", "unknown" }, report.Rows.Select(row => row.Repository).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        var combined = report.Rows.Single(row => row.Repository == DetailsQuery.UnsupportedOrigin);
        Assert.Equal(7L, combined.Totals.Requests);
        Assert.Equal(31L, combined.Totals.InputTokens);
        Assert.Equal(4.0m, combined.Totals.Credits);
        Assert.Null(combined.Totals.Chats);
        Assert.DoesNotContain(report.Rows, row => row.Repository!.Contains("secret") || row.Repository.Contains("token"));
        Assert.Equal(13L, report.Total!.Requests);
    }

    [Fact]
    public async Task CombinedUnsupportedOriginsKeepUnknownCreditsUnknown()
    {
        var report = await Reader(new Runner(Envelope("\"repo\"",
            "{\"repo\":\"/a/b\",\"requests\":1,\"input_tokens\":1,\"output_tokens\":1,\"credits\":null,\"chats\":1},{\"repo\":\"/c/d\",\"requests\":1,\"input_tokens\":1,\"output_tokens\":1,\"credits\":2,\"chats\":1}", 2)))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Repository), default);
        var row = Assert.Single(report.Rows);
        Assert.Equal(2L, row.Totals.Requests);
        Assert.Null(row.Totals.Credits);
    }

    [Fact]
    public async Task UnsupportedOriginRowsAreDroppedByAnExactRepositoryFilter()
    {
        var report = await Reader(new Runner(Envelope("\"repo\"", "{\"repo\":\"https://host/org/a\"," + Metrics + "},{\"repo\":\"org/a\"," + Metrics + "}", 2)))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Repository, "org/a"), default);
        Assert.Equal("org/a", Assert.Single(report.Rows).Repository);
        Assert.Equal(3L, report.Total!.Requests);
    }

    [Fact]
    public async Task DuplicateRecordedOriginsAreStillRejectedBeforeRelabelling()
    {
        var report = await Reader(new Runner(Envelope("\"repo\"", "{\"repo\":\"/a\"," + Metrics + "},{\"repo\":\"/a\"," + Metrics + "}", 2)))
            .ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Repository), default);
        Assert.Equal("history_invalid_data", report.ErrorCode);
    }

    [Fact]
    public async Task ProcessFailuresAreSanitizedCodes()
    {
        var report = await Reader(new Runner { Error = "history_timeout" }).ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Day), default);
        Assert.Equal("unavailable", report.Status);
        Assert.Equal("history_timeout", report.ErrorCode);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(new Runner()).ReadDetailsAsync(new DetailsQuery(From, To, DetailsGrouping.Day), stop.Token));
    }

    [Fact]
    public void QueryValidatesBoundsGroupingAndFilters()
    {
        Assert.Throws<ArgumentException>(() => new DetailsQuery(From.AddHours(1), To, DetailsGrouping.Day));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetailsQuery(To, From, DetailsGrouping.Day));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetailsQuery(From, From, DetailsGrouping.Day));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetailsQuery(From, From.AddDays(367), DetailsGrouping.Day));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetailsQuery(From, To, DetailsGrouping.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetailsQuery(From, To, (DetailsGrouping)8));
        Assert.Throws<ArgumentException>(() => new DetailsQuery(From, To, DetailsGrouping.Day, repositoryFilter: "org/a"));
        Assert.Throws<ArgumentException>(() => new DetailsQuery(From, To, DetailsGrouping.Day, modelFilter: "m-1"));
        Assert.Throws<ArgumentException>(() => new DetailsQuery(From, To, DetailsGrouping.Repository, repositoryFilter: "--help"));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            new DetailsQuery(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.FromHours(5.5)), To, DetailsGrouping.Day).FromUtc);
        Assert.Equal(DetailsQuery.MaximumDays, (new DetailsQuery(From, From.AddDays(DetailsQuery.MaximumDays), DetailsGrouping.Day).ToUtc - From).TotalDays);
    }

    [Theory]
    [InlineData("org/repo", true)]
    [InlineData("Org-1/re.po_x", true)]
    [InlineData("-org/repo", false)]
    [InlineData("org", false)]
    [InlineData("org/", false)]
    [InlineData("org/..", false)]
    [InlineData("a/b/c", false)]
    [InlineData("org/re po", false)]
    [InlineData("org/--help", true)]
    [InlineData("org/re\"po", false)]
    [InlineData("", false)]
    public void RepositoryFilterAcceptsOnlyOwnerSlashRepository(string value, bool expected) =>
        Assert.Equal(expected, DetailsQuery.IsValidRepositoryFilter(value));

    [Theory]
    [InlineData("gpt-5.1-codex", true)]
    [InlineData("claude-sonnet-4.5", true)]
    [InlineData("vendor/model:1", true)]
    [InlineData("--help", false)]
    [InlineData("-m", false)]
    [InlineData("a b", false)]
    [InlineData("a\"b", false)]
    [InlineData("a;b", false)]
    [InlineData("", false)]
    public void ModelFilterRejectsOptionsSpacesAndQuoting(string value, bool expected) =>
        Assert.Equal(expected, DetailsQuery.IsValidModelFilter(value));

    [Theory]
    [InlineData("usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z", "--group-by", "day,repo,model", "--limit", "500", true)]
    [InlineData("usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z", "--group-by", "day", "--limit", "100", true)]
    [InlineData("usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z", "--group-by", "repo", "--limit", "500", true)]
    [InlineData("usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z", "--group-by", "day,model", "--limit", "100", false)]
    [InlineData("usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z", "--group-by", "model,day", "--limit", "500", false)]
    [InlineData("usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z", "--group-by", "day", "--limit", "501", false)]
    [InlineData("usage-breakdown", "--from", "2026-11-01", "--to", "2026-11-08T00:00:00.000Z", "--group-by", "day", "--limit", "100", false)]
    [InlineData("usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z", "--text", "x", "--limit", "500", false)]
    public async Task RunnerAcceptsOnlyTheFixedUsageBreakdownShapes(params object[] values)
    {
        var expected = (bool)values[^1];
        var arguments = values[..^1].Cast<string>().ToArray();
        var result = await new ChatHistoryProcessRunner().RunAsync(Path.Combine(AppContext.BaseDirectory, "missing-001.exe"), arguments, default);
        // A valid shape reaches the start attempt and fails there; an invalid one is refused before any process starts.
        Assert.Equal(expected ? "history_process_failed" : "history_invalid_configuration", result.ErrorCode);
    }

    [Theory]
    [InlineData("--repo", "org/repo", "--model", "m-1", true)]
    [InlineData("--repo", "org/repo", null, null, true)]
    [InlineData("--model", "m-1", null, null, true)]
    [InlineData("--model", "m-1", "--repo", "org/repo", false)]
    [InlineData("--repo", "--help", null, null, false)]
    [InlineData("--model", "--help", null, null, false)]
    [InlineData("--repo", "org/repo", "--repo", "org/other", false)]
    [InlineData("--cwd", "x", null, null, false)]
    public async Task RunnerAcceptsFiltersOnlyInExactFormAndOrder(string first, string firstValue, string? second, string? secondValue, bool expected)
    {
        var arguments = new List<string> { "usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z", first, firstValue };
        if (second != null) { arguments.Add(second); arguments.Add(secondValue!); }
        arguments.AddRange(new[] { "--group-by", "repo,model", "--limit", "500" });
        var result = await new ChatHistoryProcessRunner().RunAsync(Path.Combine(AppContext.BaseDirectory, "missing-001.exe"), arguments, default);
        Assert.Equal(expected ? "history_process_failed" : "history_invalid_configuration", result.ErrorCode);
    }

    [Theory]
    [InlineData("--repo", "org/repo", "day", false)]
    [InlineData("--repo", "org/repo", "day,model", false)]
    [InlineData("--repo", "org/repo", "day,repo", true)]
    [InlineData("--model", "m-1", "repo", false)]
    [InlineData("--model", "m-1", "day,repo", false)]
    [InlineData("--model", "m-1", "model", true)]
    public async Task RunnerRequiresTheFilteredDimensionToBeGrouped(string option, string value, string group, bool expected)
    {
        var arguments = new[] { "usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z", option, value, "--group-by", group, "--limit", "500" };
        var result = await new ChatHistoryProcessRunner().RunAsync(Path.Combine(AppContext.BaseDirectory, "missing-001.exe"), arguments, default);
        Assert.Equal(expected ? "history_process_failed" : "history_invalid_configuration", result.ErrorCode);
    }

    [Theory]
    [InlineData(new[] { "--root", "C:\\history" }, true)]
    [InlineData(new[] { "--root", "relative" }, false)]
    [InlineData(new[] { "--root", "C:\\a", "--root", "C:\\b" }, false)]
    public async Task RunnerAppliesTheSameShapesWithATrailingRoot(string[] root, bool expected)
    {
        var arguments = new List<string> { "usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-11-08T00:00:00.000Z",
            "--repo", "org/repo", "--model", "m-1", "--group-by", "day,repo,model", "--limit", "500" };
        arguments.AddRange(root);
        var result = await new ChatHistoryProcessRunner().RunAsync(Path.Combine(AppContext.BaseDirectory, "missing-001.exe"), arguments, default);
        Assert.Equal(expected ? "history_process_failed" : "history_invalid_configuration", result.ErrorCode);
    }

    private static ChatHistoryReader Reader(Runner runner) => new(AppContext.BaseDirectory, typeof(object).Assembly.Location, runner: runner, clock: new LocalFakeClock(Now));

    private sealed class Runner(params string[] output) : IChatHistoryProcessRunner
    {
        private readonly Queue<string> output = new(output);
        public List<string[]> Calls { get; } = [];
        public string? Error { get; set; }
        public Task<ChatHistoryProcessResult> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Add(arguments.ToArray());
            return Task.FromResult(new ChatHistoryProcessResult { ErrorCode = Error, StandardOutput = output.Count == 0 ? "" : output.Dequeue() });
        }
    }
}
