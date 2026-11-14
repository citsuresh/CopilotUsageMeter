using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Readers.ChatHistoryCli
{
    public interface IChatHistoryProcessRunner
    {
        Task<ChatHistoryProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
    }

    public sealed class ChatHistoryProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = "";
        public string? ErrorCode { get; set; }
    }

    public sealed class ChatHistoryProcessRunner : IChatHistoryProcessRunner
    {
        public const int MaximumOutputCharacters = 1024 * 1024;
        public const int MaximumErrorCharacters = 64 * 1024;
        private readonly TimeSpan timeout;

        public ChatHistoryProcessRunner(TimeSpan? timeout = null)
        {
            this.timeout = timeout ?? TimeSpan.FromSeconds(15);
            if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(2))
                throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        public async Task<ChatHistoryProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!Path.IsPathRooted(executablePath) || !executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || !ValidArguments(arguments)) return Failure("history_invalid_configuration");
            }
            catch (ArgumentException) { return Failure("history_invalid_configuration"); }

            using (var process = new Process())
            using (var timeoutSource = new CancellationTokenSource(timeout))
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = JoinArguments(arguments),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(executablePath)!
                };
                process.StartInfo.EnvironmentVariables.Remove("DOTNET_ROOT");
                process.StartInfo.EnvironmentVariables.Remove("DOTNET_ROOT(x86)");
                process.StartInfo.EnvironmentVariables.Remove("DOTNET_ROOT_X64");
                process.StartInfo.EnvironmentVariables.Remove("DOTNET_ROOT_X86");
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.EnableRaisingEvents = true;
                process.Exited += (_, __) => exited.TrySetResult(true);
                var started = false;
                try
                {
                    started = process.Start();
                    if (!started) return Failure("history_start_failed");
                    var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (stop.Token.Register(() => { KillOwnedProcess(process); stopped.TrySetResult(true); }))
                    {
                        var stdout = ReadBoundedAsync(process.StandardOutput, MaximumOutputCharacters, true, process);
                        var stderr = ReadBoundedAsync(process.StandardError, MaximumErrorCharacters, false, process);
                        var all = Task.WhenAll(stdout, stderr, exited.Task);
                        if (await Task.WhenAny(all, stopped.Task).ConfigureAwait(false) != all)
                        {
                            // Do not wait indefinitely on a pipe inherited by another process.
                            _ = all.ContinueWith(task => { var ignored = task.Exception; }, CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                            cancellationToken.ThrowIfCancellationRequested();
                            return Failure("history_timeout");
                        }
                        await all.ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (timeoutSource.IsCancellationRequested) return Failure("history_timeout");
                        return new ChatHistoryProcessResult { ExitCode = process.ExitCode, StandardOutput = stdout.Result };
                    }
                }
                catch (OutputLimitException) { return Failure("history_output_limit"); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                    || error is System.ComponentModel.Win32Exception || error is System.Security.SecurityException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Diagnostics.Warning("history.process", timeoutSource.IsCancellationRequested ? "history_timeout" : "history_process_failed", error);
                    return Failure(timeoutSource.IsCancellationRequested ? "history_timeout" : "history_process_failed");
                }
                finally
                {
                    if (started) KillOwnedProcess(process);
                }
            }
        }

        private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, bool retain, Process process)
        {
            var buffer = new char[4096];
            var result = retain ? new StringBuilder() : null;
            var count = 0;
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
            {
                count += read;
                if (count > limit)
                {
                    KillOwnedProcess(process);
                    throw new OutputLimitException();
                }
                result?.Append(buffer, 0, read);
            }
            return result?.ToString() ?? "";
        }

        private static void KillOwnedProcess(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static string JoinArguments(IReadOnlyList<string> arguments)
        {
            var result = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (result.Length != 0) result.Append(' ');
                result.Append('"');
                var slashes = 0;
                foreach (var character in argument)
                {
                    if (character == '\\') { slashes++; continue; }
                    result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                    result.Append(character);
                    slashes = 0;
                }
                result.Append('\\', slashes * 2).Append('"');
            }
            return result.ToString();
        }

        private static bool ValidArguments(IReadOnlyList<string> arguments)
        {
            if (arguments == null || arguments.Count == 0) return false;
            var count = arguments.Count;
            if (count >= 2 && arguments[count - 2] == "--root")
            {
                if (!Path.IsPathRooted(arguments[count - 1])) return false;
                count -= 2;
            }
            if (arguments[0] == "usage-turns")
                return count == 4 && IsValidSessionId(arguments[1]) && arguments[2] == "--limit" && arguments[3] == "20";
            return arguments[0] == "usage-breakdown" && ValidUsageBreakdown(arguments, count);
        }

        // Two fixed shapes: the monthly reads (no filters; day/100 or repo/500) and the details read (optional exact-form --repo and
        // --model filters, any day/repo/model grouping, 500 rows). Each option may appear once, in this order.
        private static bool ValidUsageBreakdown(IReadOnlyList<string> arguments, int count)
        {
            if (count < 9 || arguments[1] != "--from" || !IsUtcBound(arguments[2]) || arguments[3] != "--to" || !IsUtcBound(arguments[4])) return false;
            var index = 5;
            var repoFilter = false;
            var modelFilter = false;
            if (arguments[index] == "--repo")
            {
                if (index + 1 >= count || !Core.DetailsQuery.IsValidRepositoryFilter(arguments[index + 1])) return false;
                index += 2; repoFilter = true;
            }
            if (index < count && arguments[index] == "--model")
            {
                if (index + 1 >= count || !Core.DetailsQuery.IsValidModelFilter(arguments[index + 1])) return false;
                index += 2; modelFilter = true;
            }
            if (count != index + 4 || arguments[index] != "--group-by" || arguments[index + 2] != "--limit") return false;
            var group = arguments[index + 1];
            var limit = arguments[index + 3];
            if (!repoFilter && !modelFilter && ((group == "day" && limit == "100") || (group == "repo" && limit == "500"))) return true;
            if (!Core.DetailsQuery.IsValidGroupByArgument(group) || limit != "500") return false;

            // A filter is only exact when its dimension is also grouped, which is the shape DetailsQuery produces.
            var dimensions = group.Split(',');
            return (!repoFilter || Array.IndexOf(dimensions, "repo") >= 0) && (!modelFilter || Array.IndexOf(dimensions, "model") >= 0);
        }

        private static bool IsUtcBound(string value) => DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);

        internal static bool IsValidSessionId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 160 || !char.IsLetterOrDigit(id[0])) return false;
            foreach (var character in id)
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9') || character == '-' || character == '_')) return false;
            return true;
        }

        private static ChatHistoryProcessResult Failure(string code) => new ChatHistoryProcessResult { ExitCode = -1, ErrorCode = code };
        private sealed class OutputLimitException : Exception { }
    }
}