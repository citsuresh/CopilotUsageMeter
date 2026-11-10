using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Readers.GitHub
{
    public sealed class GitHubApiResponse
    {
        public GitHubApiResponse(int statusCode, string json, DateTimeOffset? retryNotBeforeUtc = null)
        { StatusCode = statusCode; Json = json; RetryNotBeforeUtc = retryNotBeforeUtc; }
        public int StatusCode { get; }
        public string Json { get; }
        public DateTimeOffset? RetryNotBeforeUtc { get; }
    }

    public interface IApprovedGitHubApi
    {
        Task<GitHubApiResponse> GetProfileAsync(string token, DateTimeOffset now, CancellationToken cancellationToken);
        Task<GitHubApiResponse> GetQuotaAsync(string token, DateTimeOffset now, CancellationToken cancellationToken);
    }

    public sealed class GitHubApiClient : IApprovedGitHubApi, IDisposable
    {
        private readonly HttpClient http;
        private readonly IAccountClock? timeoutClock;
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        public GitHubApiClient() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }
        public GitHubApiClient(HttpMessageHandler handler, IAccountClock? timeoutClock = null)
        {
            this.timeoutClock = timeoutClock;
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }
        public Task<GitHubApiResponse> GetProfileAsync(string token, DateTimeOffset now, CancellationToken cancellationToken) =>
            GetAsync("https://api.github.com/user", token, now, cancellationToken);
        public Task<GitHubApiResponse> GetQuotaAsync(string token, DateTimeOffset now, CancellationToken cancellationToken) =>
            GetAsync("https://api.github.com/copilot_internal/user", token, now, cancellationToken);

        private async Task<GitHubApiResponse> GetAsync(string url, string token, DateTimeOffset now, CancellationToken cancellationToken)
        {
            using (var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var stopDeadline = new CancellationTokenSource())
            {
                var deadline = CancelAtDeadlineAsync(requestDeadline, stopDeadline.Token);
                try
                {
                    return await ReadResponseAsync(url, token, now, requestDeadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && requestDeadline.IsCancellationRequested)
                {
                    throw new TimeoutException("GitHub account request timed out.");
                }
                finally
                {
                    stopDeadline.Cancel();
                    await deadline.ConfigureAwait(false);
                }
            }
        }

        private async Task CancelAtDeadlineAsync(CancellationTokenSource requestDeadline, CancellationToken stopDeadline)
        {
            try
            {
                await (timeoutClock?.DelayAsync(RequestTimeout, stopDeadline) ?? Task.Delay(RequestTimeout, stopDeadline)).ConfigureAwait(false);
                requestDeadline.Cancel();
            }
            catch (OperationCanceledException) when (stopDeadline.IsCancellationRequested) { }
        }

        private async Task<GitHubApiResponse> ReadResponseAsync(string url, string token, DateTimeOffset now, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.UserAgent.ParseAdd("CopilotUsageMeter/0.1");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    DateTimeOffset? retry = response.Headers.RetryAfter?.Date;
                    if (response.Headers.RetryAfter?.Delta != null) retry = now + response.Headers.RetryAfter.Delta.Value;
                    if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) &&
                        response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && string.Join("", remaining) == "0" &&
                        long.TryParse(string.Join("", reset), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
                    {
                        try
                        {
                            var rateReset = DateTimeOffset.FromUnixTimeSeconds(epoch);
                            if (!retry.HasValue || rateReset > retry) retry = rateReset;
                        }
                        catch (ArgumentOutOfRangeException) { }
                    }
                    // Error bodies can contain arbitrary server/proxy text; never read or expose them.
                    if (!response.IsSuccessStatusCode) return new GitHubApiResponse((int)response.StatusCode, "", retry);
                    if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("GitHub response exceeds the supported size.");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var buffer = new MemoryStream())
                    {
                        var bytes = new byte[8192];
                        int count;
                        while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false)) != 0)
                        {
                            if (buffer.Length + count > 1024 * 1024) throw new InvalidDataException("GitHub response exceeds the supported size.");
                            buffer.Write(bytes, 0, count);
                        }
                        return new GitHubApiResponse((int)response.StatusCode, System.Text.Encoding.UTF8.GetString(buffer.ToArray()), retry);
                    }
                }
            }
        }
        public void Dispose() => http.Dispose();
    }
}