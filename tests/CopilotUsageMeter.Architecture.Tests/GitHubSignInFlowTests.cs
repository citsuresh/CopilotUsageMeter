using System.Collections.Concurrent;
using System.ComponentModel;
using CopilotUsageMeter.Readers.GitHub;
using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class GitHubSignInFlowTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
    private const string ClipboardLine = "! One-time code (ABCD-1234) copied to clipboard";

    [Fact]
    public async Task ShowsTheCodeThenConnectsTheAccountAndFinishesWithSuccess()
    {
        var h = new Harness();
        Assert.True(h.Flow.Begin());

        h.Launcher.Process!.Emit(ClipboardLine);
        h.Launcher.Process.Exit(0);
        await h.WaitUntil(() => h.Dialog.Finish == SignInFinish.Success);

        Assert.Equal(new[] { "ABCD-1234" }, h.Dialog.Codes.ToArray());
        Assert.True(h.Dialog.ConnectingShown);
        Assert.Equal(1, h.ConnectCalls);
        Assert.False(h.Dialog.Closed);
        Assert.Equal(0, h.FallbackCalls);
    }

    [Fact]
    public void ShowsNoWindowAndThrowsWhenGhCannotBeStarted()
    {
        var h = new Harness(new FakeLauncherFailing());

        Assert.Throws<Win32Exception>(() => h.Flow.Begin());

        Assert.Empty(h.Dialogs);
        // A failed start must not leave the flow stuck as "already running".
        Assert.Throws<Win32Exception>(() => h.Flow.Begin());
    }

    [Fact]
    public async Task FallsBackToTheVisibleConsoleWhenGhPrintsNoCode()
    {
        var h = new Harness(codeTimeout: TimeSpan.FromMilliseconds(150));
        Assert.True(h.Flow.Begin());

        await h.WaitUntil(() => h.FallbackCalls == 1);

        Assert.True(h.Launcher.Process!.Killed);
        Assert.True(h.Dialog.Closed);
        Assert.Equal(0, h.ConnectCalls);
    }

    [Fact]
    public async Task CancellingFromTheWindowStopsGhAndAllowsANewSignIn()
    {
        var h = new Harness();
        Assert.True(h.Flow.Begin());
        h.Launcher.Process!.Emit(ClipboardLine);

        h.Dialog.RaiseCancel();
        await h.WaitUntil(() => h.Launcher.Process.Killed && h.Dialog.Closed);

        Assert.Equal(0, h.ConnectCalls);
        Assert.Equal(0, h.FallbackCalls);
        Assert.True(await h.TryBeginEventually());
        Assert.Equal(2, h.Launcher.Started.Count);
    }

    [Fact]
    public async Task ASecondRequestWhileRunningReusesTheWindow()
    {
        var h = new Harness();
        Assert.True(h.Flow.Begin());
        h.Launcher.Process!.Emit(ClipboardLine);
        var bringsBefore = h.Dialog.Brings;

        Assert.False(h.Flow.Begin());

        Assert.Single(h.Launcher.Started);
        Assert.Single(h.Dialogs);
        Assert.True(h.Dialog.Brings > bringsBefore);
        h.Launcher.Process.Exit(0);
        await h.WaitUntil(() => h.Dialog.Finish == SignInFinish.Success);
    }

    [Fact]
    public async Task OffersRetryAfterAFailureAndRetryStartsAFreshWindow()
    {
        var h = new Harness();
        Assert.True(h.Flow.Begin());
        var first = h.Dialog;
        h.Launcher.Process!.Emit(ClipboardLine);
        h.Launcher.Process.Exit(1);
        await h.WaitUntil(() => first.Finish == SignInFinish.Retry);

        first.RaiseRetry();

        Assert.Equal(2, h.Launcher.Started.Count);
        Assert.True(first.Closed);
        Assert.Equal(2, h.Dialogs.Count);
        Assert.False(h.Dialog.Closed);
        h.Launcher.Process.Emit("! One-time code (WXYZ-9876) copied to clipboard");
        h.Launcher.Process.Exit(0);
        await h.WaitUntil(() => h.Dialog.Finish == SignInFinish.Success);
        Assert.Equal(new[] { "WXYZ-9876" }, h.Dialog.Codes.ToArray());
    }

    [Fact]
    public async Task OffersRetryWhenTheCodeIsNeverApproved()
    {
        var h = new Harness(overallTimeout: TimeSpan.FromMilliseconds(150));
        Assert.True(h.Flow.Begin());
        h.Launcher.Process!.Emit(ClipboardLine);

        await h.WaitUntil(() => h.Dialog.Finish == SignInFinish.Retry);

        Assert.True(h.Launcher.Process.Killed);
        Assert.Contains("timed out", h.Dialog.Message);
    }

    [Fact]
    public async Task SeveralGhLoginsLetTheUserChooseAnAccount()
    {
        var h = new Harness { Connection = new SignInConnection(SignInConnectionState.ChooseAccount) };
        Assert.True(h.Flow.Begin());
        h.Launcher.Process!.Emit(ClipboardLine);
        h.Launcher.Process.Exit(0);
        await h.WaitUntil(() => h.Dialog.Finish == SignInFinish.ChooseAccount);

        h.Dialog.RaiseChoices();

        Assert.Equal(1, h.ChoiceCalls);
        Assert.True(h.Dialog.Closed);
    }

    [Fact]
    public async Task ReportsWhyTheAccountCouldNotBeConnected()
    {
        var h = new Harness { Connection = new SignInConnection(SignInConnectionState.Failed, "invented failure text") };
        Assert.True(h.Flow.Begin());
        h.Launcher.Process!.Emit(ClipboardLine);
        h.Launcher.Process.Exit(0);

        await h.WaitUntil(() => h.Dialog.Finish == SignInFinish.Retry);

        Assert.Contains("invented failure text", h.Dialog.Message);
    }

    [Fact]
    public async Task AFailingAccountImportEndsWithARetryNotAHang()
    {
        var h = new Harness { ConnectThrows = true };
        Assert.True(h.Flow.Begin());
        h.Launcher.Process!.Emit(ClipboardLine);
        h.Launcher.Process.Exit(0);

        await h.WaitUntil(() => h.Dialog.Finish == SignInFinish.Retry);

        Assert.True(await h.TryBeginEventually());
    }

    [Fact]
    public async Task DisposeStopsARunningSignInAndClosesTheWindow()
    {
        var h = new Harness();
        Assert.True(h.Flow.Begin());
        h.Launcher.Process!.Emit(ClipboardLine);

        h.Flow.Dispose();

        await h.WaitUntil(() => h.Launcher.Process.Killed);
        Assert.True(h.Dialog.Closed);
        Assert.False(h.Flow.Begin());
    }

    private sealed class FakeLauncherFailing : ISignInProcessLauncher
    {
        public ISignInProcess Start(Action<string> onLine) => throw new Win32Exception("not found");
    }

    private sealed class Harness
    {
        private readonly ISignInProcessLauncher launcher;
        private int connectCalls;
        private int fallbackCalls;
        private int choiceCalls;

        public Harness(ISignInProcessLauncher? launcher = null, TimeSpan? codeTimeout = null, TimeSpan? overallTimeout = null)
        {
            this.launcher = launcher ?? new GitHubCliSignInTests.FakeLauncher();
            // Posting runs the action inline, which stands in for the UI thread hop.
            Flow = new GitHubSignInFlow(new GitHubCliSignIn(this.launcher, codeTimeout ?? TimeSpan.FromSeconds(30), overallTimeout ?? TimeSpan.FromMinutes(5)),
                action => action(), CreateDialog, ConnectAsync, () => Interlocked.Increment(ref choiceCalls), () => Interlocked.Increment(ref fallbackCalls));
        }

        public GitHubSignInFlow Flow { get; }
        public GitHubCliSignInTests.FakeLauncher Launcher => (GitHubCliSignInTests.FakeLauncher)launcher;
        public ConcurrentQueue<FakeDialog> Dialogs { get; } = new();
        public FakeDialog Dialog => Dialogs.Last();
        public SignInConnection Connection { get; init; } = new(SignInConnectionState.Connected);
        public bool ConnectThrows { get; init; }
        public int ConnectCalls => Volatile.Read(ref connectCalls);
        public int FallbackCalls => Volatile.Read(ref fallbackCalls);
        public int ChoiceCalls => Volatile.Read(ref choiceCalls);

        public async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Wait;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was not met in time.");
                await Task.Delay(10);
            }
        }

        // The flow frees its "already running" state just after the run completes, so give it a moment.
        public async Task<bool> TryBeginEventually()
        {
            var deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline)
            {
                if (Flow.Begin()) return true;
                await Task.Delay(10);
            }
            return false;
        }

        private ISignInDialog CreateDialog()
        {
            var dialog = new FakeDialog();
            Dialogs.Enqueue(dialog);
            return dialog;
        }

        private Task<SignInConnection> ConnectAsync()
        {
            Interlocked.Increment(ref connectCalls);
            if (ConnectThrows) throw new InvalidOperationException("import failed");
            return Task.FromResult(Connection);
        }
    }

    private sealed class FakeDialog : ISignInDialog
    {
        private int brings;
        private int finish = -1;

        public event Action? CancelRequested;
        public event Action? RetryRequested;
        public event Action? ChoicesRequested;

        public ConcurrentQueue<string> Codes { get; } = new();
        public volatile bool Closed;
        public volatile bool ConnectingShown;
        public SignInFinish? Finish => Volatile.Read(ref finish) < 0 ? null : (SignInFinish)Volatile.Read(ref finish);
        public volatile string Message = "";
        public int Brings => Volatile.Read(ref brings);

        public void ShowCode(string code) => Codes.Enqueue(code);
        public void ShowConnecting() => ConnectingShown = true;

        public void ShowFinished(SignInFinish finish, string message)
        {
            Message = message;
            Volatile.Write(ref this.finish, (int)finish);
        }

        public void Bring() => Interlocked.Increment(ref brings);
        public void Close() => Closed = true;
        public void RaiseCancel() => CancelRequested?.Invoke();
        public void RaiseRetry() => RetryRequested?.Invoke();
        public void RaiseChoices() => ChoicesRequested?.Invoke();
    }
}
