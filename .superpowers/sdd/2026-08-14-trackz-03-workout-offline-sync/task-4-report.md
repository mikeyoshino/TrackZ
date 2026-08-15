# Task 4 Report: Idempotent Sync Push

## Status

Implemented Task 4 only. The server now accepts ordered batches of the Task 3 workout outbox operations, processes each operation in its own transaction, records terminal results under `(UserId, OperationId)`, and replays an immutable prior result without duplicating mutations.

Task 5 sync-pull, tombstone, mobile-coordinator, and conflict-resolution work was not started.

## Wire contract

The public request follows the plan interface:

- `SyncPushRequest(Operations)`
- `SyncOperationDto(OperationId, EntityType, Action, Payload, BaseVersion)`

The entity ID remains in the exact Task 3 payload as `workoutId`; no second payload shape was introduced. Supported operation contracts are case-sensitive:

- `EntityType = "Workout"`, `Action = "StartWorkout"`
  - Payload: `workoutId`, `startedAt`, and ordered `exercises`
  - Exercise: `workoutExerciseId`, `exerciseDefinitionId`, numeric `trackingMode`, and zero-based `order`
- `EntityType = "Workout"`, `Action = "SaveSet"`
  - Payload: `workoutId`, `workoutExerciseId`, `setId`, zero-based `order`, string `weightKg`, string `assistedKg`, `reps`, and `completedAt`

Required-field presence is checked before typed deserialization. Null batch entries, missing/undefined payloads, null exercise entries, empty operation IDs, omitted causal versions/orders, malformed payloads, and unknown actions return per-operation `Rejected` results without aborting later entries.

Responses contain one result per input operation, in input order, with status `Applied`, `Rejected`, `Conflict`, or `Retryable`. Conflicts use `VersionConflict = 60001` and capture the server version at the time of the conflict; replay returns that same stored version even after later mutations.

## Transaction and idempotency behavior

`PushSyncHandler` starts one database transaction per operation and uses typed MediatR commands for `StartWorkout` and `SaveSet`.

For a valid operation ID it:

1. Acquires a user-and-operation PostgreSQL transaction advisory lock.
2. Reads `ProcessedClientOperation` by composite key `(UserId, OperationId)`.
3. Replays the stored JSON result only when the immutable request fingerprint matches.
4. Fails closed with `Rejected` when the same ID is reused with an altered entity type, action, payload, or base version.
5. Acquires aggregate/entity advisory locks, applies exact optimistic-version checks, persists the aggregate and terminal processed row together, and commits.

Workout, workout-exercise, and set identifiers are locked and checked before insertion. Cross-owner/global-ID collisions therefore become generic permanent rejections instead of key-violation 500 responses or owner-data disclosures.

Expected domain/shape failures are terminal `Rejected` results. Optimistic mismatches are terminal `Conflict` results. Npgsql/timeout failures classified as transient are returned as `Retryable`; their transaction rolls back, their processed row is absent, tracking state is cleared, and later batch entries continue. Unexpected non-transient infrastructure exceptions remain handled by the existing generic middleware and do not expose database or exception details.

## Persistence and architecture

Added the `ProcessedClientOperation` domain entity with:

- Composite primary key `(UserId, OperationId)`
- SHA-256 immutable-request fingerprint
- Full serialized result in PostgreSQL `jsonb`
- UTC processed timestamp
- Restricted user foreign key

Migration `20260815171532_AddProcessedClientOperations` is appended after `20260815161433_HardenWorkoutPersistence`; historical migrations were not rewritten. Tests cover Up/Down chronology, latest target-model/snapshot parity, no pending migrations/model changes, and the older hardening migration remaining historical.

Application accesses `AppDbContext` through the framework-neutral `ISyncPushStore` port. Architecture guards confirm Application exposes no EF Core or Npgsql types and retains no Infrastructure/API dependencies.

## TDD evidence

Genuine RED states captured before their production fixes included:

- Authenticated sync push returned `404 NotFound` before endpoint registration.
- Duplicate/global stable child IDs escaped as `500 InternalServerError` before collision locks/checks.
- Null/missing payload entries returned `500` and aborted the batch.
- Empty operation ID applied a workout instead of rejecting it.
- Omitted required numeric fields defaulted to valid zero values and applied.
- Omitted required nullable Task 3 payload fields defaulted silently and applied.
- The initial new migration used `length(jsonb)` and failed against real PostgreSQL; the model constraint was corrected to `jsonb_typeof(...) = 'object'` and the uncommitted generated migration was regenerated.

Each case was followed by a focused GREEN run. Independent code review completed after the fixes with no remaining Critical or Important findings.

## Fresh verification

All commands ran sequentially with one MSBuild node and no emulator:

- `dotnet test tests/TrackZ.Api.Tests --filter SyncPush -m:1 --no-restore`
  - Passed 13/13, including real PostgreSQL/API duplicate replay, concurrent delivery, immutable-request mismatch, ordered partial batches, stable conflicts, auth/owner isolation, global child-ID collisions, and transaction rollback via SQLSTATE `40001`.
- `dotnet test tests/TrackZ.Domain.Tests -m:1 --no-restore`
  - Passed 126/126.
- `dotnet test tests/TrackZ.Application.Tests -m:1 --no-restore`
  - Passed 61/61, including architecture guards.
- `dotnet test tests/TrackZ.Infrastructure.Tests -m:1 --no-restore`
  - Passed 106/106, including migration target/snapshot and Up/Down chronology.
- `dotnet build src/TrackZ.Api/TrackZ.Api.csproj -m:1 --no-restore`
  - Succeeded with 0 warnings and 0 errors.
- `git diff --check`
  - Clean.

The full API regression run completed 190/192. Its two failures are unchanged-from-base message-text expectations in `BusinessExceptionMiddlewareTests` and `UnhandledExceptionMiddlewareTests`: the tests expect `"Workout session was not found."` while the unchanged resource returns `"The workout was not found."`. Task 4 files and behavior are not involved; the focused Task 4 API acceptance suite is fully green.

## Concerns and follow-up

- The two unchanged-from-base API localization expectation failures remain outside Task 4 scope.
- A solution-wide build was attempted once early and queried MAUI/iOS simulator services before failing on unavailable platform assets. Verification was then restricted to server/test projects as required; no emulator was launched.
- Future Task 5 code should serialize the existing Task 3 outbox wrapper into the five-field public sync operation contract without changing the typed payload JSON documented above.
