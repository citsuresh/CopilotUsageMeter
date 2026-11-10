# Copilot Usage Meter: architecture (living draft, v0.4)

This brief is the shared context for every build phase. It describes a first-cut architecture that is meant to grow and change. Where it says "v1", later versions may replace it, so keep the seams narrow and the contracts additive. If a phase prompt and this brief disagree, stop and ask. Decisions not covered here are reported in the phase's final report, not made silently.

Updated 2026-11-10. See `PHASE_PLAN.md` for verified progress and `DECISIONS.md` for decisions. Production behavior below remains intended unless explicitly marked implemented. Phase 1 scaffolding and the Phase 2 invented-data end-to-end demo are complete; real account/history integration and chat embedding are not.

## 0. Summary of decisions (v0.4)

| # | Decision |
|---|---|
| D1 | Name: **Copilot Usage Meter** (code `CopilotUsageMeter.*`, repo `C:\MyFiles\Git\CopilotUsageMeter`). "for GitHub Copilot" only descriptively; no "GitHub" in product or assembly names. |
| D2 | **GitHub account usage** (allowance, consumed and remaining credits, basic profile) and **local analytics** (repo, day, model, tokens, current-chat credits per turn). Account access, allowance availability and current-chat attribution must be proven in the POC. Chat titles and text remain excluded. |
| D3 | Architecture is layered and decoupled: a reusable **core library**; thin **hosts** (tray now, others later); a **transport** (named pipe) that is swappable; a **client** abstraction the UI depends on; **panel core** view models shared by the Visual Studio panel and the tray window. |
| D4 | The **Copilot Usage Tray** app hosts the aggregation service (no separate Windows service). Visual Studio panels are thin clients. The tray app and the extension are **installed separately**. |
| D5 | The Visual Studio panel shows the **last numbers dimmed plus an error** when the tray is not reachable, with a Start button. |
| D6 | The history toolkit (`VsChatHistoryToolkit`) is a **git submodule** pinned to a commit; the build publishes its CLI (self-contained) into `tools\chathistory\` next to the tray. It is a **run-time dependency through the CLI only**, behind one adapter. No fixed install path. |
| D7 | Local refresh is event-driven, debounced and throttled, and continues while the tray runs even if every usage view is hidden. GitHub sync uses a separate 5-minute active / 15-minute idle schedule. Explicit Pause updates suspends automatic refreshes. |
| D8 | Tray icon is a **round progress ring with no digits**; the numbers are in the hover tooltip. Icons are drawn on demand, never pre-made per percentage. No continuous animation. |
| D9 | A **WPF details window** opens from the tray menu or a double-click and shows complete usage with filters. |
| D10 | Credits are the cost measure; tokens are volume. **No dollars** (the local data has none). The monthly allowance is a manual setting. |
| D11 | Authenticated GitHub requests retrieve profile and account usage only. **Never upload local chat history**, titles, text, paths or analytics. Store authorization securely; support logout and account switching. Local and GitHub scopes remain visibly separate. |

## 1. Purpose and scope

Show GitHub-reported account consumption alongside separately labeled usage recorded on this machine, in a tray app and a compact Visual Studio panel.

**Intended scope:** GitHub profile, allowance and consumed/remaining credits when accessible; first-run browser sign-in with Skip, Settings sign-in/reconnect and logout; local repo/day/model/token analytics; projection; tray ring, tooltip, menu and flyout; WPF details with filters; an expandable section above Copilot Chat's Total changes, with a rolling current-chat turn chart; independent refresh schedules, reconnect and version handling. GitHub permissions, panel placement, chat matching and accounting completeness are POC gates, not guarantees.

**Excluded:** chat titles/text, dollar estimates, history upload, telemetry, account-wide claims based on local totals, automatic attribution of local history to the signed-in GitHub account, and a separately installed Windows service. Never add GitHub and local totals together or silently substitute one for the other. Unavailable allowance/usage is explicitly unavailable, never zero or guessed.

## 2. Architecture

```
Panel UI (VSIX shell, tray flyout, WPF details window)     Hosts: Tray | Windows service | in-panel | console
        |                                                          |
  Panel.Core (view models, bar math, state machine)         Transport.Pipes (server)   <- swappable
        |                                                          |
  Client (IUsageClient) ------------------------------------> Contracts (DTOs, protocol v1)
        |                                                          |
        +--------------------------> Core (aggregation engine) <---- Readers (ChatHistoryCli | later Sqlite | later GitHub API)
                                                ^
                                          Signals (FileWatcher)
```

| Project | Target | Responsibility |
|---|---|---|
| `CopilotUsageMeter.Contracts` | netstandard2.0 | DTOs and protocol messages (JSON). Versioned, additive only. No logic. |
| `CopilotUsageMeter.Core` | netstandard2.0 | All tracking logic: month window, two-level aggregation, repo key normalization, breakdowns by day, model and repo, token accounting, projection, refresh orchestration (debounce, throttle, visibility), snapshot building. No pipes, UI, Visual Studio or file watcher types. |
| `CopilotUsageMeter.Readers.ChatHistoryCli` | netstandard2.0 | `IHistoryReader` over the `chathistory` CLI (runs it, parses JSON, strips titles). |
| `CopilotUsageMeter.Readers.GitHub` | netstandard2.0 | Separate account/profile adapter; authentication and billing API support to be established by the POC. Not a local-history reader. |
| `CopilotUsageMeter.Signals.FileWatcher` | netstandard2.0 | `IChangeSignal` over `FileSystemWatcher`. |
| `CopilotUsageMeter.Transport.Pipes` | netstandard2.0 | Named-pipe server and client for the protocol. |
| `CopilotUsageMeter.Client` | netstandard2.0 | `IUsageClient` with `PipeUsageClient` and `InProcUsageClient`. |
| `CopilotUsageMeter.Panel.Core` | netstandard2.0 | View models, bar layer math, connection state machine, number formatting. No UI framework types. |
| `CopilotUsageMeter.Tray` | net10.0-windows | Host: tray icon, tooltip, menu, flyout, WPF details window, settings, pipe server, startup entry, single instance. |
| `CopilotUsageMeter.Panel.Vsix` | net48 / VSSDK in-proc | Visual Studio shell: temporary Phase 2 test surface; intended chat placement remains a Phase 3 gate. |
| `external\VsChatHistoryToolkit` | submodule | Source of the CLI. Not referenced by code. |
| `tests\*` | net10.0 | Architecture tests exist in Phase 1; focused library tests are added with behavior. |

**Seams (keep narrow; add more only when a second implementation exists):** `IHistoryReader`, `IChangeSignal`, `IClock`, `IUsageClient`, plain composition roots per host.

**Dependency rules (enforced by the architecture test suite; run after building):**
- Contracts, Core and Panel.Core reference no pipe, UI, Visual Studio, process-start or file-watcher types.
- Only hosts and the client reference transport projects.
- Only the Vsix project references Visual Studio SDK packages; WPF is allowed in Tray and Vsix, WinForms in Tray. Shared projects have no UI framework types.
- No project references the toolkit's assemblies.

**Why netstandard2.0:** a Visual Studio extension runs on the .NET Framework in-process, so anything it reuses (Core, Contracts, Client, Panel.Core) must be usable there. It also lets the panel run `InProcUsageClient` later if the tray is dropped.

**Host lifetime:** the tray owns the background Usage Engine and pipe server; no Windows service is installed. Exiting the tray stops tracking. The standalone details window is created on demand, actually closed/released, and recreated with saved geometry. VS controls its own panel lifetime; collapsing the expander does not stop tracking.

**Phase 1 toolchain:** installed VS 2026 Insiders MSBuild at `C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe` builds the solution and real VSIX. `global.json` temporarily pins installed SDK `11.0.100-rc.3.26522.148`; this is a preview compiler, not a change to the draft's net8.0/net472/netstandard2.0 targets. Revisit supported SDK/runtime targets before packaging. The WPF launch window and empty AsyncPackage are scaffolding, not an implemented tray/panel.

**Approved Phase 2 baseline (2026-11-10):** supersedes the historical Phase 1 targets/pin above. Stable SDK 10.0.403 (`latestPatch`, prerelease disabled), Tray net10.0-windows, tests net10.0, in-process VSIX net48 with matching manifest prerequisite; shared libraries retain netstandard2.0. Migration build/packaging and 23 architecture tests passed. Retargeting alone did not establish runtime support.

Final build and 58 tests passed.

## 3. The history toolkit dependency

- Path in the repo: `external\VsChatHistoryToolkit`, a git submodule of `https://github.com/citsuresh/VsChatHistoryToolkit`, pinned to a reviewed commit. The pointer is bumped deliberately. Clone with `--recurse-submodules`.
- Build: a script publishes the CLI self-contained for win-x64 into `tools\chathistory\` next to the tray program (the toolkit's own publish script `scripts\agent\Publish-VsChatHistoryToolkit.ps1` is the reference; do not depend on `C:\Shared`).
- Run-time lookup order for `chathistory.exe`: (1) the path in settings; (2) `tools\chathistory\chathistory.exe` next to the tray; (3) otherwise a clear status "history tool not found" (the tray shows it, panels dim).
- Contract after toolkit retrieval: `external\VsChatHistoryToolkit\docs\CLI_CONTRACT.md`. JSON on stdout, snake_case. Clear `DOTNET_ROOT` before running it. Do not rely on a developer's personal skill installation at runtime.
- The adapter checks the CLI works (a cheap `stats` call) and treats unexpected JSON as a reportable status, not a crash. The toolkit expects store schema version 8; a store change is fixed in the toolkit, not in this code.
- Toolkit changes made for this project (for example a `usage-breakdown` verb, see PHASE-PLAN) are committed in the toolkit's own repository first, and then the pointer is bumped here.
- Phase 1 does not fetch the submodule or publish a CLI. Inspect and validate its contract in the POC before integrating it; toolkit repository edits require their own approved scope.

## 4. Data model

**Window:** one calendar month in UTC (1st 00:00 UTC to the next 1st), selectable in the details window (earlier months allowed). Day buckets are UTC days (the CLI groups by UTC); local-time "today" is approximate and labelled so.

**Local credits:** the CLI's `credits` (sum of each request's `request_multiplier`), always the `total` (main plus subagent requests). Never `nano_aiu`, never converted to money. Do not assume these equal GitHub billing units. GitHub reports carry their own unit, billing scope, reporting period and timestamp. Account allowance is retrieved if possible; a manually entered fallback is clearly labeled and has no default.

**Tokens ("tokens processed"):** per request the store records `input_tokens` (the whole prompt, re-sent context included), `cache_read_tokens`, `cache_write_tokens`, `output_tokens`, `reasoning_tokens`. Show them as input, of which cached, output, reasoning. Always label summed input as "tokens processed", never as the length of a conversation.

**Repo key:** canonical lower-case full path of the git root, else of the cwd. The same repository can appear under two display names (the git remote form `owner/name`, or just the folder name when the chat had no remote), so never key by display name. Display name = repository field if present, else last path segment.

**Invariant:** repo <= overall for the same window; if data breaks it (lag, truncation), clamp for drawing and set a `note`.

**Reference figures (measured on one machine, November so far, for sanity checks only):** 1,187 requests; 104.2 M input tokens of which 98.3 M cached (94 percent); 0.77 M output; 0.40 M reasoning; 1,183 credits; about 11 credits per million input tokens (depends on the model mix; not a price).

**Proposed local snapshot** (not implemented): version/sequence, local as-of time, UTC window, overall and repo credits/requests/tokens, day/model breakdowns, projection, stale/capped flags and notes. Current-chat turn credits are included only after reliable matching is demonstrated. A turn includes associated subagent work without double-counting.

**Separate proposed account snapshot** (not implemented): connected profile, accessible billing scope, reported unit/period, optional allowance/consumed/remaining values, availability/error status, last successful sync, next scheduled refresh. Local and account figures have independent freshness and failure states. Logging out clears credentials and that account's profile/billing cache; it never reassigns local history to the next account.

## 5. Data access in v1 (existing CLI verbs)

Read-only. Overall credits and per-day figures from `activity --from --to --limit` (days with no usage are omitted; never truncated for a month). Per-chat rows for the month from `usage-top --from <start> --to <end> --by credits --limit 100`, mapped to a repo via `sessions --from <start> --limit 100` (repository, cwd, git root) and aggregated by repo **in memory**. When a month has more than 100 chats or rows are `truncated`, split the window (for example by week) and sum; set `flags.capped` if still partial. Per-model figures: from `usage <id> --by-turn`'s `by_model` for the chats of the month, done **on demand** when the details window opens that view (progress shown), until the toolkit verb below exists.
A proposed toolkit work item, evaluated during Phase 3 of `PHASE_PLAN.md`, adds one verb returning credits and tokens grouped by day, model, repo or chat for a window in a single call. Prefer it when advertised; allow a legacy-verb fallback only after its correctness is proven.

The legacy-verb strategy above is unverified. The POC must prove request-window semantics, inclusion of sessions started before the window, boundary-safe splitting, and behavior beyond 100 rows. Independent per-day/per-model/per-repo sums are insufficient for combined filters: require jointly attributable data or server-side filter support. Partial results are flagged, not presented as complete. Filtered views reconcile to the filtered subset, not necessarily the full month.

**Privacy at the boundary:** several CLI answers contain chat titles or summaries (for example the per-chat ranking has a `title`). The adapter must drop every title, summary and message text immediately; none may appear in any DTO, log, cache, test fixture or exception message. A unit test serializes everything the adapter produces and fails if a title-like field is present. Session ids are used only inside the adapter and Core and are not exposed in DTOs in v1.

**Cost reference (measured):** one CLI call takes about 0.2 to 0.4 s wall and 0.15 to 0.3 s CPU, mostly process start. A full refresh (activity + usage-top + sessions) is about 0.8 to 1.0 s wall. Therefore refresh is event-driven and throttled, never a tight timer. Hard rules: never open `session-store.db`, `events.jsonl` or `workspace.yaml` directly, never run SQL, never read chat text.

## 6. Refresh policy

- Change signal: `FileSystemWatcher` on `%LOCALAPPDATA%\Microsoft\VisualStudio\CopilotCli`, filter `session-store.db*`, LastWrite and Size. The main `.db` timestamp lags; updates land in `session-store.db-wal`, so the WAL must be watched.
- Debounce 1.5 s of quiet, at most one local refresh per 5 s, one refresh at a time; a slow safety check recovers missed watcher events. Final safety-check cost/frequency is measured in the POC.
- Local watching/tracking continues as long as the tray runs, regardless of panel visibility. Pause updates suspends automatic local and GitHub refreshes; retain cached values, mark them paused, and use a paused tray icon. Resume must catch changes missed while paused. Explicit manual refresh remains a deliberate user action.
- GitHub account sync: on successful sign-in/startup when authorized, then every 5 minutes during local activity or 15 minutes while idle. Proposed activity window: a database change in the preceding 5 minutes, to be verified in the POC. Schedule from the last successful sync; new database events must not continually postpone an already due refresh. Manual refresh has a cooldown and shares a single-flight request across clients. Failures use backoff and honor server rate limits; do not promise billing reports are real-time.
- Both the standalone details window and expanded VS panel show last account sync and next scheduled timestamp/countdown; replace the countdown with the actual paused/offline/rate-limited state when applicable. Countdown rendering itself performs no network calls.
- Pushes to panels are coalesced to at most one per 2 s.
- Several Visual Studio instances follow these rules independently; the host (one process) does the reading, so the cost does not multiply.
- Deferred seams (do not build yet): incremental windowed recompute (settled total older than 10 minutes plus a recent window), direct database reader, long-running helper process.

## 7. Protocol v1 (named pipe, JSON lines, UTF-8)

Pipe name `CopilotUsageMeter.v1.<current user SID>`, access limited to the current user, no network. Every message has `type` and `protocol`; snapshots and updates have `seq`.

- `Hello {clientKind, clientVersion, processId}` / `HelloAck {hostVersion, protocol}`
- `Subscribe {solutionPath?}` -> `Snapshot {snapshot}`; then `Update {snapshot}` when figures change
- `Visibility {visible}`
- `Query {queryId, range, groupBy: day|model|repo|tokens, filter {repoKeys?, models?}}` -> `QueryResult {queryId, rows[], note?}` (used by the details window and later clients)
- `Error {code, message}` (`version_mismatch`, `history_tool_missing`, `history_unreadable`, `busy`)
- `Bye`

Rules: unknown fields are ignored; additions are optional fields; a major protocol mismatch ends with `Error {version_mismatch}` and a readable message naming which side to update; a client that sees a gap in `seq` asks for a fresh snapshot; if the host restarts the client reconnects, resends Hello and Subscribe and receives a new Snapshot. Reserved but unused in v1: chat-level messages. If Visual Studio runs elevated and the tray does not, the pipe must still accept it (test this).

Named pipes, not WCF, are the local transport. Phase 4 implements real separate account/local payloads, refresh/pause/status/account commands, per-connection ephemeral active-chat/repository context, delivery sequencing, resynchronization, bounded frames and cancellation; the implemented wire contract is `PROTOCOL.md`. The legacy demo timer/settings DTO remains only for compatibility/fixtures, not production scheduling. Same-user elevated-client/unelevated-host interoperability was verified during Phase 2. Intentional coalescing may skip host revisions, never delivery sequences. The legacy message outline above is not the production contract; no solution path or chat identifier appears in a snapshot.

## 8. User interface

### 8.1 Tray icon
- A round progress ring: GitHub-reported account allowance consumed by default. Local-budget mode, if offered, is an explicit separately labeled selection, never an automatic account-data fallback. **No digits.** Blue normally, amber from 80 percent, red from 100 percent with a small badge, so colour is never the only signal. Grey/slashed when unavailable, a distinct paused indication when updates are paused, dashed when stale; optional short refresh sweep only. No continuous animation.
- Drawn in code at the current system tray icon size (16, 20, 24, 32 px for 100, 125, 150, 200 percent), light or dark taskbar aware (listen for user preference changes), high-contrast aware. Never pre-made per percentage. Small cache of the last icons keyed by (percent, state, size, theme); every replaced icon handle is destroyed (`DestroyIcon`) to avoid GDI leaks. Only the program's own icon files (exe, shortcut) are static.
- When no allowance is set, the ring shows only the connection state in grey.

### 8.2 Tooltip, flyout, menu
- **Tooltip** (plain text, at most 127 characters), for example: `GitHub: 1,183 / 1,500 credits (79%) · Local repo: 659`. Updated when numbers change. Scope and paused/stale/unavailable state take priority over extra detail. A rich hover card is a later extra.
- **Left-click flyout** next to the tray: the two-layer bar, token line (input / cached / output / reasoning), top repos, last days' bars.
- **Right-click menu:** header (month total against allowance, today and the last day with data), **Open details** (default action, also on double-click), Refresh now, Pause updates, Ring shows (submenu, default Overall), Set monthly allowance…, Settings…, Start with Windows (checkable), Copy diagnostics, a footer (history tool version, tray version, data as of), Exit.
- Optional balloon notification at 80 percent and 100 percent, once each per month, off by default.

### 8.3 WPF details window (hosted in the tray process)
Created on open, actually closed and released on close; single instance; remembers size and position; follows the Windows light or dark theme; keyboard accessible. Built on Panel.Core view models. No third-party charting library (a few simple custom controls). GitHub account and local analytics are separate labeled sections with independent freshness; local repo totals are not nested inside an account-total bar.
- **Summary:** month against allowance, two-layer bar, daily bar chart, projection ("at this rate about X by month end"), token breakdown.
- **By repo:** repo display name, requests, credits, share of month, tokens.
- **By day:** credits and tokens per UTC day.
- **By model:** credits, requests, tokens per model (computed on demand).
- **Filters:** month picker (earlier months too) or date range, repo multi-select, model multi-select, minimum credits, search by repo name; a level switch (overall or repo) and drill-down (click a repo to see its days and models).
- **Export view** writes a CSV of the numbers shown (numbers and repo display names only).
- Footer: "this machine only", data as of, history tool status.

### 8.4 Visual Studio panel
- Desired placement is an expandable/collapsible section immediately above Copilot Chat's Total changes expander. Prove the integration mechanism in the experimental instance before proceeding; internal WPF injection may be fragile across VS updates. A separate tool window is an explicitly discussed fallback, not a silent replacement.
- Collapsed header: `› Copilot Usage  79% | This Repo 659 credits | ▂▅▃▁▇▄▂█  Last 2.5 credits`. The percentage is GitHub-reported allowance consumed; repo credits are local for the selected month; bars/latest value are local to the current chat. Illustrative numbers only; unavailable data is not zero. Adapt to narrow widths without implying scopes reconcile.
- Keep at most 20 turn bars, oldest left/newest right, with a shared visible-window scale and newest-turn highlight. Update an in-progress turn's existing bar instead of appending duplicates. Each bar exposes exact credits and time on hover/keyboard focus **even when collapsed**; interacting with a bar must not toggle the expander. No chat title/text is needed.
- Expanded content: separate GitHub account totals/remaining credits, manual GitHub refresh, last/next sync countdown; local overall/repo/token analytics with local freshness; current-chat chart explanation; Settings and Open details actions. Do not duplicate the header chart. Persist expansion state. Any nested overall/repo bars use local data for both layers.
- If allowance is unavailable, show an explicit status or clearly labeled manual fallback, not a fabricated percentage.
- States: **Connected** (normal); **Connecting** (last numbers dimmed); **Tray not running** (last numbers dimmed, error text, Start button, automatic retry with backoff); **Tray not installed** (empty dimmed bar, install instructions); **Version mismatch** (dimmed, names which side to update); **History unreadable** (numbers marked stale, reason in tooltip). The panel never reads the history itself unless built with `InProcUsageClient`.
- Visibility can reduce UI delivery/rendering, not background tracking. On start, restore or show, request the latest cached snapshot; GitHub calls remain governed by the shared refresh policy.
- Phase 4 context: resolve the solution Git origin locally, send only a supported public owner/repo key, and match the toolkit's recorded repository group exactly. This is not canonical local-path proof. The approved opaque visible-session ID is sent only in same-user pipe context requests, held in memory per connection and never logged/persisted/exported/uploaded or included in snapshots. Missing identity/source data is explicit.

## 9. Settings, install and layout

- **Tray install:** per user, no administrator rights: folder `%LOCALAPPDATA%\Programs\CopilotUsageMeter` (program, `tools\chathistory`), Start menu shortcut, optional per-user "start with Windows" entry (settings toggle). First run writes `%LOCALAPPDATA%\CopilotUsageMeter\location.json` with its install path, which the panel reads for its Start button. Single instance via a named mutex (a second launch brings up the first). First-run message explains pinning the icon (Windows 11 hides new tray icons).
- **Settings file** `%LOCALAPPDATA%\CopilotUsageMeter\settings.json`: optional labeled manual allowance, thresholds, explicit ring source, `chathistory.exe` path override, refresh tuning, start with Windows, notifications. Validated with defaults, no default allowance or secrets. Other writes are location metadata, scope-separated small caches, secure credential storage and user-requested exports. Never cache chat titles/text.
- **Extension:** a separate `.vsix`. Versions may drift; the protocol check handles it.
- **Authentication:** first launch explains access and offers browser sign-in or Skip for now. Settings and tray menu always expose sign-in/reconnect and logout/account switching. Store secrets in Windows Credential Manager, not settings/source/logs. Successful authentication does not imply billing authorization; verify a supported auth flow and total allowance/consumption retrieval during the POC.
- First installation of an unsigned extension or program may need company approval; phase 9 documents it.

## 10. Privacy and security

Numbers/times and explicitly requested GitHub profile data only. Local titles/message text are stripped at the adapter boundary, never logged/cached/exported/uploaded, and must be tested when the adapter exists. Authenticated HTTPS is for GitHub profile/account reports only; no telemetry or local history upload. Keep credentials secure and the pipe private to the current user. The history store is only read through the CLI. Scope-specific failures remain visible. Architecture tests in Phase 1 are not a substitute for future adapter privacy tests.

## 11. Rules of work (every phase)

- Small steps, one file at a time, no giant single writes. Build and test as you go. Run the full suite once after your last edit of the phase and do not repeat green runs. A focus-dependent UI test that fails is rerun alone once; report both results.
- No automatic commits or pushes; wait for explicit user approval. Work in `C:\MyFiles\Git\CopilotUsageMeter`; toolkit repository changes require a separately approved scope.
- Never use the user's keyboard or mouse in any other application. Never stop a process you did not start. The Visual Studio extension is tested only in the experimental instance (`devenv.exe /rootsuffix Exp`); never install into the main instance, never close the user's Visual Studio windows.
- Runtime network access is limited to the approved GitHub integration; never upload local chat history. Development package restores and approved toolkit retrieval are separate from runtime behavior. No telemetry or chat text in code/logs/caches/tests/fixtures; use invented numbers and ids.
- Review is a separate step: a read-only prompt afterwards ("do not edit, build or run tests; ranked real findings only, at most N").
- Keep `docs\ARCHITECTURE.md` (this brief, updated) and `docs\DECISIONS.md` (one record per decision: what, why, what was given up, when to revisit) in the repository; open questions at the bottom of ARCHITECTURE.md.
- Final report of a phase, plain text: files created or changed, test numbers, what was verified live, what could not be verified, decisions the brief did not cover.

## 12. Testing strategy

Fakes for reader, clock and change signal make Core, the debounce and throttle, the visibility state machine, the cache rules and the projection deterministic. Contract tests use small sample JSON files written by hand in the CLI's shape. Architecture tests enforce the dependency rules and the no-titles rule. Pipe tests use real pipes in-process. UI tests that need focus carry a trait and are kept few. Manual checks (tray icon at several DPI scales, light and dark taskbar, details window, Visual Studio experimental instance) are listed in each phase's report.

view-model tests (58 total)

## 13. Deferred items and open questions

Deferred: chat titles/text; broad per-chat browsing beyond the current-chat turn chart; incremental windowed recompute; direct database reader; dollar estimates; richer hover cards; notifications beyond the two thresholds; other IDE panels. GitHub account access, active-chat identification and chat embedding are now **Phase 3 POC gates**, not deferred features. Supported authorization and billing permissions must be established experimentally; no universal subscription/allowance API claim.

Open questions to report, not resolve silently: (1) whether the local `credits` equals GitHub's billed credits (checked only against chat footers); (2) company policy for installing an unsigned extension and a per-user program; (3) the true allowance and reset day (assumed calendar month, UTC); (4) whether `usage-breakdown` should live in the toolkit permanently; (5) whether the Visual Studio build in use keeps tool-window visibility notifications stable across preview updates.

## 14. Glossary
Credit: Copilot's cost unit as recorded locally (sum of request multipliers). Tokens processed: summed prompt tokens including re-sent context. Allowance: the user's monthly credit budget (manual). Host: the process that reads the history and serves clients (the tray). Panel: the small UI inside Visual Studio. WAL: the SQLite write-ahead log file next to the store, where updates land first.
