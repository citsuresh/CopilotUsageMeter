# Memory design decisions

Product and implementation decisions are recorded in the existing append-only `DECISIONS.md`; read it before revisiting scope, hosting, data separation, refresh, UI placement, toolkit integration or the solution/toolchain. Do not copy its history into this file or rewrite prior entries.

## 2026-11-10 - Memory complements the phase plan

- Decision: `PHASE_PLAN.md` stays the authoritative verified checklist; README summarizes capabilities/build instructions; `PROJECT_STATE.md` is the disposable session snapshot; this memory set provides concise architecture/handoff pointers. `DECISIONS.md` retains authoritative product decision history.
- Rationale: adding project memory should not create a second conflicting roadmap or reset completed Phase 1 work. The initial solution graph is a structural aid, not proof of runtime/accounting feasibility.
- Alternatives considered: replace the established plan/decision records or duplicate their contents; rejected to avoid drift.