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
