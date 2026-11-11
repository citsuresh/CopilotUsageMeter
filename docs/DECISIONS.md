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

## 2026-11-10 - Phase 4 authorization and ephemeral active-chat context

- Decision: Phase 4 is authorized in six parts: contracts/accounting core, local tracking, GitHub tracking, tray/protocol orchestration, compact UI/integration, and verified closeout. Phases 1-3 are complete; no automatic commit or push is authorized.
- Decision: send only the opaque active session id from the VSIX to the tray over the same-user local pipe. Hold it in memory per connection, deduplicate active reads across IDE instances, and project each connection's chart separately. Never log, persist, export, upload or include the identifier in snapshots. A numeric context version prevents late updates for a previously selected chat from being displayed.
- Why: different IDE instances can show different chats; one global current-chat snapshot would misattribute usage. Disconnect removes that connection's context; the client may retain it only in memory to reconnect.
- Given up: the earlier no-chat-identifiers-on-the-pipe restriction. Source-separated account/local figures, privacy restrictions and no-text/title rules are unchanged.
- Revisit: if a supported Copilot active-session API replaces the approved fragile internal view-model properties.

## 2026-11-10 - Canonical current-chat deserialization

- Decision: legacy/null current-chat objects are unavailable. Available data requires freshness and nonempty valid turn rows; null rows, duplicate/negative indices and negative credits invalidate the chart as a whole. Preserve nullable credit/time fields as unknown. Other explicit statuses retain their reason but clear rows/freshness.
- Why: DataContractJsonSerializer bypasses property initializers; independently patched defaults repeatedly left inconsistent available states. A single fail-closed contract avoids presenting incomplete accounting as valid.
- Verification: Part 1 Debug solution build and all 79 tests passed; the independent review reported no actionable issues. Later Phase 4 integration is verified separately in the checklist.

## 2026-11-10 - Phase 4 source composition and secure optional account import

- Implementation: keep one UTC-month aggregate per local refresh, deduplicate active-chat reads, and project charts/repository groups per connection. Match only exact recorded GitHub origin keys; do not pretend stored repository labels prove a canonical local filesystem root. Unknown/ambiguous/capped-missing groups stay unavailable. No paths or identifiers are sent to GitHub.
- Account choice: an explicit gh browser/device-flow launch followed by named Windows Credential Manager import into an app-owned entry. No token extraction command, environment token, plaintext hosts.yml fallback or new OAuth app registration. Logout removes only app authorization. Legacy/refreshable gh keyring formats remain unsupported and explicit; live import/account switching is unverified.
- Toolkit prerequisite: additive usage-turns selects greatest turn indices then returns ascending snapshots with usage-event timestamps. The verified source was transferred into the pinned submodule working tree and published portably; neither toolkit nor Meter was committed/pushed. A reproducible release pin requires later explicit commit approval.
- Review: the independent whole-phase review found context-promotion loss, non-atomic pause transitions and stale countdown deadlines. All three were fixed and independently rechecked. Live UIA testing additionally found that Click-only expansion ignored TogglePattern; expansion now follows Checked/Unchecked and bounds expanded details with scrolling.

## 2026-11-10 - Bounded active-chat identity bridges verified in Exp

- Confirmed runtime shape: the current HarnessSessionViewModel exposes Session, and HarnessChatViewModel exposes Router; the approved identity members are on these nested owners, not directly on the wrappers. Read only Session.SessionId or Router.DisplayedSessionId (or the approved direct member on earlier shapes), using properties/fields and GUID validation. Do not enumerate unrelated values or use currentSessionId. Temporary type/member metadata diagnostics were removed.
- Verification: real charts followed recorded-chat selection, replaced streaming turn usage and cleared on an unrecorded new chat; per-context reconnect and exact collapsed/expanded keyboard tooltip checks passed. Added synthetic opaque-ID/null/malformed/throwing-unrelated-getter fixtures; focused independent review found no significant issues. Internal shape support is still fragile, not a supported VS extension API.

## 2026-11-11 - Blank-login discovery and direct panel sign-in

- Problem: browser sign-in through the tray left the account signed-out because connecting needed a separately typed login. Decision: a blank login in Connect account (and the Exp panel's Sign in action) discovers the login by enumerating only `gh:github.com:*` credential target names; exactly one is imported, none gives `cli-login-not-found`, several give `multiple-cli-logins` and ask for a typed login. Secrets are read only for the chosen login.
- The tray now starts browser sign-in via `gh auth login --web` and connects automatically when `gh` exits 0. Panel `SignIn` connects directly when possible, starts browser sign-in when no CLI login exists, and otherwise opens Status. `OpenStatus` still only shows the window.
- Verification: 282 tests; live Exp tests passed tray-first and Exp-first on one enterprise seat. The interactive browser step, auto-connect after a real flow and two-account switching are unverified.
- Tooling note: partial-line replacements with the editor tool truncated following lines in three files during this work (fixed); use whole-line replacements and review the diff.

## 2026-11-11 - Token-billed premium credits are accepted when finite

- Problem (found by the user in the live UI): after sign-in the account showed `unavailable` although the endpoint returned a full `premium_interactions` snapshot. Cause: the parser rejected any root `token_based_billing: true` as `unsupported-billing`, a Phase 4 guard added before the real seat shape was checked. The tested enterprise-managed Business seat reports `true` together with finite `entitlement`, `credits_used` and `remaining`.
- Decision: `token_based_billing: true` is accepted only when the premium snapshot reports both finite `entitlement` and `credits_used` (or is unlimited). Structured/non-boolean billing shapes and partial token-billed snapshots (for example remaining only) remain `unsupported-billing`. `remaining` is still never derived; the reported value is shown.
- Verification: 286 tests; live tray Status showed Consumed 6339 / 100000, reported remaining 93444, reset 2026-12-01. My earlier note calling this seat's quota a known limit was incorrect and is superseded.

## 2026-11-12 - Post-Phase-4 UI rework: adaptive row + popup (user-approved)

- Decision: the Visual Studio panel is one adaptive row (wide >= 560 DIP, medium >= 330, narrow below) with an anchored popup for details, replacing the Phase 4 inline-expansion header. The Expanded setting is dropped. "Full details..." reuses `OpenStatus`; no new protocol command. Palette: GitHub bar blue, orange from 85%, red when over; local pill red (current chat), orange (rest of repo), light blue (other repos).
- Preserved: at most 20 bars with exact hover/keyboard-focus tooltips, request-only session id, and never adding or nesting GitHub and local figures.
- Reconciliations: account data older than 30 minutes is shown stale; a down tray cannot report its own fault, so no host-problem protocol field (the offline row is a client-side state); the tray shows runtime faults as a banner; the "Start host" action stays in Phase 5 (needs an install location). The 85% and 30-minute thresholds are defaults, not user-confirmed values.
- Additive `CurrentChatUsage.TotalCredits` carries the whole-chat total the toolkit already returns; it is cleared whenever a chat is unusable.

## 2026-11-11 - Automatic recovery for local refresh and the pipe listener (user-authorised)

- Decision: a failed local refresh retries by itself (5 s, 15 s, 45 s, then every 5 min) instead of waiting for the next database event; the user is told only after 3 consecutive failures and the last good figures stay visible meanwhile. Informational codes (nothing recorded yet) and permanent codes (history tool missing/misconfigured) are not retried. The pipe listener restarts itself after any non-cancellation failure (1, 2, 4, 8, 16 s, then 30 s), reports a fault only at the 3rd consecutive failure and clears that banner when it recovers.
- Rationale: the 60 s file-watcher reconciliation only signals when metadata or the error code changes, so a transient CLI/database failure could leave stale local data indefinitely; a listener fault previously required restarting the tray. GitHub retry (15 s doubling to 15 min) and the client reconnect loop already existed and are unchanged.
- Not verified live; the retry schedule and the notify-after-3 threshold are defaults, not user-confirmed values.

## 2026-11-11 - Busy-tray handling: heartbeat, command queue and Refresh timeout (user-authorised)

- Decision: the tray sends a `Heartbeat` every 15 s to subscribed clients; a client that has seen one drops the connection after 45 s of silence and reconnects. Commands on a connection run through an ordered queue (16 at most, then a `busy` error) instead of blocking the read loop. `Refresh` gets a 45 s client timeout (other commands 10 s), and a Refresh timeout is shown as "taking longer than usual", not as a failure.
- Rationale: a busy or stuck tray previously froze every request on that connection, and a client that stopped hearing from the tray never noticed. The values (15/45 s, queue of 16, 45 s Refresh) are defaults, not user-confirmed. Not verified live.
- Deferred: cancelling commands the client abandoned (needs a cancel message or a handler timeout).

## 2026-11-11 - VSIX starts in every instance; probe commands stay temporarily
- Decision: the Exp-only gate in `UsagePackage` is removed so the user can test in the main VS. This supersedes the earlier note that the probe/Recheck commands stay only because they are Exp-only: the four Tools-menu test commands remain for now at the user's direction and MUST be removed before publishing (Phase 5). The manifest description now describes the real behaviour. Agent-run runtime tests remain restricted to Exp; the user installs into the main instance themselves.
- Rationale: the main-instance install otherwise did nothing. The user accepted the temporary test commands during development.

## 2026-11-11 - GitHub sign-in runs `gh` hidden and shows the code in a tray dialog (user-authorised)

- Decision: `gh auth login --web` runs without a window. The tray reads only the one-time code from its output and shows it in a **Sign in to GitHub** dialog whose button copies the code and opens the fixed address `https://github.com/login/device`; the URL printed by `gh` is never opened. If no code is read within about 10 s, the tray falls back to the earlier visible console. No new OAuth app is added and credential storage is unchanged (`gh` stores the login; the tray imports it as before).
- Rationale: a console or PowerShell window was not intuitive. Parsing only the code and using a constant URL limits what untrusted output can do; the fallback covers a `gh` version whose text differs. The timeouts (10 s for the code, 15 min overall) are defaults, not user-confirmed. `gh` also copies the code to the clipboard on its own. Not verified live.
- Rejected: a tray-owned OAuth device flow (new app registration and token handling).

## 2026-11-11 - Saved accounts can be removed from the chooser with `gh auth logout` (user-authorised)

- Decision: each saved GitHub CLI login in the chooser gets a remove button; after confirmation the tray runs `gh auth logout --hostname github.com --user <login>` hidden. This supersedes the earlier rule that the app never changes GitHub CLI's own login: it now does so, only on this explicit action. Removing the account the tray is connected with also signs the tray out. The command is in-process only, not a pipe command.
- Rationale: Log out removed only the app's copy, so the account stayed in the sign-in list. `gh auth logout` is the supported way and also updates gh's own configuration; deleting the Credential Manager entry directly would leave a stale entry in gh's config. The 30 s timeout is a default, not user-confirmed. Not verified against a real `gh` login.

## 2026-11-11 - Failures are logged type-only and surfaced, never fatal (user-authorised)

- Decision: a shared `Diagnostics` facade records only component, a fixed code, the exception type and its HRESULT. The tray writes `%LOCALAPPDATA%\CopilotUsageMeter\logs\tray.log` (256 KB roll to `tray.1.log`, 10 s dedupe); the extension writes to the Visual Studio activity log. Unexpected exceptions in the tray and extension are caught, logged and shown as a banner/note instead of crashing; workers restart with backoff.
- Rationale: exception messages and stack traces can carry chat titles, paths or payloads, which the privacy rules forbid in logs. The path, size, dedupe window and one-banner-a-minute throttle are defaults, not user-confirmed.
- Programming faults in `LocalRefreshScheduler`, `AccountRefreshCoordinator` and `GitHubAccountReader` are logged and still propagate to their supervisors (existing tests rely on that).

## 2026-11-11 - Refresh progress is shown for tray-side reads, and the tray icon is a thin track with a thick arc (user-authorised)

- Decision: the snapshot carries an optional `IsRefreshing` flag, true while a local or GitHub account read has run for at least 500 ms. The extension row/popup and the tray window show their existing progress bar for it. The tray icon uses a 2 px light-grey track and a 6 px cyan arc (orange from 80%, red ring at 100%).
- Rationale: previously only manual refresh and chat switching showed progress, so automatic refreshes looked like a frozen number. The icon arc was hard to read at 16 px; the user compared rendered variants and chose this one.
- Given up: very short reads never show progress (by design). The 500 ms value and the colours are defaults. The red dot at 100% was replaced by a full red ring.


## 2026-11-13 - Usage alerts: opt-in, configurable levels, tray owns notifications (user-authorised, stage 1 of 3)

- Decision: the tray alerts when GitHub-reported account usage crosses user-chosen levels (whole percent of the monthly allowance, 1-200, up to 10; defaults 80 and 100; custom levels such as 50 allowed). Each level has its own channel: Windows notification (balloon), a small non-modal message window, or off. Alerts are off by default. This replaces the earlier "balloon at 80 and 100, off by default" line in ARCHITECTURE.md.
- Decision: a level alerts once per UTC month and per crossing; a jump across levels gives one alert for the highest non-Off level; a level added at or below current usage never alerts retroactively; only fresh "available" account data from the current UTC month counts, never local credits. Alert text is fixed wording plus numbers (no login, titles, paths or chat data).
- Rationale: the alerts are a convenience on top of GitHub data; they must never imply local credits equal billing units, and must stay quiet when data is stale or unavailable.
- Given up: switching to an account with lower usage and back can repeat an alert (levels re-arm when a fresh reading drops below them); toast buttons and app-identity toasts are deferred; the extension sends no Windows notifications (it will use infobars in stage 3). Stage 2 (tray health alerts) and stage 3 (extension infobars) are not authorised yet.
## 2026-11-11 - Tray problem notifications: on by default, once per problem, fixed wording (user-authorised, stage 2 of 3)

- Decision: the tray shows a warning balloon for: not signed in at launch (once per launch; an explicit sign-out or a skipped account never notifies), expired sign-in, usage unavailable for the account (2 reads), GitHub errors (3 reads), a sustained network outage (3 reads and 30 minutes; short drops are deliberately quiet, as the user preferred), local read failing (2 reads) or history tool missing, and background task faults. Each problem notifies once until it recovers and not again for 6 hours. Clicking the balloon opens Full details. Wording is fixed per condition.
- Decision: a single "Notify me about problems" switch in the alerts window, independent of the usage levels. Unlike usage levels it is on by default, because without sign-in or working reads nothing else in the tray works; the user can turn it off. Existing alerts.json files without the setting default to on.
- Rationale: unsolicited noise is the main risk, so notifications need persistence, one-shot behaviour and a cool-down. Startup failures already show a message box and exit, so they get no notification. A network-problem tray-icon marker was preferred by the user over many notifications but is not built.
- Given up: a blocked or inactive GitHub account cannot be told apart from a missing plan (both are 403/404, shown as "usage unavailable"); cool-down state is kept in memory only, so a persistent problem can notify once per tray launch; extension-side problems (tray lost, unknown chat layout) are stage 3 and not authorised.

## 2026-11-12 - Extension infobars: three fixed notices, no settings, no Windows notifications (user-authorised, stage 3 of 3)

- Decision: the extension shows Visual Studio infobars (not Windows notifications, which stay with the tray) for: the tray not reachable for 20 seconds, tray and extension versions differing, and the Copilot Chat layout not recognised for 30 seconds (the chat pane was found but the usual anchor for the usage row was not). Each shows once per problem, closes itself when the problem clears, respects the user closing it, and is not repeated within 30 minutes. Wording is fixed.
- Decision: no setting. The extension cannot read the tray's `alerts.json` without crossing the tray/extension privacy boundary, and these notices describe the extension's own state, so they are always on.
- Rationale: the 20-second delay avoids notices when Visual Studio starts before the tray or the tray restarts; the cool-down avoids a flapping connection producing repeated infobars.
- Given up: when Copilot Chat is not open the extension cannot tell that from a changed layout, so a layout change that hides every anchor is reported only through the status bar; the infobar has no action button because the extension does not know where the tray is installed; a tray-icon marker for network problems is still not built.