# Project state

_Overwritten at the end of each working session; the detailed checklist lives in `docs/PHASE_PLAN.md`._

## Current focus

Phase 5 (authorised 2026-11-11 "start stage 5, run on autopilot") is IN PROGRESS; only a safe slice is done and is UNCOMMITTED: `Package-Tray.ps1` / `Install-Tray.ps1` / `Sample-ProcessResources.ps1` (`scripts\agent`), `Tray\StartWithWindows.cs` plus the "Start with Windows" tray menu item, 8 new tests (578 pass), README/PHASE_PLAN/SCRIPTS/DESIGN_DECISIONS/CODE_SUMMARY updates, `.gitignore` `artifacts/`, and the toasts line removed from ROADMAP. One independent review: 1 High finding (uninstall had no path guard) fixed and re-tested with harmless cases. First idle re-measurement of the user's Debug tray under the debugger: 138-149 MB working set, 181-189 MB private, ~840 handles, median 1.5% of a core; not like-for-like with the Phase 3 baseline.

User direction (2026-11-11): startup/registry writes (Run value, install-time registry) are not to be pursued or verified further for now; any installer is planned later.

Stages 1-3 of usage alerts plus the notification-identity fix are committed and pushed as `6e747b5`.

## Open tasks / known issues

- Phase 5 still open (needs your decisions or live testing): details filters/export, theme/high-contrast/DPI/screen-reader testing, supported VS range and main-instance install, shipping model and signing, packaging the VSIX, Release/no-debugger and busy-state measurements, multi-client read dedup, removal of the Tools-menu test commands (only just before the final release).
- Not verified: install script real registry/Start Menu writes (deferred by the user), launching the packaged exe, a machine without the .NET 10 runtime, stage 1 alerts live, Focus Assist, a real chat-layout change.
- README screenshots `row-offline.png` and `row-signedout.png` are outdated.
- `external/VsChatHistoryToolkit` is clean at `676fee4`.
- Build notes: private feeds give NU1900 (harmless, use `--no-restore`). The normal tray output folder is locked while the user's tray runs; build to `%TEMP%\cum\tray\`.

## Runtime

- The user's tray (PID 99456, Debug build) and Exp devenv instances may be open. Nothing is stopped without an exact PID and a request.

## Changed files this session (uncommitted)

New: `scripts\agent\Package-Tray.ps1`, `Install-Tray.ps1`, `Sample-ProcessResources.ps1`, `src\CopilotUsageMeter.Tray\StartWithWindows.cs`, `tests\CopilotUsageMeter.Architecture.Tests\StartWithWindowsTests.cs`. Modified: `Tray\TrayHost.cs`, the test csproj, `.gitignore`, `README.md`, `docs\PHASE_PLAN.md`, `ROADMAP.md`, `SCRIPTS.md`, `DESIGN_DECISIONS.md`, `CODE_SUMMARY.md`, `PROJECT_STATE.md`.