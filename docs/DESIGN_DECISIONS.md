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

## 2026-11-11 - Diagnostics facade lives in Contracts; hosts install the sink

- Decision: static `Diagnostics` (netstandard2.0, never throws, labels restricted to `[A-Za-z0-9._-]`, 64 chars) in Contracts so Core, Readers, Transport and Client can log without a host dependency; `App.OnStartup` installs `FileDiagnosticsSink` and `UsagePackage` installs `ActivityLogDiagnosticsSink`. `FileDiagnosticsSink` is UI-free and linked into the test assembly.
- Decision: `TrayHost.SuperviseAsync` restarts the publications, countdown and account workers (5 s to 60 s backoff, banner only on the first failure, publication re-requested after a restart); `ProcessAsync` converts unexpected exceptions into an `Error` reply (`command_failed`); `ReportUnexpectedFailure` raises the window only the first time. WinForms `ThreadException` is routed to the same path because tray-icon handlers bypass the WPF dispatcher.
- Decision: `ChatUsageIntegration` logs a repeating tick fault once per code/type until a tick succeeds, using a stopwatch because net48 lacks `TickCount64`.
- Accepted: no circuit breaker for the account worker; a persistent programming fault retries every 60 s.

## 2026-11-11 - Refresh flag is derived, never trusted from events

- Decision: `IsRefreshing` is additive on `UsageSnapshot` (protocol stays 1; older peers ignore it). `RefreshActivity` debounces by source ("local", "account"); `TrayHost.SetRefreshActivity` updates it and `TrackingUsageState` under one `refreshGate`, and `SyncAccountRefreshing` always re-reads `AccountRefreshCoordinator.IsRefreshing` (on each coordinator event, after the delay and on the 1 s countdown tick), so a missed event cannot latch the bar on. `AccountRefreshCoordinator` now raises `StateChanged` on read start, cancel and fault, as well as completion.
- Decision: `RefreshAsync` was split into `StartRefresh` plus a wrapper so the start event is raised outside the lock. The earlier line about loading behaviour (only manual refresh and chat switch) is superseded by this entry.
- Decision: tray icon drawing lives in `TrayIconPainter` (`Style` = track width/colour, arc width/colour); the tray still draws a 32 px bitmap and Windows scales it.


## 2026-11-13 - Alert state and settings live in two small files; the service has no UI types

- Decision: `alerts.json` (settings) and `alert-state.json` (month key plus fired percentages) under `%LOCALAPPDATA%\CopilotUsageMeter`, written atomically (temp file plus move), capped at 8 KB; a missing, oversized or corrupt file gives defaults and a code-only log (`alert_settings`/`alert_state` `load-failed`). Hand-edited files are normalised on load.
- Decision: `ThresholdAlertService` (UI-free, linked into the tests) takes a present callback; `TrayHost.PresentAlert` marshals to the dispatcher and draws the balloon or `AlertWindow`. Every call from `AccountChanged` is wrapped so an alert fault is logged and never reaches the tray.
- Decision: the service keeps the last usable reading so a level added during a failed read is judged against it; with none, `ThresholdAlertTracker` marks such levels as silently passed on the next usable reading. Month rollover requires `AccountUsage.AsOfUtc` in the new UTC month, so last month's kept result cannot re-fire levels.
- Fired and test alerts are logged by code only (`threshold-fired-<level>`, `test-shown-<level>`) through the existing warning level, because the sink has no information level.
## 2026-11-11 - Health notifications count reads, not events, and live in a pure tracker

- Decision: `HealthAlertTracker` (Core) holds the rules (observation count, minimum duration, once until a good observation, 6 h cool-down, remembered-but-silent while switched off); `HealthSignals` maps `AccountReadResult` and local status/code to conditions; `HealthAlertService` (Tray, UI-free, linked into tests) feeds them. `TrayHost` wraps every call so a fault in this code is logged (`health_alerts` / `evaluate-failed`, `present-failed`) and never reaches the tray.
- Decision: `AccountChanged` is raised for schedule and progress changes, so `AccountRefreshCoordinator.ReadSequence` counts completed reads and a failure only counts when the sequence changed. Rate limiting is ignored (neither a problem nor a recovery). Shown notifications log only the fixed condition name (`health_alerts` / `shown-<Condition>`).

## 2026-11-12 - Extension notices live in a pure Panel.Core tracker; the VSIX only presents them

- Decision: `ExtensionNoticeTracker` (Panel.Core, no VS types) decides Show/Close/None per notice from the pipe client's status text (`ExtensionSignals.Classify`) and a layout flag; `InfoBarPresenter` (Panel.Vsix, the only place using the VS infobar APIs) shows the infobar in the main window host and falls back to status-bar text when the host is unavailable. `ChatUsageIntegration.EvaluateNotices` runs at the end of each 2 s tick and never throws.
- Decision: `Handled` is set only when a notice is actually shown, so a notice held back by the cool-down is still shown later in a long outage; a failed show calls `ShowFailed` and is retried after 30 s. The client cycles through `Connecting` while the tray is down, so `Connecting` counts as unavailable; `Resynchronizing`, `Stopped` and unknown statuses are transitional and change nothing. A test reads `PipeUsageClient.cs` to guard the literal status texts.
- Decision: the layout notice depends on `layoutUnrecognised`, set only in the top-of-chat fallback tier and only when that tier was not forced from the Tools menu; the no-anchor tier is silent because it also occurs whenever Copilot Chat is closed. Logs carry only fixed codes (`vsix.infobar` / `shown.<Notice>`, `host_unavailable`, `show_failed`).
## 2026-11-11 - Layout infobar needs an open chat
Live Exp test: Copilot Chat on its session list (no conversation, no working-set anchor) triggered the fallback-position infobar although the layout was normal. The layout notice is now raised only while an active chat id is known (ChatUsageIntegration.EvaluateNotices). Unverified: that the session list yields no active id, and that a real layout change still yields one.

## 2026-11-11 - Notification identity for the tray
Tray balloons showed Windows' blank slashed-circle placeholder and the exe name: the process had no AppUserModelID and the exe no icon. The tray exe now has an pp.ico (plain ring) as ApplicationIcon, and NotificationIdentity.Register (called first in App.OnStartup) sets the process AppUserModelID and writes a per-user HKCU\Software\Classes\AppUserModelId\CopilotUsageMeter.Tray DisplayName/IconUri. Failure is logged, never fatal. Unverified that Windows honours it for NotifyIcon balloons; real toasts (as in KekaAttendenceChecker) are deferred to the ROADMAP.
