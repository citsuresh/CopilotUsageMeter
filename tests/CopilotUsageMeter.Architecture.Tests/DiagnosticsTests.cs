using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;
using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

[Collection("DiagnosticsSink")]
public sealed class DiagnosticsTests
{
    private const string SecretMessage = "invented-secret-message-90417 C:\\invented\\repo\\path";

    private sealed class RecordingSink : IDiagnosticsSink
    {
        public readonly List<(DiagnosticLevel Level, string Component, string Code, string? Detail)> Entries = new();
        public void Write(DiagnosticLevel level, string component, string code, string? exceptionDetail)
        {
            lock (Entries) Entries.Add((level, component, code, exceptionDetail));
        }
        public (DiagnosticLevel Level, string Component, string Code, string? Detail)[] Snapshot()
        {
            lock (Entries) return Entries.ToArray();
        }
    }

    private sealed class ThrowingSink : IDiagnosticsSink
    {
        public void Write(DiagnosticLevel level, string component, string code, string? exceptionDetail) =>
            throw new InvalidOperationException("sink failure");
    }

    private static T WithSink<T>(IDiagnosticsSink sink, Func<T> action)
    {
        var previous = Diagnostics.Sink;
        Diagnostics.Sink = sink;
        try { return action(); }
        finally { Diagnostics.Sink = previous; }
    }

    [Fact]
    public void DescribeRecordsTypeAndHResultButNeverTheMessage()
    {
        var detail = Diagnostics.Describe(new IOException(SecretMessage, new InvalidDataException("inner-secret-55")));
        Assert.NotNull(detail);
        Assert.Contains("System.IO.IOException (0x", detail);
        Assert.Contains("System.IO.InvalidDataException (0x", detail);
        Assert.DoesNotContain("invented-secret", detail);
        Assert.DoesNotContain("inner-secret", detail);
        Assert.DoesNotContain("invented\\repo", detail);
    }

    [Fact]
    public void DescribeOfNoExceptionIsNull() => Assert.Null(Diagnostics.Describe(null));

    [Theory]
    [InlineData("tray.worker", "tray.worker")]
    [InlineData("worker_failed.account", "worker_failed.account")]
    [InlineData("has space/and\\slash", "has_space_and_slash")]
    [InlineData("line\r\nbreak", "line__break")]
    [InlineData("caf\u00e9", "caf_")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void TokenKeepsOnlyAFixedAlphabet(string? input, string expected) => Assert.Equal(expected, Diagnostics.Token(input));

    [Fact]
    public void TokenIsLengthBounded() => Assert.Equal(64, Diagnostics.Token(new string('a', 500)).Length);

    [Fact]
    public void EntriesReachTheSinkWithLevelAndSanitisedLabels()
    {
        var sink = new RecordingSink();
        WithSink(sink, () =>
        {
            Diagnostics.Warning("test.unique-component-1", "bad code!", new TimeoutException(SecretMessage));
            Diagnostics.Error("test.unique-component-1", "other");
            return 0;
        });
        var mine = sink.Snapshot().Where(entry => entry.Component == "test.unique-component-1").ToArray();
        Assert.Equal(2, mine.Length);
        Assert.Equal(DiagnosticLevel.Warning, mine[0].Level);
        Assert.Equal("bad_code_", mine[0].Code);
        Assert.Contains("System.TimeoutException", mine[0].Detail);
        Assert.DoesNotContain("invented-secret", mine[0].Detail);
        Assert.Equal(DiagnosticLevel.Error, mine[1].Level);
        Assert.Null(mine[1].Detail);
    }

    [Fact]
    public void ASinkThatThrowsNeverBreaksTheCaller()
    {
        WithSink(new ThrowingSink(), () =>
        {
            Diagnostics.Error("test.unique-component-2", "x", new InvalidOperationException(SecretMessage));
            return 0;
        });
    }

    [Fact]
    public void NoSinkIsANoOp()
    {
        WithSink(null!, () =>
        {
            Diagnostics.Warning("test.unique-component-3", "x");
            return 0;
        });
    }

    [Fact]
    public async Task HandledLocalRefreshFailureIsLoggedWithoutItsMessage()
    {
        var sink = new RecordingSink();
        var previous = Diagnostics.Sink;
        Diagnostics.Sink = sink;
        try
        {
            var clock = new LocalFakeClock();
            using var signal = new LocalFakeSignal();
            using var scheduler = new LocalRefreshScheduler(signal, clock, _ => throw new IOException(SecretMessage));
            await scheduler.ManualRefreshAsync();
        }
        finally { Diagnostics.Sink = previous; }
        var entries = sink.Snapshot();
        Assert.Contains(entries, entry => entry.Component == "local.refresh" && entry.Code == "local_refresh_failed"
            && entry.Detail != null && entry.Detail.Contains("System.IO.IOException"));
        Assert.DoesNotContain(entries, entry => (entry.Detail ?? "").Contains("invented-secret"));
    }
}

[Collection("DiagnosticsSink")]
public sealed class FileDiagnosticsSinkTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "cum-diag-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
    }

    private FileDiagnosticsSink Create(long maxBytes = FileDiagnosticsSink.DefaultMaxBytes) => new(directory, maxBytes, () => now);

    [Fact]
    public void WritesOneLinePerEntryWithTimeLevelComponentCodeAndType()
    {
        var sink = Create();
        sink.Write(DiagnosticLevel.Error, "tray.worker", "worker_failed.account", "System.IO.IOException (0x80131620)");
        sink.Write(DiagnosticLevel.Warning, "pipe.listener", "listener_failed", null);
        var lines = File.ReadAllLines(sink.CurrentPath);
        Assert.Equal(2, lines.Length);
        Assert.Equal("2030-01-02T03:04:05.000Z ERROR tray.worker worker_failed.account System.IO.IOException (0x80131620)", lines[0]);
        Assert.Equal("2030-01-02T03:04:05.000Z WARN pipe.listener listener_failed", lines[1]);
    }

    [Fact]
    public void RollsToOneBackupAndStaysBounded()
    {
        var sink = Create(maxBytes: 400);
        for (var index = 0; index < 100; index++)
        {
            now = now.AddSeconds(30);
            sink.Write(DiagnosticLevel.Warning, "test.component", "code_" + index, "System.IO.IOException (0x80131620)");
        }
        var files = Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "tray.1.log", "tray.log" }, files);
        Assert.True(new FileInfo(sink.CurrentPath).Length < 400 + 200);
        Assert.True(new FileInfo(Path.Combine(directory, "tray.1.log")).Length < 400 + 200);
        Assert.Contains("code_99", File.ReadAllText(sink.CurrentPath));
    }

    [Fact]
    public void AnIdenticalEntryRepeatingQuicklyIsRecordedOncePerWindow()
    {
        var sink = Create();
        for (var index = 0; index < 20; index++)
        {
            now = now.AddMilliseconds(100);
            sink.Write(DiagnosticLevel.Error, "tray.ui", "dispatcher_unhandled", "System.InvalidOperationException (0x80131509)");
        }
        Assert.Single(File.ReadAllLines(sink.CurrentPath));
        now = now.AddSeconds(11);
        sink.Write(DiagnosticLevel.Error, "tray.ui", "dispatcher_unhandled", "System.InvalidOperationException (0x80131509)");
        Assert.Equal(2, File.ReadAllLines(sink.CurrentPath).Length);
    }

    [Fact]
    public void AnUnwritableLocationNeverThrows()
    {
        Directory.CreateDirectory(directory);
        var blocker = Path.Combine(directory, "blocker");
        File.WriteAllText(blocker, "x");
        var sink = new FileDiagnosticsSink(Path.Combine(blocker, "logs"), clock: () => now);
        sink.Write(DiagnosticLevel.Error, "tray.ui", "dispatcher_unhandled", null);
        Assert.False(Directory.Exists(Path.Combine(blocker, "logs")));
    }

    [Fact]
    public void FacadeToFileKeepsMessagesAndPathsOutOfTheLog()
    {
        var sink = Create();
        var previous = Diagnostics.Sink;
        Diagnostics.Sink = sink;
        try
        {
            Diagnostics.Error("test.unique-component-file", "boom", new UnauthorizedAccessException("invented-secret-77 C:\\invented\\chat title"));
        }
        finally { Diagnostics.Sink = previous; }
        var text = File.ReadAllText(sink.CurrentPath);
        Assert.Contains("test.unique-component-file boom System.UnauthorizedAccessException (0x", text);
        Assert.DoesNotContain("invented-secret", text);
        Assert.DoesNotContain("invented\\chat", text);
    }
}
