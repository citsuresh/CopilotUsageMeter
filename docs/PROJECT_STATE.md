# Project state

_Overwritten at the end of each working session; the detailed checklist lives in `docs/PHASE_PLAN.md`._

## Current focus

Phase 5 is in progress. Geometry is committed as `7ad8dfd` (not pushed). The user authorised committing the theme fixes and moving to live UI checks on 2026-11-12; no push is authorised. Theme fixes affect the tray Full details window and VS row/popup: host-theme headings/links, contrasting badge glyphs, high-contrast system colours, opaque text and visible keyboard focus. `UsageTheme` is host-only linked source; `ColorContrast` is numeric-only in Panel.Core. No running windows, theme settings or deployments changed before this commit.

Packaging/startup scripts and the initial resource sample are committed and pushed as `9d0e566`. Usage alerts stages 1-3 and notification identity are committed/pushed as `6e747b5`; the user confirmed the notification header/icon fix.

## Verification

- Tray build to `%TEMP%\cum\tray\`: succeeded with zero errors; private-feed NU1900 vulnerability-metadata warnings remain.
- Visual Studio build tool declined to apply changes because debugging is active. The running tray has not been updated.
- Theme increment: tray and non-deploying VSIX builds succeeded, zero errors; final full suite 599 passed, zero failed/skipped. Ten new contrast cases include amber at 8.01:1 and 4,096 sampled RGB combinations at or above 4.5:1. Existing private-feed NU1900 and demo-tool-window threading warnings remain. Source/diff review only; the final independent phase review and live theme tests are not done.

## Open tasks / known issues

- Next: live theme/DPI/screen-reader validation (item 2), including keyboard focus and custom/high-contrast palettes. Geometry close/reopen also remains unverified live; its primary-monitor/session-only limitations remain documented.
- Details filters/export require an agreed design/privacy scope. Supported VS versions, separate VSIX release packaging, final tool-command removal and whole-phase verification remain open.
- Release/no-debugger and busy measurements, icon handle growth and multi-client read dedup remain open. The running Debug tray must be exited by the user first; do not stop it automatically.
- Registry/startup verification and a proper installer remain deferred by the user (2026-11-11). Do not install the extension into main Visual Studio.
- Stage 1 alerts live, Focus Assist, a real chat-layout change, packaged startup and clean-machine/runtime behavior remain unverified.

## Current changes

Committed in `7ad8dfd`: geometry implementation/tests and docs. This user-approved theme commit contains new `Panel.Core\ColorContrast.cs`, `Tray\UsageTheme.cs`, `ColorContrastTests.cs`; modified `Panel.Vsix\CompactUsageView.cs` and its project, `Tray\MainWindow.xaml(.cs)`, README, PHASE_PLAN, DECISIONS, CODE_SUMMARY and this snapshot. Recheck runtime prerequisites before the next live tests; do not stop the user's Debug tray or main Visual Studio.
