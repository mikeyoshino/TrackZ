# Task 2 — Exercise Catalog Persistence and List Query

## Delivered

- Added PostgreSQL mappings and migration for exercise definitions, images, and the user/exercise performance projection.
- Added an application-owned catalog read-store port, list query handler, opaque HMAC-protected cursor contract, and cursor page contract. Application code has no EF Core, Npgsql, or `IQueryable` dependency.
- Added authenticated `GET /api/v1/exercises` with stable localized validation errors, user-scoped visibility, exact body-part filtering, normalized search, deterministic `Name` + `Id` cursor pagination, and page-size validation.
- The infrastructure read store uses one SQL projection to return a summary and the authenticated user's LAST/PR projection. Object-storage keys are not API URLs; no thumbnail is exposed until Task 4 supplies secure URL resolution.
- Weighted results expose only `weightKg`; assisted results expose only `assistedKg`; bodyweight results expose neither.

## Fix Round 1

- **RED:** added canonical `TrackZ.Domain.Progress` tests for valid/invalid tracking-mode set shapes; they failed because the progress namespace/model did not exist.
- **GREEN:** moved `ExercisePerformance` to `Progress`, added durable `TrackingMode`, validated complete nullable set triples, and normalized `LastPerformedAt` to UTC. PostgreSQL check constraints mirror the same mode-aware shape rules.
- **Sensitivity evidence:** weighted sets with no load, mixed weight/assistance, non-positive loads/reps, loaded bodyweight sets, and mixed/zero assisted sets are rejected.
- **Thumbnail safety:** catalog responses now always return `thumbnailUrl: null` until Task 4 can resolve secure public/signed URLs; no object-storage key can cross the API boundary.
- **Page cap:** positive values above 50 are capped at 50; non-positive values remain localized validation errors.

## Deliberate Scope Boundary

`ExercisePerformance` is a persisted read projection only in this task. Workout completion/edit/delete recomputation remains for the workout plan; this task does not introduce workout mutation behavior or custom-exercise CRUD.
