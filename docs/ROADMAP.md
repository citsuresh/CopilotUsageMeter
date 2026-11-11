# Roadmap pointer

Edited deliberately when priorities change; not automatically overwritten each session. The detailed, maintained acceptance/progress checklist is `PHASE_PLAN.md`, not this file. No additional feature priorities are implied here.

## Near-term

- [x] Next increment approved; stable SDK 10.0.403 and .NET 10/net48 baseline applied and build/test verified. Independent increment review complete; its sole documentation finding corrected with user approval. See PHASE_PLAN.md for evidence.
- [x] Phase 2: real tray-hosted engine, invented data and named-pipe extension skeleton.
- [x] Phase 3: feasibility proofs and initial idle/busy resource measurements; complete with limits 2026-11-10 (user sign-off). Phase 4 awaits user direction.

## Following proven feasibility

- [ ] Phase 4: independent account/local tracking, schedules, compact expander and 20-turn chart.
- [ ] Phase 5: standalone details, packaging/hardening and final resource measurements.

## Future alternatives (not scheduled)

- GitHub App user token for account usage instead of the unsupported `copilot_internal/user` endpoint: needs organisation admin approval, untested. Revisit if the internal endpoint changes or is blocked.
- Real Windows toasts for the tray alerts (`Microsoft.Toolkit.Uwp.Notifications` with a `net10.0-windows10.0.19041.0` target, as in `KekaAttendenceChecker`), replacing `NotifyIcon` balloons: gives a proper app identity, display name and icon on notifications. Deferred 2026-11-11; the tray currently declares an AppUserModelID and HKCU display name/icon (`NotificationIdentity`) instead, and whether Windows honours that for balloons is unverified.

Do not treat the archived P0-P9 proposal