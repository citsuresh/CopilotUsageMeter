# GitHub account adapter (Phase 4 standalone)

This adapter reads **only** `GET https://api.github.com/user` and `GET https://api.github.com/copilot_internal/user`. The quota endpoint is **unsupported, undocumented and may change or be blocked**. Account data never uses local history and is never added to local totals. No universal plan/billing support is claimed.

## Secure sign-in

The host displays `WindowsAccountCredentialStore.SignInInstructions`. The user explicitly runs `gh auth login --web --hostname github.com` (GitHub CLI's own OAuth app), then supplies the GitHub login to `GitHubAccountReader.SignInAsync(login)`. `GitHubAccountReader` does **not** launch login, register an OAuth app or read `gh auth token`, environment tokens or `hosts.yml`. It imports **only** a Windows Credential Manager generic credential at `gh:github.com:<login>` and stores its own protected copy at `CopilotUsageMeter:github.com`. A gh plaintext fallback or a different keyring layout is rejected with `secure-credential-required`, not silently accepted. Browser/device-flow UX is provided by GitHub CLI, not an in-app OAuth implementation. Separately, `GitHubCliSignIn` and `GitHubCliProcessLauncher` run `gh auth login --web` hidden for the tray host and report only the one-time device code (redacted in `ToString`, never logged); `gh` still owns the OAuth app and token storage, and the code is not used for credential import. `GitHubCliLogout` runs `gh auth logout --hostname github.com --user <login>` hidden to remove a saved account and reports only the exit code.

`RestoreAsync()` loads only the application's secure credential. `SignOutAsync()` and `SkipAsync()` clear the app credential, profile and quota; they intentionally do not sign GitHub CLI out or delete its credential. `SwitchAccountAsync(login)` first clears the old app identity and imports the selected secure gh entry. A failed removal returns an explicit secure-storage error; the host must not claim persisted logout succeeded. An old in-flight read cannot republish a previous identity after any account command. Every read verifies the login against `/user` before requesting quota; a mismatch or HTTP 401 removes app authorization.

Composition: create `GitHubApiClient`, `WindowsAccountCredentialStore`, a host implementation of Core `IAccountClock` (`UtcNow`, `DelayAsync`) and `GitHubAccountReader(api, store, clock)`. HTTP redirects and cookies are disabled by the default transport. The host owns/disposes the reader, transport and scheduler. Injected transports/stores are trusted seams used by deterministic tests.

Credential convention verified from public source on 2026-11-10 against installed `gh --version` **2.103.3**, without inspecting any real credential: [`TokenFromKeyringForUser` and `keyringServiceName` in gh v2.103.3](https://github.com/cli/cli/blob/v2.103.3/internal/config/config.go) use service `gh:<hostname>` plus the username; [gh's keyring wrapper](https://github.com/cli/cli/blob/v2.103.3/internal/keyring/keyring.go) delegates to `zalando/go-keyring`; [go-keyring v0.2.8 Windows implementation](https://github.com/zalando/go-keyring/blob/v0.2.8/keyring_windows.go) combines `service + ":" + username` and writes the token as UTF-8 bytes in the generic credential blob. Thus the named non-expiring token target is exactly `gh:github.com:<login>`. The [pinned gh module dependency](https://github.com/cli/cli/blob/v2.103.3/go.mod) is go-keyring v0.2.8. Legacy unkeyed credentials and gh's separate managed refreshable/expiring format are deliberately **not imported**; they return `secure-credential-required`. Live OAuth/keyring operation remains unverified.

## Public integration API

All account commands return `Task<AccountReadResult>`: `RestoreAsync()`, `SignInAsync(string login)`, `SwitchAccountAsync(string login)`, `SignOutAsync()`, `SkipAsync()`. They change authorization/storage but perform no network requests. `ReadAsync(CancellationToken)` performs the profile/quota read; `IsAuthorized`, `Current` and `event EventHandler? AuthorizationChanged` implement Core `IAccountReader`. `Current` is a detached copy; identity-change events do not fire on ordinary successful reads.

Core `AccountReadResult` fields:

| Property | Type | Meaning |
| --- | --- | --- |
| `Status` | `string` | Explicit account state listed below |
| `Login` | `string?` | `/user`-verified login, never a local-history identity |
| `AccountId` | `string?` | `/user` numeric id formatted as invariant text |
| `Source` | `string` | `GitHub copilot_internal/user (unsupported; may change)` |
| `Plan` | `string?` | Optional bounded source plan label; not a promise of plan support |
| `TokenBasedBilling` | `bool?` | Optional source boolean; unsupported structured shapes remain null |
| `IsUnlimited` | `bool` | Source premium-interaction unlimited flag; no fabricated finite numbers |
| `ResetUtc` | `DateTimeOffset?` | Parsed UTC reset date; `_utc` field takes precedence |
| `RetryNotBeforeUtc` | `DateTimeOffset?` | Later Retry-After/rate-reset deadline when reported |
| `ErrorCode` | `string?` | Stable app diagnostic, not server response text |
| `ErrorMessage` | `string?` | Fixed sanitized display text |
| `Usage` | `AccountUsage` | Separate GitHub-reported premium-interaction values |

`Usage.Unit` is `github-premium-interactions`; nullable `Allowance`, `Consumed`, `Remaining` preserve source `entitlement`, `credits_used`, `remaining`; `AsOfUtc` is populated only for available quota. The reader also projects `Login`, `Source`, `ErrorCode`, `ResetUtc`, `IsUnlimited` onto `Usage`. The scheduler's `Current` adds `Usage.NextRefreshUtc`; use `NextSyncUtc` directly when assembling host snapshots. `Copy()` returns a detached copy; `Empty(string status, string? code = null, string? message = null)` creates an empty result. Callers must supply only fixed sanitized diagnostic strings to `Empty`.

`GitHubApiClient()` uses the secure fixed-endpoint default handler. `GitHubApiClient(HttpMessageHandler handler, IAccountClock? timeoutClock = null)` is the injected transport/testing overload. Its linked `RequestTimeout` is **30 seconds across SendAsync and full body streaming**, not just response headers. A deadline becomes a fixed-message `TimeoutException`, then reader error `github-request-timeout`; caller cancellation stays cancellation. Response bodies are limited to 1 MiB and error bodies are never read.

Expected storage failures use public `GitHubCredentialStoreException` (fixed message), plus concrete unsupported-platform/native-entry availability failures. Expected HTTP/stream/JSON/timeout errors become fixed account errors. Unexpected programming faults propagate through the returned task and release single-flight state; they are not converted to account results. The Core scheduler expects readers to normalize their own operational failures into `AccountReadResult`; it does not catch arbitrary exceptions. Hosts must never log raw exception messages or response payloads.

## Mapping and scheduling

`AccountReadResult` wraps `AccountUsage`, verified login/id, source label, optional plan/billing/reset/unlimited metadata, fixed error messages and optional retry deadline. Statuses are `signed-out`, `skipped`, `available`, `unavailable`, `rate-limited` and `error`. Missing/null numbers remain null, reported zero remains zero, and `remaining` is **never** calculated from `entitlement - credits_used`. Only `premium_interactions` is mapped. `token_based_billing: true` is accepted only when the premium snapshot reports both a finite `entitlement` and `credits_used`; otherwise, and for structured/unfamiliar billing shapes, the result is `unsupported-billing` without guessed usage.

Core `AccountRefreshCoordinator(reader, clock)` exposes `Current`, `LastSuccessfulSyncUtc`, `NextSyncUtc`, `IsPaused`, `IsRefreshing`, `StateChanged`; invoke `RunAsync(lifetimeToken)` for its independent loop, `NotifyDatabaseActivity()` for DB/WAL activity, `RefreshAsync(manual: true)` for a manual refresh and `SetPaused(bool)` for pause/resume. Do not equate manual/local refreshes with database activity. Authorized startup is due immediately. Normal scheduling is 5 minutes while activity occurred in the preceding 5 minutes, otherwise 15 minutes from the last successful sync. Due work is not continually postponed. Manual requests share a single flight, have a 5-second cooldown and work while paused, but do not bypass server/retry backoff. Failures retry at 15, 30, 60, 120, 240, 480, then at most 900 seconds, extended by a later GitHub Retry-After/rate-reset deadline. Identity changes immediately invalidate scheduler data and timing. Resume catches up when due.

Tests use invented IDs/numbers, injected credential stores, API responses and clocks. They make no network requests and do not read/write real credentials. Actual OAuth/keyring compatibility, other plans and live endpoint behavior remain runtime verification items.