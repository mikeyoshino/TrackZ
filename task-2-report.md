# Task 2 — Exercise Catalog Persistence and List Query

## Delivered

- Added PostgreSQL mappings and migration for exercise definitions, images, and the user/exercise performance projection.
- Added an application-owned catalog read-store port, list query handler, opaque HMAC-protected cursor contract, and cursor page contract. Application code has no EF Core, Npgsql, or `IQueryable` dependency.
- Added authenticated `GET /api/v1/exercises` with stable localized validation errors, user-scoped visibility, exact body-part filtering, normalized search, deterministic `Name` + `Id` cursor pagination, and page-size validation.
- The infrastructure read store uses one SQL projection to return a summary, a single usable thumbnail reference, and the authenticated user's LAST/PR projection. It has no per-exercise reads.
- Weighted results expose only `weightKg`; assisted results expose only `assistedKg`; bodyweight results expose neither.

## Verification

- Application integration tests cover visibility, user-specific performance, and one SQL reader command for the catalog projection.
- API integration tests cover JWT authentication, ownership isolation, body-part filtering, normalized search, equal-name cursor pagination, invalid parameters/cursors, and the 50-item page-size limit.
- Infrastructure integration tests cover nullable image review state, ignored derived readiness, and migration/model parity.

## Deliberate Scope Boundary

`ExercisePerformance` is a persisted read projection only in this task. Workout completion/edit/delete recomputation remains for the workout plan; this task does not introduce workout mutation behavior or custom-exercise CRUD.
