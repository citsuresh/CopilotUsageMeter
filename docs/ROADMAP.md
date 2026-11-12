# Roadmap pointer

Edited deliberately when priorities change; not automatically overwritten each session. The detailed, maintained acceptance/progress checklist is `PHASE_PLAN.md`, not this file. No additional feature priorities are implied here.

## Near-term

- [x] Next increment approved; stable SDK 10.0.403 and .NET 10/net48 baseline applied and build/test verified. Independent increment review complete; its sole documentation finding corrected with user approval. See PHASE_PLAN.md for evidence.
- [x] Phase 2: real tray-hosted engine, invented data and named-pipe extension skeleton.
- [x] Phase 3: feasibility proofs and initial idle/busy resource measurements; complete with limits 2026-11-10 (user sign-off). Phase 4 awaits user direction.

## Following proven feasibility

- [ ] Phase 4: independent account/local tracking, schedules, compact expander and 20-turn chart.
- [ ] Phase 5: standalone details, packaging/hardening and final resource measurements.

## Deferred theme and accessibility validation

Low priority, deferred by the user on 2026-11-12. These remaining checks are not current Phase 5 blockers; they are unverified, not passed. The verified Escape/focus behaviour and normal-state Dark row/popup readability remain recorded in `PHASE_PLAN.md`.

- [ ] Visual Studio Light and additional available colour/custom themes (for example Cool Breeze or Juicy Plum), including switching while the popup is open. Blue is unavailable in the current instance's theme selector; no theme installation is required.
- [ ] Tray Full details readability in Windows light/dark.
- [ ] Paused, amber/warning, red/over-limit, offline, disabled and "Update needed" states; hover/keyboard tooltips and focus visibility across these states.
- [ ] High-contrast rendering in both hosts; obtain permission before changing Windows-wide settings.
- [ ] DPI scaling; obtain permission before changing desktop-wide settings.
- [ ] Real screen-reader names and navigation, and keyboard-focus visibility during theme changes.

Restore the intended test environment when testing resumes/ends: the original Exp theme was Light and the Settings search was `theme` (last changed to `Color theme`). Neither is changed by this documentation update. No broad theme/contrast/accessibility compliance claim is supported yet.

## Future alternatives (not scheduled)

- GitHub App user token for account usage instead of the unsupported `copilot_internal/user` endpoint: needs organisation admin approval, untested. Revisit if the internal endpoint changes or is blocked.

Do not treat the archived P0-P9 proposal
