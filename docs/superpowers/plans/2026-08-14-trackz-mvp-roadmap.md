# TrackZ MVP Implementation Roadmap

The approved design is intentionally split into five executable plans. Execute them in order; each plan ends in a working, independently testable milestone.

1. [Foundation and Identity](./2026-08-14-trackz-01-foundation-identity.md) — repository, solution, Clean Architecture guardrails, PostgreSQL, ProblemDetails, registration/login/refresh.
2. [Exercise Catalog and Media](./2026-08-14-trackz-02-exercise-catalog-media.md) — system/custom exercises, LAST/PR query contract, image metadata/upload pipeline, initial catalog manifest.
3. [Workout and Offline Sync](./2026-08-14-trackz-03-workout-offline-sync.md) — workout aggregate, MAUI SQLite/outbox, push/pull, conflicts, history editing.
4. [Progress and Gamification](./2026-08-14-trackz-04-progress-gamification.md) — projections, XP ledger, levels, weekly streaks, badges, progress screens.
5. [MAUI Experience and Release Hardening](./2026-08-14-trackz-05-maui-experience-release.md) — native XAML screens, localization, motion/haptics, accessibility, security and release gates.

Current workspace facts:

- `.NET SDK 10.0.302` is installed.
- No .NET workloads are installed; MAUI workload installation is required during Plan 1 execution.
- Docker is installed; `psql` is not installed, so integration tests use containerized PostgreSQL.
- The workspace is not a Git repository. Plan 1 requests explicit authorization before `git init`; all later commit steps assume that authorization was granted.

