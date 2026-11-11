# Project state

_Overwritten at the end of each working session; the detailed checklist lives in `docs/PHASE_PLAN.md`._

## Current focus

Phases 1-4 are complete. The user-authorised post-Phase-4 UI rework and polish is committed and pushed: `origin/main` = `6a26ed1` (after `c3123fc`, `1d90452`, `64b5523`, `f38151f`). Phase 5 is NOT authorised; do not start it or push without explicit direction.

Latest session: a second user-authorised resilience increment (not Phase 5) for a busy or stuck tray: server heartbeat (15 s) plus client watchdog (45 s), ordered per-session command queue (16, then `busy`), Refresh timeout 45 s with a "taking longer" message. 387 tests pass; one independent audit, findings 1-3 fixed (not re-audited); finding 4 (abandoned commands still run late) deferred by the user. Not exercised live. Before it, the resilience increment (committed as `38cc5ab` and `89ab8cf`, not pushed): 376 tests pass, independent code-review audit done and its findings applied. Local refresh retries on its own (5 s, 15 s, 45 s, then 5 min; notify after 3 failures; informational and permanent codes are not retried, via `Contracts\LocalHistoryErrorCodes`). The pipe listener restarts itself after any non-cancellation failure (1-16 s, then 30 s), notifies at the 3rd failure and clears the banner on recovery. The tray was compiled to a temp output dir only (the user's running tray locks the normal one). Not exercised live. Nothing is pushed.

Previous session (VSIX and tray 0.1.17, 357 tests): signed-out amber warning with `@login` heading, "Usage tray not running" wording, paused grey row plus orange "!" badge and orange Full details chip, "This chat <n>" label, darker solid bar hover, version display (tray version in the Full details title, extension name and version at the top right of the popup; shared `Directory.Build.props` version via `ProductVersion`), and a protocol-mismatch state ("Update needed", red, client retries every 10 s). `scripts\agent\Deploy-ExpVsix.ps1` deploys to the Exp instance only.

## Open tasks / known issues

- Not verified live: the mismatch state (needs a deliberately mismatched build), 0.1.16/0.1.17 visuals, the Full details foreground fix (tray restart needed), card look and tints.
- The tray shows nothing for a protocol mismatch (only the VSIX row does).
- Candidate scenarios not yet handled/checked: tray-side mismatch notice, dimmed old figures after a failed refresh, command failures in the VS row, tray icon problem state, forced refresh after sleep/resume, several VS windows on one tray, pause across tray restart, always-show Add account with one saved login. Single-instance tray is implemented (per-user mutex plus OpenStatus handoff) but has no test for the second-launch path.
- Phase 5 checklist (not started): theme testing (VS Light/Dark/Blue/custom, Windows light/dark, high contrast, DPI, screen-reader names), details/export, auth-storage validation, packaging, supported VS range and earlier VS versions, resource measurements, removal of Tools-menu test commands before release.
- README screenshots `row-offline.png` and `row-signedout.png` are outdated.
- `external/VsChatHistoryToolkit` is clean and pinned at `676fee4`, which is on the toolkit's `origin/main` (verified 2026-11-11). The parent's two newest commits (`547accc`, `e4f8bfd`) are not pushed yet.
- Build notes: private feeds return 401, restore with `dotnet restore CopilotUsageMeter.slnx --source https://api.nuget.org/v3/index.json`, then MSBuild `-restore:false`. Historical demo-tool-window analyzer warnings remain.

## Runtime

- Exp instances launched during the session (including PID 10340 with 0.1.17) may still be open; the user's debug tray is a separate process. Nothing is to be stopped without an exact PID and a request.

## Changed files this session

Busy-tray increment: `Transport.Pipes\UsagePipeServer.cs`, `Client\PipeUsageClient.cs`, `Client\CommandTimeoutException.cs` (new), `Panel.Vsix\ChatUsageIntegration.cs`, `Panel.Core\UsagePanelViewModel.cs`, test `PipeBusyTrayTests.cs` (new), docs. Resilience increment: `src\CopilotUsageMeter.Contracts\LocalHistoryErrorCodes.cs` (new), `Core\LocalRefreshScheduler.cs`, `Transport.Pipes\UsagePipeServer.cs`, `Tray\TrayHost.cs`, `Tray\MainWindow.xaml.cs`, `Panel.Core\UsageDisplay.cs`, tests `LocalRefreshRetryTests.cs` and `PipeListenerRecoveryTests.cs` (new), docs (`PHASE_PLAN`, `DECISIONS`, `DESIGN_DECISIONS`, `README`, `PROJECT_STATE`). Earlier: `Directory.Build.props`, `README.md`, `docs\DESIGN_DECISIONS.md`, `docs\PHASE_PLAN.md`, `docs\PROJECT_STATE.md`, `docs\CODE_SUMMARY.md`, `docs\SCRIPTS.md`, `scripts\agent\Deploy-ExpVsix.ps1`, `src\CopilotUsageMeter.Client\PipeUsageClient.cs`, `src\CopilotUsageMeter.Panel.Core\UsageDisplay.cs`, `src\CopilotUsageMeter.Panel.Core\ProductVersion.cs`, `src\CopilotUsageMeter.Panel.Vsix\CompactUsageView.cs` and manifest, `src\CopilotUsageMeter.Tray\MainWindow.xaml(.cs)`, tests (`ProtocolTests`, `UsageDisplayTests`, `ProductVersionTests`). Memory files (graph, CODE_SUMMARY, PROJECT_STATE) are refreshed at session end and not yet committed.
