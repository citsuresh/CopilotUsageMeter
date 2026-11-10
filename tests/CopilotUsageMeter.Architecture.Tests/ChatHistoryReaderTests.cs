using CopilotUsageMeter.Core;
using CopilotUsageMeter.Readers.ChatHistoryCli;
using Newtonsoft.Json;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class ChatHistoryReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private const string Total = "{\"requests\":3,\"input_tokens\":21,\"output_tokens\":7,\"credits\":2.5}";
    private static string Month(string total = Total, string rows = "[]", string tail = "") =>
        "{\"ok\":true,\"command\":\"usage-breakdown\",\"recorded\":true,\"group_by\":[\"day\"],\"limit\":100,\"count\":"
        + JsonConvert.DeserializeObject<object[]>(rows)!.Length + ",\"total\":" + total + ",\"rows\":" + rows + tail + "}";
    private static string Turn(int index, string total = Total, string timestamps = ",\"first_at\":\"2026-11-10T11:00:00Z\",\"last_at\":\"2026-11-10T12:00:00Z\",\"timestamps_complete\":true") =>
        "{\"turn_index\":" + index + ",\"totals\":" + total + timestamps + "}";
    private static string Turns(string rows, string total = Total, string tail = "") =>
        "{\"ok\":true,\"command\":\"usage-turns\",\"recorded\":true,\"limit\":20,\"count\":"
        + JsonConvert.DeserializeObject<object[]>(rows)!.Length + ",\"total\":" + total + ",\"by_turn\":" + rows + tail + "}";

    [Fact]
    public async Task MonthlyReadUsesExactUtcCalendarWindowAndNoRepositoryDisplayFilter()
    {
        var runner = new Runner(Month(rows: "[{\"day\":\"2026-11-02\",\"requests\":3,\"input_tokens\":21,\"output_tokens\":7,\"credits\":2.5}]"));
        var result = await Reader(runner).ReadAsync(new LocalHistoryRequest(Now, canonicalRepositoryKey: "repo-001"), default);
        Assert.Equal("available", result.Status);
        Assert.Equal(2.5m, result.Overall!.Credits);
        Assert.Equal(21L, result.Overall.InputTokens);
        Assert.Null(result.Overall.CacheReadTokens);
        Assert.Equal(Now, result.AsOfUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), Assert.Single(result.Days).DayUtc);
        Assert.Equal("unavailable", result.RepositoryStatus);
        Assert.Equal("repository_key_unsupported", result.RepositoryErrorCode);
        Assert.Null(result.Repository);
        Assert.Equal(new[] { "usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-12-01T00:00:00.000Z", "--group-by", "day", "--limit", "100" }, Assert.Single(runner.Calls));
    }

    [Fact]
    public void MonthBoundsNormalizeOffsetsAndYearRollover()
    {
        var request = new LocalHistoryRequest(new DateTimeOffset(2027, 1, 1, 1, 0, 0, TimeSpan.FromHours(2)));
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), request.FromUtc);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), request.ToUtc);
    }

    [Theory]
    [InlineData("null", null)]
    [InlineData("0", "0")]
    [InlineData("2.5", "2.5")]
    public async Task CreditsPreserveUnknownAndValidZero(string credits, string? expected)
    {
        var response = Month(Total.Replace("2.5", credits));
        var result = await Reader(new Runner(response)).ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("available", result.Status);
        Assert.Equal(expected == null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result.Overall!.Credits);
    }

    [Fact]
    public async Task MissingCreditsStayNullAndMissingRequiredTotalsAreRejected()
    {
        var reader = Reader(new Runner(Month("{\"requests\":0,\"input_tokens\":0,\"output_tokens\":0}"), Month("{\"credits\":0}")));
        var known = await reader.ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("available", known.Status);
        Assert.Null(known.Overall!.Credits);
        Assert.Equal(0L, known.Overall.Requests);
        var incomplete = await reader.ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("history_invalid_data", incomplete.ErrorCode);
        Assert.Null(incomplete.Overall);
    }

    [Theory]
    [InlineData("{\"ok\":true,\"command\":\"usage-breakdown\",\"recorded\":false}", "history_not_recorded")]
    [InlineData("{}", "history_invalid_data")]
    [InlineData("{\"ok\":true,\"command\":\"usage-breakdown\"}", "history_invalid_data")]
    [InlineData("{\"ok\":false,\"error\":{\"code\":\"123456\"}}", "history_invalid_data")]
    [InlineData("[]", "history_invalid_data")]
    [InlineData("null", "history_invalid_data")]
    [InlineData("1", "history_invalid_data")]
    [InlineData("{", "history_invalid_data")]
    public async Task MissingOrUnrecordedEnvelopeNeverFabricatesZeros(string json, string error)
    {
        var result = await Reader(new Runner(json)).ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("unavailable", result.Status);
        Assert.Equal(error, result.ErrorCode);
        Assert.Null(result.Overall);
        Assert.Null(result.AsOfUtc);
        Assert.Empty(result.Days);
    }

    [Theory]
    [InlineData("requests", "-1")]
    [InlineData("input_tokens", "-1")]
    [InlineData("output_tokens", "-1")]
    [InlineData("cache_read_tokens", "-1")]
    [InlineData("cache_write_tokens", "-1")]
    [InlineData("reasoning_tokens", "-1")]
    [InlineData("credits", "-0.1")]
    [InlineData("credits", "\"1\"")]
    [InlineData("credits", "1e100")]
    [InlineData("credits", "NaN")]
    [InlineData("requests", "9223372036854775808")]
    [InlineData("requests", "1.5")]
    public async Task InvalidMetricsRejectWholeMonthlyResult(string field, string value)
    {
        var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(Total)!;
        dictionary.Remove(field);
        var json = JsonConvert.SerializeObject(dictionary);
        json = json[..^1] + ",\"" + field + "\":" + value + "}";
        var result = await Reader(new Runner(Month(json))).ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("history_invalid_data", result.ErrorCode);
        Assert.Null(result.Overall);
    }

    [Fact]
    public async Task DayCapsRetainAuthoritativeOverallTotalWithoutInventingMissingDays()
    {
        var result = await Reader(new Runner(Month(tail: ",\"truncated\":true"))).ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("available", result.Status);
        Assert.Equal(2.5m, result.Overall!.Credits);
        Assert.True(result.DaysCapped);
        Assert.Empty(result.Days);
    }

    [Theory]
    [InlineData("2026-10-31")]
    [InlineData("2026-12-01")]
    [InlineData("2026-11-31")]
    [InlineData("2026-11-01T00:00:00Z")]
    public async Task InvalidOrOutOfWindowDaysAreRejected(string day)
    {
        var row = Total[..^1] + ",\"day\":\"" + day + "\"}";
        var result = await Reader(new Runner(Month(rows: "[" + row + "]"))).ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("history_invalid_data", result.ErrorCode);
        Assert.Empty(result.Days);
    }

    [Fact]
    public async Task MonthRejectsDuplicateDaysWrongCountAndWrongGrouping()
    {
        var row = Total[..^1] + ",\"day\":\"2026-11-01\"}";
        foreach (var json in new[] { Month(rows: "[" + row + "," + row + "]"), Month().Replace("\"count\":0", "\"count\":1"), Month().Replace("[\"day\"]", "[\"repo\"]"), Month() + "{}" })
        {
            var result = await Reader(new Runner(json)).ReadAsync(new LocalHistoryRequest(Now), default);
            Assert.Equal("history_invalid_data", result.ErrorCode);
        }
    }

    [Fact]
    public async Task CurrentChatUsesNewestTwentyContractWithNoExtraMainOrSubagentArithmetic()
    {
        var rows = string.Join(",", Enumerable.Range(101, 20).Select(index => Turn(index)));
        var runner = new Runner(Turns("[" + rows + "]", Total.Replace("2.5", "123"), ",\"subagents\":{\"credits\":1000}"));
        var result = await Reader(runner).ReadCurrentChatAsync("id-001", default);
        Assert.Equal("available", result.CurrentChat.Status);
        Assert.Equal(Enumerable.Range(101, 20), result.CurrentChat.Turns.Select(turn => turn.TurnIndex));
        Assert.All(result.CurrentChat.Turns, turn => { Assert.Equal(2.5m, turn.Credits); Assert.Equal(Now, turn.TimestampUtc); });
        Assert.Equal(123m, result.Total!.Credits);
        Assert.Equal(new[] { "usage-turns", "id-001", "--limit", "20" }, Assert.Single(runner.Calls));
    }

    [Fact]
    public async Task CurrentChatNullCreditsAndIncompleteTimestampsRemainUnknown()
    {
        var result = await Reader(new Runner(Turns("[" + Turn(5, Total.Replace("2.5", "null"), ",\"timestamps_complete\":false") + "]")))
            .ReadCurrentChatAsync("id-001", default);
        Assert.Equal("available", result.CurrentChat.Status);
        var turn = Assert.Single(result.CurrentChat.Turns);
        Assert.Null(turn.Credits);
        Assert.Null(turn.TimestampUtc);
    }

    [Fact]
    public async Task IncompleteTimestampsDoNotPresentMaximumKnownTimestampAsExactLastUsage()
    {
        var timestamps = ",\"first_at\":\"2026-11-10T11:00:00Z\",\"last_at\":\"2026-11-10T12:00:00Z\",\"timestamps_complete\":false";
        var result = await Reader(new Runner(Turns("[" + Turn(1, timestamps: timestamps) + "]"))).ReadCurrentChatAsync("id-001", default);
        Assert.Equal("available", result.CurrentChat.Status);
        Assert.Null(Assert.Single(result.CurrentChat.Turns).TimestampUtc);
    }

    [Theory]
    [InlineData("[{\"turn_index\":-1}]")]
    [InlineData("[null]")]
    [InlineData("[{\"turn_index\":1,\"totals\":{}}]")]
    public async Task InvalidCurrentChatRowsAreUnavailable(string rows)
    {
        var result = await Reader(new Runner(Turns(rows))).ReadCurrentChatAsync("id-001", default);
        Assert.Equal("history_invalid_data", result.ErrorCode);
        Assert.Empty(result.CurrentChat.Turns);
        Assert.Null(result.Total);
    }

    [Fact]
    public async Task CurrentChatRejectsDuplicateUnsortedAndOverCapRows()
    {
        foreach (var rows in new[] { Turn(1) + "," + Turn(1), Turn(2) + "," + Turn(1), string.Join(",", Enumerable.Range(1, 21).Select(index => Turn(index))) })
        {
            var result = await Reader(new Runner(Turns("[" + rows + "]"))).ReadCurrentChatAsync("id-001", default);
            Assert.Equal("history_invalid_data", result.ErrorCode);
            Assert.Empty(result.CurrentChat.Turns);
        }
    }

    [Theory]
    [InlineData(",\"timestamps_complete\":true")]
    [InlineData(",\"first_at\":\"2026-11-10T12:00:00Z\",\"last_at\":\"2026-11-10T11:00:00Z\",\"timestamps_complete\":true")]
    [InlineData(",\"first_at\":\"2026-11-10T11:00:00+01:00\",\"last_at\":\"2026-11-10T12:00:00Z\",\"timestamps_complete\":true")]
    [InlineData(",\"first_at\":\"2026-11-10T11:00:00\",\"last_at\":\"2026-11-10T12:00:00Z\",\"timestamps_complete\":true")]
    [InlineData(",\"first_at\":\"2026-11-10Z\",\"last_at\":\"2026-11-10T12:00:00Z\",\"timestamps_complete\":true")]
    public async Task InvalidTimestampsNeverBecomeFabricatedUtc(string timestamps)
    {
        var result = await Reader(new Runner(Turns("[" + Turn(1, timestamps: timestamps) + "]"))).ReadCurrentChatAsync("id-001", default);
        Assert.Equal("history_invalid_data", result.ErrorCode);
        Assert.Empty(result.CurrentChat.Turns);
    }

    [Fact]
    public async Task MissingNewVerbFailsExplicitlyWithNoLegacyFallback()
    {
        var runner = new Runner(Month()) { ExitForTurns = 1 };
        var result = await Reader(runner).ReadAsync(new LocalHistoryRequest(Now, "id-001"), default);
        Assert.Equal("available", result.Status);
        Assert.Equal("unavailable", result.CurrentChat.Status);
        Assert.Equal("history_command_failed", result.CurrentChatErrorCode);
        Assert.Equal(2, runner.Calls.Count);
        Assert.DoesNotContain(runner.Calls, call => call[0] == "usage");
    }

    [Fact]
    public async Task SeparateSessionReadsDoNotRepeatMonthlyReadOrLeakPreviousSession()
    {
        var runner = new Runner(Turns("[" + Turn(101) + "]"), "{\"ok\":true,\"command\":\"usage-turns\",\"recorded\":false}");
        var reader = Reader(runner);
        var first = await reader.ReadCurrentChatAsync("id-001", default);
        var second = await reader.ReadCurrentChatAsync("id-002", default);
        Assert.Single(first.CurrentChat.Turns);
        Assert.Empty(second.CurrentChat.Turns);
        Assert.Equal("history_not_recorded", second.ErrorCode);
        Assert.All(runner.Calls, call => Assert.Equal("usage-turns", call[0]));
    }

    [Theory]
    [InlineData(null, "history_session_unavailable")]
    [InlineData("", "history_session_unavailable")]
    [InlineData("--help", "history_invalid_session")]
    [InlineData("id 001", "history_invalid_session")]
    [InlineData("id\"001", "history_invalid_session")]
    public async Task InvalidSessionIdentityNeverStartsAProcess(string? id, string error)
    {
        var runner = new Runner();
        var result = await Reader(runner).ReadCurrentChatAsync(id, default);
        Assert.Equal(error, result.ErrorCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task OutputErrorsAndUnknownFieldsNeverEscapeTheAdapter()
    {
        var runner = new Runner(Month(tail: ",\"ignored\":7654321,\"session_id\":\"id-999\""), Turns("[" + Turn(1) + "]"));
        var result = await Reader(runner).ReadAsync(new LocalHistoryRequest(Now, "id-001", "repo-001"), default);
        var json = JsonConvert.SerializeObject(result);
        Assert.DoesNotContain("7654321", json);
        Assert.DoesNotContain("id-", json);
        Assert.DoesNotContain("repo-001", json);
        foreach (var name in new[] { "session_id", "title", "summary", "message", "stderr", "path" })
            Assert.DoesNotContain(name, json, StringComparison.OrdinalIgnoreCase);
        runner.Exception = new IOException("7654321");
        var failure = await Reader(runner).ReadCurrentChatAsync("id-001", default);
        Assert.Equal("history_process_failed", failure.ErrorCode);
        Assert.DoesNotContain("7654321", JsonConvert.SerializeObject(failure));
    }

    [Fact]
    public async Task RunnerProgrammingFaultIsNotConvertedToAnUnavailableResponse()
    {
        var bug = new InvalidOperationException("7654321");
        var reader = Reader(new Runner { Exception = bug });
        Assert.Same(bug, await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(new LocalHistoryRequest(Now), default)));
        Assert.Same(bug, await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadCurrentChatAsync("id-001", default)));
    }

    [Fact]
    public async Task ClockProgrammingFaultIsNotMistakenForMalformedInput()
    {
        var clock = new FaultClock();
        var reader = new ChatHistoryReader(AppContext.BaseDirectory, typeof(object).Assembly.Location,
            runner: new Runner(Month(), Turns("[" + Turn(1) + "]")), clock: clock);
        Assert.Same(clock.Fault, await Assert.ThrowsAsync<FormatException>(() => reader.ReadAsync(new LocalHistoryRequest(Now), default)));
        Assert.Same(clock.Fault, await Assert.ThrowsAsync<FormatException>(() => reader.ReadCurrentChatAsync("id-001", default)));
    }

    private sealed class FaultClock : IClock
    {
        public FormatException Fault { get; } = new("7654321");
        public DateTimeOffset UtcNow => throw Fault;
        public Task Delay(TimeSpan delay, CancellationToken token) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("history_timeout", "history_timeout")]
    [InlineData("history_output_limit", "history_output_limit")]
    [InlineData("7654321", "history_process_failed")]
    public async Task ProcessErrorsAreWhitelisted(string code, string expected)
    {
        var result = await Reader(new Runner { Error = code }).ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal(expected, result.ErrorCode);
        Assert.Null(result.Overall);
    }

    [Fact]
    public async Task OverBudgetOutputAndCancellationAreBounded()
    {
        var result = await Reader(new Runner(new string('1', ChatHistoryProcessRunner.MaximumOutputCharacters + 1)))
            .ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("history_output_limit", result.ErrorCode);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new Runner();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(runner).ReadAsync(new LocalHistoryRequest(Now), cancellation.Token));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task MissingOverrideDoesNotSilentlyRunAnotherExecutable()
    {
        var runner = new Runner();
        var reader = new ChatHistoryReader(AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "missing-001.exe"), runner: runner);
        var result = await reader.ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("history_tool_missing", result.ErrorCode);
        Assert.Empty(runner.Calls);
        var invalid = await new ChatHistoryReader("relative-001", runner: runner).ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal("history_invalid_configuration", invalid.ErrorCode);
    }

    [Fact]
    public async Task ExplicitStoreRootIsAnArgumentNotShellSyntax()
    {
        var runner = new Runner(Month());
        var root = Path.Combine(AppContext.BaseDirectory, "root 001");
        var reader = new ChatHistoryReader(AppContext.BaseDirectory, typeof(object).Assembly.Location, root, runner, new LocalFakeClock(Now));
        await reader.ReadAsync(new LocalHistoryRequest(Now), default);
        Assert.Equal(new[] { "--root", root }, Assert.Single(runner.Calls).TakeLast(2));
    }

    private static ChatHistoryReader Reader(Runner runner) => new(AppContext.BaseDirectory, typeof(object).Assembly.Location, runner: runner, clock: new LocalFakeClock(Now));

    private sealed class Runner(params string[] output) : IChatHistoryProcessRunner
    {
        private readonly Queue<string> output = new(output);
        public List<string[]> Calls { get; } = [];
        public int ExitForTurns { get; set; }
        public Exception? Exception { get; set; }
        public string? Error { get; set; }
        public Task<ChatHistoryProcessResult> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Add(arguments.ToArray());
            if (Exception != null) throw Exception;
            return Task.FromResult(new ChatHistoryProcessResult
            {
                ExitCode = arguments[0] == "usage-turns" ? ExitForTurns : 0,
                ErrorCode = Error,
                StandardOutput = output.Count == 0 ? "" : output.Dequeue()
            });
        }
    }
}