| Script | Purpose | How to call | Read-only or modifies | Date |
| --- | --- | --- | --- | --- |
| `scripts\agent\Get-ProcessElevation.ps1` | Verify exact Windows process token elevation for pipe interoperability tests | `& .\scripts\agent\Get-ProcessElevation.ps1 -TargetProcessId 123,456` | Read-only | 2026-11-10 |
| `scripts\agent\Start-DemoTrayUnelevated.ps1` | Launch one demo tray through the same-user desktop folder broker and verify its token | `& .\scripts\agent\Start-DemoTrayUnelevated.ps1 -ShowStatus -DesktopExplorerProcessId 123` (PID owning Shell_TrayWnd; optional path/wait overrides in header) | Starts a process/temporary owned folder window; no direct file modifications | 2026-11-10 |
