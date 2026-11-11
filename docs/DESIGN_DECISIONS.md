# Memory design decisions

Product and implementation decisions are recorded in the existing append-only `DECISIONS.md`; read it before revisiting scope, hosting, data separation, refresh, UI placement, toolkit integration or the solution/toolchain. Do not copy its history into this file or rewrite prior entries.

## 2026-11-10 - Memory complements the phase plan

- Decision: `PHASE_PLAN.md` stays the authoritative verified checklist; README summarizes capabilities/build instructions; `PROJECT_STATE.md` is the disposable session snapshot; this memory set provides concise architecture/handoff pointers. `DECISIONS.md` retains authoritative product decision history.
- Rationale: adding project memory should not create a second conflicting roadmap or reset completed Phase 1 work. The initial solution graph is a structural aid, not proof of runtime/accounting feasibility.
- Alternatives considered: replace the established plan/decision records or duplicate their contents; rejected to avoid drift.
## 2026-11-12 - One presentation model for both UIs

- Decision: `Panel.Core\UsageDisplay` is the single source of text, thresholds and segment arithmetic; the VSIX row/popup and the tray Full details window only draw it. Both redraw only when a computed signature changes, so the 2-second tick and 1-second countdown do not drop focus or tooltips.
- Rationale: two UIs formatting independently drifted before (raw wording, inconsistent states). UI types stay out of the shared library, so the drawing helpers are duplicated per UI rather than shared.
- Alternatives rejected: a host-problem protocol field (a down host cannot send it), a new OpenDetails command (OpenStatus already opens the window), real-time bar rebuilds (flicker, focus loss).

## 2026-11-13 - Loading indication is client-side and best-effort

- Decision: the row/popup shows a thin indeterminate bar and dims while loading. Loading starts on a manual Refresh (until the tray acks) or on an active-chat change (until the snapshot RefreshCount rises above the value seen at the switch, paused, or 12 s). The Full details window shows a bar while any command is pending. Hover tooltips appear after 150 ms; the Pause button sits in the Full details header.
- Rationale: the tray debounces (1.5 s) and throttles (5 s) local reads, so a switched chat briefly shows the previous data. Snapshots carry no session id (privacy), so the client cannot tell whose data it holds and uses RefreshCount as the baseline.
- Alternatives rejected: a protocol field for "loading" (the tray cannot know what the client switched to without storing more), blocking the UI until data arrives.

## 2026-11-11 - Bar sizing rules and package starts in every instance

- Decision: bars scale against the chat's largest turn with a square-root height curve and a 4 px minimum, so small turns stay visible next to a large one; slot width adapts to the row (6-16 px), bars are rounded with a 1 px gap (0.5 px when slots are tight). The VSIX package no longer returns early outside the Exp instance, so the row and Tools commands start in every VS instance. The extension version is bumped by hand in the manifest on each redeploy.
- Rationale: linear scaling made turns of a few credits nearly invisible; the Exp-only gate meant the main VS never injected the row and its menu commands did nothing.
- Known limits: only VS 18.13 Insiders has been tested (manifest floor is 17.0); the four Tools test commands must be removed before publishing (Phase 5). Build-time deploy to Exp was observed not to register the extension; installing with VSIXInstaller /rootSuffix:Exp is the reliable route (see README).

## 2026-11-11 - Full details window takes the foreground
- `ForegroundWindow.Bring` (Tray) attaches to the foreground thread, calls `SetForegroundWindow`, falls back to a synthetic F24 key press and finally a brief topmost flip. A plain `Activate()` only flashed the taskbar when the request came from Visual Studio, because Windows blocks background processes from taking focus. Not verified live yet (needs a tray restart).

## 2026-11-13 - Three-level problem presentation (normal / warning / error)

- Decision: `UsageDisplay` derives a `DisplayProblem` (scope, title, detail, code, severity) from the account and local error codes. Normal = the host theme's card (TextBox background and border brushes, 1 px, rounded, like the Total changes panel). Warning = translucent amber tint, Error = red tint, each with a "!" badge whose tooltip is the title and detail; the popup and the Full details window show the title, detail and error code. Signed out stays a normal state (Not signed in + Sign in), and "over the allowance" keeps its existing red badge.
- Error codes (red): secure-storage-failed, profile-mismatch, invalid-login, invalid-response, history_tool_missing, history_invalid_configuration, history_invalid_data, history_output_limit, and the usage host being offline. Everything else with a code (no usage figures for the account, unsupported plan/billing, rate limit, timeout/request failure, stale figures, local history not watched, local refresh failed) is a warning. `not-synced` and the informational local codes (history_not_recorded, history_no_turns, history_session_unavailable) are not problems.
- Rationale: an account without a subscription showed a neutral "credits unavailable" that looked like normal data. The sign-in never expires on a timer: only an HTTP 401 clears the credential, and that is shown as Not signed in, so there is no separate "expired" state. Messages are derived from codes because `AccountUsage` carries no message text (privacy).
- Not verified live yet: the card look, tints and popup rounded corners. The tray icon itself does not change.

## 2026-11-11 - Retry lives in the scheduler; failure is reported through a callback

- Decision: `LocalRefreshScheduler` owns the retry schedule (`RetryDelays` 5/15/45 s, then `LongRetryDelay` 5 min; `NotifyAfterFailures` 3). Because `ChatHistoryReader` reports failure as a status instead of throwing, the scheduler takes an optional `attemptFailed` callback that `TrayHost` sets from the monthly result. `TrayHost.ReadLocalAsync` suppresses `UpdateLocal` for failures 1 and 2 when the current local snapshot is available. Change events and manual refresh bypass the retry delay.
- Decision: `UsagePipeServer.ListenAsync` is a retry loop with an injectable `listenerRetryDelay` (default 1/2/4/8/16/30 s) and `ListenerFaultNotifyAfter` = 3. It catches every non-cancellation exception (not only I/O). After a fault notice the first accepted connection raises `Recovered` with the same message, and the tray hides the banner only if it still shows that message.
- Decision: `Contracts\LocalHistoryErrorCodes` is the one place that classifies local codes. Informational codes (history_not_recorded, history_no_turns, history_session_unavailable) and permanent codes (history_tool_missing, history_invalid_configuration) are not retried; they are published immediately. `UsageDisplay` reuses the informational list. Retry applies to the remaining failures (timeout, process/command failure, invalid data, output limit).
- Rationale: keeps the policy in Core/Transport (unit-testable with the fake clock) and keeps UI types out of libraries.

## 2026-11-11 - Heartbeat is a transport-level message; commands are queued per session

- Decision: `Heartbeat` is a new message type with no new fields, so the protocol version stays 1. The client ignores it and arms its watchdog only after the first one, so an older tray is never dropped; an older client ignores the unknown type. The watchdog reuses the handshake pattern (linked CTS plus `pipe.Dispose()`).
- Decision: `UsagePipeServer` keeps a per-session `Queue` guarded by a lock with one runner task at a time (order preserved). Runner tasks are tracked in `commandRuns` so `StopAsync` waits for them; they are not part of the session's worker task, so a slow command does not hold a connection slot after the client leaves. A failing runner closes the session and the read loop skips its own fault report when the commands already closed it.
- Decision: `PipeUsageClient.SendAsync` throws `CommandTimeoutException : IOException` only when the command timeout itself fired, so existing `IOException` handlers still work and the UI can word a Refresh timeout differently. A late `Error` that carries a `RequestId` is ignored; only an unmatched `Error` without one ends the connection.
- Rationale: smallest change that keeps the extension responsive; the heartbeat comes from the thread pool, so it shows the pipe is alive but not that the tray UI thread is responsive.

## 2026-11-14 - Protocol mismatch, versions and paused state (UI polish)
- Decision: only `UsageProtocol.Version` must match between tray and extension; app versions may differ. A mismatch (`version_mismatch` error, wrong HelloAck or message protocol) now sets `PipeUsageClient.VersionMismatchStatus`, keeps retrying every 10 s (injectable for tests) and no longer stops. The VSIX row shows red "Update needed"; tooltip/popup say extension and tray versions do not match and include the extension version. The tray still shows nothing for a mismatch.
- Decision: one shared `<Version>` in `Directory.Build.props` (enforced equal to the vsixmanifest version by a test); `ProductVersion` formats it. Full details shows the tray version in its title; the popup shows the extension name and version top right.
- Decision: paused is a light-grey row with an orange "!" badge and a tooltip; a problem tint wins. The Full details Paused chip is orange. Bar hover is a solid darker red fill (outline only for keyboard focus). The chat label reads "This chat <n>".
- Decision (supersedes the 2026-11-13 line "Signed out stays a normal state"): signed out is an amber Warning problem ("Not signed in") with the Sign in action, so it is visible in the collapsed row.
- Not verified live: the mismatch state (needs a deliberately mismatched build) and 0.1.16/0.1.17 visuals.

## 2026-11-11 - Sign-in orchestration is UI-free and injected

- Decision: `GitHubCliSignIn` (netstandard2.0 library) owns parsing, timeouts and cancellation behind `ISignInProcessLauncher`; `GitHubSignInFlow` (Tray, no WPF types, compiled into the test assembly) owns the dialog state machine behind `ISignInDialog` and a `post` delegate. `GitHubCliSignInRun.Completion` never faults and `Cancel()` is always safe; a throwing UI callback is swallowed. The dialog is created only after `Begin` succeeds so a missing `gh` shows nothing.
- Rationale: output arrives on pool threads, so all UI work is posted; keeping WPF out lets the whole flow be tested with fakes and real `cmd.exe`. Netstandard2.0 has no `Process.Kill(bool)`, so only the `gh` process itself is killed.
- The dialog text says the account is connected, not who it is, because the login is verified separately afterwards.

## 2026-11-11 - Account removal is an in-process delegate

- Decision: `MainWindow` receives a removal delegate instead of sending a protocol command. `UsagePipeServer` passes any command type to `TrayHost.ProcessAsync`, so a `RemoveAccount` case there would let any same-user pipe client delete `gh` logins. `GitHubAccountReader.ConnectedLogin` exposes the credential's login (not the token) because the snapshot login is null until the profile is verified.
- Rationale: keeps a credential-deleting action reachable only from the tray's own window.
