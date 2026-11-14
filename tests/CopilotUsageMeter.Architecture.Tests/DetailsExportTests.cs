using System.Text;
using System.Text.Json;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;
using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class DetailsExportTests
{
    private static readonly DateTimeOffset Exported = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static DetailsReport Report(DetailsGrouping grouping, DetailsTotals? total, params DetailsRow[] rows) => new()
    {
        Status = "available",
        AsOfUtc = new DateTimeOffset(2026, 11, 13, 9, 30, 0, TimeSpan.Zero),
        Query = new DetailsQuery(Utc(2026, 11, 1), Utc(2026, 11, 8), grouping),
        Rows = rows,
        Total = total
    };

    private static DetailsRow Row(string? repository, string? model, decimal? credits, long? chats) => new()
    {
        DayUtc = Utc(2026, 11, 2),
        Repository = repository,
        Model = model,
        Totals = new DetailsTotals { Requests = 5, Credits = credits, InputTokens = 1000, OutputTokens = 400, Chats = chats }
    };

    private static DetailsTotals Total() => new() { Requests = 12, Credits = 7.5m, InputTokens = 3000, OutputTokens = 1200, Chats = 4 };

    private static AccountUsage Account() => new()
    {
        Status = "available",
        Unit = "ai-credits",
        Allowance = 100000m,
        Consumed = 6339m,
        Remaining = 93444m,
        Login = "invented-login",
        Source = "invented-source",
        ResetUtc = Utc(2026, 12, 1),
        AsOfUtc = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero)
    };

    private static string[] Lines(string csv) => csv.Split("\r\n");

    [Fact]
    public void CsvKeepsLocalAndAccountSectionsSeparateAndNeverSumsThem()
    {
        var report = Report(DetailsGrouping.Day | DetailsGrouping.Repository, Total(), Row("octo/api", null, 2.25m, 2));

        var lines = Lines(DetailsExporter.ToCsv(report, Account(), Exported));

        var local = Array.IndexOf(lines, "Local usage (this machine only)");
        var account = Array.IndexOf(lines, "GitHub account usage (all machines)");
        Assert.InRange(local, 1, account - 1);
        Assert.Contains("Day (UTC),Repository,Requests,Credits,Input tokens,Output tokens,Chats", lines);
        Assert.Contains("2026-11-02,octo/api,5,2.25,1000,400,2", lines);
        Assert.Contains("Total,,12,7.5,3000,1200,4", lines);
        Assert.Contains("Consumed,6339", lines);
        Assert.Contains("Allowance,100000", lines);
        // The only line after the account heading that names credits or tokens is the account's own.
        Assert.DoesNotContain(lines.Skip(account), line => line.StartsWith("Total,"));
        Assert.DoesNotContain(lines.Take(account), line => line.StartsWith("Consumed,"));
    }

    [Fact]
    public void ExportsNeverContainTheLoginOrSource()
    {
        var report = Report(DetailsGrouping.Day, Total(), Row(null, null, 1m, 1));
        var account = Account();

        var csv = DetailsExporter.ToCsv(report, account, Exported);
        var json = DetailsExporter.ToJson(report, account, Exported);

        Assert.DoesNotContain("invented-login", csv + json);
        Assert.DoesNotContain("invented-source", csv + json);
    }

    [Fact]
    public void UnavailableValuesAreEmptyInCsvAndNullInJsonNeverZero()
    {
        var report = Report(DetailsGrouping.Day | DetailsGrouping.Model, null, Row(null, "gpt-x", null, null));
        var signedOut = new AccountUsage { Status = "signed-out", Consumed = 5m, Allowance = 9m };

        var lines = Lines(DetailsExporter.ToCsv(report, signedOut, Exported));
        using var json = JsonDocument.Parse(DetailsExporter.ToJson(report, signedOut, Exported));

        Assert.Contains("2026-11-02,gpt-x,5,,1000,400,", lines);
        Assert.Contains("Consumed,", lines);
        Assert.Contains("Allowance,", lines);
        Assert.Contains(lines, line => line.StartsWith("Total,Not available"));
        var local = json.RootElement.GetProperty("local");
        var row = local.GetProperty("rows")[0];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("credits").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("chats").ValueKind);
        Assert.Equal(JsonValueKind.Null, local.GetProperty("total").ValueKind);
        var github = json.RootElement.GetProperty("githubAccount");
        Assert.Equal("signed-out", github.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, github.GetProperty("consumed").ValueKind);
        Assert.Equal(JsonValueKind.Null, github.GetProperty("allowance").ValueKind);
    }

    [Fact]
    public void JsonHasSeparateSectionsAndOnlyTheGroupedColumns()
    {
        var report = Report(DetailsGrouping.Day | DetailsGrouping.Repository, Total(), Row("octo/api", "ignored", 2.25m, 2));

        using var json = JsonDocument.Parse(DetailsExporter.ToJson(report, Account(), Exported));

        var root = json.RootElement;
        Assert.Equal("copilot-usage-meter-export", root.GetProperty("format").GetString());
        Assert.Equal("2026-11-13T10:00:00Z", root.GetProperty("exportedUtc").GetString());
        var local = root.GetProperty("local");
        Assert.Equal("2026-11-01", local.GetProperty("fromUtcDay").GetString());
        Assert.Equal("2026-11-07", local.GetProperty("toUtcDayInclusive").GetString());
        Assert.Equal(new[] { "day", "repository" }, local.GetProperty("groupedBy").EnumerateArray().Select(item => item.GetString()).ToArray());
        var row = local.GetProperty("rows")[0];
        Assert.Equal("octo/api", row.GetProperty("repository").GetString());
        Assert.False(row.TryGetProperty("model", out _));
        Assert.Equal(2.25m, row.GetProperty("credits").GetDecimal());
        Assert.Equal(7.5m, local.GetProperty("total").GetProperty("credits").GetDecimal());
        var github = root.GetProperty("githubAccount");
        Assert.Equal(6339m, github.GetProperty("consumed").GetDecimal());
        Assert.False(github.TryGetProperty("login", out _));
        Assert.False(github.TryGetProperty("requests", out _));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData(" =1+1", "' =1+1")]
    [InlineData("\u00A0=1+1", "'\u00A0=1+1")]
    [InlineData("  @x", "'  @x")]
    [InlineData("\t=1", "'\t=1")]
    [InlineData("a=b", "a=b")]
    [InlineData("gpt-4.1", "gpt-4.1")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void CsvNeutralisesFormulasAndQuotesSpecialCharacters(string model, string expected)
    {
        var report = Report(DetailsGrouping.Model, Total(), Row(null, model, 1m, 1));

        var lines = Lines(DetailsExporter.ToCsv(report, Account(), Exported));

        Assert.Contains(expected + ",5,1,1000,400,1", lines);
    }

    [Fact]
    public void RepositoryAndModelLabelsMatchTheOnScreenWording()
    {
        var report = Report(DetailsGrouping.Repository | DetailsGrouping.Model, null,
            Row("unknown", null, 1m, 1), Row(DetailsQuery.UnsupportedOrigin, "m", null, null));

        var csv = DetailsExporter.ToCsv(report, Account(), Exported);

        Assert.Contains("(unknown),(unknown),5,1,", csv);
        Assert.Contains("(unsupported origin),m,5,,", csv);
    }

    [Fact]
    public void FiltersAndCapAreStated()
    {
        var report = new DetailsReport
        {
            Status = "available",
            Query = new DetailsQuery(Utc(2026, 11, 1), Utc(2026, 11, 8), DetailsGrouping.Repository | DetailsGrouping.Model, "octo/api", "gpt-x"),
            RowsCapped = true
        };

        var lines = Lines(DetailsExporter.ToCsv(report, Account(), Exported));

        Assert.Contains("Repository filter,octo/api", lines);
        Assert.Contains("Model filter,gpt-x", lines);
        Assert.Contains("Rows capped,Yes", lines);
    }

    [Fact]
    public void OnlyAvailableReportsCanBeExported()
    {
        var failed = new DetailsReport { Status = "unavailable", ErrorCode = "history_command_failed" };

        Assert.Throws<ArgumentException>(() => DetailsExporter.ToCsv(failed, Account(), Exported));
        Assert.Throws<ArgumentException>(() => DetailsExporter.ToJson(failed, Account(), Exported));
        Assert.Throws<ArgumentException>(() => DetailsExporter.SuggestedFileName(failed));
    }

    [Fact]
    public void RepositoryNamesAreFlaggedOnlyWhenRepositoriesAreExported()
    {
        Assert.True(DetailsExporter.ContainsRepositoryNames(Report(DetailsGrouping.Repository, null, Row("octo/api", null, 1m, 1))));
        Assert.False(DetailsExporter.ContainsRepositoryNames(Report(DetailsGrouping.Day, null, Row(null, null, 1m, 1))));
        Assert.False(DetailsExporter.ContainsRepositoryNames(Report(DetailsGrouping.Repository, null)));
    }

    [Theory]
    [InlineData("C:\\x\\a.json", DetailsExportFormat.Json)]
    [InlineData("C:\\x\\a.JSON", DetailsExportFormat.Json)]
    [InlineData("C:\\x\\a.csv", DetailsExportFormat.Csv)]
    [InlineData("C:\\x\\a", DetailsExportFormat.Csv)]
    public void FormatFollowsTheChosenExtension(string path, DetailsExportFormat expected) =>
        Assert.Equal(expected, DetailsExporter.FormatForPath(path));

    [Fact]
    public void SuggestedFileNameUsesTheInclusiveDateRange() =>
        Assert.Equal("copilot-usage-breakdown-20261101-20261107",
            DetailsExporter.SuggestedFileName(Report(DetailsGrouping.Day, null)));

    [Fact]
    public void FilesAreWrittenAtomicallyWithTheExpectedEncoding()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cum-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var csv = Path.Combine(folder, "a.csv");
            var json = Path.Combine(folder, "a.json");
            File.WriteAllText(csv, "old");

            DetailsExporter.WriteAtomically(csv, "new,é\r\n", DetailsExportFormat.Csv);
            DetailsExporter.WriteAtomically(json, "{}", DetailsExportFormat.Json);

            var csvBytes = File.ReadAllBytes(csv);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csvBytes.Take(3).ToArray());
            Assert.Equal("new,é\r\n", Encoding.UTF8.GetString(csvBytes, 3, csvBytes.Length - 3));
            Assert.Equal("{}", File.ReadAllText(json));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void UnlimitedAccountsHaveNoAllowanceAndSayUnlimited()
    {
        var report = Report(DetailsGrouping.Day, Total(), Row(null, null, 1m, 1));
        var account = Account();
        account.IsUnlimited = true;
        account.Allowance = 5m;

        var lines = Lines(DetailsExporter.ToCsv(report, account, Exported));
        using var json = JsonDocument.Parse(DetailsExporter.ToJson(report, account, Exported));

        Assert.Contains("Unlimited,Yes", lines);
        Assert.Contains("Allowance,", lines);
        var github = json.RootElement.GetProperty("githubAccount");
        Assert.True(github.GetProperty("unlimited").GetBoolean());
        Assert.Equal(JsonValueKind.Null, github.GetProperty("allowance").ValueKind);
    }

    [Fact]
    public void AFailedMoveLeavesTheExistingFileIntactAndNoTemporaryFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cum-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var target = Path.Combine(folder, "keep.csv");
            File.WriteAllText(target, "original");

            // Holding the target open without sharing lets the temporary file be written but makes the final move fail.
            using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<Exception>(() => DetailsExporter.WriteAtomically(target, "replacement", DetailsExportFormat.Json));

            Assert.Equal("original", File.ReadAllText(target));
            Assert.Equal(new[] { target }, Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void LongFileNamesStillExport()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cum-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var target = Path.Combine(folder, new string('a', 240) + ".csv");

            DetailsExporter.WriteAtomically(target, "x", DetailsExportFormat.Csv);

            Assert.True(File.Exists(target));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void AFailedWriteLeavesTheExistingFileAndNoTemporaryFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cum-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A directory occupies the target name, so the final move must fail.
            var target = Path.Combine(folder, "blocked.csv");
            Directory.CreateDirectory(target);

            Assert.ThrowsAny<Exception>(() => DetailsExporter.WriteAtomically(target, "x", DetailsExportFormat.Csv));

            Assert.True(Directory.Exists(target));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
