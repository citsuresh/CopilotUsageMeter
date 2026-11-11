using System.Net;
using System.Net.Http;
using CopilotUsageMeter.Core;
using CopilotUsageMeter.Readers.GitHub;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class GitHubAccountTests
{
    private const string Quota = "{\"quota_snapshots\":{\"premium_interactions\":{\"entitlement\":50,\"credits_used\":10,\"remaining\":39}}}";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task SecureImportReadAndLogoutAreSeparateFromGhAndLocalHistory()
    {
        var api = new FakeApi();
        var store = new Store();
        using var reader = new GitHubAccountReader(api, store, new Clock());
        Assert.Equal("signed-out", reader.Current.Status);
        await reader.SkipAsync();
        Assert.Equal("skipped", reader.Current.Status);
        Assert.Equal(0, api.Calls);
        await reader.SignInAsync("account-001");
        Assert.True(reader.IsAuthorized);
        Assert.Null(reader.Current.Login);
        Assert.Equal(0, api.Calls);
        var result = await reader.ReadAsync(default);
        Assert.Equal("available", result.Status);
        Assert.Equal("account-001", result.Login);
        Assert.Equal("101", result.AccountId);
        Assert.Equal(39m, result.Usage.Remaining);
        Assert.Equal(result.Source, result.Usage.Source);
        result.Usage.Remaining = 999m;
        Assert.Equal(39m, reader.Current.Usage.Remaining);
        await reader.SignOutAsync();
        Assert.False(reader.IsAuthorized);
        Assert.Null(reader.Current.Login);
        Assert.Null(reader.Current.Usage.Consumed);
        Assert.Null(store.Saved);
    }

    [Theory]
    [InlineData(401, "signed-out", "authorization-expired")]
    [InlineData(403, "unavailable", "quota-unavailable")]
    [InlineData(404, "unavailable", "quota-unavailable")]
    [InlineData(429, "rate-limited", "github-rate-limited")]
    [InlineData(500, "error", "github-http-failed")]
    public async Task MapsHttpStatusWithoutRawBody(int status, string expected, string code)
    {
        var api = new FakeApi { QuotaResponse = new GitHubApiResponse(status, "DO-NOT-EXPOSE") };
        using var reader = new GitHubAccountReader(api, new Store(), new Clock());
        await reader.SignInAsync("account-001");
        var result = await reader.ReadAsync(default);
        Assert.Equal(expected, result.Status);
        Assert.Equal(code, result.ErrorCode);
        Assert.Null(result.Usage.Remaining);
        Assert.DoesNotContain("DO-NOT-EXPOSE", result.ErrorMessage!);
        Assert.Equal(status != 401, reader.IsAuthorized);
        if (status == 401) Assert.Null(result.Login);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankLoginConnectsTheSingleDiscoveredGitHubCliLogin(string typed)
    {
        var api = new FakeApi();
        var store = new Store();
        using var reader = new GitHubAccountReader(api, store, new Clock());
        var connected = await reader.SignInAsync(typed);
        Assert.True(reader.IsAuthorized);
        Assert.Equal("not-synced", connected.ErrorCode);
        Assert.Equal("account-001", store.Saved!.Login);
        Assert.Equal(0, api.Calls);
        Assert.Equal("account-001", (await reader.ReadAsync(default)).Login);
    }

    [Fact]
    public async Task BlankLoginWithNoOrSeveralDiscoveredLoginsNeverAuthorizesOrSavesACredential()
    {
        var api = new FakeApi();
        var store = new Store();
        store.Logins.Clear();
        using var reader = new GitHubAccountReader(api, store, new Clock());
        var none = await reader.SignInAsync("");
        Assert.Equal("signed-out", none.Status);
        Assert.Equal("cli-login-not-found", none.ErrorCode);
        store.Logins.AddRange(new[] { "account-001", "account-002" });
        var several = await reader.SignInAsync("");
        Assert.Equal("signed-out", several.Status);
        Assert.Equal("multiple-cli-logins", several.ErrorCode);
        Assert.Contains("account-002", several.ErrorMessage!);
        Assert.Equal(new[] { "account-001", "account-002" }, reader.ListCliLogins());
        Assert.False(reader.IsAuthorized);
        Assert.Null(store.Saved);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task BlankLoginWithUnavailableSecureStorageIsSanitizedAndExplicitInvalidLoginStillFails()
    {
        var store = new Store();
        using var reader = new GitHubAccountReader(new FakeApi(), store, new Clock());
        Assert.Equal("invalid-login", (await reader.SignInAsync("bad login!")).ErrorCode);
        store.Throw = true;
        var failed = await reader.SignInAsync("");
        Assert.Equal("secure-storage-failed", failed.ErrorCode);
        Assert.False(reader.IsAuthorized);
    }

    [Fact]
    public async Task MissingSecureCredentialAndStoreFailureDoNotAuthorize()
    {
        var store = new Store { Missing = true };
        var api = new FakeApi();
        using var reader = new GitHubAccountReader(api, store, new Clock());
        Assert.Equal("secure-credential-required", (await reader.SignInAsync("account-001")).ErrorCode);
        Assert.False(reader.IsAuthorized);
        store.Throw = true;
        Assert.Equal("secure-storage-failed", (await reader.SignInAsync("account-001")).ErrorCode);
        Assert.DoesNotContain("PRIVATE", reader.Current.ErrorMessage!);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task ProfileMismatchNeverRequestsQuotaOrRetainsAuthorization()
    {
        var api = new FakeApi { Profile = "{\"login\":\"account-002\",\"id\":102}" };
        using var reader = new GitHubAccountReader(api, new Store(), new Clock());
        await reader.SignInAsync("account-001");
        var result = await reader.ReadAsync(default);
        Assert.Equal("profile-mismatch", result.ErrorCode);
        Assert.False(reader.IsAuthorized);
        Assert.Null(result.Login);
        Assert.Equal(1, api.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldInFlightReadCannotRestoreClearedIdentityOrOverwriteNewAccount(bool switching)
    {
        var pending = new TaskCompletionSource<GitHubApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeApi { PendingQuota = pending.Task };
        using var reader = new GitHubAccountReader(api, new Store(), new Clock());
        await reader.SignInAsync("account-001");
        var old = reader.ReadAsync(default);
        Assert.Same(old, reader.ReadAsync(default));
        Assert.Equal(2, api.Calls);
        if (switching)
        {
            await reader.SwitchAccountAsync("account-002");
            Assert.Null(reader.Current.Login);
            Assert.Null(reader.Current.Usage.Consumed);
            api.PendingQuota = null;
            api.Profile = "{\"login\":\"account-002\",\"id\":102}";
            await reader.ReadAsync(default);
            Assert.Equal("account-002", reader.Current.Login);
        }
        else await reader.SignOutAsync();
        pending.SetResult(new GitHubApiResponse(200, Quota));
        await old;
        Assert.Equal(switching ? "account-002" : null, reader.Current.Login);
        Assert.Equal(switching ? "available" : "signed-out", reader.Current.Status);
        Assert.Equal(switching, reader.IsAuthorized);
    }

    [Fact]
    public async Task RestoreReadsOnlyOwnSecureStoreAndDoesNotMakeNetworkRequests()
    {
        var store = new Store { Saved = new GitHubCredential("account-001", "fixture-credential") };
        var api = new FakeApi();
        using var reader = new GitHubAccountReader(api, store, new Clock());
        await reader.RestoreAsync();
        Assert.True(reader.IsAuthorized);
        Assert.Equal(0, api.Calls);
        Assert.Equal(0, store.Clears);
        Assert.Null(reader.Current.Login);
    }

    [Fact]
    public async Task NetworkExceptionIsSanitizedAndRateResetIsPreserved()
    {
        var api = new FakeApi { Throw = true };
        using var reader = new GitHubAccountReader(api, new Store(), new Clock());
        await reader.SignInAsync("account-001");
        Assert.Equal("github-request-failed", (await reader.ReadAsync(default)).ErrorCode);
        Assert.DoesNotContain("PRIVATE", reader.Current.ErrorMessage!);
        api.Throw = false;
        api.QuotaResponse = new GitHubApiResponse(403, "PRIVATE", Now.AddMinutes(2));
        var result = await reader.ReadAsync(default);
        Assert.Equal("rate-limited", result.Status);
        Assert.Equal(Now.AddMinutes(2), result.RetryNotBeforeUtc);
    }

    [Fact]
    public async Task ProgrammingFaultsAreNotConvertedToSanitizedStorageOrHttpFailures()
    {
        var api = new FakeApi();
        var store = new Store();
        using var reader = new GitHubAccountReader(api, store, new Clock());
        await reader.SignInAsync("account-001");
        api.Fault = new InvalidOperationException("invented-programming-fault");
        var first = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(default));
        Assert.Same(api.Fault, first);
        api.Fault = null;
        Assert.Equal("available", (await reader.ReadAsync(default)).Status);
        store.Fault = new InvalidOperationException("invented-storage-programming-fault");
        var second = await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.SignOutAsync());
        Assert.Same(store.Fault, second);
        Assert.False(reader.IsAuthorized);
        Assert.Null(reader.Current.Login);
    }

    [Theory]
    [InlineData("http", "github-request-failed")]
    [InlineData("io", "github-request-failed")]
    [InlineData("invalid-data", "github-request-failed")]
    [InlineData("json", "invalid-response")]
    [InlineData("timeout", "github-request-timeout")]
    public async Task ExpectedTransportAndJsonErrorsHaveFixedMessages(string kind, string code)
    {
        Exception fault = kind switch
        {
            "http" => new HttpRequestException("PRIVATE"),
            "io" => new IOException("PRIVATE"),
            "invalid-data" => new InvalidDataException("PRIVATE"),
            "json" => new Newtonsoft.Json.JsonReaderException("PRIVATE"),
            _ => new TimeoutException("PRIVATE")
        };
        using var reader = new GitHubAccountReader(new FakeApi { Fault = fault }, new Store(), new Clock());
        await reader.SignInAsync("account-001");
        var result = await reader.ReadAsync(default);
        Assert.Equal("error", result.Status);
        Assert.Equal(code, result.ErrorCode);
        Assert.DoesNotContain("PRIVATE", result.ErrorMessage!);
    }

    [Fact]
    public async Task CallerCancellationIsNotMappedToAnErrorAndReaderCanRetry()
    {
        var api = new FakeApi { Fault = new OperationCanceledException() };
        using var reader = new GitHubAccountReader(api, new Store(), new Clock());
        await reader.SignInAsync("account-001");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cancellation.Token));
        Assert.Equal("not-synced", reader.Current.ErrorCode);
        api.Fault = null;
        Assert.Equal("available", (await reader.ReadAsync(default)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledBodyIsBoundedByThirtySecondDeadlineOrCallerCancellation(bool callerCancels)
    {
        var clock = new DeadlineClock();
        using var body = new StalledBody();
        using var api = new GitHubApiClient(new StalledHandler(body), clock);
        using var cancellation = new CancellationTokenSource();
        var read = api.GetProfileAsync("fixture-credential", Now, cancellation.Token);
        await body.ReadStarted.Task;
        Assert.Equal(TimeSpan.FromSeconds(30), clock.Delay);
        Assert.False(read.IsCompleted);
        if (callerCancels)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        }
        else
        {
            clock.Expire();
            var failure = await Assert.ThrowsAsync<TimeoutException>(() => read);
            Assert.Equal("GitHub account request timed out.", failure.Message);
        }
        Assert.True(body.CancellationObserved);
    }

    [Fact]
    public async Task ApiOnlyUsesApprovedFixedGetUrlsAndHonorsBothRateHeaders()
    {
        var handler = new Handler();
        using var api = new GitHubApiClient(handler);
        var profile = await api.GetProfileAsync("fixture-credential", Now, default);
        var quota = await api.GetQuotaAsync("fixture-credential", Now, default);
        Assert.Equal(new[] { "https://api.github.com/user", "https://api.github.com/copilot_internal/user" }, handler.Urls);
        Assert.Equal(Now.AddMinutes(3), profile.RetryNotBeforeUtc);
        Assert.Equal(Now.AddMinutes(3), quota.RetryNotBeforeUtc);
        Assert.Empty(profile.Json);
    }

    private sealed class DeadlineClock : IAccountClock
    {
        private readonly TaskCompletionSource<bool> elapsed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan Delay;
        public DateTimeOffset UtcNow => Now;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delay = delay;
            cancellationToken.Register(() => elapsed.TrySetCanceled(cancellationToken));
            return elapsed.Task;
        }
        public void Expire() => elapsed.TrySetResult(true);
    }

    private sealed class StalledHandler(StalledBody body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
    }

    private sealed class StalledBody : Stream
    {
        public readonly TaskCompletionSource<bool> ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult(true);
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Urls.Add(request.RequestUri!.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("PRIVATE") };
            response.Headers.TryAddWithoutValidation("Retry-After", "60");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", Now.AddMinutes(3).ToUnixTimeSeconds().ToString());
            return Task.FromResult(response);
        }
    }

    private sealed class Clock : IAccountClock
    {
        public DateTimeOffset UtcNow => Now;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Store : IAccountCredentialStore
    {
        public GitHubCredential? Saved;
        public bool Missing, Throw;
        public Exception? Fault;
        public int Clears;
        public List<string> Logins = new() { "account-001" };
        public GitHubCredential? Load() => Saved;
        public IReadOnlyList<string> ListGitHubCliLogins()
        {
            if (Throw) throw new GitHubCredentialStoreException();
            return Logins;
        }
        public GitHubCredential? ImportFromGitHubCli(string login) => Missing ? null : new(login, "fixture-credential");
        public void Save(GitHubCredential credential) => Saved = credential;
        public void Clear()
        {
            if (Fault != null) throw Fault;
            if (Throw) throw new GitHubCredentialStoreException();
            Saved = null;
            Clears++;
        }
    }
    private sealed class FakeApi : IApprovedGitHubApi
    {
        public string Profile = "{\"login\":\"account-001\",\"id\":101}";
        public GitHubApiResponse QuotaResponse = new(200, Quota);
        public Task<GitHubApiResponse>? PendingQuota;
        public bool Throw;
        public Exception? Fault;
        public int Calls;
        public Task<GitHubApiResponse> GetProfileAsync(string token, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fault != null) throw Fault;
            if (Throw) throw new HttpRequestException("PRIVATE");
            return Task.FromResult(new GitHubApiResponse(200, Profile));
        }
        public Task<GitHubApiResponse> GetQuotaAsync(string token, DateTimeOffset now, CancellationToken cancellationToken)
        { Calls++; return PendingQuota ?? Task.FromResult(QuotaResponse); }
    }
}