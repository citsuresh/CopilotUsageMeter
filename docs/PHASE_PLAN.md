# Copilot Usage Meter: phase plan (draft v0.3)

Updated 2026-11-10. Read with `ARCHITECTURE.md` v0.4 and `DECISIONS.md`. The five phases below supersede the original P0-P9 proposal, retained afterward for reference only. Implementation is skeleton-first; later phases require user direction. Each phase ends with build/test evidence and a separate review. No automatic commits or pushes.

## Progress checklist - source of truth

Maintain this checklist as work progresses, including meaningful progress within a phase, not only phase completion. Check an item only when its required outcome has been verified; keep blocked/deferred items unchecked with a short reason. Record build/test/review evidence here and keep the README's summary synchronized. Completing a phase does not authorize starting the next one.

- [x] **Phase 1 - Solution and project skeleton: complete, 2026-11-10**
  - [x] Create one solution with ten separate production projects and one architecture-test project.
  - [x] Establish project references and UI/infrastructure boundaries; remove template placeholder tests/classes.
  - [x] Build all 11 projects and produce a real VSIX without deployment.
  - [x] Verify dependency guards, including an actual temporary forbidden Core-to-transport reference that fails the test; remove the temporary reference.
  - [x] Final verification: Debug build passed; 23 tests passed, zero failed/skipped; independent review reported no Phase 1 findings. Preview-SDK notices only. No UI/extension runtime verification was claimed.
  - [x] Adopt `CopilotUsageMeter.slnx` as the sole solution file; migrate all 11 project entries, update solution-aware architecture guards and remove the old `.sln`. Verified SLNX Debug build and all 23 tests pass; reopen SLNX if Visual Studio still holds the old workspace path.
	- [x] Persist new-chat instructions in `.github/copilot-instructions.md` to read/follow the active plan, maintain progress and preserve agreed privacy/safety boundaries.
	- [x] Tray scaffold smoke test, 2026-11-10: explicitly launched the built executable, attached VS debugger by PID, paused/resumed, confirmed the expected window title and responsiveness, and closed the window normally; process exited and debugger returned to design mode. Exception inspection returned `no-stack-frame` on the selected paused context, so no separate exception-inspection result is claimed. No VSIX installation or runtime test.
  - [x] Final step: update README with current capabilities, limitations, build/open instructions and next phase.
- [x] **Phase 2 - End-to-end skeleton: complete, 2026-11-10**
  - [x] Resolve the framework/SDK baseline before feature implementation: approved stable SDK 10.0.403 installed and selected, Tray net10.0-windows/tests net10.0/VSIX net48, shared netstandard2.0 retained. Full SLNX Debug build produced a real net48 VSIX with deployment disabled; 23 architecture tests passed, zero failed/skipped via CLI (Test Explorer returned no matching tests). Independent increment review found no code/configuration regressions and one stale SDK-wording finding, corrected with user approval. Post-migration Tray smoke test passed: expected title, responsive window, loaded .NET 10.0.14 CoreCLR, normal close and exit code 0. No new debugger attach or VSIX runtime verification.
	- [x] Add tray-hosted engine, invented test data, status, pause/resume and basic settings. Real notification icon/menu and single-instance activation; closing/recreating status preserves tracking; Exit stops it. Demo timer/settings are explicitly not production refresh policies. Seven engine and seven settings tests pass. Independent host-increment review found shutdown-command cancellation and invalid-settings startup handling defects; both corrected under autonomous-fix authorization.
  - [x] Specify and implement source-separated protocol, commands, sequencing, limits and cancellation.
	All 18 protocol/lifecycle tests pass (the 18th covers shutdown with an owner-cancelled in-flight command; Exit now always reaches Shutdown).
  - [x] Connect named-pipe server/client to the minimal VS extension in the experimental instance. Explicit temporary Tools-menu demo tool window, NOT chat embedding; separate source labels/freshness, stale cache/dimming and disabled disconnected commands. Three view-model tests pass, including stale labeling throughout reconnect attempts. Nothing installed into the main instance.
	- [x] Verify reconnect, tray restart, multiple VS instances/read deduplication and elevated-client interoperability where feasible. Live checks 2026-11-10: real icon/menu, pause/manual/automatic refresh/resume, persisted 5-second demo interval across restart, status close/recreation, second-launch activation and normal Exit (code 0). Actual Exp VS PIDs 73200 and 91064 showed identical host/revision/count; subscribing caused no increment and one remote refresh advanced the shared count once. Token probe verified both VS clients elevated and tray PID 102256 non-elevated; pause and refresh commands succeeded bidirectionally with count 6 -> 7. Restart changed host identity and both clients reconnected. Final audited-artifact pass: fresh Exp PID 5472 (elevated), tray PIDs 56848/52156 (non-elevated), remote refresh/pause/settings (10 seconds), stale cache retained with disabled commands, restart to new identity with persisted interval, default windowless startup and remote OpenStatus. All owned processes closed normally; original absence of settings restored and task-created activity logs removed. Launch via documented Tools menu; the attempted bare `/Command CopilotUsageMeter.ShowDemo` alias was rejected, not a supported launch contract.
  All 58 tests passed, zero failed/skipped, after the audit fix and a post-commit shutdown-hardening review fix.
  - [x] Final step: README and checklist synchronized, memory/flows updated and graph refreshed (274 nodes/1,029 edges, 25.47 seconds; existing metadata-reference warnings persist). Phase 2 complete; stop for user direction before Phase 3. User authorized local milestone commits, not pushes.
- [ ] **Phase 3 - Feasibility proofs and resource baseline: not started**
  - [ ] Prove supported GitHub sign-in, profile, allowance/consumption access, logout/account switching and unavailable-data states.
  - [ ] Prove chat-expander placement above Total changes and document integration fragility/support.
  - [ ] Prove active-chat matching and per-turn/subagent accounting without text/title access or duplicate bars.
  - [ ] Validate toolkit contract, time windows, older sessions, truncation and combined-filter completeness.
  - [ ] Measure initial idle/busy CPU, memory, refresh duration and process launches.
  - [ ] Build/test, review and report POC outcomes; obtain the user's decision on unresolved gates before full features.
  - [ ] Final step: update README and this checklist, including supported/unsupported POC capabilities.
- [ ] **Phase 4 - Tracking and compact UI: not started**
  - [ ] Implement proven account/local readers with separate totals, status and freshness.
  - [ ] Implement local debounce/throttle, GitHub active/idle schedule, manual refresh, backoff and pause/resume.
  - [ ] Implement the agreed collapsed header and expanded account/local sections with refresh countdown.
  - [ ] Implement at most 20 current-chat bars and exact credits/time tooltips in both collapsed and expanded states, subject to the POC gate.
  - [ ] Verify schedules, accounting, logout isolation, reconnect and chart behavior; complete the phase review.
  - [ ] Final step: update README and this checklist with working features and remaining limitations.
- [ ] **Phase 5 - Details, packaging and final measurements: not started**
  - [ ] Complete details views/filters/export, settings and release-on-close lifetime.
  - [ ] Validate secure authorization storage, privacy, accessibility, DPI/themes and failure states.
  - [ ] Package tray/VSIX separately; verify per-user install/uninstall and supported shipping toolchain.
  - [ ] Repeat resource measurements against the Phase 3 baseline; report actual results.
  - [ ] Complete final build/test, whole-change review and documentation consistency checks.
  - [ ] Final step: publish accurate README setup/use instructions, verification results and known limitations; update this checklist.

## Framework/SDK baseline - approved and applied 2026-11-10

Phase 1 retained the original draft targets and release-candidate SDK to prove the scaffold. That historical baseline is superseded below. Microsoft's support table lists .NET 8 end of support as **2026-11-10**, and .NET 10 LTS through **2028-11-14**.

Approved and applied before Phase 2 feature work: `net10.0-windows` for Tray, `net10.0` for tests, `net48` for the in-process VSIX, and retain `netstandard2.0` for shared libraries consumed by both hosts. Modern VS templates use .NET Framework 4.8 for in-process extensions; the VSIX manifest prerequisite is now 4.8. Stable SDK 10.0.403 was installed with explicit approval and Microsoft hash/signature verification; `global.json` uses `latestPatch` with prerelease disabled. Full solution/VSIX build and 23 tests passed. This does not establish extension runtime support or prove Phase 3 gates.

Sources: [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [Modern VSIX project targets](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio-extensibility/ship/create-vsix-with-project-sdk?view=visualstudio).

## Approved sequence

### Phase 1 - Solution and project skeleton

Deliverables: `CopilotUsageMeter.slnx` (the sole solution file), shared build properties and pinned installed SDK; separate Contracts, Core, Readers.ChatHistoryCli, Readers.GitHub, Signals.FileWatcher, Transport.Pipes, Client, Panel.Core, Tray and Panel.Vsix projects; one architecture-test project. Libraries target netstandard2.0, Tray net8.0-windows, VSIX net472 and tests net8.0, preserving the original draft targets pending packaging review.

Establish the dependency graph, actual VSIX package build route, and negative tests for architecture guards. Update the drafts and README. Empty libraries have no fake feature implementations. The WPF launch window is explicitly a scaffold, not a functioning tray app; the VSIX registers an empty package, not a tool window or chat injection.

Acceptance: all 11 projects build; a real VSIX is produced without deployment; dependency tests pass and reject forbidden dependencies. No GitHub/history reads, authentication, pipes, tray engine, submodule integration or UI injection in this phase. User opens the solution before continuing.

### Phase 2 - End-to-end skeleton

Tray-hosted Usage Engine with invented test data, status, pause/resume and basic settings. Wire a versioned, same-user named-pipe server/client to a minimal VS extension. Define source-separated payloads, refresh/settings commands, sequence semantics, size limits and cancellation before freezing protocol v1.

Acceptance: test data reaches the panel; tray exit/restart, reconnect and multiple VS instances behave correctly without duplicate reads. Test elevated VS with a non-elevated tray where feasible. Experimental VS instance only. No independent Windows service.

### Phase 3 - Feasibility proofs and resource baseline

| Probe | Evidence required |
| --- | --- |
| GitHub access | Supported browser authentication; profile; allowance and consumption availability; sign-out/account switch; explicit permission/unavailable states. Do not promise every subscription is supported. |
| Chat placement | Expander immediately above Total changes in the experimental instance; document whether supported APIs or fragile internal UI integration are used. |
| Current-chat matching | Reliable open-chat identity; turn credits, including subagents without duplication; distinguish streaming updates from new turns. No titles/text. |
| Local completeness | Validate toolkit contract, UTC windows, sessions started before the window, more than 100 rows, truncation/window splitting and jointly filterable repo/model/day data. |
| Resources | Idle and sustained-activity CPU time, memory, refresh duration and child-process launches; compare event-driven throttled reading with the consolidated toolkit command if available. |

Submodule retrieval/publish integration and any toolkit changes are deliberately deferred to this phase, not silently included in scaffolding. Inspect reusable toolkit scripts before writing another. Toolkit origin changes need their own approved scope.

Acceptance: report actual results and limitations, then stop for the user's decision before full feature work. Do not silently substitute a tool window for chat embedding or local usage for account figures. Decide any refresh-frequency optimization from measurements, not assumed battery efficiency.

### Phase 4 - Tracking and compact UI

Implement proven local/account integrations with independent statuses and timestamps. Local refresh: database/WAL events, 1.5-second debounce, 5-second throttle, single flight, background tracking while the tray runs. GitHub refresh: every 5 minutes with local activity or 15 minutes idle; manual refresh, backoff and last/next-refresh display. New events must not continually postpone scheduled sync. Explicit pause suspends automatic refreshes and shows a paused tray icon.

Build the collapsible section and the user's header: `› Copilot Usage  79% | This Repo 659 credits | ▂▅▃▁▇▄▂█  Last 2.5 credits`. Keep at most 20 current-chat bars, oldest left/newest right; hover and keyboard-focus tooltips work while collapsed and expanded. Expanded content separates GitHub account totals from local repo/token analytics, with refresh, countdown, Settings and Open details actions. Never add the two data sources or nest a local-repo bar inside an account-total bar.

Acceptance: source-specific totals/freshness/errors are explicit; guards cover accounting, schedules, pause/resume, logout isolation and chart updates. Active-chat UI proceeds only if its Phase 3 gate is met.

### Phase 5 - Details, packaging and final measurements

Finish standalone details, filters, settings, exports, authentication storage, themes/accessibility/DPI and install/uninstall. Actually close/release the details window and restore its geometry on recreation. Package tray and VSIX separately, without main-instance installation during development.

Repeat resource measurements against Phase 3's baseline, including icon handles, idle/busy behavior and multi-client read deduplication. Revalidate SDK/runtime support before shipping. Full build/test, docs consistency and final review; report measurements and remaining limitations.

## Required final step - README and checklist sync

The last step of every phase, including the final release phase, is to update `README.md` and the progress checklist above. Document only implemented and verified capabilities, current build/run/install instructions, actual validation results, limitations/blocked items and the next approved step. Update the README during a phase as user-visible facts change; never imply an unchecked feature is working. This is a completion requirement, not a new implementation phase or permission to commit/push.

## Archived v0.1 proposal - reference only

The original P0-P9 text below is not the active execution plan. Its local-only scope, late VSIX milestone, visibility-gated refresh and deferred GitHub/chat features were superseded above. Its estimates and prior credit measurements are historical context, not promises or current acceptance values.

## P0 Bootstrap (S)
Goal: repository and guard rails, nothing functional.
Deliverables: `C:\MyFiles\Git\CopilotUsageMeter` with the solution and empty projects from the brief (names, targets, `Directory.Build.props` holding the product name), the toolkit as submodule at `external\VsChatHistoryToolkit` pinned to the current commit, `docs\ARCHITECTURE.md` (the brief), `docs\DECISIONS.md` (D1 to D11 as records), `README.md`, a script that publishes the CLI self-contained into `tools\chathistory`, an architecture test project that enforces the dependency rules and the no-titles rule (the no-titles test initially checks a placeholder), a `.gitignore`.
Acceptance: solution builds; empty tests and architecture tests pass; submodule clones with `--recurse-submodules`; the publish script produces a working `chathistory.exe` that returns `stats`.
Executor: Copilot (mechanical) or a Claude nested session. Review: read-only, focus on the dependency-rule test being real (it must fail when a forbidden reference is added).

## P1 Toolkit `usage-breakdown` verb (M, separate repository: the submodule's origin)
Goal: one CLI call returns credits and tokens grouped by day, model, repo or chat for a window, so the aggregator needs one or two calls instead of dozens, and the 100-row limits stop mattering.
Deliverables (in `VsChatHistoryToolkit`): the verb with `--from --to --group-by day|model|repo|session [--limit]`, rows with requests, credits, input, cache read, cache write, output, reasoning; contract text; tests; a capability flag so the adapter can detect it (for example listed by `chathistory capabilities` or a field in `stats`). Titles and summaries are never part of its output. Commit in the toolkit repository; bump the submodule pointer in this repository as a separate commit.
Acceptance: the full toolkit suite passes; figures equal the existing verbs for the same window (credits for November equal the `activity` sum); the repo grouping uses the same repo key rule as the brief or documents the difference.
Executor: a nested Claude session in the toolkit repo (it knows the codebase patterns from this week), with a Copilot read-only review. Depends on: nothing (parallel to P2 to P4). Optional for M1: if it slips, the adapter uses the existing verbs.

## P2 Contracts and Core (M)
Goal: the tracking logic with no I/O.
Deliverables: DTOs and protocol message types (v1) with JSON round-trip tests; `IHistoryReader`, `IChangeSignal`, `IClock`; the Core engine: month window and UTC maths, repo key normalization (the two-names case from real data as a test), two-level aggregation, per-day and per-model breakdowns, token accounting, projection, snapshot builder, refresh orchestrator (debounce 1.5 s, throttle 5 s, single flight, visibility gate, immediate refresh on first visible subscriber, safety refresh), stale and capped flags.
Acceptance: all logic covered by deterministic tests using fakes (debounce and throttle with a fake clock; visibility state machine; month boundaries; clamp rule; projection edge cases such as day 1 and no data).
Executor: Copilot. Review: read-only, focus on time and boundary logic and on thread-safety of the orchestrator.

## P3 CLI reader and file signal (M)
Goal: real data in and real change events.
Deliverables: `ChatHistoryCliReader` (process start with a timeout, `DOTNET_ROOT` cleared, JSON parsing from hand-written fixtures in the CLI's shape, truncation handling by window splitting, capability detection for `usage-breakdown`, **title stripping with the serialization test**, mapping of unexpected output to `history_unreadable`, missing tool to `history_tool_missing`); `FileWatcherSignal` on the CopilotCli folder watching `session-store.db*` (WAL included), start and stop under host control, handling of dropped events and of the folder not existing yet.
Acceptance: contract tests pass; the no-titles test passes for every fixture; a live check (read-only) produces November figures equal to `activity` (1,183 credits at the time of writing, as a sanity check, not a fixed assertion); the watcher test uses a temp folder.
Executor: Copilot. Review: read-only, focus on process handling (timeouts, no zombie processes), privacy at the boundary, and path handling.

## P4 Pipes transport and client (M)
Goal: protocol over a named pipe, and the client abstraction.
Deliverables: pipe server and client for protocol v1 (per-user pipe name with SID, access limited to the current user, JSON lines, sequence numbers, coalescing of updates, `Query` handling hooks), `IUsageClient` with `PipeUsageClient` (connection state machine: connecting, connected, offline, version mismatch; automatic reconnect with backoff; resend of Hello, Subscribe, Visibility) and `InProcUsageClient` (runs Core directly), protocol tests with real pipes in-process, a version-mismatch test, a gap-in-seq test.
Acceptance: client and server pass the protocol tests; killing the server yields the offline state and recovery; an elevated-client case is documented (tested manually if feasible).
Executor: Copilot. Review: read-only, focus on pipe security, reconnect loops and resource leaks.

## P5 Tray host, milestone M1 (L)
Goal: the tray app with ring, tooltip and menu, serving real numbers.
Deliverables: single-instance tray program (named mutex); composition root (Core, CLI reader, file signal, pipe server); settings file with validation and defaults (allowance, thresholds, ring-shows level, history tool path override, start with Windows, notifications); lookup order for `chathistory.exe`; the ring icon renderer (sizes by system metric, theme and high-contrast aware, states: normal, warning, over budget with badge, offline slash, stale dashed, optional refresh sweep; icon cache; handle destruction); tooltip text (at most 127 characters); right-click menu as in the brief; Pause updates; Set monthly allowance dialog; Start with Windows entry; `location.json`; Copy diagnostics (no chat data); first-run message about pinning the icon; the left-click flyout with the two-layer bar, token line, top repos, last days.
Acceptance: the ring and the tooltip follow real data; the icon renders correctly at 100, 125, 150 and 200 percent scaling and on light and dark taskbars (manual checks listed in the report); no handle or memory growth over a few hundred redraws (a test that redraws repeatedly and checks GDI object counts); starting a second copy brings up the first; pausing stops all refreshes.
Executor: Copilot for the plumbing; the icon renderer and the flyout are good candidates for a Claude nested session if rounds start to grow. Review: read-only, focus on GDI leaks, thread affinity, and settings validation.

## P6 Details window, milestone M2 (L)
Goal: the WPF details window with filters.
Deliverables: window opened from the menu and from double-click, created on open and released on close, light and dark theme resources (no third-party charting), views: Summary (allowance bar, daily bars, projection, token breakdown), By repo, By day, By model (on demand with progress), Tokens; filters (month picker including earlier months, date range, repo multi-select, model multi-select, minimum credits, repo search); drill-down; CSV export of the numbers on screen; footer with as-of time, history tool status and "this machine only"; the `Query` messages wired through the client; keyboard navigation and automation names on controls.
Acceptance: each filter combination returns figures that reconcile with the snapshot (sums equal the month total); window memory is released after close (measured); a month with more than 100 chats is handled.
Executor: Copilot, in two rounds (shell and Summary first, then the other views). Review: read-only, focus on reconciliation of totals and on UI-thread blocking.

## P7 Panel.Core (S to M)
Goal: shared view models.
Deliverables: view models for the snapshot bar (two-layer maths with minimum widths, clamp rule, thresholds on label and outline), the token line, the connection states table from the brief (including dimming and the exact messages), number formatting, the projection text; tests for each state transition and for the layer maths.
Acceptance: all states from the brief covered by tests; no UI framework types referenced.
Executor: Copilot. Depends on: P4. Review: read-only, wording and boundary values.

## P8 Visual Studio panel, milestone M3 (L)
Goal: the panel inside Visual Studio, in the experimental instance only.
Deliverables: VSIX project (confirm the build route for the installed Visual Studio 2026 Insiders first and report it), tool window "Copilot Usage Meter" with the two-layer bar and token line, `PipeUsageClient` usage, visibility reporting (main window not minimized and tool window shown), refresh on start, restore and show, Visual Studio theme resources, the dimmed offline and error states, Start button using `location.json`, "Set allowance" link, solution folder sent in Subscribe, a View menu command.
Acceptance: verified in the experimental instance: live numbers; kill the tray and the panel dims with an error, start the tray and it recovers; minimize the window and no requests arrive at the host; two instances both work without duplicate reading. Nothing installed into the main instance.
Executor: a Claude nested session (the riskiest phase: extension plumbing, experimental instance), then a Copilot read-only review. Depends on: P5 (host) and P7.

## P9 Packaging and hardening (M)
Goal: something installable and robust.
Deliverables: tray install script (per-user copy, shortcut, startup toggle, uninstall) and a zip; VSIX build output and install notes (including the company-approval caveat); version-mismatch checks in both directions; measurements of CPU and memory of the host at idle and during a busy chat (reported, with the reference of about 0.6 s CPU per refresh); a decision record on whether the incremental recompute or the direct database reader are needed; updated `ARCHITECTURE.md` and `DECISIONS.md`; a short user guide.
Acceptance: fresh-profile install works without administrator rights; uninstall leaves no files except settings (documented); measurements recorded.
Executor: Copilot with a Claude review of the measurements.

## Later (not planned in detail)
Per-chat usage and titles (opt-in titles); active-chat identification spike and chat picker; drawing inside the chat panel (adorner, experimental, off by default); incremental windowed recompute; direct SQLite reader; GitHub billing API reader (opt-in token in the Windows Credential Manager); dollar estimate with a user-entered price; richer hover card; Windows service host; other IDE panels.

## Execution cadence and cost control
- One build prompt per phase (large phases split into rounds); one separate read-only review prompt per phase, run after the full suite is green.
- Each prompt restates the rules of work, names the exact files and acceptance, forbids pushing, and asks for a plain-text report.
- The full test suite runs once per round after the last edit; no repeated green runs.
- The two Claude-executed phases (P1, P8) and any growing rounds are the expensive ones; everything mechanical goes to Copilot.
- Parallelism: P1 runs alongside P2 to P4. P6 and P7 can run side by side after P5 and P4. Only one phase at a time edits the same files.
- Commits stay local; the user decides when to push. Each phase ends with a short status note appended to `docs\DECISIONS.md`.

## Decisions needed from the user before phase prompts are written
1. Confirm the name and the repo path.
2. Confirm D2 (credits and tokens only; per-chat and titles deferred).
3. Confirm D6 (submodule plus bundled CLI) and that P1 may change the toolkit repository.
4. Confirm who executes: the split above (Copilot for mechanical phases, Claude for P1 and P8), or all Copilot.
5. Confirm the month window (calendar month, UTC) and whether the allowance should have a default.
6. Confirm that the Visual Studio panel is the last milestone (M3) and that nothing is installed into the main Visual Studio before then.
