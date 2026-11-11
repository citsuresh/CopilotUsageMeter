using System.ComponentModel;
using CopilotUsageMeter.Readers.GitHub;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class GitHubCliSignInTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
    private const string ClipboardLine = "! One-time code (ABCD-1234) copied to clipboard";

    [Theory]
    [InlineData("! One-time code (ABCD-1234) copied to clipboard", "ABCD-1234")]
    [InlineData("! First copy your one-time code: WXYZ-9876", "WXYZ-9876")]
    [InlineData("! First copy your one-time code: \u001b[1mQRST-5555\u001b[0m", "QRST-5555")]
    public void ParsesTheCodeFromEitherLineGhPrintsWithoutATerminal(string line, string expected)
    {
        Assert.True(GitHubCliSignIn.TryParseCode(line, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Open this URL to continue in your web browser: https://github.com/login/device")]
    [InlineData("! Failed to copy one-time code to clipboard")]
    [InlineData("ABCD-1234 with no mention of the code")]
    [InlineData("One-time code ABCDE-12345 is too long")]
    [InlineData("One-time code abcd-1234 is not upper case")]
    public void IgnoresLinesWithoutARecognisableCode(string? line)
    {
        Assert.False(GitHubCliSignIn.TryParseCode(line, out var code));
        Assert.Equal(string.Empty, code);
    }

    [Fact]
    public void DeviceCodeIsRedactedInStringOutput()
    {
        var text = new GitHubDeviceCode("ABCD-1234").ToString();
        Assert.DoesNotContain("ABCD", text);
    }

    [Fact]
    public async Task ReportsTheCodeOnceAndSucceedsWhenGhExitsCleanly()
    {
        var fake = new FakeLauncher();
        var codes = new List<string>();
        var run = new GitHubCliSignIn(fake).Begin(c => codes.Add(c.Code));

        fake.Process!.Emit("");
        fake.Process.Emit(ClipboardLine);
        fake.Process.Emit("! First copy your one-time code: ABCD-1234");
        fake.Process.Exit(0);

        var result = await run.Completion.WaitAsync(Wait);
        Assert.Equal(GitHubCliSignInOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new[] { "ABCD-1234" }, codes);
        Assert.False(fake.Process.Killed);
    }

    [Fact]
    public async Task ReportsFailureWithTheExitCode()
    {
        var fake = new FakeLauncher();
        var run = new GitHubCliSignIn(fake).Begin(_ => { });

        fake.Process!.Emit(ClipboardLine);
        fake.Process.Exit(1);

        var result = await run.Completion.WaitAsync(Wait);
        Assert.Equal(GitHubCliSignInOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task CancelStopsGhAndReportsCancelled()
    {
        var fake = new FakeLauncher();
        var shown = new TaskCompletionSource<bool>();
        var run = new GitHubCliSignIn(fake).Begin(_ => shown.TrySetResult(true));

        fake.Process!.Emit(ClipboardLine);
        await shown.Task.WaitAsync(Wait);
        run.Cancel();

        var result = await run.Completion.WaitAsync(Wait);
        Assert.Equal(GitHubCliSignInOutcome.Cancelled, result.Outcome);
        Assert.True(fake.Process.Killed);
        Assert.True(fake.Process.Disposed);
    }

    [Fact]
    public async Task CancelBeforeAnyCodeStopsGh()
    {
        var fake = new FakeLauncher();
        var run = new GitHubCliSignIn(fake).Begin(_ => { });

        run.Cancel();

        var result = await run.Completion.WaitAsync(Wait);
        Assert.Equal(GitHubCliSignInOutcome.Cancelled, result.Outcome);
        Assert.True(fake.Process!.Killed);
    }

    [Fact]
    public async Task StopsGhAndTimesOutWhenTheUserNeverFinishes()
    {
        var fake = new FakeLauncher();
        var run = new GitHubCliSignIn(fake, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200)).Begin(_ => { });

        fake.Process!.Emit(ClipboardLine);

        var result = await run.Completion.WaitAsync(Wait);
        Assert.Equal(GitHubCliSignInOutcome.TimedOut, result.Outcome);
        Assert.True(fake.Process.Killed);
    }

    [Fact]
    public async Task StopsGhAndReportsNoCodeWhenGhStaysSilent()
    {
        var fake = new FakeLauncher();
        var shown = false;
        var run = new GitHubCliSignIn(fake, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(30)).Begin(_ => shown = true);

        var result = await run.Completion.WaitAsync(Wait);
        Assert.Equal(GitHubCliSignInOutcome.NoCode, result.Outcome);
        Assert.False(shown);
        Assert.True(fake.Process!.Killed);
    }

    [Fact]
    public async Task ReportsNoCodeWithoutKillingWhenGhExitsFirst()
    {
        var fake = new FakeLauncher();
        var run = new GitHubCliSignIn(fake).Begin(_ => { });

        fake.Process!.Emit("error connecting to github.com");
        fake.Process.Exit(1);

        var result = await run.Completion.WaitAsync(Wait);
        Assert.Equal(GitHubCliSignInOutcome.NoCode, result.Outcome);
        Assert.Equal(1, result.ExitCode);
        Assert.False(fake.Process.Killed);
    }

    [Fact]
    public async Task AFailingCodeCallbackDoesNotFaultTheRun()
    {
        var fake = new FakeLauncher();
        var run = new GitHubCliSignIn(fake).Begin(_ => throw new InvalidOperationException("ui failed"));

        fake.Process!.Emit(ClipboardLine);
        fake.Process.Exit(0);

        var result = await run.Completion.WaitAsync(Wait);
        Assert.Equal(GitHubCliSignInOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task CancelAfterCompletionIsHarmless()
    {
        var fake = new FakeLauncher();
        var run = new GitHubCliSignIn(fake).Begin(_ => { });
        fake.Process!.Emit(ClipboardLine);
        fake.Process.Exit(0);
        await run.Completion.WaitAsync(Wait);

        run.Cancel();

        Assert.Equal(GitHubCliSignInOutcome.Succeeded, (await run.Completion).Outcome);
    }

    [Fact]
    public void BeginPropagatesALauncherFailureSoTheHostCanReportMissingGh()
    {
        var signIn = new GitHubCliSignIn(new FakeLauncher { FailWith = new Win32Exception("not found") });

        Assert.Throws<Win32Exception>(() => signIn.Begin(_ => { }));
    }

    [Fact]
    public async Task RealLauncherCapturesHiddenOutputAndTheExitCode()
    {
        // cmd stands in for gh: it writes the code line to stderr, as gh does without a terminal, and exits non-zero.
        var launcher = new GitHubCliProcessLauncher("cmd.exe", "/c \"echo ! One-time code (ABCD-1234) copied to clipboard 1>&2 & exit 3\"");
        var codes = new List<string>();
        var run = new GitHubCliSignIn(launcher).Begin(c => codes.Add(c.Code));

        var result = await run.Completion.WaitAsync(Wait);

        Assert.Equal(GitHubCliSignInOutcome.Failed, result.Outcome);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(new[] { "ABCD-1234" }, codes);
    }

    [Fact]
    public async Task RealLauncherStopsAProcessThatNeverFinishes()
    {
        var launcher = new GitHubCliProcessLauncher("cmd.exe", "/c \"echo One-time code (ABCD-1234) 1>&2 & ping -n 60 127.0.0.1 >nul\"");
        var shown = new TaskCompletionSource<bool>();
        var run = new GitHubCliSignIn(launcher).Begin(_ => shown.TrySetResult(true));

        await shown.Task.WaitAsync(Wait);
        run.Cancel();

        var result = await run.Completion.WaitAsync(Wait);
        Assert.Equal(GitHubCliSignInOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public void RealLauncherThrowsWhenTheExecutableIsMissing()
    {
        var launcher = new GitHubCliProcessLauncher("cum-no-such-cli-" + Guid.NewGuid().ToString("N"), "");

        Assert.Throws<Win32Exception>(() => launcher.Start(_ => { }));
    }

    internal sealed class FakeLauncher : ISignInProcessLauncher
    {
        public FakeProcess? Process { get; private set; }
        public List<FakeProcess> Started { get; } = new();
        public Exception? FailWith { get; init; }

        public ISignInProcess Start(Action<string> onLine)
        {
            if (FailWith != null) throw FailWith;
            Process = new FakeProcess(onLine);
            Started.Add(Process);
            return Process;
        }
    }

    internal sealed class FakeProcess : ISignInProcess
    {
        private readonly Action<string> onLine;
        private readonly TaskCompletionSource<int> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeProcess(Action<string> onLine) { this.onLine = onLine; }

        public bool Killed { get; private set; }
        public bool Disposed { get; private set; }
        public Task<int> Exited => exit.Task;

        public void Emit(string line) => onLine(line);
        public void Exit(int code) => exit.TrySetResult(code);

        public void Kill()
        {
            Killed = true;
            exit.TrySetResult(-1);
        }

        public void Dispose() => Disposed = true;
    }
}
