# Project state

This file is overwritten, not appended, at the end of each working session. The phase checklist in `PHASE_PLAN.md` remains authoritative.

## Current focus

2026-11-10: User authorized autonomous Phase 2 implementation, appropriate in-scope review fixes and local commits, not pushes. Baseline committed as `a395639`. Protocol/client library review's four findings fixed; all 39 tests pass. Next: mock engine/tray and explicit temporary VSIX test surface, then reliability verification/docs. Phase 1 remains complete; no Phase 3 authorization. Only `CopilotUsageMeter.slnx` exists.

## Verified

- Eleven-project SLNX Debug build and real non-deploying VSIX build succeeded; 23 architecture tests passed, zero failed/skipped. Negative control caught a real forbidden Core-to-transport reference, then was removed. Phase 1 review found no issues.
- Built WPF scaffold was launched, attached by PID to VS debugger, paused/resumed, confirmed responsive with expected title, and closed normally; process exited and debugger returned to design mode. Exception inspection was unavailable (`no-stack-frame`), not a verified exception check.
- No tray icon/engine, pipes, authentication/history reads or VS panel yet. No VSIX runtime deployment/testing. No performance/battery measurements.
- Stable SDK 10.0.403 installed with user approval and Microsoft hash/signature verification (successful retry after installer exit 1618); temporary installer removed. SDK selection verified, full SLNX Debug build produced a net48 VSIX with deployment disabled, and all 23 architecture tests passed on net10.0, zero failed/skipped. Test Explorer returned no matching tests; CLI execution passed. Post-migration Tray smoke test passed: expected title, responsive window, actual .NET 10.0.14 CoreCLR loaded, normal close and exit code 0. No debugger attach or VSIX runtime test in this check. Manual pending-diff review completed; no commit/push authorized.
- Memory graph rebuilt from current HEAD `0aa1080` during Bootstrap: 23 nodes, 93 edges across 11 projects, 14.02 seconds. The full graph build succeeded; existing Contracts/Core/Transport.Pipes metadata-reference warnings limit completeness. Graph registry uses slug `copilotusagemeter`. Prior Phase 1 solution build and test results remain recorded above.

## Open tasks / gates

- Protocol library review, then mock engine/tray: command dispatch, protected same-user endpoint and persistent demo settings remain to be composed. Full build plus Test Explorer: 35 passed, zero failed/skipped. Current targets: Tray net10.0-windows/tests net10.0/VSIX net48, shared netstandard2.0; stable SDK 10.0.403 pin with prerelease disabled.
- Phase 2: real tray and fake-data pipe/VS skeleton. Phase 3: account allowance/consumption authentication/logout, chat placement, active-chat turn attribution, local completeness and initial resource baseline. Stop for user decisions on failed gates. Phase 5 repeats resource measurements.
- Never conflate account/local units/scopes, upload local history, install into main VS, or automatically commit/push.

## Recently changed files

- Existing Bootstrap changes preserved: repository instructions, CODE_SUMMARY, PROJECT_STATE and graph/dependencies JSON (HEAD is local memory commit `0aa1080`; do not infer publication).
- Current increment: Contracts demo DTOs/envelopes, PipeConnection/UsagePipeServer, IUsageClient/PipeUsageClient, ProtocolTests, PROTOCOL.md and related README/checklist/memory summaries. No production account/history reads or VSIX deployment.
