# Project state

_Overwritten at the end of each working session; the detailed checklist lives in `docs/PHASE_PLAN.md`._

## Current focus

Phases 1-4 are complete. The user-authorised post-Phase-4 UI rework and polish is committed and pushed: `origin/main` = `6a26ed1` (after `c3123fc`, `1d90452`, `64b5523`, `f38151f`). Phase 5 is NOT authorised; do not start it or push without explicit direction.

Latest session (VSIX and tray 0.1.17, 357 tests pass): signed-out amber warning with `@login` heading, "Usage tray not running" wording, paused grey row plus orange "!" badge and orange Full details chip, "This chat <n>" label, darker solid bar hover, version display (tray version in the Full details title, extension name and version at the top right of the popup; shared `Directory.Build.props` version via `ProductVersion`), and a protocol-mismatch state ("Update needed", red, client retries every 10 s). `scripts\agent\Deploy-ExpVsix.ps1` deploys to the Exp instance only.

## Open tasks / known issues

- Not verified live: the mismatch state (needs a deliberately mismatched build), 0.1.16/0.1.17 visuals, the Full details foreground fix (tray restart needed), card look and tints.
- The tray shows nothing for a protocol mismatch (only the VSIX row does).
- Candidate scenarios not yet handled/checked: tray-side mismatch notice, dimmed old figures after a failed refresh, command failures in the VS row, tray icon problem state, forced refresh after sleep/resume, several VS windows on one tray, pause across tray restart, always-show Add account with one saved login. Single-instance tray is implemented (per-user mutex plus OpenStatus handoff) but has no test for the second-launch path.
- Phase 5 checklist (not started): theme testing (VS Light/Dark/Blue/custom, Windows light/dark, high contrast, DPI, screen-reader names), details/export, auth-storage validation, packaging, supported VS range and earlier VS versions, resource measurements, removal of Tools-menu test commands before release.
- README screenshots `row-offline.png` and `row-signedout.png` are outdated.
- `external/VsChatHistoryToolkit` has uncommitted submodule changes (pin 12bbfeb); distribution needs explicit toolkit commit/push approval.
- Build notes: private feeds return 401, restore with `dotnet restore CopilotUsageMeter.slnx --source https://api.nuget.org/v3/index.json`, then MSBuild `-restore:false`. Historical demo-tool-window analyzer warnings remain.

## Runtime

- Exp instances launched during the session (including PID 10340 with 0.1.17) may still be open; the user's debug tray is a separate process. Nothing is to be stopped without an exact PID and a request.

## Changed files this session

`Directory.Build.props`, `README.md`, `docs\DESIGN_DECISIONS.md`, `docs\PHASE_PLAN.md`, `docs\PROJECT_STATE.md`, `docs\CODE_SUMMARY.md`, `docs\SCRIPTS.md`, `scripts\agent\Deploy-ExpVsix.ps1`, `src\CopilotUsageMeter.Client\PipeUsageClient.cs`, `src\CopilotUsageMeter.Panel.Core\UsageDisplay.cs`, `src\CopilotUsageMeter.Panel.Core\ProductVersion.cs`, `src\CopilotUsageMeter.Panel.Vsix\CompactUsageView.cs` and manifest, `src\CopilotUsageMeter.Tray\MainWindow.xaml(.cs)`, tests (`ProtocolTests`, `UsageDisplayTests`, `ProductVersionTests`). Memory files (graph, CODE_SUMMARY, PROJECT_STATE) are refreshed at session end and not yet committed.
