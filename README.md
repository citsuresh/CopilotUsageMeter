# CopilotUsageMeter

Planned Windows tray app and compact Visual Studio usage expander. GitHub-reported account allowance/consumption is separate from locally recorded repository, token and current-chat analytics. Local history is never uploaded. Authentication, billing availability and Copilot Chat embedding require feasibility proofs.

## Current state - Phase 2 demo

all **58 tests pass**, zero failed/skipped: 23 architecture, 18 protocol/lifecycle,

| Project | Target | Boundary |
| --- | --- | --- |
| Contracts | netstandard2.0 | Source-separated demo DTOs/protocol |
| Core | netstandard2.0 | Deterministic invented-data engine; no infrastructure |
| Readers.ChatHistoryCli | netstandard2.0 | Local CLI boundary |
| Readers.GitHub | netstandard2.0 | Separate account/profile integration |
| Signals.FileWatcher | netstandard2.0 | Local change notifications |
| Transport.Pipes | netstandard2.0 | Bounded JSON-lines framing and pipe server |
| Client | netstandard2.0 | Reconnecting pipe client abstraction |
| Panel.Core | netstandard2.0 | UI-independent view models |
| Tray | net10.0-windows | Notification icon/menu, engine/pipe lifetime and WPF demo status/settings |
| Panel.Vsix | net48 | Explicit temporary transport-test tool window |
| Architecture.Tests | net10.0 | Architecture, real-pipe lifecycle, demo engine/settings and view-model tests |

Set **CopilotUsageMeter.Tray** as startup project. It starts a real notification-area icon and one background demo engine/pipe server, without opening a window. Double-click the icon or choose **Status / Settings...**; `--show-status` opens it at launch. A second launch activates the existing host's status window. Closing status keeps the engine running; the tray menu's **Exit** stops it. Pause suspends automatic demo updates; manual refresh remains available, and resume refreshes once.

Demo settings persist only the refresh interval (5-300 seconds, default 15) in `%LOCALAPPDATA%\CopilotUsageMeter\settings.json`. This timer is not the future independent GitHub/local refresh policy. Invalid settings are reported rather than silently replaced. See the [wire contract](docs/PROTOCOL.md).

In an explicitly deployed **experimental** VS instance, choose **Tools > Copilot Usage Meter - Phase 2 Demo**. This temporary tool window displays separate invented account/local values and freshness, refresh/pause/settings controls and a tray-status action. Disconnected values are retained, dimmed and labeled stale; commands are disabled until connected. This is **not Copilot Chat embedding** or an approved production fallback. Readers and file-watcher integration remain empty; there is no authentication, real usage/history reading, service or WCF.

## Phase progress

| Phase | Status |
| --- | --- |
| 1 - Solution and project skeleton | Complete: Debug build and VSIX packaging passed; 23 tests passed, zero failed/skipped; independent review found no Phase 1 issues |
| 2 - End-to-end skeleton | Complete: demo tray/engine, protected pipes and experimental VSIX verified live; independent review fixes and 58-test final verification passed |
| 3 - Feasibility proofs and resource baseline | Complete with limits (user sign-off 2026-11-10): toolkit submodule pinned at `12bbfeb` (adds `usage-breakdown`); local-accounting completeness, active-chat matching and per-turn accounting verified with limits; GitHub account usage only through an unsupported endpoint, accepted for now (supported billing API returns 404 for the tested enterprise-managed seat; a GitHub App is a noted future option); chat placement partly proven and fragile (layered design agreed); resource baseline partial (demo engine only, no battery claim). See the checklist |
| 4 - Tracking and compact UI | Not started |
| 5 - Details, packaging and final measurements | Not started |

The [phase checklist](docs/PHASE_PLAN.md#progress-checklist---source-of-truth) is authoritative. Phase completion does not authorize the next phase. No resource/battery measurements or production account/chat feasibility claims have been made.

Phase 2 live checks (2026-11-10): real icon/menu and paused state; automatic/manual refresh and resume; settings persistence; status close/recreation and single-instance activation; normal tray Exit; cached-value retention and reconnect to a new host; two actual experimental VS instances sharing identical host/revision/count. One remote refresh advanced the shared count exactly once. Process-token inspection confirmed both VS clients elevated and the tray non-elevated; bidirectional pause/refresh commands succeeded. A fresh final VSIX/unelevated-tray pass also verified remote settings saved as 10 seconds, persistence across restart, default windowless startup, remote status creation, retained stale values and disabled disconnected commands. All task-owned tray/experimental VS processes were closed normally; originally absent test settings and named activity logs were removed. The demo extension remains deployed only to Exp.

Full solution/VSIX verification and all 58 tests passed after the fixes. A later post-commit review also hardened Exit against an owner-cancelled in-flight pipe command (code-trace finding; not reproduced at runtime, so the added test is a shutdown smoke test, not a proven red/green regression). No unresolved audit findings remain.

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

complete 58-case suite

## Build and tests

Windows with Visual Studio's extension-development tooling is required for the full solution. The locally verified route is VS 2026 Insiders MSBuild 18.12.2 with stable SDK 10.0.403 pinned in `global.json`. Tray/tests require .NET 10; the in-process VSIX requires .NET Framework 4.8. Shipping packaging/support and real-data/chat feasibility require later validation.

From the repository root in PowerShell:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe' '.\CopilotUsageMeter.slnx' -restore -m -p:Configuration=Debug -p:DeployExtension=false -p:VsixDeployOnDebug=false
dotnet test '.\tests\CopilotUsageMeter.Architecture.Tests\CopilotUsageMeter.Architecture.Tests.csproj' --no-build --no-restore
```

On other machines, use the corresponding VS installation's MSBuild path and the pinned SDK (or deliberately update the SDK pin). NuGet restore requires network access. Build output is ignored under each project's `bin`/`obj` folders.

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

Await user direction. Phase 3 must prove supported account access, actual chat-expander placement, current-chat attribution and local accounting completeness, and measure initial resource costs. Phase 5 repeats measurements before shipping. None of those gates is satisfied by this invented-data demo.

- [Architecture](docs/ARCHITECTURE.md)
- [Approved phase plan and archived original proposal](docs/PHASE_PLAN.md)
- [Decision records](docs/DECISIONS.md)

No automatic commits or pushes. No local chat text/titles in logs, fixtures, caches or outbound requests.
