# CopilotUsageMeter

Planned Windows tray app and compact Visual Studio usage expander. GitHub-reported account allowance/consumption is separate from locally recorded repository, token and current-chat analytics. Local history is never uploaded. Authentication, billing availability and Copilot Chat embedding require feasibility proofs.

## Current state - Phase 2 baseline

Open `CopilotUsageMeter.slnx`. This is the only supported solution file; the old `.sln` has been removed. Reopen this file if an existing Visual Studio workspace still references the old solution path. The SLNX Debug build and all 23 tests pass. It contains ten production projects and one architecture-test project:

| Project | Target | Boundary |
| --- | --- | --- |
| Contracts | netstandard2.0 | Future source-separated DTOs/protocol |
| Core | netstandard2.0 | Future aggregation/scheduling, no infrastructure |
| Readers.ChatHistoryCli | netstandard2.0 | Local CLI boundary |
| Readers.GitHub | netstandard2.0 | Separate account/profile integration |
| Signals.FileWatcher | netstandard2.0 | Local change notifications |
| Transport.Pipes | netstandard2.0 | Named-pipe transport |
| Client | netstandard2.0 | Client abstraction |
| Panel.Core | netstandard2.0 | UI-independent view models |
| Tray | net10.0-windows | WPF host scaffold |
| Panel.Vsix | net48 | In-process VSIX package scaffold |
| Architecture.Tests | net10.0 | Dependency/compiled-type guards, including negative guard tests |

Set **CopilotUsageMeter.Tray** as the startup project if inspecting the scaffold. It opens a clearly labeled placeholder window: there is no tray icon, engine, pipe server, sign-in or history reading yet. Libraries are intentionally empty. The VSIX contains an empty package, not a panel. No extension is deployed by building.

## Phase progress

| Phase | Status |
| --- | --- |
| 1 - Solution and project skeleton | Complete: Debug build and VSIX packaging passed; 23 tests passed, zero failed/skipped; independent review found no Phase 1 issues |
| 2 - End-to-end skeleton | In progress: framework migration built and tested; engine/tray/pipes/VS client not implemented |
| 3 - Feasibility proofs and resource baseline | Not started |
| 4 - Tracking and compact UI | Not started |
| 5 - Details, packaging and final measurements | Not started |

The [phase checklist](docs/PHASE_PLAN.md#progress-checklist---source-of-truth) is the authoritative task-level progress record. Update it as verified work progresses, and synchronize this summary. Updating the README is the required final step of every phase; completion does not automatically start the next phase. No performance/battery measurements or extension runtime validation have been completed yet.

Tray scaffold smoke test (2026-11-10): launched the built executable, attached the VS debugger by PID, paused/resumed, verified the expected window title and responsiveness, then closed the window normally. The process exited and VS returned to design mode. Exception inspection was unavailable on the selected paused frame (`no-stack-frame`); no separate exception-inspection result is claimed. No VSIX was launched or installed.

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

SDK 10.0.403 was installed with explicit approval after verifying Microsoft's published SHA512 and installer signature. `global.json` selects 10.0.403 with `latestPatch` and prerelease disabled. The full SLNX Debug build produced a net48 VSIX without deployment; all 23 architecture tests passed on net10.0 (zero failed/skipped). Test Explorer returned no matching tests, so tests ran with the CLI command below. Tray launch/close on .NET 10 passed as recorded above; extension runtime validation remains pending. See [Microsoft's .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) and [modern VSIX targets](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio-extensibility/ship/create-vsix-with-project-sdk?view=visualstudio).

## Build and tests

Windows with Visual Studio's extension-development tooling is required for the full solution. The locally verified route is VS 2026 Insiders MSBuild 18.12.2 with stable SDK 10.0.403 pinned in `global.json`. Tray/tests require .NET 10; the in-process VSIX requires .NET Framework 4.8. Packaging and runtime feasibility still require later validation.

From the repository root in PowerShell:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe' '.\CopilotUsageMeter.slnx' -restore -m -p:Configuration=Debug -p:DeployExtension=false -p:VsixDeployOnDebug=false
dotnet test '.\tests\CopilotUsageMeter.Architecture.Tests\CopilotUsageMeter.Architecture.Tests.csproj' --no-build --no-restore
```

On other machines, use the corresponding VS installation's MSBuild path and the pinned SDK (or deliberately update the SDK pin). NuGet restore requires network access. Build output is ignored under each project's `bin`/`obj` folders.

VSIX output: `src\CopilotUsageMeter.Panel.Vsix\bin\Debug\net48\CopilotUsageMeter.Panel.Vsix.vsix`. Debug configuration uses `/RootSuffix Exp`; runtime testing is reserved for an experimental instance. Nothing has been installed into the main instance, and the extension has not yet been run. Never open/install the VSIX into the main instance as a shortcut.

## Continuing in a new chat

[Repository instructions](.github/copilot-instructions.md) direct Copilot to read this README and the active [phase checklist/plan](docs/PHASE_PLAN.md) at the start of a repository-related chat. Resume from verified checklist progress, not the archived original proposal. Do not automatically start the next phase; maintain the checklist as work progresses and update this README at each phase's final step. Keep these files with the repository so continuity does not depend on a particular chat session.

Project memory is summarized in `docs\CODE_SUMMARY.md` and `docs\PROJECT_STATE.md`; `docs\ROADMAP.md` points back to the authoritative plan. The solution graph registry/query instructions are in `.github\copilot-instructions.md`. Open `docs\graph-viewer.html` and select/drag in the slug-named graph/dependencies files; its automatic loader still expects legacy filenames. The initial graph contains metadata-reference warnings, so graph absence is not proof of absent source. Memory does not change Phase 1's scaffold-only status.

## Next

Phase 2 wires invented data from the tray through named pipes to the extension. Phase 3 proves account access, chat embedding, current-chat attribution and local accounting completeness, and measures initial resource costs. Phase 5 repeats measurements before shipping.

- [Architecture](docs/ARCHITECTURE.md)
- [Approved phase plan and archived original proposal](docs/PHASE_PLAN.md)
- [Decision records](docs/DECISIONS.md)

No automatic commits or pushes. No local chat text/titles in logs, fixtures, caches or outbound requests.
