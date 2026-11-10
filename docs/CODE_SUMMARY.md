# Code summary

Copilot Usage Meter is a Windows tray host and planned Visual Studio usage expander. GitHub account usage and this-machine analytics remain separate; local chat history is never uploaded. Eight netstandard2.0 libraries now include demo DTOs, pipe framing/server and a reconnecting client; Core/readers/signals/Panel.Core and the net10.0-windows/net48 hosts remain scaffolds. The existing net10.0 test project contains architecture and protocol/lifecycle tests.

`PHASE_PLAN.md` is the authoritative progress/acceptance record. The approved Phase 2 baseline uses stable SDK 10.0.403 with prerelease disabled; full solution/VSIX build and 23 architecture tests passed after retargeting. Phase 2 behavior is not implemented. Open `CopilotUsageMeter.slnx`, the sole solution.

## Project dependencies

Arrows mean project references, not implemented runtime flows. Names omit `CopilotUsageMeter.`.

```mermaid
graph LR
  Core --> Contracts
  Readers.ChatHistoryCli --> Core
  Readers.ChatHistoryCli --> Contracts
  Readers.GitHub --> Core
  Readers.GitHub --> Contracts
  Signals.FileWatcher --> Core
  Transport.Pipes --> Contracts
  Client --> Core
  Client --> Contracts
  Client --> Transport.Pipes
  Panel.Core --> Client
  Panel.Core --> Contracts
  Tray --> Core
  Tray --> Contracts
  Tray --> Readers.ChatHistoryCli
  Tray --> Readers.GitHub
  Tray --> Signals.FileWatcher
  Tray --> Transport.Pipes
  Tray --> Client
  Tray --> Panel.Core
  Panel.Vsix --> Client
  Panel.Vsix --> Contracts
  Panel.Vsix --> Panel.Core
```

Architecture.Tests references all eight shared libraries and reads project/solution metadata. It is not referenced by production projects.

## Structural symbols

| Project | Symbol | File relative to repo root | Responsibility |
| --- | --- | --- | --- |
| Tray | `App` | `src/CopilotUsageMeter.Tray/App.xaml.cs` | WPF application entry; StartupUri opens the scaffold window |
| Tray | `MainWindow` | `src/CopilotUsageMeter.Tray/MainWindow.xaml.cs` | Initializes the explicitly labeled scaffold UI; not a tray icon/engine |
| Panel.Vsix | `UsagePackage` | `src/CopilotUsageMeter.Panel.Vsix/UsagePackage.cs` | Empty AsyncPackage registration; no panel/injection yet |
| Architecture.Tests | `DependencyRulesTests` | `tests/CopilotUsageMeter.Architecture.Tests/DependencyRulesTests.cs` | SLNX membership and source/compiled dependency guards, with negative controls |
| Contracts | `UsageSnapshot`, `ProtocolMessage`, `UsageProtocol` | `src/CopilotUsageMeter.Contracts/` | Separate demo account/local payloads and v1 envelopes; see PROTOCOL.md |
| Transport.Pipes | `PipeConnection`, `UsagePipeServer` | `src/CopilotUsageMeter.Transport.Pipes/` | Bounded framing, handshake, subscription, delivery sequencing and broadcast |
| Client | `IUsageClient`, `PipeUsageClient` | `src/CopilotUsageMeter.Client/` | Reconnect/backoff, resynchronization, command acknowledgement and stale cached snapshots |

Core, Readers.ChatHistoryCli, Readers.GitHub, Signals.FileWatcher and Panel.Core have no implemented types yet. Proposed production accounting/authentication types in ARCHITECTURE.md are not existing code. All 39 current tests passed via Test Explorer; extension runtime is not verified.

See `docs/KEY_FLOWS.md` for traced end-to-end call flows.

## Graph coverage

Registry and query command are in `.github/copilot-instructions.md`. Full graph rebuilt from commit `0aa1080`: 23 nodes, 93 edges across 11 projects, in 14.02 seconds. Builder warned about project references without matching metadata references for Contracts, Core and Transport.Pipes; structural queries may be incomplete. A missing graph result is not proof of absent source. Viewer loads slug-named files using file picker/drag-and-drop, not its legacy automatic filenames.