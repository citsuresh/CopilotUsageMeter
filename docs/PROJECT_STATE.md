# Project state

_Overwritten at the end of each working session; the detailed checklist lives in `docs/PHASE_PLAN.md`._

## Current focus

Compact row follow-up (2026-11-14): exact consumed digits above an abbreviated allowance (`17,930` above `100K`) in every width tier, now with a subtle decorative divider; exact tooltip/popup/Full details figures and non-numeric/problem labels are preserved. VSIX build succeeded. Initial suite: 773 passed, one `AccountRefreshTests.AFaultedReadPublishesStateSoProgressIsNotLeftOn` failure; all 15 account-refresh tests and the full 774-test suite passed on rerun (zero failed/skipped). Installed the latest VSIX into Exp only, verified installed VSIX/Panel.Core assembly hashes against the build, refreshed Exp configuration and launched a responsive experimental window (PID 93292). Divider appearance has not been checked live. Changed: `UsageDisplay.cs`, `CompactUsageView.cs`, `UsageDisplayTests.cs`, README and DECISIONS. No other Phase 5 item was started; existing main VS windows were left untouched.

Phase 5 item "details filters, export and a persisted settings page" is built as Parts A-E (user-authorised, run on autopilot). Each part had an independent audit and its in-scope findings were fixed; Part E was the docs sync and whole-change review. Verified: Tray build 0 errors/0 warnings, 760 `CopilotUsageMeter.Architecture.Tests` passed. The parent checklist item stays open because nothing new was run live in the tray or Exp (Save As dialog, tray menu items, Settings window in the tray, remembered pause/placement across a real restart, Open data folder, Run registry write).

## Open tasks / known issues

- Live verification of the new UI in the tray and the Exp instance (above); resource measurements of the new code.
- Out-of-scope notes: the privacy wording ("repository paths") vs the user's `owner/repo` decision; three copies of the owner/repo validation rule; `RowsCapped` is true at exactly 500 rows; the model filter disallows spaces; `ChatHistoryReader.Dimension` validates model text only by length/control characters; no UI tests for the export flow; `StopAsync` does not explicitly close the Settings window; an unreachable UNC export folder could stall Save As; a newer-version `settings.json` is replaced by defaults on the next save.
- Other Phase 5 items: secure authorization/privacy/failure-state validation; longer-duration handle growth and multi-client measurements; remove leftover Tools-menu test commands; supported Visual Studio range and main-instance install; packaging (VSIX, packaged exe, no-runtime machine, signing); `global.json` pin decision; final whole-phase review and README sync.
- Deferred by the user: theme/DPI/screen-reader validation (`ROADMAP.md`), registry/startup verification and a proper installer. Do not install into main Visual Studio.

## Current changes

The user approved commit and push on 2026-11-14. Scope: reviewed Parts A-E source/tests and the compact stacked-figures/divider follow-up, with their documentation. Final pre-commit verification: full Visual Studio solution build succeeded; all 774 architecture-project tests passed, zero failed/skipped. Unrelated `.github/copilot-instructions.md` and both graph JSON files are excluded and left untouched. Further changes still require explicit commit/push approval; other Phase 5 items remain open.
