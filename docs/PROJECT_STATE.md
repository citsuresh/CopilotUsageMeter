# Project state

This file is overwritten, not appended, at the end of each working session. The phase checklist in `PHASE_PLAN.md` remains authoritative.

## Current focus

2026-11-10: **Phase 2 complete.** Independent whole-change review, fixes, final build/57-test run, fresh experimental runtime verification, cleanup and incremental memory refresh completed. Await user direction; do not start Phase 3. User authorized local milestone commits, not pushes. Baseline `a395639`; protocol `32505be`; the completion commit contains this snapshot. Sole solution: `CopilotUsageMeter.slnx`.

## Verified

- Eleven-project SLNX Debug build and real non-deploying VSIX build succeeded; 23 architecture tests passed, zero failed/skipped. Negative control caught a real forbidden Core-to-transport reference, then was removed. Phase 1 review found no issues.
- Built WPF scaffold was launched, attached by PID to VS debugger, paused/resumed, confirmed responsive with expected title, and closed normally; process exited and debugger returned to design mode. Exception inspection was unavailable (`no-stack-frame`), not a verified exception check.
- Real tray/engine and protected same-user pipes feed an explicitly temporary experimental VSIX tool window. Demo account/local values remain separate. No authentication/history reads, production schedules, actual chat embedding or performance/battery measurements.
- Stable SDK 10.0.403 installed with approval and Microsoft hash/signature verification; temporary installer removed. Tray net10.0-windows/tests net10.0/VSIX net48, shared netstandard2.0. Final full SLNX Debug build passed with deployment disabled; VSIX packaged and explicitly deployed only to Exp. All 57 tests passed, zero failed/skipped (23 architecture, 17 protocol/lifecycle, 7 engine, 7 settings, 3 view-model). Protocol/host/final audit findings were fixed. A deterministic real-pipe regression reproduced the final disposed-command race before its IOException normalization and passed afterward.
- Live checks: actual notification icon/menu; pause/manual/automatic refresh/resume; settings persistence; status close/recreation and second-launch activation; normal Exit; stale cache and reconnect to new identity. Two real Exp clients shared host/revision/count with no subscription-driven work. Token-verified elevated clients interoperated bidirectionally with a non-elevated tray; one refresh advanced count once. Final audited-artifact pass additionally verified remote settings (10 seconds), default windowless startup, remote OpenStatus and disabled stale commands. All owned tray/Exp processes closed normally; original absence of settings restored and named activity logs removed. Exp-only extension deployment remains for development.
- Incremental registered graph refresh: 274 nodes, 1,029 edges across 11 projects, 25.47 seconds; paired dependencies output immediately renamed correctly. New panel symbols are queryable. Existing Contracts/Core/Transport.Pipes metadata-reference warnings still limit completeness; absence is not proof of absent code.

## Open tasks / gates

- No remaining Phase 2 acceptance tasks or unresolved review findings. The temporary VS tool window is not Copilot Chat embedding; use the documented Tools-menu launch route, not the rejected bare command-line alias.
- Phase 3 requires user direction: supported account authentication/allowance/logout, actual chat placement, active-chat/subagent accounting, local completeness and initial resource baseline. Phase 5 repeats measurements.
- Never conflate account/local scopes, upload local history, deploy into main VS or push without approval.

## Recently changed files

- Preserved Bootstrap changes were included in approved baseline commit `a395639`; protocol milestone is `32505be`. Neither was pushed in this task.
- Completion increment: DemoUsageEngine, TrayHost/DemoSettingsStore/App/status UI, UsagePanelViewModel, VSIX menu/tool window and client disconnect normalization; eighteen new engine/settings/view-model/race regression cases; protocol/README/checklist/memory/graph updates. Reusable process-elevation and unelevated-demo-launch helpers are indexed in `docs\SCRIPTS.md`; both were executed successfully. No push performed.
