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

## Project Guidelines

- Manual commit review before any commit.
- Build/test verification after every change; documentation-only edits need no build unless documentation tests exist.
- Do not commit or push automatically - wait for explicit user confirmation first.
- This project has a WinForms/WPF UI. If you need to debug it in Visual Studio, use the `vs-debug` skill (also independently useful for debugging tasks not reached via UI navigation). If you need to navigate through the running application to reach a specific screen or feature - including as part of debugging it - use the `ui-navigation-orchestrator` skill, which composes `vs-debug` and `ui-interaction` for you.

## Response Guidelines

- Concise, minimal replies by default: no filler, restating the question or unnecessary preamble. Provide full detail for design rationale, build/error diagnosis, before destructive actions, multiple approaches, and when generating docs content itself.

<!-- project-memory-management-graph: skill-version=24 -->
## Persistent Project Memory

This section is fully regenerated on every Bootstrap. Project Guidelines and Response Guidelines are additive/merge-safe. Existing session-continuity rules and `docs/PHASE_PLAN.md` remain authoritative for scope and progress; memory does not authorize another phase.

- Read `docs/CODE_SUMMARY.md` and `docs/DESIGN_DECISIONS.md`, if present, before exploring the codebase for a new task. Also consult the established `docs/DECISIONS.md` for product choices. Missing memory files are not errors; fall back to normal exploration.
- Read `docs/PROJECT_STATE.md` and `docs/ROADMAP.md` when the user references prior work, asks what is next or asks whether you remember. Read the active phase checklist at every repository-related session start per the continuity rules above.
- Read `docs/domain-lookup-patterns.md`, if present, before investigating domain conventions, naming schemes or business logic the graph cannot represent. Record a newly discovered pattern only after explicit user confirmation; do not create an empty placeholder.
- Read `docs/FEATURE_CHECKLIST.md`, if present, when planning or adding a feature; include its items in the breakdown and parity checks. It is user-curated; never create or modify it without an explicit request.
- `docs/KNOWN_OPEN_FINDINGS.md` is user-controlled. Check existing entries when investigating an unexpected issue, but add/edit/resolve only when asked. Ask whether a similar entry is a recurrence before merging it; never infer that decision.

### Tracked solution registry

| Solution path relative to repo root | Slug | Graph file | Dependencies file |
| --- | --- | --- | --- |
| `CopilotUsageMeter.slnx` | `copilotusagemeter` | `docs/full-graph.copilotusagemeter.json` | `docs/project-dependencies.copilotusagemeter.json` |

- Query graphs through `C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1 -Tool Query -- <args>`, never read graph JSON wholesale. Clear-runtime handling is in that wrapper; never invoke raw GraphTools executables directly.
- Graph-first is the default before general-purpose text/symbol search for any C# class/interface/enum/method definition, callers, callees or relationship. Conceptual explanations of named C# symbols are not exempt. Confirm graph existence with Test-Path/direct read, not indexed filename search.
- Query the current IDE solution's graph first only if determinable; otherwise state it is unknown. Then check other registered graphs as needed. Label results by solution and report multiple matches. Example: `& 'C:\MyFiles\Git\GraphTools\tools\Invoke-GraphTools.ps1' -Tool Query -- --graph 'C:\MyFiles\Git\CopilotUsageMeter\docs\full-graph.copilotusagemeter.json' --list-symbols --project 'CopilotUsageMeter.Tray'`. Use an exact discovered symbol id with `--symbol '<id>'`; optional `--direction callers` or `--direction callees`.
- If no graph covers the question, a graph is missing or the query errors, explicitly announce that before falling back to search. Report the full tool error; symbol-not-found may mean stale/incomplete graph, not absent code. Non-code layout/content questions can fall back after stating graph coverage is inapplicable. The initial graph has Contracts/Core/Transport.Pipes metadata-reference warnings: do not treat it as complete evidence.
- If a recurring friction point is noticed during graph/domain lookup, briefly mention it at the end; do not search for optimizations or act on it without direction.

### Memory maintenance

- Update `docs/CODE_SUMMARY.md` for a new project/structural class, changed component responsibility or dependency, not routine fixes. Keep its KEY_FLOWS pointer.
- Add fully traced, confirmed multi-symbol flows to `docs/KEY_FLOWS.md` as short symbol arrow-chains only, never in CODE_SUMMARY. Add a one-line domain-pattern pointer only when a genuinely relevant documented pattern already exists; no speculative cross-references or domain details in flows.
- Append dated, non-obvious design/architecture decisions to `docs/DESIGN_DECISIONS.md` when needed. Preserve the existing authoritative product decision history in `docs/DECISIONS.md`; avoid duplicate records. Never delete earlier decisions; record reversals as new entries.
- Overwrite `docs/PROJECT_STATE.md` at session end with current focus, unresolved tasks and changed files. Edit `docs/ROADMAP.md` only for deliberate priority changes; the detailed progress checklist remains in PHASE_PLAN. Keep memory concise.
- Bootstrap builds registered graphs sequentially with `-Tool Builder -- --solution '<absolute solution>' --output '<absolute slug graph>' --mode full`. End Session updates only the current solution incrementally using `--mode incremental --graph '<absolute slug graph>'`; skip if solution unknown or graph absent. After each successful build, rename Builder's fixed `docs/project-dependencies.json` immediately to the registered slug-named dependencies file. Stop and report failures, missing outputs or collisions; never guess or silently retain partial success. Confirm wrapper/executable availability first, print exact commands, and report elapsed time/node/edge counts. Ask before deleting superseded graph files. Viewer file picker/drag-and-drop is required for slug-named pairs; automatic loading uses legacy filenames.
- Reusable scripts: before writing code for a repeatable mechanical step (filtering a log, applying the same edit to many files, extracting data from several files), check `docs/SCRIPTS.md` if it exists and run a script that fits, as its index line says; if the index line says it modifies or deletes files, ask the user before each run, and check its output before relying on it. If you wrote working code for such a step that is likely to be reused with different inputs, is more than a one-liner, can take its inputs as parameters, and contains no credentials, tokens, customer data or personal details, save it as `scripts/agent/<verb-noun>.ps1` with a header comment (purpose, parameters, whether it modifies anything), add one line to `docs/SCRIPTS.md` (table: script | purpose | how to call | read-only or modifies | date; create the file with the table header if it is missing), and say so in your reply. Before saving, check whether an existing script in `scripts/agent/` can take the new case as a parameter. If one can, extend that script with a backward-compatible parameter (its default keeps the current behaviour, and existing parameter names, order and output format stay unchanged), update its header comment, index line and date, and say so, rather than saving a near-copy. If the task's file rules forbid changing the existing script, do not save a near-copy; say in your reply which parameter the existing script needs. Every part that varies between uses (paths, patterns, comment markers, row counts, exclusions) must be a parameter. For C# projects, skip `obj` and `bin` folders by default, with a switch to include them. Before finishing a task, check once whether you wrote any script worth saving. If a script fails or is out of date, fix it in place and update its date, or do the step normally; never loop on a broken script. When a task runs a listed script, set its date in `docs/SCRIPTS.md` to today if the task's file rules allow editing that file; otherwise leave the date unchanged and say so in your reply. The user curates these files: never delete scripts or index lines on your own.
