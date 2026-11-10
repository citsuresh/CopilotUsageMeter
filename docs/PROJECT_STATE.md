# Project state

This file is overwritten, not appended, at the end of each working session. The phase checklist in `PHASE_PLAN.md` remains authoritative.

## Current focus

2026-11-10: **Phase 2 complete and pushed to origin/main.** Post-commit review found one shutdown race (owner-cancelled in-flight pipe command could leave Exit unfinished); fixed in `12972d2` with a shutdown smoke test (58 tests). Await user direction; do not start Phase 3. Commits: baseline `a395639`; protocol `32505be`; Phase 2 completion `037a4c5`; review hardening `12972d2`; this snapshot. Sole solution: `CopilotUsageMeter.slnx`.

## Verified

- Full SLNX Debug build (eleven projects, VSIX build without deployment) succeeded; 58 tests passed, zero failed/skipped (23 architecture, 18 protocol/lifecycle, 7 engine, 7 settings, 3 view-model).
- Live checks (before the final hardening commit): tray icon/menu; pause/manual/automatic refresh/resume; settings persistence; status close/recreation; single-instance activation (per-user mutex); normal Exit; stale cache and reconnect to a new host identity; two Exp VS clients sharing one host; elevated clients with a token-verified non-elevated tray. The `12972d2` change was verified by build and tests only, not re-run live.
- Real tray/engine and protected same-user pipes feed an explicitly temporary experimental VSIX tool window. Demo account/local values stay separate. No authentication/history reads, production schedules, chat embedding or performance/battery measurements.
- Incremental graph refresh: 275 nodes, 1,053 edges, 22.4 seconds; dependencies output renamed to the slug name. Contracts/Core/Transport.Pipes metadata-reference warnings still limit completeness.

## Open tasks / gates

- No unresolved Phase 2 findings. The temporary VS tool window is not Copilot Chat embedding; use the Tools-menu launch route.
- Observation, not established: the tray icon was slow to appear once when launched from Visual Studio; a direct launch was fast. Ask whether it was F5, Ctrl+F5 or direct if it recurs.
- Phase 3 requires user direction: account authentication/allowance/logout, actual chat placement, active-chat/subagent accounting, local completeness and initial resource baseline. Phase 5 repeats measurements.
- Never conflate account/local scopes, upload local history, deploy into main VS, or push without approval.

## Recently changed files

- Review hardening (`12972d2`): `UsagePipeServer.cs`, `App.xaml.cs`, `ProtocolTests.cs`, and test-count text in README, PHASE_PLAN, ARCHITECTURE, CODE_SUMMARY.
- Session end: this file and the refreshed graph/dependencies files.
