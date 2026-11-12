# Project state

_Overwritten at the end of each working session; the detailed checklist lives in `docs/PHASE_PLAN.md`._

## Current focus

Phase 5 is in progress. Geometry is committed as `7ad8dfd` and theme fixes as `7f6f3da`. On 2026-11-12 the user authorised committing the 0.1.20 deployment record and pushing it with those two commits. Version 0.1.20 is installed in Exp (task-launched PID 74028). UI Automation verified the compact row, popup controls and popup open/close. After user-authorised debugger shutdown and rebuild, updated Release tray PID 49436 is running without the debugger, with Full details open. No theme settings were changed; the visual theme/DPI/screen-reader matrix remains open.

Packaging/startup scripts and the initial resource sample are committed and pushed as `9d0e566`. Usage alerts stages 1-3 and notification identity are committed/pushed as `6e747b5`; the user confirmed the notification header/icon fix.

## Verification

- Updated Release tray rebuilt to `%TEMP%\cum\release-tray\`: zero warnings/errors. Version 0.1.20 started with `--show-status`, PID 49436; responsive process and visible Full details window verified through UI Automation.
- With explicit user approval, detached the debugger from the old Debug tray PID 99456 and stopped that process; verified its exit and debugger design mode. Visual Studio tray-project build now succeeds. Neither Visual Studio window was closed; the main instance is no longer debugging the old tray.
- Theme increment before deployment: final full suite 599 passed, zero failed/skipped. Ten new contrast cases include amber at 8.01:1 and 4,096 sampled RGB combinations at or above 4.5:1. Version 0.1.20 VSIX build/install succeeded and the installed identity was verified; eight product-version tests passed afterward. Existing private-feed NU1900 and demo-tool-window threading warnings remain. The final independent phase review is not done.
- Exp smoke check was UIA-only, not a visual contrast or screen-reader test. Screenshot viewing failed because the session filesystem provider cannot read image bytes; the one task-created screenshot was deleted. UI cursor indicator stopped; no task-owned installer remains active.

## Open tasks / known issues

- Next: use the running Release tray for live geometry/theme checks and no-debugger measurements. Live theme/DPI/screen-reader validation includes keyboard focus and custom/high-contrast palettes; get permission before changing desktop-wide settings. Geometry close/reopen remains unverified live; its primary-monitor/session-only limitations remain documented.
- Details filters/export require an agreed design/privacy scope. Supported VS versions, separate VSIX release packaging, final tool-command removal and whole-phase verification remain open.
- Release/no-debugger and busy measurements, icon handle growth and multi-client read dedup remain open. The previous Debug-tray blocker is cleared; no new resource sample has been taken.
- Registry/startup verification and a proper installer remain deferred by the user (2026-11-11). Do not install the extension into main Visual Studio.
- Stage 1 alerts live, Focus Assist, a real chat-layout change, packaged startup and clean-machine/runtime behavior remain unverified.

## Current changes

The 0.1.20 deployment increment contains `Directory.Build.props` and the VSIX manifest version bump, plus README, PHASE_PLAN, SCRIPTS execution date and this snapshot updated with actual evidence. No additional application-code changes. Commit/push authorised by the user; use Git status for the current synchronization state. Leave the new Release tray running for the user's tests; do not close either Visual Studio window.
