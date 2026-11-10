# Design decisions

Append-only records. Phase status notes are not approval to commit or start the next phase.

## 2026-11-10 - Product and privacy boundary (D1, D2, D10, D11 revised)

- Decision: Copilot Usage Meter shows GitHub-reported account allowance/consumption and basic profile alongside separately labeled local analytics. Never upload local history, titles, text, repo paths or analytics. No dollars.
- Why: account usage can include other machines; local data explains activity but cannot substitute for billing reports.
- Given up: the original no-network/no-account scope. Do not sum overlapping sources or assume local credits equal GitHub billing units.
- Revisit: after the POC establishes supported authentication, permissions, allowance availability and reporting units. Universal subscription support is not promised.

## 2026-11-10 - Tray-hosted engine and named pipes (D3, D4, D5)

- Decision: the tray owns the background Usage Engine and same-user named-pipe server. VS is a client. No Windows service or WCF; package tray and extension separately. Disconnected clients retain dimmed/stale values and a clear status.
- Why: one reader serves multiple IDE instances without administrator-required service installation.
- Given up: tracking when the tray is not running. Same-user elevated-client interoperability still needs testing.
- Revisit: only if a proven requirement needs a separate host process.

## 2026-11-10 - Independent refresh policies and measurement (D7 revised)

- Decision: local event-driven refresh continues while the tray runs, with 1.5-second debounce / 5-second throttle. GitHub sync uses 5 minutes during activity and 15 minutes idle, with manual refresh and last/next sync. Pause suspends automatic updates and marks the icon/state paused.
- Why: local changes should not cause frequent network calls; hidden windows must not leave the tray indefinitely outdated.
- Given up: the original visibility gate. The POC must measure refresh CPU time, duration, memory and process launches; repeat at the final phase. No unmeasured battery-efficiency claim.
- Revisit: measured refresh cost, activity-window definition, missed-event recovery and API backoff behavior.

## 2026-11-10 - Compact chat expander and turn chart (D8, D9; D2 expanded)

- Decision: desired VS UI is an expander above Total changes with the user's one-line summary and at most 20 current-chat credit bars. Exact credits/time are accessible while collapsed. Standalone details actually closes/releases and remembers geometry.
- Why: preserve chat vertical space while retaining drill-down. Account/local scopes must remain explicit in expanded content/tooltips.
- Given up: assuming a conventional tool window meets the requested placement. Internal UI injection and active-chat attribution are not yet proven.
- Revisit: Phase 3 experimental-instance results; obtain user approval for any fallback. Titles/text remain out of scope.

## 2026-11-10 - Toolkit boundary (D6 retained, integration deferred)

- Decision: keep the toolkit behind the local CLI adapter; proposed pinned submodule/bundled CLI integration occurs after contract inspection in the POC, not during Phase 1.
- Why: skeleton-first delivery should not imply unverified accounting/truncation behavior is correct. Validate combined-filter data and older sessions with current-window activity.
- Given up: original P0's immediate toolkit fetch/publish requirement. Toolkit origin changes require a separate approved scope.
- Revisit: consolidated usage-breakdown capabilities and measured CLI cost.

## 2026-11-10 - Phase 1 project/toolchain scaffold

- Decision: ten production projects plus one test project in a single .sln. Separate GitHub reader avoids conflating account access with IHistoryReader. Preserve draft netstandard2.0/net8.0-windows/net472/net8.0 targets. Pin installed SDK 11.0.100-rc.3.26522.148 temporarily; build real VSIX with VS 2026 Insiders MSBuild and NuGet VSSDK tools, without deployment.
- Why: shared libraries must remain usable by the .NET Framework in-process extension. A real package build proves more than a renamed class library; no production auth/history/transport logic belongs in this phase.
- Given up: a stable SDK baseline on this machine for now; preview SDK notices are expected. Current shipping support needs review before packaging. Initial architecture guards do not prove future privacy/accounting behavior.
- Revisit: supported SDK/runtime targets before shipping; validate UI integration before building the full panel.

## 2026-11-10 - Phase 1 status

Solution, project graph, architecture guards, WPF entry scaffold and real VSIX package scaffold created. Full Debug build produced a VSIX without deployment. Runtime feasibility probes and resource measurements are intentionally not part of this phase. User will open the solution before directing Phase 2. Final test/review evidence is reported with the phase completion.

## 2026-11-10 - Adopt SLNX as the sole solution format

- Decision: replace `CopilotUsageMeter.sln` with `CopilotUsageMeter.slnx`, superseding the Phase 1 scaffold's original solution-format choice. Keep all 11 project entries and source/test solution folders. Tests parse XML project entries and locate the repository using the new filename.
- Why: this new project targets modern Visual Studio/MSBuild; SLNX is concise and easier to review/merge. One authoritative solution avoids configuration drift.
- Given up: support for older tooling that cannot open SLNX. No framework targets, SDK pin, project references or runtime behavior are changed by this migration.
- Revisit: only if an actual consumer requires older solution tooling.

## 2026-11-10 - Approved stable Phase 2 framework baseline

- Decision: supersede the temporary Phase 1 SDK/host targets with stable SDK 10.0.403, Tray net10.0-windows, tests net10.0 and in-process VSIX net48; retain all eight shared libraries on netstandard2.0. Pin `latestPatch`, disallow prerelease SDK selection and require .NET Framework 4.8 in the VSIX manifest.
- Why: begin Phase 2 on a stable supported toolchain while preserving modern-tray/.NET Framework-extension interoperability. The user explicitly approved the migration and SDK installation; the Microsoft installer hash and signature were verified before installation.
- Given up: the preview compiler pin and original net8.0/net472 scaffold baseline. No dependency package upgrades, production behavior or extension runtime support is implied by retargeting. Full SLNX/non-deploying VSIX build and 23 architecture tests passed; Test Explorer discovery returned no matches, so tests ran via CLI.
- Revisit: shipping toolchain/support and Phase 3 extension runtime feasibility; do not infer chat embedding or account availability from a successful package build.

## 2026-11-10 - Phase 2 invented-data host and temporary VS test surface

- Decision: compose one tray-owned deterministic demo engine and protected current-user pipe endpoint. Serialize host commands/refreshes; keep a configurable 5-300-second demo timer and persist only that interval. Closing the status/settings window releases it without stopping the engine; only tray Exit stops tracking. Local commands share shutdown cancellation and are drained before disposal.
- Why: prove lifetime, source separation, commands, reconnect and shared work across IDE instances before integrating any real history or account data. Seven settings tests exercise the production store through a linked source file, keeping UI/framework boundaries and the eleven-project solution intact.
- Given up: no production GitHub/local schedules or accounting claims. The explicitly approved Tools-menu tool window is a temporary Phase 2 transport test surface, not a substitute for chat embedding. No secrets, real chat content or paths enter payloads.
- Evidence: two actual experimental VS instances shared one host/revision/count; a remote refresh advanced it once. Token inspection verified elevated VS clients interoperating bidirectionally with a non-elevated tray. This closes the earlier hosting decision's Phase 2 interoperability test, not the Phase 3 chat/account gates.
- Revisit: the Phase 3 supported-account/chat-placement/accounting probes, then independent production refresh policies and final details-window geometry/lifetime.

## 2026-11-10 - Phase 3: toolkit submodule and legacy-verb limits

- Decision: add `VsChatHistoryToolkit` as submodule `external/VsChatHistoryToolkit` pinned to `6e1aa5d` (toolkit origin/main), as already planned in D6. Detect row caps with `count >= limit` (never the envelope `truncated` flag), split windows with half-open ISO bounds, and map chats to repositories with `sessions --from` only.
- Why: probes on this machine showed exact agreement across `activity`/`stats`/`usage-top` and exact window splitting, but the envelope flag also fires on title character caps and `sessions --to` omits sessions ending after the window.
- Given up: joint repo x model x day filters from legacy verbs. `usage <id>` is not date-windowed and many chats span several models/days, so such views need a consolidated toolkit verb, which requires a separately approved toolkit-repository scope.
- Revisit: after the user decides on the toolkit scope; CLI bundling/publish into `tools\chathistory` is still pending.

## 2026-11-10 - Phase 3: GitHub account usage for an enterprise-managed seat

- Finding: for a Copilot Business seat on an enterprise-managed (EMU) account, the supported user billing endpoints (`premium_request/usage`, `usage/summary`) return 404, so supported account usage is unavailable. The unsupported `copilot_internal/user` endpoint, run only with the user's explicit approval, returns the premium-request quota (entitlement, remaining, credits used, reset date) with a classic OAuth token from `gh`'s own browser device flow.
- Decision pending: whether the product may depend on that internal endpoint. If used, it must be opt-in, labelled as unofficial/fragile, fail to an explicit "unavailable" state, and keep GitHub-reported quota visibly separate from local analytics (never summed or substituted). The registered-GitHub-App route is untested; the app form was never submitted and no client ID exists.
- Why: avoid claiming support the evidence does not show; one seat on one plan is not universal subscription support.
- Revisit: after the user decides; test a GitHub App user token and a personally billed seat if one becomes available.

## 2026-11-10 - Chat placement: feasible only through Copilot internals (fragile)

- Finding: in an Exp instance, a marker inserted into the `StackPanel` that holds Copilot's `ChatWorkingSet` rendered directly above "Total changes". No supported extension point was found; the approach depends on internal element names and tree shape and is lost when a new chat session view is created.
- Decision pending (user): whether Phase 4 builds on this fragile injection (with per-session re-injection, absent-row handling and a version-drift check) or accepts a different placement. A tool window is not silently substituted.
- Follow-up findings (same day): the "immediately before `ChatWorkingSet`" insertion works whether Total changes is shown or collapsed, but must be re-done for every new session view. A top-of-pane bar added as a new first row of `mainContentGrid` persisted across new chat and chat switch. User direction: use the top-of-pane position as the fallback when the Total-changes anchor is not available. Fallback order proposed: above Total changes, then top of pane, then a supported status-bar item; each tier must show its degraded state, not fail silently.

## 2026-11-10 - Active-chat identity and per-turn accounting

- Finding: the open chat can be identified by the `CopilotSessionId` on the chat view model, which matches toolkit session ids; this depends on Copilot internals like the placement integration. Per-turn rows already include subagent usage (main + subagents = total = sum of per-turn rows in all 97 recorded sessions checked).
- Rule adopted for Phase 4+: key usage by (session id, turn_index) and replace snapshots on refresh (streaming grows the last row); never add subagents to totals; never trust `turn_count`; show unavailable when a session has no recorded usage or the id does not match a toolkit session.

## 2026-11-10 - Phase 3 partial resource baseline (demo engine only)

- Measured with `scripts\agent\Measure-Resources.ps1` on one machine (14 logical cores, Release tray): the toolkit CLI refresh is 3 process launches at about 1.5 s wall and 1.3 s CPU per refresh; the idle demo tray uses about 63-67 MB working set and about 0.02 % of the machine; a simulated refresh every 5 s adds about 1.2 % of the machine, almost all in the CLI child processes.
- Consequence: the dominant cost is the per-refresh CLI process launches, not the tray. The 5 s throttle with continuous activity is the expensive case, so the real engine should coalesce refreshes and prefer fewer CLI calls per refresh. This is guidance for Phase 4 design, not a measured product figure.
- Limits recorded in the plan: demo engine only, single runs, no battery measurement and therefore no battery-impact claim, 5 s tray interval not confirmed applied. Re-measure the real engine at the final phase.

## 2026-11-10 - Toolkit pin bumped to usage-breakdown (12bbfeb)

- The submodule now points at toolkit commit 12bbfeb, which adds usage-breakdown. Joint repo x model x day filters are answered by one CLI call instead of per-chat calls; rows sum to total and day grouping matches activity. Distinct chats counts are not additive across groups, so never sum them.

## 2026-11-10 - Phase 3 gate decisions

- GitHub account usage: use the unsupported copilot_internal/user endpoint for now. It must be labelled unsupported, may change or be blocked, and show an explicit unavailable state on failure; never substitute local figures. A GitHub App user token is a noted future alternative (needs admin approval; untested). Production sign-in and secure token storage are still to be designed in Phase 4.
- Placement: layered design agreed (above Total changes, then top of pane, then a status-bar item). Neither embedded position is a supported extension point, so it must tolerate failure and re-inject whenever the chat session view is recreated.
- Probe commands: kept for now because they are Exp-only (registered only with /RootSuffix Exp) and the placement probes still need re-running; remove or replace them with the real injector in Phase 4 so reflection-based probing does not ship.
