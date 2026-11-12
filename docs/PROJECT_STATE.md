# Project state

_Overwritten at the end of each working session; the detailed checklist lives in `docs/PHASE_PLAN.md`._

## Current focus

Phase 5 is in progress. The user authorised committing the geometry increment and continuing on 2026-11-12; no push is authorised. In-memory Full details geometry restores normal bounds on recreation, constrained to the primary monitor work area. It does not preserve maximization, secondary-monitor placement or bounds across tray restarts. The helper has 11 automated cases; live close/reopen remains unverified.

Packaging/startup scripts and the initial resource sample are committed and pushed as `9d0e566`. Usage alerts stages 1-3 and notification identity are committed/pushed as `6e747b5`; the user confirmed the notification header/icon fix.

## Verification

- Tray build to `%TEMP%\cum\tray\`: succeeded with zero errors; private-feed NU1900 vulnerability-metadata warnings remain.
- Visual Studio build tool declined to apply changes because debugging is active. The running tray has not been updated.
- Final full rerun: 589 passed, zero failed/skipped. Initially one unchanged account-refresh test failed (`AFaultedReadPublishesStateSoProgressIsNotLeftOn`, final observation assertion); it and all 11 geometry cases passed on the targeted rerun before the full green rerun. No unrelated code changed.

## Open tasks / known issues

- Next safe step: source-level theme/contrast audit; no live theme/DPI/screen-reader validation is implied.
- Details filters/export require an agreed design/privacy scope. Supported VS versions, separate VSIX release packaging, final tool-command removal and whole-phase verification remain open.
- Release/no-debugger and busy measurements, icon handle growth and multi-client read dedup remain open. The running Debug tray must be exited by the user first; do not stop it automatically.
- Registry/startup verification and a proper installer remain deferred by the user (2026-11-11). Do not install the extension into main Visual Studio.
- Stage 1 alerts live, Focus Assist, a real chat-layout change, packaged startup and clean-machine/runtime behavior remain unverified.

## Current increment files

New: `src\CopilotUsageMeter.Tray\WindowPlacement.cs`, `tests\CopilotUsageMeter.Architecture.Tests\WindowPlacementTests.cs`. Modified: `TrayHost.cs`, the test project, `README.md`, `docs\PHASE_PLAN.md`, `CODE_SUMMARY.md`, `PROJECT_STATE.md`.
