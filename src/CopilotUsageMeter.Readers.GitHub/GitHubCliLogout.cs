using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotUsageMeter.Readers.GitHub
{
    public enum GitHubCliLogoutOutcome { Removed, Failed, Unavailable, TimedOut, InvalidLogin }

    // Removes one saved GitHub CLI login with `gh auth logout`, hidden and non-interactive. Only the exit code is used; gh's output is discarded.
    public sealed class GitHubCliLogout
    {
        public const string DefaultFileName = "gh";
        public const string DefaultArgumentFormat = "auth logout --hostname github.com --user {0}";

        private readonly string fileName;
        private readonly string argumentFormat;
        private readonly TimeSpan timeout;

        public GitHubCliLogout(string fileName = DefaultFileName, string argumentFormat = DefaultArgumentFormat, TimeSpan? timeout = null)
        {
            this.fileName = fileName;
            this.argumentFormat = argumentFormat;
            this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        public async Task<GitHubCliLogoutOutcome> RemoveAsync(string login, CancellationToken cancellationToken)
        {
            // The login is placed on a command line, so only the strict login shape is accepted, and never one that could read as an option.
            if (!GitHubAccountReader.IsValidLogin(login) || login.StartsWith("-", StringComparison.Ordinal)) return GitHubCliLogoutOutcome.InvalidLogin;
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, string.Format(argumentFormat, login))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            })
            {
                var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.OutputDataReceived += (_, __) => { };
                process.ErrorDataReceived += (_, __) => { };
                process.Exited += (_, __) => Task.Run(() =>
                {
                    try
                    {
                        process.WaitForExit();
                        exit.TrySetResult(process.ExitCode);
                    }
                    catch (Exception)
                    {
                        exit.TrySetResult(-1);
                    }
                });
                try
                {
                    process.Start();
                    process.StandardInput.Close();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                catch (Exception exception) when (exception is Win32Exception || exception is FileNotFoundException)
                {
                    return GitHubCliLogoutOutcome.Unavailable;
                }

                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    deadline.CancelAfter(timeout);
                    var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (deadline.Token.Register(() => cancelled.TrySetResult(true)))
                    {
                        var finished = await Task.WhenAny(exit.Task, cancelled.Task).ConfigureAwait(false);
                        if (finished == exit.Task) return exit.Task.Result == 0 ? GitHubCliLogoutOutcome.Removed : GitHubCliLogoutOutcome.Failed;
                    }
                }

                try { process.Kill(); }
                catch (InvalidOperationException) { }
                cancellationToken.ThrowIfCancellationRequested();
                return GitHubCliLogoutOutcome.TimedOut;
            }
        }
    }
}
