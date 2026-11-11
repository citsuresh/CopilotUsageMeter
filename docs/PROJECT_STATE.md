# Project state

## Current focus

Phases 1-3 are complete/pushed at `3532972`. Phase 4 is complete and committed locally (`f38151f`, not pushed). The approved post-Phase-4 UI rework (adaptive row + popup, Full details window, shared `UsageDisplay`) is implemented and locally verified but UNCOMMITTED (commit pending review and user approval); 330 tests pass and the Debug solution/VSIX build passes. Later live-feedback tweaks (black tick, smaller chevron, tooltips with hover highlight and 150 ms delay, Pause in header, shorter window, loading bars, refresh icons, adaptive/rounded/thicker bars with square-root heights, Exp-only gate removed, VSIX 0.1.5) are in the same uncommitted set; README screenshots are not yet regenerated. The user tests in Exp (installed with VSIXInstaller /rootSuffix:Exp) and in the main VS. Do not start Phase 5 or push. PHASE_PLAN remains authoritative.

## Implemented / verified

- Integrated real toolkit reader, DB/WAL signal/reconciliation and shared local debounce/throttle/single flight; optional secure gh account import and independent account scheduler; production tray, per-client request-only context protocol and compact injection/reinjection (the Exp-only gate was removed; it starts in every VS instance).
- Monthly account/local scopes remain separate. Recorded repository values match exact public origin keys, not canonical local-path proof. Session IDs remain ephemeral, never snapshot/log/settings/network data. PROTOCOL documents the implemented contract.
- Final Meter suite: 286 passed, zero failed/skipped; full CLI and IDE Debug solution/VSIX builds pass. Toolkit builds clean; 467 synthetic tests passed independently in the external checkout and submodule working tree.
- Independent Phase 4 review found three issues (context promotion, atomic pause, countdown metadata); all fixed and independently rechecked. Narrow follow-up found no significant issue in the UIA expansion and identity helper fixes. No live credentials/account requests were made.
- Live Exp verified active identity via the observed Session/Router bridges, real chart switches/streaming replacement/new-chat clearing, exact collapsed/expanded keyboard tooltip popups, one panel across view recreation, all three placement tiers and preferred recovery. Expansion follows Checked/Unchecked with a 240-DIP details limit; bar activation does not toggle it. Temporary metadata diagnostics were removed. Focused independent identity review is clean.
- Production tray pause/manual refresh while paused/resume passed. Deliberate owned-host loss showed client reconnecting; a rebuilt host reconnected the same Exp client and restored charts. This is one VS/Copilot build, not broad runtime support.
- Final graph/dependencies refreshed incrementally: 1,173 nodes / 4,186 edges in 21.02 seconds; paired output renamed immediately. Metadata-reference warnings and linked DemoSettingsStore query limitation persist. Both worktree diff checks pass.

## Workers / runtime

- External toolkit Copilot finished usage-turns and its audit fix. Approved changes transferred unchanged to the submodule working tree and published portably; recorded pin stays 12bbfeb. Both worktrees remain uncommitted; reproducible distribution needs explicit toolkit commit/push and submodule-pin approval later.
- Local/GitHub implementation agents and independent reviewer finished. Reuse known IDs only for same-task follow-up; no duplicate implementation agents.
- No task-owned runtime remains: final Exp PID 37248 closed normally; tray PIDs 31596/22832 stopped by exact PID for loss/reconnect verification and cleanup. Other owned Exps closed normally. User-owned VS instances were untouched; cursor stopped and the two named task-created toolkit artifacts removed.
- UI loader cached the moved backup path; canonical installed ui-interaction v17 was read directly. Existing UI CLI Release was rebuilt to match its current contract; navigation v5/debug v6 match source.

## Remaining / limitations

- UI rework independent review found two Medium issues (tray toolbar stayed hidden when signing out during the Log out confirm; popup lost keyboard focus/Esc after a rebuild); both fixed. Build clean, 317 tests pass after the fixes; the fixes were not re-driven live.
- UI rework verification gaps:
- README/checklist/memory synchronized; no Phase 5 work is authorized. Post-review sign-in fix (blank-login discovery, auto-connect after gh, panel SignIn connects directly) verified live in Exp both directions on one enterprise seat. Unverified: interactive gh browser step, two-account switching, other seats. Next: UI rework (replace tray fault message boxes with embedded errors; review screenshots in C:\MyFiles\Screenshots\CopilotUsageMeter-signin-tests) only when the user directs it. Nothing committed.
- Full DPI/theme/accessibility, details/export, shipping/auth-storage validation, installation and real-engine resource measurements remain Phase 5. Reproducible distribution also needs explicit toolkit commit/push and submodule-pin approval.
- NuGet private feeds return 401; restore from nuget.org, then MSBuild `/restore:false`. Historical unregistered demo-tool-window analyzer warnings remain.
- Reusable Publish-ChatHistoryCli and read-only Get-UiElementBounds helpers added. Publish syntax is valid; script itself was not executed (the underlying publish command was verified). Bounds helper was executed. No source/title/text data was uploaded.
- Editor recovery: delete/create failed to obtain a text view, leaving this path absent; create_file and edit_file also failed. With the user unavailable, the exact missing file was recreated once through a guarded filesystem fallback. Normal edits return to the editor tools.

## Changed surfaces

Contracts/Core/readers/signals/transport/client/tray/Panel.Core/VSIX and architecture tests; portable publish/UI-bounds helpers; README, PHASE_PLAN, PROTOCOL, ARCHITECTURE, DECISIONS, CODE_SUMMARY, KEY_FLOWS, SCRIPTS, this state file and registered graph pair. Approved toolkit source/tests remain dirty in the submodule. No staging, commits, pushes or pin changes.
- TODO after final testing: remove the Tools-menu test commands (Status demo window and the three Exp placement probes); recorded in PHASE_PLAN Phase 5. VSIX supported-VS range is untested (only 18.13 Insiders); first main-instance install failed while a VS upgrade was in progress.
- TODO: test the VSIX on earlier Visual Studio versions (2022, 2019, 2017 if possible); manifest floor is 17.0, so 2019/2017 need a decision. Logging: the extension only writes two ActivityLog warnings (visible with devenv /log); the tray writes no log file.
