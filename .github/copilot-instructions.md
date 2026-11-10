# Copilot Usage Meter - repository instructions

## Session continuity and source of truth

- At the start of every new chat involving this repository, read `docs/PHASE_PLAN.md` (the current checklist and active five-phase plan, before the archived proposal) and `README.md` before planning or changing code. Do not rely on memory of earlier conversations.
- Follow the active plan and resume from its recorded progress. The archived P0-P9 proposal is historical, not executable guidance. Read `docs/ARCHITECTURE.md` for design context and `docs/DECISIONS.md` before revisiting an architectural choice.
- Do not restart completed work or automatically advance to another phase. Implement only the phase/increment the user has authorized. Surface conflicts or unresolved prerequisites and obtain agreement before changing the plan or substituting a feature.
- Maintain the checklist as verified progress occurs, including within a phase. Check items only after their outcomes are verified; record blocked/deferred items and actual build/test/review evidence without claiming future features work.
- Update the README as capabilities/instructions change. The final step of every phase is to synchronize the README and checklist, report verification/limitations, and stop for user direction. Record non-obvious design changes as append-only dated entries in `docs/DECISIONS.md`.

## Architecture and tooling

- `CopilotUsageMeter.slnx` is the sole solution file. Do not recreate or maintain a parallel `.sln`. Use Windows paths for filesystem commands.
- The tray hosts the background Usage Engine and named-pipe server. There is no separately installed Windows service and no WCF requirement. Exiting the tray stops tracking; closing a details window or collapsing the panel does not.
- Preserve the library/UI/infrastructure boundaries enforced by architecture tests. Keep Visual Studio SDK references in the VSIX project and UI framework types out of shared libraries.
- Read the plan's current framework/SDK decision before changing targets or installing tooling. A successful scaffold build does not establish a supported shipping baseline.
- GitHub billing access/allowance retrieval, placement above Copilot Chat's Total changes, reliable active-chat matching and complete local accounting are POC gates. Never promise universal subscription support or silently replace chat embedding with a tool window.

## Data sources, privacy and refresh

- Clearly separate GitHub-reported account usage across machines from local analytics recorded on this machine. Never add overlapping totals, imply they must reconcile, silently substitute local figures for account data, or assume local credits equal GitHub billing units.
- Never upload local chat history, titles, message text, repository paths or local analytics. Do not put titles/text in DTOs, logs, caches, exports, exceptions or fixtures. Use invented numeric/id fixtures. Read local history through the toolkit CLI, not direct database/SQL access.
- GitHub network access is for the approved profile/account integration. Keep authorization out of source/settings/logs; use secure credential storage and support logout/account switching. Do not automatically attribute local history to the signed-in account. Unavailable data is explicit, never a fabricated zero.
- Local refresh is event-driven on database/WAL changes, with 1.5-second debounce, 5-second throttle and single flight while the tray runs. GitHub sync is separate: 5 minutes during local activity, otherwise 15 minutes, plus manual refresh and last/next-refresh information. Follow the plan for remaining scheduling details.
- Pause updates suspends automatic refreshes and shows a paused state/icon. Measure idle/busy resource usage during the POC and again at the final phase; do not claim low battery impact without measurements.

## Validation and safety

- Build/test changes with relevant existing tooling; complete the separate phase-boundary review and final whole-change verification required by the plan. Report what actually ran and what could not be verified.
- Extension runtime tests and UI integration experiments are restricted to the experimental Visual Studio instance (`/RootSuffix Exp`). Never install into the main instance, close the user's Visual Studio windows, or stop processes not started for the task.
- Preserve unrelated worktree changes. Review changes manually before any commit. Never commit or push automatically; require explicit user approval.
- Respond concisely by default, while surfacing blockers, design tradeoffs and actual verification results.