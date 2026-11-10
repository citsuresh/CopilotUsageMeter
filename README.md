# CopilotUsageMeter

Windows tray host and experimental compact Visual Studio usage expander. GitHub-reported account allowance/consumption is separate from locally recorded repository, token and current-chat analytics. Local history is never uploaded.

## Current state - Phase 4 complete with verification limits

The normal tray now hosts real local tracking, optional GitHub account tracking and the same-user pipe server. The Debug solution/VSIX build and **286 automated tests** pass. Toolkit changes independently pass **467 tests**. Independent whole-phase review findings are resolved. Live Exp checks verified active-chat matching, per-turn charts, collapsed/expanded keyboard tooltips, all three placement tiers, reinjection, pause/manual refresh/resume and tray-loss/reconnect. Live sign-in was also verified in both directions (tray Connect -> Exp, and Exp panel sign-in -> tray) with one enterprise-managed seat, including real GitHub-reported credits in the tray Status. This is development verification on one VS/Copilot build, not a shipping/support claim. The interactive `gh auth login --web` browser step itself was not automated.

| Project | Target | Boundary |
| --- | --- | --- |
| Contracts | netstandard2.0 | Source-separated DTOs and request-only context protocol |
| Core | netstandard2.0 | Accounting, detached state, local/account schedulers; demo test fixture |
| Readers.ChatHistoryCli | netstandard2.0 | Local CLI boundary |
| Readers.GitHub | netstandard2.0 | Separate account/profile integration |
| Signals.FileWatcher | netstandard2.0 | Local change notifications |
| Transport.Pipes | netstandard2.0 | Bounded JSON-lines framing and pipe server |
| Client | netstandard2.0 | Reconnecting pipe client abstraction |
| Panel.Core | netstandard2.0 | UI-independent view models |
| Tray | net10.0-windows | Notification icon, readers/schedulers, protected pipe and status/account controls |
| Panel.Vsix | net48 | Exp-only compact chat integration and explicit placement fallbacks |
| Architecture.Tests | net10.0 | Architecture, accounting, adapters, scheduling, credentials, pipes and presentation |

Set **CopilotUsageMeter.Tray** as startup project. It starts a notification-area icon and background tracking without opening a window. Double-click the icon or choose **Status / Settings...**; `--show-status` opens it at launch. A second launch activates the existing host's status window. Closing status keeps tracking; tray **Exit** stops it. Pause suspends automatic local and account refreshes; manual refresh remains deliberate and available. Resume catches up.

Local reads use only the portable toolkit CLI, covering `session-store.db` on this machine: one UTC-month aggregate and distinct visible-chat reads. DB/WAL events use 1.5-second debounce, 5-second throttle and single flight; a 60-second metadata check recovers missed changes. Chat/month changes share the scheduler. No titles/text are read. Repository figures match an exact recorded GitHub origin `owner/repo` key; unsupported origins, unavailable context and capped/missing groups remain explicit. This is not proof of canonical local-path attribution.

GitHub sign-in is optional. In Status, choose **Open GitHub browser sign-in** (requires `gh`) and complete its flow; the tray connects automatically when `gh` exits successfully (this auto-connect after a real browser flow is not yet verified live). **Connect account** also works manually: leave the login blank to import the single `gh:github.com:<login>` entry (only credential target names are listed; with none or several it asks you to type the login), or enter a login (never a token). The Visual Studio panel's **Sign in / switch GitHub account** does the same blank-login connect itself, falling back to browser sign-in when no CLI login exists. Only the matching Windows Credential Manager entry is imported into the application's own secure entry. Plaintext storage and refreshable/legacy keyring formats are not imported. **Log out** removes this app's authorization, not GitHub CLI's login, and never changes local-history attribution. **Skip account** leaves local tracking available. The quota source is the **unsupported** `copilot_internal/user` endpoint; it may change or be blocked. Only premium-interaction fields are mapped; unfamiliar or partial billing formats are unavailable, never replaced with local credits. On the one enterprise-managed Copilot Business seat tested, sign-in, profile verification and quota all work (token-billed credits, e.g. consumed/allowance shown from the endpoint). Authorized sync is 5 minutes during DB activity in the preceding 5 minutes, otherwise 15 minutes; failures back off and honor server deadlines. Account switching between two accounts and other seat types remain unverified. See the [adapter contract](src/CopilotUsageMeter.Readers.GitHub/README.md).

In an explicitly deployed **experimental** VS instance, the compact panel attempts placement above **Total changes**, then at the top of Copilot Chat, then a clearly labeled status-bar fallback. Both embedded positions depend on unsupported internal WPF names. The header chart retains the newest 20 recorded turns, replacing streaming snapshots per index; exact local credits and last usage-event time are available on hover/keyboard focus, including while collapsed. Unknown timestamps remain unknown. Context is per pipe connection and ephemeral; it is never in snapshots/logs/settings/network requests. **Tools > Copilot Usage Meter** opens tray status; placement commands permit Exp-only fallback checks. Disconnected cached data is marked stale. There is no separate Windows service or WCF. See the [wire contract](docs/PROTOCOL.md).

## Phase progress

| Phase | Status |
| --- | --- |
| 1 - Solution and project skeleton | Complete: Debug build and VSIX packaging passed; 23 tests passed, zero failed/skipped; independent review found no Phase 1 issues |
| 2 - End-to-end skeleton | Complete: demo tray/engine, protected pipes and experimental VSIX verified live; independent review fixes and 58-test final verification passed |
| 3 - Feasibility proofs and resource baseline | Complete with limits (user sign-off 2026-11-10): toolkit submodule pinned at `12bbfeb` (adds `usage-breakdown`); local-accounting completeness, active-chat matching and per-turn accounting verified with limits; GitHub account usage only through an unsupported endpoint, accepted for now (supported billing API returns 404 for the tested enterprise-managed seat; a GitHub App is a noted future option); chat placement partly proven and fragile (layered design agreed); resource baseline partial (demo engine only, no battery claim). See the checklist |
| 4 - Tracking and compact UI | Complete with limits: real readers/schedules, tray/protocol and compact integration; 286 tests, Debug build/VSIX and independent review pass; Exp runtime checks pass; live sign-in and GitHub-reported credits verified on one enterprise seat; browser step, two-account switching and other seats unverified |
| 5 - Details, packaging and final measurements | Not started |

The [phase checklist](docs/PHASE_PLAN.md#progress-checklist---source-of-truth) is authoritative. Phase completion does not authorize the next phase. The Phase 3 resource baseline is partial; no low-battery-impact or universal subscription-support claim is made.

Phase 2 live checks (2026-11-10): real icon/menu and paused state; automatic/manual refresh and resume; settings persistence; status close/recreation and single-instance activation; normal tray Exit; cached-value retention and reconnect to a new host; two actual experimental VS instances sharing identical host/revision/count. One remote refresh advanced the shared count exactly once. Process-token inspection confirmed both VS clients elevated and the tray non-elevated; bidirectional pause/refresh commands succeeded. A fresh final VSIX/unelevated-tray pass also verified remote settings saved as 10 seconds, persistence across restart, default windowless startup, remote status creation, retained stale values and disabled disconnected commands. All task-owned tray/experimental VS processes were closed normally; originally absent test settings and named activity logs were removed. The demo extension remains deployed only to Exp.

Historical Phase 2 solution/VSIX verification and all 58 tests passed after the fixes. A later post-commit review also hardened Exit against an owner-cancelled in-flight pipe command (code-trace finding; not reproduced at runtime, so the added test is a shutdown smoke test, not a proven red/green regression). No unresolved audit findings remain.

Historical Phase 1 scaffold smoke test (2026-11-10): launched, attached debugger by PID, paused/resumed, confirmed responsiveness/title and closed normally. Exception inspection was unavailable (`no-stack-frame`). This was before the tray lifetime and VSIX integration existed.

Post-migration smoke test (2026-11-10): launched the net10.0-windows scaffold, verified the expected window title and responsiveness, confirmed the process loaded .NET 10.0.14 CoreCLR, then requested normal window close. The process exited with code 0. No debugger attach or VSIX runtime test was performed in this check.

## Framework baseline - applied 2026-11-10

The approved baseline replaces the original draft targets and temporary preview SDK pin:

| Component | Current target | Reason |
| --- | --- | --- |
| Tray | net10.0-windows | .NET 10 LTS is supported through 2028-11-14; .NET 8 reaches end of support on 2026-11-10 |
| Tests | net10.0 | Match the supported modern runtime baseline |
| In-process VSIX | net48 | Align with modern Visual Studio in-process extension templates; not .NET 10 inside the VS process |
| Shared libraries | netstandard2.0 | Remain consumable by both the modern tray and .NET Framework VSIX |
| Build SDK | 10.0.403, stable .NET 10 SDK | Pin the installed stable SDK; allow only stable patches within its feature band |

## Build and tests

Windows with Visual Studio's extension-development tooling is required for the full solution. The locally verified route is VS 2026 Insiders MSBuild 18.12.2 with stable SDK 10.0.403 pinned in `global.json`. Tray/tests require .NET 10; the in-process VSIX requires .NET Framework 4.8. Shipping packaging/support and real-data/chat feasibility require later validation.

From the repository root in PowerShell:

```powershell
dotnet restore '.\CopilotUsageMeter.slnx' --source https://api.nuget.org/v3/index.json
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe' '.\CopilotUsageMeter.slnx' -restore:false -m -p:Configuration=Debug -p:DeployExtension=false -p:VsixDeployOnDebug=false
dotnet test '.\tests\CopilotUsageMeter.Architecture.Tests\CopilotUsageMeter.Architecture.Tests.csproj' --no-build --no-restore
```

On other machines, use the corresponding VS installation's MSBuild path and the pinned SDK (or deliberately update the SDK pin). NuGet restore requires network access. Build output is ignored under each project's `bin`/`obj` folders.

Before building the tray, publish its portable local reader:

```powershell
git submodule update --init --recursive
powershell -NoProfile -File .\scripts\agent\Publish-ChatHistoryCli.ps1
```

The script publishes self-contained win-x64 into `tools\chathistory`; the tray build copies it beside the program. A missing tool produces explicit unavailable data. Current Phase 4 `usage-turns` changes exist uncommitted in both the toolkit source checkout and submodule working tree; the recorded submodule commit is still `12bbfeb`. Preserve those changes: a fresh checkout needs the reviewed toolkit change committed/pushed and the pin updated **after user approval**, before reproducible distribution. Do not silently depend on another developer checkout. Packaging remains Phase 5.

VSIX output: `src\CopilotUsageMeter.Panel.Vsix\bin\Debug\net48\CopilotUsageMeter.Panel.Vsix.vsix`. Ordinary builds do not deploy it. For development, close only your task-owned experimental instances, resolve the intended VS installation's instance ID with `vswhere -all -prerelease -format json`, then explicitly deploy to **Exp only**:

```powershell
$instanceId = '<the intended VS installation instanceId>'
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe' '.\src\CopilotUsageMeter.Panel.Vsix\CopilotUsageMeter.Panel.Vsix.csproj' '-t:CreateVsixContainer;DeployVsixExtensionFiles' -p:Configuration=Debug -p:DeployExtension=true -p:VsixDeployOnDebug=false -p:VSSDKTargetPlatformRegRootSuffix=Exp "-p:DeployTargetInstanceId=$instanceId"
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe' /RootSuffix Exp
```

The full build must succeed first. The standalone deploy target without the packaging target fails because `TargetVsixContainer` is not initialized. Never install/open the VSIX in the main instance or terminate unrelated VS processes. Experimental deployment/runtime verification does not prove Copilot Chat integration.

## Continuing in a new chat

[Repository instructions](.github/copilot-instructions.md) direct Copilot to read this README and the active [phase checklist/plan](docs/PHASE_PLAN.md) at the start of a repository-related chat. Resume from verified checklist progress, not the archived original proposal. Do not automatically start the next phase; maintain the checklist as work progresses and update this README at each phase's final step. Keep these files with the repository so continuity does not depend on a particular chat session.

Project memory is summarized in `docs\CODE_SUMMARY.md` and `docs\PROJECT_STATE.md`; `docs\ROADMAP.md` is a static priority pointer, not the current progress checklist. The graph registry/query instructions are in `.github\copilot-instructions.md`. Open `docs\graph-viewer.html` and select/drag in the slug-named graph/dependencies files; its automatic loader expects legacy filenames. Metadata-reference warnings mean graph absence is not proof of absent source.

## Next

Phase 4 is complete within the documented development limits. Stop for direction before Phase 5; details/filters/export, installation, broad accessibility/DPI/theme validation and final real-engine resource measurements remain there. After the phase review, a live sign-in session (user-approved) imported the existing `gh` credential for one enterprise-managed seat and exercised both tray-first and Exp-first sign-in. Toolkit commit/push and submodule-pin changes still require explicit approval.

- [Architecture](docs/ARCHITECTURE.md)
- [Approved phase plan and archived original proposal](docs/PHASE_PLAN.md)
- [Decision records](docs/DECISIONS.md)

No automatic commits or pushes. No local chat text/titles in logs, fixtures, caches or outbound requests.
