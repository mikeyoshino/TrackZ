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

## Fix Round 2

- **RED:** lifecycle test showed a persisted row could omit all-time best; PostgreSQL test showed null-valued comparisons could bypass a check constraint.
- **GREEN:** every projection row now requires UTC last-performed time plus coherent LAST and all-time-best sets. Tracking mode is restricted to published values and participates in a composite foreign key to the exercise definition, preventing mode mismatch or changing an exercise mode while performance exists.
- **Cursor contract:** endpoint-level validation converts malformed/tampered/version-incompatible cursors into localized `10009` validation ProblemDetails with a `cursor` field error, trace ID, and no internals.
- **Commands:** `dotnet test ...ExercisePerformanceTests` (11 passed); `dotnet test ...Invalid_cursor_returns_thai` (1 passed); `dotnet ef migrations has-pending-model-changes` (clean).
- **Acceptance regression matrix:** signed payload/signature corruption and valid-HMAC unsupported version return `10009`; nine-row traversal (seven equal names) with page size two has no duplicate/skip; PostgreSQL rejects invalid enum and composite-FK mode mismatch; image-seeded catalog projection remains one SELECT and never exposes storage keys.
- **Focused results:** API cursor matrix 3 passed; infrastructure catalog persistence 3 passed; application catalog projection 2 passed.
- **Final API acceptance:** full `ListExercisesEndpointTests` was run after adding archive/ownership visibility, weighted/bodyweight/assisted/no-performance JSON-shape, and Draft/Published/private image nonleak coverage. Every returned `thumbnailUrl` is null and all seeded object-key tokens are absent from the raw body.

## Fix Round 3

- **Sensitivity correction:** each invalid mode shape is now exercised twice: once as LAST with a valid PR, and once as PR with a valid LAST. This reaches `ValidateSet` rather than the projection-completeness guard.
- **Response correction:** Bodyweight and Assisted `allTimeBest` JSON fields now have independent null/value/reps assertions.
- **Results:** `ExercisePerformanceTests`: 11 passed; full `ListExercisesEndpointTests` run after assertions were added.
