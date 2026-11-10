# Project state

This file is overwritten, not appended, at the end of each working session. The phase checklist in `PHASE_PLAN.md` remains authoritative.

## Current focus

2026-11-10: Phase 1 scaffolding complete, committed and pushed as `5d18b87` to origin/main. User will continue in a new chat. Next authorized work must be confirmed; Phase 2 has not started. Only `CopilotUsageMeter.slnx` exists. Startup project: CopilotUsageMeter.Tray.

## Verified

- Eleven-project SLNX Debug build and real non-deploying VSIX build succeeded; 23 architecture tests passed, zero failed/skipped. Negative control caught a real forbidden Core-to-transport reference, then was removed. Phase 1 review found no issues.
- Built WPF scaffold was launched, attached by PID to VS debugger, paused/resumed, confirmed responsive with expected title, and closed normally; process exited and debugger returned to design mode. Exception inspection was unavailable (`no-stack-frame`), not a verified exception check.
- No tray icon/engine, pipes, authentication/history reads or VS panel yet. No VSIX runtime deployment/testing. No performance/battery measurements.
- Initial memory graph built: 23 nodes, 93 edges, 17.03 seconds; End Session incremental update detected zero changed files and retained those counts in 7.14 seconds. Contracts/Core/Transport.Pipes metadata-reference warnings limit completeness. Graph registry uses slug `copilotusagemeter`. Solution build confirmed after Bootstrap; reusable-script instruction string matched the skill template exactly.

## Open tasks / gates

- Apply and verify the recommended stable .NET 10 SDK + Tray/tests .NET 10 + VSIX net48 baseline before Phase 2; retain shared netstandard2.0. Not applied; current targets remain net8.0-windows/net8.0/net472 with pinned SDK 11 RC3.
- Phase 2: real tray and fake-data pipe/VS skeleton. Phase 3: account allowance/consumption authentication/logout, chat placement, active-chat turn attribution, local completeness and initial resource baseline. Stop for user decisions on failed gates. Phase 5 repeats resource measurements.
- Never conflate account/local units/scopes, upload local history, install into main VS, or automatically commit/push.

## Recently changed files

- Committed: solution, shared build/SDK settings, ten src projects, architecture tests, .gitignore, README, ARCHITECTURE/DECISIONS/PHASE_PLAN and repository instructions.
- End-session Bootstrap: CODE_SUMMARY, KEY_FLOWS, DESIGN_DECISIONS, PROJECT_STATE, ROADMAP, graph/dependencies JSON, graph-viewer.html; updated repository memory instructions and README pointer. These were created after the Phase 1 push; the user authorized a separate local memory commit. Inspect git status/log for current commit and publication status; no further push was authorized.
