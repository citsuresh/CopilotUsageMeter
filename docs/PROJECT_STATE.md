# Project state

This file is overwritten, not appended, at the end of each working session. The phase checklist in `PHASE_PLAN.md` remains authoritative.

## Current focus

2026-11-10: **Phase 3 (feasibility proofs) is complete with limits (user sign-off 2026-11-10).** Phases 1 and 2 are complete and pushed (HEAD `b7f0479`). Do not start Phase 4 until the user directs it.

## Phase 3 outcomes (details and limits in `PHASE_PLAN.md` and `DECISIONS.md`)

- Toolkit added as submodule `external/VsChatHistoryToolkit`, pinned at `12bbfeb` (adds `usage-breakdown`); CLI published to gitignored `tools\chathistory`.
GitHub App route untested and parked as a future alternative. Decision 2026-11-10: use `copilot_internal/user` for now.
- Placement: a marker inserted into `ChatWorkingSet`'s parent panel lands directly above Total changes (and above the prompt when it is absent) but is lost when the session view is recreated; a top-of-pane row survives new chats and switches. Neither is a supported extension point.
- Active chat: `HarnessSessionViewModel.SessionId` equals the toolkit session id. Per-turn rows already include subagents; refresh must replace per-(session, turn_index) snapshots.
- Local completeness: `usage-breakdown` gives joint repo x model x day totals (rows sum to total; matches `activity`).
- Resource baseline (demo engine only, one machine, no battery claim): idle tray about 63-67 MB and about 0.02 % of the machine; CLI refresh (3 launches) about 1.5 s wall and 1.3 s CPU; simulated 5 s refresh cycle about 1.2 % of the machine, almost all CLI children.

## Unresolved / awaiting user

- Decisions made 2026-11-10: `copilot_internal/user` for GitHub account usage now (GitHub App noted for later); layered placement agreed; Exp-only probes kept until Phase 4 replaces them. Still open: authorise Phase 4.
- Not established: meaning of `currentSessionId`; cause of the `by_turn` versus `turn_count` mismatch; live subagent turn behaviour; duplicate-injection idempotence; real-engine resource cost; tray-icon slowness seen once when launched from VS (ask whether F5, Ctrl+F5 or direct if it recurs).
- Build note: restore fails on private Itron feeds (401); use `dotnet restore <slnx> --source https://api.nuget.org/v3/index.json` then build with `/restore:false`.

## Uncommitted changes (manual diff review required before any commit)

- Staged: `.gitmodules`, submodule `external/VsChatHistoryToolkit` at `12bbfeb`.
- Modified: `.gitignore`, `README.md`, `docs/DECISIONS.md`, `docs/PHASE_PLAN.md`, `docs/SCRIPTS.md`, VSIX `Commands.vsct` and `UsagePackage.cs`.
- New: `scripts/agent/Measure-Resources.ps1`, `Test-GitHubQuota.ps1`, `Test-LocalAccountingCompleteness.ps1`, `Test-TurnAccounting.ps1`; VSIX `ActiveChatProbe.cs`, `ChatPlacementProbe.cs`.

## Verified

- Solution build succeeded; 58 architecture tests passed; toolkit 393 tests passed (re-run independently); phase-boundary review findings addressed.