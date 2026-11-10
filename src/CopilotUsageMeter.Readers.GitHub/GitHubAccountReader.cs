using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CopilotUsageMeter.Readers.GitHub
{
    public sealed class GitHubAccountReader : IAccountReader, IDisposable
    {
        private readonly object gate = new object();
        private readonly IApprovedGitHubApi api;
        private readonly IAccountCredentialStore store;
        private readonly IAccountClock clock;
        private GitHubCredential? credential;
        private AccountReadResult current = AccountReadResult.Empty("signed-out");
        private CancellationTokenSource identity = new CancellationTokenSource();
        private Task<AccountReadResult>? flight;
        private int generation;
        private bool disposed;

        public GitHubAccountReader(IApprovedGitHubApi api, IAccountCredentialStore store, IAccountClock clock)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }
        public bool IsAuthorized { get { lock (gate) return credential != null; } }
        public AccountReadResult Current { get { lock (gate) return current.Copy(); } }
        public event EventHandler? AuthorizationChanged;

        public Task<AccountReadResult> RestoreAsync() => ChangeIdentity(null, false, false, true);
        public Task<AccountReadResult> SignInAsync(string login) => ChangeIdentity(login, false, false, false);
        public Task<AccountReadResult> SwitchAccountAsync(string login) => SignInAsync(login);
        public Task<AccountReadResult> SignOutAsync() => ChangeIdentity(null, true, false, false);
        public Task<AccountReadResult> SkipAsync() => ChangeIdentity(null, true, true, false);

        private Task<AccountReadResult> ChangeIdentity(string? login, bool signOut, bool skip, bool restore)
        {
            lock (gate)
            {
                ThrowIfDisposed();
                generation++;
                credential = null;
                current = AccountReadResult.Empty(skip ? "skipped" : "signed-out");
                flight = null;
                var previous = identity;
                identity = new CancellationTokenSource();
                previous.Cancel();
                previous.Dispose();
                try
                {
                    if (!restore) store.Clear();
                    if (!signOut)
                    {
                        var discovery = !restore && string.IsNullOrWhiteSpace(login) ? DiscoverLogin() : null;
                        if (discovery != null && discovery.Failure != null) current = discovery.Failure;
                        else if (!restore && !IsValidLogin(discovery?.Login ?? login))
                            current = AccountReadResult.Empty("error", "invalid-login", "Enter a valid GitHub login.");
                        else
                        {
                            var imported = restore ? store.Load() : store.ImportFromGitHubCli(discovery?.Login ?? login!);
                            if (imported == null || !IsValidLogin(imported.Login) || !IsValidToken(imported.Token))
                                current = AccountReadResult.Empty("signed-out", "secure-credential-required", "Sign in with GitHub CLI using secure credential storage, then reconnect.");
                            else
                            {
                                if (!restore) store.Save(imported);
                                credential = imported;
                                current = AccountReadResult.Empty("unavailable", "not-synced", "GitHub account has not been refreshed.");
                                // The profile is trusted only after GET /user, not from local history or a supplied name.
                            }
                        }
                    }
                }
                catch (GitHubCredentialStoreException) { StorageFailed(); }
                catch (PlatformNotSupportedException) { StorageFailed(); }
                catch (DllNotFoundException) { StorageFailed(); }
                catch (EntryPointNotFoundException) { StorageFailed(); }
            }
            AuthorizationChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(Current);
        }

        public Task<AccountReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (credential == null) return Task.FromResult(current.Copy());
                if (flight != null) return flight;
                var completion = new TaskCompletionSource<AccountReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                flight = completion.Task;
                var epoch = generation;
                var operation = ReadAndPublishAsync(credential, epoch, identity.Token, cancellationToken, completion);
                _ = operation.ContinueWith(finished =>
                {
                    if (!finished.IsFaulted && !finished.IsCanceled) return;
                    lock (gate) if (epoch == generation) flight = null;
                    // Forward unexpected faults to the returned task; never turn programming errors into account data.
                    if (finished.IsFaulted) completion.TrySetException(finished.Exception!.InnerExceptions);
                    else completion.TrySetCanceled();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return completion.Task;
            }
        }

        private async Task ReadAndPublishAsync(GitHubCredential selected, int epoch, CancellationToken identityToken,
            CancellationToken requestToken, TaskCompletionSource<AccountReadResult> completion)
        {
            AccountReadResult result;
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(identityToken, requestToken))
            {
                try
                {
                    var profile = await api.GetProfileAsync(selected.Token, clock.UtcNow, cancellation.Token).ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                    result = FromStatus(profile);
                    if (profile.StatusCode == 200)
                    {
                        var root = GitHubQuotaParser.ParseObject(profile.Json);
                        var login = root["login"]?.Type == JTokenType.String ? root["login"]!.Value<string>() : null;
                        if (!IsValidLogin(login) || !string.Equals(login, selected.Login, StringComparison.OrdinalIgnoreCase) ||
                            root["id"]?.Type != JTokenType.Integer || !long.TryParse(root["id"]!.ToString(), out var id) || id <= 0)
                            result = AccountReadResult.Empty("error", "profile-mismatch", "GitHub profile could not be verified.");
                        else
                        {
                            var quota = await api.GetQuotaAsync(selected.Token, clock.UtcNow, cancellation.Token).ConfigureAwait(false);
                            cancellation.Token.ThrowIfCancellationRequested();
                            result = quota.StatusCode == 200 ? GitHubQuotaParser.Parse(quota.Json, clock.UtcNow) : FromStatus(quota);
                            result.Login = login;
                            result.AccountId = id.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                }
                catch (OperationCanceledException) when (requestToken.IsCancellationRequested && !identityToken.IsCancellationRequested)
                {
                    lock (gate) if (epoch == generation) flight = null;
                    completion.TrySetCanceled();
                    return;
                }
                catch (OperationCanceledException) when (identityToken.IsCancellationRequested)
                {
                    lock (gate) completion.TrySetResult(current.Copy());
                    return;
                }
                catch (OperationCanceledException) { result = RequestTimedOut(); }
                catch (TimeoutException) { result = RequestTimedOut(); }
                catch (HttpRequestException) { result = RequestFailed(); }
                catch (IOException) { result = RequestFailed(); }
                catch (InvalidDataException) { result = RequestFailed(); }
                catch (JsonException) { result = AccountReadResult.Empty("error", "invalid-response", "GitHub returned an unsupported account response."); }
            }
            var changed = false;
            AccountReadResult published;
            lock (gate)
            {
                if (disposed || epoch != generation)
                {
                    completion.TrySetResult(current.Copy());
                    return;
                }
                flight = null;
                if (result.Status == "signed-out" || result.ErrorCode == "profile-mismatch")
                {
                    credential = null;
                    result.Login = result.AccountId = null;
                    current = result.Copy();
                    if (!ClearSecureCredential())
                    {
                        result.ErrorCode = "secure-storage-failed";
                        result.ErrorMessage = "Secure GitHub credential storage is unavailable.";
                    }
                    changed = true;
                }
                Stamp(result);
                current = result.Copy();
                published = current.Copy();
            }
            if (changed) AuthorizationChanged?.Invoke(this, EventArgs.Empty);
            completion.TrySetResult(published);
        }

        private sealed class LoginDiscovery
        {
            public string? Login;
            public AccountReadResult? Failure;
        }

        // Only login names from Windows Credential Manager are listed; no secret is read until a single login is chosen.
        private LoginDiscovery DiscoverLogin()
        {
            var logins = store.ListGitHubCliLogins();
            if (logins.Count == 1) return new LoginDiscovery { Login = logins[0] };
            return new LoginDiscovery
            {
                Failure = logins.Count == 0
                    ? AccountReadResult.Empty("signed-out", "cli-login-not-found", "No GitHub CLI login was found in Windows Credential Manager. Use browser sign-in first.")
                    : AccountReadResult.Empty("signed-out", "multiple-cli-logins", "Several GitHub CLI logins were found (" + string.Join(", ", logins) + "). Enter the one to connect.")
            };
        }

        private void StorageFailed()
        {
            credential = null;
            current = AccountReadResult.Empty("error", "secure-storage-failed", "Secure GitHub credential storage is unavailable.");
        }

        private bool ClearSecureCredential()
        {
            try { store.Clear(); return true; }
            catch (GitHubCredentialStoreException) { return false; }
            catch (PlatformNotSupportedException) { return false; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        private static AccountReadResult RequestFailed() =>
            AccountReadResult.Empty("error", "github-request-failed", "GitHub account access failed. Try reconnecting.");
        private static AccountReadResult RequestTimedOut() =>
            AccountReadResult.Empty("error", "github-request-timeout", "GitHub account access timed out. Try again.");

        private static AccountReadResult FromStatus(GitHubApiResponse response)
        {
            AccountReadResult result;
            if (response.StatusCode == 401)
                result = AccountReadResult.Empty("signed-out", "authorization-expired", "GitHub authorization expired. Sign in again.");
            else if (response.StatusCode == 429 || (response.StatusCode == 403 && response.RetryNotBeforeUtc.HasValue))
                result = AccountReadResult.Empty("rate-limited", "github-rate-limited", "GitHub has temporarily limited account requests.");
            else if (response.StatusCode == 403 || response.StatusCode == 404)
                result = AccountReadResult.Empty("unavailable", "quota-unavailable", "GitHub account usage is unavailable for this account or permission scope.");
            else
                result = AccountReadResult.Empty("error", "github-http-failed", "GitHub account access failed.");
            result.RetryNotBeforeUtc = response.RetryNotBeforeUtc;
            return result;
        }

        private static void Stamp(AccountReadResult result)
        {
            result.Usage.Login = result.Login;
            result.Usage.Source = result.Source;
            result.Usage.ErrorCode = result.ErrorCode;
            result.Usage.ResetUtc = result.ResetUtc;
            result.Usage.IsUnlimited = result.IsUnlimited;
        }

        internal static bool IsValidLogin(string? login)
        {
            if (string.IsNullOrEmpty(login) || login!.Length > 100) return false;
            foreach (var value in login)
                if (!(value >= 'a' && value <= 'z') && !(value >= 'A' && value <= 'Z') &&
                    !(value >= '0' && value <= '9') && value != '-' && value != '_') return false;
            return true;
        }
        internal static bool IsValidToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 2560) return false;
            foreach (var value in token) if (value <= 32 || value >= 127) return false;
            return true;
        }
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(GitHubAccountReader));
        }
        public void Dispose()
        {
            lock (gate)
            {
                disposed = true; generation++; credential = null; flight = null;
                current = AccountReadResult.Empty("signed-out"); identity.Cancel(); identity.Dispose();
            }
        }
    }
}