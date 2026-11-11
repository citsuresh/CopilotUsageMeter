using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace CopilotUsageMeter.Readers.GitHub
{
    // Starts gh with no console window, closed input (so gh takes its non-interactive path and never waits for Enter) and captured output.
    public sealed class GitHubCliProcessLauncher : ISignInProcessLauncher
    {
        public const string DefaultFileName = "gh";
        public const string DefaultArguments = "auth login --web --hostname github.com --git-protocol https";

        private readonly string fileName;
        private readonly string arguments;

        public GitHubCliProcessLauncher(string fileName = DefaultFileName, string arguments = DefaultArguments)
        {
            this.fileName = fileName;
            this.arguments = arguments;
        }

        public ISignInProcess Start(Action<string> onLine)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };
            var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
            // The parameterless WaitForExit makes sure the redirected output handlers have delivered every line before the exit code is published.
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
            catch
            {
                process.Dispose();
                throw;
            }
            return new OwnedProcess(process, exit.Task);
        }

        private sealed class OwnedProcess : ISignInProcess
        {
            private readonly Process process;

            public OwnedProcess(Process process, Task<int> exited)
            {
                this.process = process;
                Exited = exited;
            }

            public Task<int> Exited { get; }

            public void Kill()
            {
                try { process.Kill(); }
                catch (InvalidOperationException) { }
            }

            public void Dispose() => process.Dispose();
        }
    }
}
