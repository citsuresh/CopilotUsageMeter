using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotUsageMeter.Readers.GitHub
{
    // The one-time device code gh prints during browser sign-in. It is short-lived and useless without the user approving it in the browser,
    // but it is still kept out of logs and string output.
    public sealed class GitHubDeviceCode
    {
        // Fixed on purpose: gh's own printed URL is never opened, so a changed or malicious output line cannot redirect the user elsewhere.
        public const string VerificationUrl = "https://github.com/login/device";

        public GitHubDeviceCode(string code) { Code = code; }
        public string Code { get; }
        public override string ToString() => "GitHub one-time code (redacted)";
    }

    public enum GitHubCliSignInOutcome
    {
        Succeeded,
        Failed,
        Cancelled,
        TimedOut,
        // gh exited or stayed silent without printing a recognisable code; the caller can fall back to a visible console.
        NoCode
    }

    public sealed class GitHubCliSignInResult
    {
        public GitHubCliSignInResult(GitHubCliSignInOutcome outcome, int? exitCode)
        {
            Outcome = outcome;
            ExitCode = exitCode;
        }

        public GitHubCliSignInOutcome Outcome { get; }
        public int? ExitCode { get; }
    }

    public interface ISignInProcess : IDisposable
    {
        // Completes with the exit code only after the process has ended and all of its output lines were delivered.
        Task<int> Exited { get; }
        void Kill();
    }

    public interface ISignInProcessLauncher
    {
        // Starts the process with no visible window and delivers each stdout/stderr line to onLine. Throws Win32Exception or
        // FileNotFoundException when gh cannot be started.
        ISignInProcess Start(Action<string> onLine);
    }

    // Runs gh browser sign-in without a console window and reports the one-time code so the host can show it in its own UI.
    public sealed class GitHubCliSignIn
    {
        public static readonly TimeSpan DefaultCodeTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromMinutes(15);

        private static readonly Regex CodePattern = new Regex(@"(?<![A-Z0-9])[A-Z0-9]{4}-[A-Z0-9]{4}(?![A-Z0-9])", RegexOptions.CultureInvariant);

        private readonly ISignInProcessLauncher launcher;
        private readonly TimeSpan codeTimeout;
        private readonly TimeSpan overallTimeout;

        public GitHubCliSignIn(ISignInProcessLauncher launcher, TimeSpan? codeTimeout = null, TimeSpan? overallTimeout = null)
        {
            this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            this.codeTimeout = codeTimeout ?? DefaultCodeTimeout;
            this.overallTimeout = overallTimeout ?? DefaultOverallTimeout;
        }

        // Starts gh. Throws when gh cannot be launched, before any run exists.
        public GitHubCliSignInRun Begin(Action<GitHubDeviceCode> onCode) => new GitHubCliSignInRun(launcher, onCode, codeTimeout, overallTimeout);

        // Accepts both lines gh prints without a terminal: "One-time code (XXXX-XXXX) copied to clipboard" and, when the clipboard
        // is unavailable, "First copy your one-time code: XXXX-XXXX". Only lines that mention the code are considered.
        public static bool TryParseCode(string? line, out string code)
        {
            code = string.Empty;
            if (line == null || line.IndexOf("one-time code", StringComparison.OrdinalIgnoreCase) < 0) return false;
            var match = CodePattern.Match(line);
            if (!match.Success) return false;
            code = match.Value;
            return true;
        }
    }

    public sealed class GitHubCliSignInRun
    {
        private readonly object gate = new object();
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> codeSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action<GitHubDeviceCode> onCode;
        private readonly ISignInProcess process;
        private bool codeReported;

        internal GitHubCliSignInRun(ISignInProcessLauncher launcher, Action<GitHubDeviceCode> onCode, TimeSpan codeTimeout, TimeSpan overallTimeout)
        {
            this.onCode = onCode ?? throw new ArgumentNullException(nameof(onCode));
            process = launcher.Start(OnLine);
            Completion = MonitorAsync(codeTimeout, overallTimeout);
        }

        // Never faults; every outcome is reported through the result.
        public Task<GitHubCliSignInResult> Completion { get; }

        // Stops gh. Safe to call at any time, including after the run has finished.
        public void Cancel() => cancel.Cancel();

        private void OnLine(string line)
        {
            if (!GitHubCliSignIn.TryParseCode(line, out var code)) return;
            lock (gate)
            {
                if (codeReported) return;
                codeReported = true;
            }
            // This runs on the process's output thread, so a failing UI callback must not escape and take the host down.
            try { onCode(new GitHubDeviceCode(code)); }
            catch (Exception) { }
            codeSeen.TrySetResult(true);
        }

        private async Task<GitHubCliSignInResult> MonitorAsync(TimeSpan codeTimeout, TimeSpan overallTimeout)
        {
            using (process)
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token))
            {
                try
                {
                    var exited = process.Exited;
                    var cancelled = Task.Delay(Timeout.Infinite, stop.Token);

                    await Task.WhenAny(codeSeen.Task, exited, Task.Delay(codeTimeout, stop.Token), cancelled).ConfigureAwait(false);
                    if (cancel.IsCancellationRequested) return Stopped(GitHubCliSignInOutcome.Cancelled);
                    if (!codeSeen.Task.IsCompleted)
                    {
                        if (exited.IsCompleted) return new GitHubCliSignInResult(GitHubCliSignInOutcome.NoCode, await exited.ConfigureAwait(false));
                        return Stopped(GitHubCliSignInOutcome.NoCode);
                    }

                    await Task.WhenAny(exited, Task.Delay(overallTimeout, stop.Token), cancelled).ConfigureAwait(false);
                    if (cancel.IsCancellationRequested) return Stopped(GitHubCliSignInOutcome.Cancelled);
                    if (!exited.IsCompleted) return Stopped(GitHubCliSignInOutcome.TimedOut);

                    var exitCode = await exited.ConfigureAwait(false);
                    return new GitHubCliSignInResult(exitCode == 0 ? GitHubCliSignInOutcome.Succeeded : GitHubCliSignInOutcome.Failed, exitCode);
                }
                finally
                {
                    stop.Cancel();
                }
            }
        }

        private GitHubCliSignInResult Stopped(GitHubCliSignInOutcome outcome)
        {
            try { process.Kill(); }
            catch (Exception) { }
            return new GitHubCliSignInResult(outcome, null);
        }
    }
}
