using CopilotUsageMeter.Core;
using CopilotUsageMeter.Readers.ChatHistoryCli;
using Newtonsoft.Json;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class ChatHistoryRepositoryTests
{
    private static readonly DateTimeOffset Month = new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
    private const string Key = "owner-001/repo-001";

    [Fact]
    public async Task OneRepoGroupedReadProvidesOverallAndMultipleExactPerClientProjections()
    {
        var runner = new Runner(Response(new[] { Row(Key, 2m), Row("owner-002/repo-002", 3m) }));
        var result = await Reader(runner).ReadAsync(new LocalHistoryRequest(Month, recordedRepositoryKey: Key, includeDays: false), default);
        Assert.Equal("available", result.Status);
        Assert.Equal(100m, result.Overall!.Credits);
        Assert.Empty(result.Days);
        Assert.Equal(2m, result.Repository!.Credits);
        Assert.Equal("available", result.RepositoryStatus);
        var mapping = Assert.IsType<LocalRecordedRepositoryMap>(result.RecordedRepositories);
        Assert.Equal("recorded-repository", mapping.Scope);
        Assert.Equal(2m, mapping.Lookup("OWNER-001/REPO-001").Totals!.Credits);
        Assert.Equal(3m, mapping.Lookup("owner-002/repo-002").Totals!.Credits);
        Assert.Equal(2, mapping.GetSnapshot().Count);
        Assert.Equal(new[] { "usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-12-01T00:00:00.000Z", "--group-by", "repo", "--limit", "500" }, Assert.Single(runner.Calls));
        Assert.DoesNotContain("--repo", runner.Calls.Single());
    }

    [Fact]
    public async Task ReturnedGroupIsCompleteEvenWhenUnrelatedRepositoryGroupsAreCapped()
    {
        var result = await Read(Response(new[] { Row(Key, 2m) }, truncated: true));
        Assert.Equal("available", result.Status);
        Assert.Equal(100m, result.Overall!.Credits);
        Assert.True(result.RecordedRepositories!.IsTruncated);
        var found = result.RecordedRepositories.Lookup(Key);
        Assert.Equal("available", found.Status);
        Assert.Equal(2m, found.Totals!.Credits);
        var absent = result.RecordedRepositories.Lookup("owner-999/repo-999");
        Assert.Equal("unavailable", absent.Status);
        Assert.Equal("repository_groups_capped", absent.ErrorCode);
        Assert.Null(absent.Totals);
    }

    [Fact]
    public async Task UntruncatedMissingGroupIsKnownZeroWithoutInventingUnsupportedTokenFields()
    {
        var result = await Read(Response(Array.Empty<object>()));
        var absent = result.RecordedRepositories!.Lookup(Key);
        Assert.Equal("available", absent.Status);
        Assert.Equal(0m, absent.Totals!.Credits);
        Assert.Equal(0L, absent.Totals.Requests);
        Assert.Equal(0L, absent.Totals.InputTokens);
        Assert.Equal(0L, absent.Totals.OutputTokens);
        Assert.Null(absent.Totals.CacheReadTokens);
        Assert.Null(absent.Totals.CacheWriteTokens);
        Assert.Null(absent.Totals.ReasoningTokens);
    }

    [Fact]
    public async Task MissingCreditSchemaRemainsUnknownEvenForKnownEmptyRepository()
    {
        var result = await Read(Response(new[] { Row(Key, null) }, credits: null));
        var found = result.RecordedRepositories!.Lookup(Key);
        Assert.Null(found.Totals!.Credits);
        var empty = result.RecordedRepositories.Lookup("owner-999/repo-999");
        Assert.Equal("available", empty.Status);
        Assert.Null(empty.Totals!.Credits);
        Assert.Equal(0L, empty.Totals.Requests);
    }

    [Theory]
    [InlineData("repo-001")]
    [InlineData("owner-001")]
    [InlineData("owner-001/repo-00")]
    [InlineData("owner-001/repo-001-extra")]
    public async Task NeverUsesSubstringOrFolderAliasMatching(string key)
    {
        var result = await Read(Response(new[] { Row(Key, 2m) }, truncated: true));
        var lookup = result.RecordedRepositories!.Lookup(key);
        Assert.Equal("unavailable", lookup.Status);
        Assert.Null(lookup.Totals);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("repo-001")]
    [InlineData("C:\\repo-001")]
    [InlineData("/owner-001/repo-001")]
    [InlineData("https://github.com/owner-001/repo-001")]
    [InlineData("owner-001/repo-001 ")]
    public async Task UnavailableRemoteNeverFabricatesARepositoryScope(string? key)
    {
        var result = await Read(Response(new[] { Row(Key, 2m) }));
        var lookup = result.RecordedRepositories!.Lookup(key);
        Assert.Equal("unavailable", lookup.Status);
        Assert.Equal("repository_key_unavailable", lookup.ErrorCode);
        Assert.Null(lookup.Totals);
    }

    [Fact]
    public async Task MappingAndEveryLookupAreDetachedAcrossClientsAndRefreshes()
    {
        var runner = new Runner(Response(new[] { Row(Key, 2m) }));
        var reader = Reader(runner);
        var first = await reader.ReadAsync(new LocalHistoryRequest(Month, includeDays: false), default);
        var second = await reader.ReadAsync(new LocalHistoryRequest(Month, includeDays: false), default);
        var projected = first.RecordedRepositories!.Lookup(Key);
        projected.Totals!.Credits = 999m;
        var snapshot = first.RecordedRepositories.GetSnapshot();
        snapshot[Key].Credits = 777m;
        Assert.Equal(2m, first.RecordedRepositories.Lookup(Key).Totals!.Credits);
        Assert.Equal(2m, second.RecordedRepositories!.Lookup(Key).Totals!.Credits);
        Assert.NotSame(first.RecordedRepositories, second.RecordedRepositories);
        Assert.DoesNotContain(Key, JsonConvert.SerializeObject(first));
    }

    [Fact]
    public async Task CaseDistinctStoredAliasesAreExplicitlyAmbiguousNotArbitrarilySelected()
    {
        var result = await Read(Response(new[] { Row(Key, 2m), Row(Key.ToUpperInvariant(), 3m) }));
        Assert.Equal(2, result.RecordedRepositories!.GetSnapshot().Count);
        var lookup = result.RecordedRepositories.Lookup(Key);
        Assert.Equal("unavailable", lookup.Status);
        Assert.Equal("repository_key_ambiguous", lookup.ErrorCode);
        Assert.Null(lookup.Totals);
        Assert.Equal("available", result.Status);
    }

    [Fact]
    public async Task RecordedFalseAndInvalidGroupShapesNeverProduceAMappingOrZeros()
    {
        foreach (var response in new[]
        {
            "{\"ok\":true,\"command\":\"usage-breakdown\",\"recorded\":false}",
            Response(new[] { Row(Key, 2m) }).Replace("\"repo\"]", "\"day\"]"),
            Response(new[] { Row(Key, 2m) }).Replace("\"count\":1", "\"count\":2"),
            Response(new[] { Row(Key, -1m) }),
            Response(new[] { Row(Key, 2m), Row(Key, 3m) }),
            Response(Enumerable.Range(0, 501).Select(index => Row("owner-001/repo-" + index, 1m)).ToArray())
        })
        {
            var result = await Read(response);
            Assert.Equal("unavailable", result.Status);
            Assert.Null(result.RecordedRepositories);
            Assert.Null(result.Repository);
            Assert.Null(result.Overall);
        }
    }

    [Fact]
    public async Task FiveHundredUntruncatedRowsAreCompleteAndMissingTruncationIsConservative()
    {
        var result = await Read(Response(Enumerable.Range(0, 500).Select(index => Row("owner-001/repo-" + index, 1m)).ToArray()));
        Assert.Equal(500, result.RecordedRepositories!.GetSnapshot().Count);
        Assert.False(result.RecordedRepositories.IsTruncated);
        Assert.Equal(0m, result.RecordedRepositories.Lookup("owner-999/repo-999").Totals!.Credits);
        var unknown = await Read(Response(Array.Empty<object>()).Replace(",\"truncated\":false", ""));
        Assert.True(unknown.RecordedRepositories!.IsTruncated);
        Assert.Equal("repository_groups_capped", unknown.RecordedRepositories.Lookup(Key).ErrorCode);
    }

    [Fact]
    public async Task StoredFolderNameIsNotAClaimAboutCanonicalLocalPathOrRemoteAlias()
    {
        var result = await Read(Response(new[] { Row("repo-001", 2m) }, truncated: true));
        Assert.Equal("unavailable", result.RepositoryStatus);
        Assert.Equal("repository_key_unsupported", result.RepositoryErrorCode);
        Assert.Null(result.Repository);
        Assert.Equal("unavailable", result.RecordedRepositories!.Lookup(Key).Status);
        Assert.Equal(2m, result.RecordedRepositories.GetSnapshot()["repo-001"].Credits);
    }

    [Fact]
    public async Task ProductionRunnerAllowsRepo500ShapeButStillRejectsSubstringFiltering()
    {
        var runner = new ChatHistoryProcessRunner();
        var executable = Path.Combine(AppContext.BaseDirectory, "missing-001.exe");
        var args = new[] { "usage-breakdown", "--from", "2026-11-01T00:00:00.000Z", "--to", "2026-12-01T00:00:00.000Z", "--group-by", "repo", "--limit", "500" };
        var accepted = await runner.RunAsync(executable, args, default);
        Assert.Equal("history_process_failed", accepted.ErrorCode);
        var rejected = await runner.RunAsync(executable, args.Concat(new[] { "--repo", Key }).ToArray(), default);
        Assert.Equal("history_invalid_configuration", rejected.ErrorCode);
    }

    private static Task<LocalReadResult> Read(string response) => Reader(new Runner(response)).ReadAsync(new LocalHistoryRequest(Month, includeDays: false), default);
    private static ChatHistoryReader Reader(Runner runner) => new(AppContext.BaseDirectory, typeof(object).Assembly.Location, runner: runner, clock: new LocalFakeClock(Month));
    private static object Row(string repo, decimal? credits) => new { repo, requests = 2L, input_tokens = 20L, output_tokens = 5L, credits };
    private static string Response(object[] rows, bool truncated = false, decimal? credits = 100m) => JsonConvert.SerializeObject(new
    {
        ok = true, command = "usage-breakdown", recorded = true, group_by = new[] { "repo" }, limit = 500, count = rows.Length,
        total = new { requests = 100L, input_tokens = 1000L, output_tokens = 100L, credits }, rows, truncated
    });

    private sealed class Runner(string response) : IChatHistoryProcessRunner
    {
        public List<string[]> Calls { get; } = [];
        public Task<ChatHistoryProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Add(arguments.ToArray());
            return Task.FromResult(new ChatHistoryProcessResult { StandardOutput = response });
        }
    }
}