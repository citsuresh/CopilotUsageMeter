# Memory design decisions

Product and implementation decisions are recorded in the existing append-only `DECISIONS.md`; read it before revisiting scope, hosting, data separation, refresh, UI placement, toolkit integration or the solution/toolchain. Do not copy its history into this file or rewrite prior entries.

## 2026-11-10 - Memory complements the phase plan

- Decision: `PHASE_PLAN.md` stays the authoritative verified checklist; README summarizes capabilities/build instructions; `PROJECT_STATE.md` is the disposable session snapshot; this memory set provides concise architecture/handoff pointers. `DECISIONS.md` retains authoritative product decision history.
- Rationale: adding project memory should not create a second conflicting roadmap or reset completed Phase 1 work. The initial solution graph is a structural aid, not proof of runtime/accounting feasibility.
- Alternatives considered: replace the established plan/decision records or duplicate their contents; rejected to avoid drift.
## 2026-11-12 - One presentation model for both UIs

- Decision: `Panel.Core\UsageDisplay` is the single source of text, thresholds and segment arithmetic; the VSIX row/popup and the tray Full details window only draw it. Both redraw only when a computed signature changes, so the 2-second tick and 1-second countdown do not drop focus or tooltips.
- Rationale: two UIs formatting independently drifted before (raw wording, inconsistent states). UI types stay out of the shared library, so the drawing helpers are duplicated per UI rather than shared.
- Alternatives rejected: a host-problem protocol field (a down host cannot send it), a new OpenDetails command (OpenStatus already opens the window), real-time bar rebuilds (flicker, focus loss).

## 2026-11-13 - Loading indication is client-side and best-effort

- Decision: the row/popup shows a thin indeterminate bar and dims while loading. Loading starts on a manual Refresh (until the tray acks) or on an active-chat change (until the snapshot RefreshCount rises above the value seen at the switch, paused, or 12 s). The Full details window shows a bar while any command is pending. Hover tooltips appear after 150 ms; the Pause button sits in the Full details header.
- Rationale: the tray debounces (1.5 s) and throttles (5 s) local reads, so a switched chat briefly shows the previous data. Snapshots carry no session id (privacy), so the client cannot tell whose data it holds and uses RefreshCount as the baseline.
- Alternatives rejected: a protocol field for "loading" (the tray cannot know what the client switched to without storing more), blocking the UI until data arrives.

## 2026-11-11 - Bar sizing rules and package starts in every instance

- Decision: bars scale against the chat's largest turn with a square-root height curve and a 4 px minimum, so small turns stay visible next to a large one; slot width adapts to the row (6-16 px), bars are rounded with a 1 px gap (0.5 px when slots are tight). The VSIX package no longer returns early outside the Exp instance, so the row and Tools commands start in every VS instance. The extension version is bumped by hand in the manifest on each redeploy.
- Rationale: linear scaling made turns of a few credits nearly invisible; the Exp-only gate meant the main VS never injected the row and its menu commands did nothing.
- Known limits: only VS 18.13 Insiders has been tested (manifest floor is 17.0); the four Tools test commands must be removed before publishing (Phase 5). Build-time deploy to Exp was observed not to register the extension; installing with VSIXInstaller /rootSuffix:Exp is the reliable route (see README).

## 2026-11-11 - Full details window takes the foreground
- `ForegroundWindow.Bring` (Tray) attaches to the foreground thread, calls `SetForegroundWindow`, falls back to a synthetic F24 key press and finally a brief topmost flip. A plain `Activate()` only flashed the taskbar when the request came from Visual Studio, because Windows blocks background processes from taking focus. Not verified live yet (needs a tray restart).
