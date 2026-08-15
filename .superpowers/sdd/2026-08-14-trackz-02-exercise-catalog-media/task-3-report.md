# Task 3 Report — Custom Exercise CRUD

## Scope and deferred media link

Implemented only Task 3 custom exercise CRUD. Create, update, and archive are authenticated MediatR commands behind the application-owned `ICustomExerciseStore` port. `TrackZ.Application` remains free of EF Core/Npgsql references.

The current aggregate has no safe association from a custom exercise to an independently-existing system image or private upload record. Task 4 owns trusted upload records. Therefore Task 3 deliberately rejects every non-null `libraryImageId` or `uploadedImageKey` with localized `10009` validation instead of persisting an unverified key or URL. No object key or URL is returned.

## RED evidence

1. `dotnet test tests/TrackZ.Application.Tests --filter FullyQualifiedName~CustomExerciseTests --no-restore`
   - Failed as intended before implementation with `CS0234`: `TrackZ.Application.Exercises.CreateCustom` did not exist.
2. The same focused command failed after the first green slice with `Assert.Throws() Failure: No exception was thrown` for the duplicate-name contract.
3. The focused application command then failed for malformed input: raw `ArgumentOutOfRangeException` escaped instead of stable `BusinessException(10009)`; it also accepted unverified image identifiers.
4. `dotnet test tests/TrackZ.Infrastructure.Tests --filter FullyQualifiedName~ExerciseCatalogPersistenceTests.Active_custom_names --no-restore`
   - Failed as intended: same-owner case-insensitive duplicate save did not throw `DbUpdateException` before the partial unique index migration.
5. `dotnet test tests/TrackZ.Api.Tests --filter FullyQualifiedName~Custom_ --no-restore`
   - Failed as intended before endpoint mapping: create/validation bodies were empty from `404`, and delete returned `404` rather than `204`.

## GREEN / verification evidence

| Command | Result |
|---|---|
| `dotnet test tests/TrackZ.Application.Tests --filter FullyQualifiedName~CustomExerciseTests --no-restore` | PASS — 11/11 |
| `dotnet test tests/TrackZ.Infrastructure.Tests --filter FullyQualifiedName~ExerciseCatalogPersistenceTests.Active_custom_names --no-restore` | PASS — 1/1 (real PostgreSQL) |
| `dotnet test tests/TrackZ.Infrastructure.Tests --filter FullyQualifiedName~ExerciseCatalogPersistenceTests.Catalog_migration_matches --no-restore` | PASS — 1/1; `HasPendingModelChanges()` false |
| `dotnet test tests/TrackZ.Api.Tests --filter 'FullyQualifiedName~Custom_|FullyQualifiedName~Concurrent_custom' --no-restore` | PASS — 7/7 (real PostgreSQL/API) |
| `dotnet test tests/TrackZ.Application.Tests --no-restore` | PASS — 24/24, including architecture rules |
| `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore` | PASS — 19/19 |
| `dotnet test tests/TrackZ.Api.Tests --no-restore` | Did not complete: the test process continued after the command harness returned, so it was stopped before it could affect later checks. Focused API coverage above passed. |
| `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --disable-build-servers` | Did not complete: the standalone process produced no output and remained blocked beyond 30 seconds; it was stopped. API compilation did succeed as part of each focused API test run. |
| `dotnet test tests/TrackZ.Domain.Tests --no-restore` | Baseline failure unrelated to Task 3: 49 passed, 1 failed — `Reviewed_system_image_can_be_published_and_records_approval_metadata_in_utc` supplies a historical review date earlier than its default current `CreatedAt`. |

## Sensitivity coverage

- Owner spoofing: request includes another account's `ownerId`; persisted/listed owner is still the JWT user.
- Authorization-first malformed JSON: an unauthenticated malformed POST returns `401`, never parser details.
- Stable localized problems: Thai `10009` field validation; Thai duplicate `20002`, `application/problem+json`, and non-empty trace ID.
- Enumeration resistance: foreign, system, archived, repeated delete, and unknown custom IDs use `20001`/`404` through owner-scoped active lookup.
- PostgreSQL partial unique index: `(OwnerId, NormalizedName)` is case-insensitive via normalized value and applies only to active custom rows; archive permits reuse, other owners do not conflict, concurrent creates produce exactly one `201` and one `20002`.
- History guard: an existing real `ExercisePerformance` marks history, forbids tracking-mode changes with `10009`, and still permits name/body-part updates.
- Archive is soft-delete: the persisted row remains and repeat deletion is not found.

## Files and schema

- Added application port/commands/handlers, contracts, PostgreSQL store implementation, endpoint DTO validation, localized exercise resources, and migration `20260815112000_AddCustomExerciseNameUniqueness`.
- The migration is represented in the snapshot; PostgreSQL parity test verifies no pending model changes.

## Fix Round 1 — review remediation

### Root cause

`ExerciseDefinition.TrackingMode` was part of the mutable principal alternate key used by the `ExercisePerformance` composite foreign key. EF Core correctly treats key members as immutable, so an owner changing a custom exercise's mode before history reached the API as `500`.

The relationship now uses `ExerciseDefinitionId` alone as the normal restrictive FK. Migration `20260815124500_EnforceExercisePerformanceTrackingMode` adds PostgreSQL triggers which preserve the prior data guarantees: a performance's stored mode must match its exercise, and direct SQL cannot change an exercise mode once any performance exists. The application history guard remains in place.

### RED evidence

1. `dotnet test tests/TrackZ.Api.Tests --filter FullyQualifiedName~Custom_tracking_mode_changes_before_any_history_exists --no-restore`
   - Failed as intended: expected `204 NoContent`, actual `500 InternalServerError`.
2. `dotnet test tests/TrackZ.Application.Tests --filter 'FullyQualifiedName~Create_rejects_a_library_image_identifier_when_linking_is_deferred|FullyQualifiedName~Create_rejects_every_uploaded_image_key_when_linking_is_deferred' --no-restore`
   - Failed as intended: empty and whitespace uploaded keys were accepted instead of returning `10009`.
3. `dotnet test tests/TrackZ.Infrastructure.Tests --filter FullyQualifiedName~Custom_exercise_migration_designer_contains_the_complete_target_model --no-restore`
   - Failed as intended: migration `TargetModel` contained `[]` rather than the five persisted entities.
4. `dotnet test tests/TrackZ.Api.Tests --filter FullyQualifiedName~Custom_json_paths_canonicalize_only_known_root_properties --no-restore`
   - Failed as intended: five casing variants returned `body` rather than the canonical field.

### GREEN / verification evidence

| Command | Result |
|---|---|
| `dotnet test tests/TrackZ.Api.Tests --filter FullyQualifiedName~Custom_tracking_mode_changes_before_any_history_exists --no-restore` | PASS — 1/1 |
| `dotnet test tests/TrackZ.Infrastructure.Tests --filter 'FullyQualifiedName~Custom_exercise_migration_designer_contains_the_complete_target_model|FullyQualifiedName~Performance_round_trips_valid_mode_shapes' --no-restore` | PASS — 2/2, real PostgreSQL trigger/FK integrity |
| `dotnet test tests/TrackZ.Api.Tests --filter FullyQualifiedName~Custom_json_paths_canonicalize_only_known_root_properties --no-restore` | PASS — 9/9 |
| `dotnet test tests/TrackZ.Api.Tests --filter FullyQualifiedName~Case_variant_malformed_custom_properties_return_canonical_thai_validation_fields --no-restore` | PASS — 5/5 |
| `dotnet test tests/TrackZ.Domain.Tests --no-restore` | PASS — 50/50 |
| `dotnet test tests/TrackZ.Application.Tests --no-restore` | PASS — 28/28 |
| `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore` | PASS — 20/20 |
| `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --disable-build-servers` (outside sandbox) | PASS — 0 warnings, 0 errors |
| `dotnet test tests/TrackZ.Api.Tests --no-restore` (outside sandbox) | Stalled without a final test-run summary and was stopped; all Task 3 sensitive API groups above passed independently. |

### Fix Round 1 sensitivity coverage

- Real API mode update before history persists the new mode and returns `204`.
- Direct PostgreSQL valid-shaped performance with a mismatched mode is rejected, and direct SQL principal mode change after persisted performance is rejected.
- Migration metadata test verifies the custom-exercise migration designer exposes `ExerciseDefinition`, `ExerciseImage`, `ExercisePerformance`, `RefreshToken`, and `User`.
- Case-insensitive JSON parser paths canonicalize all five accepted root properties; nested, bare/bracketed, unknown, and array/attacker-shaped paths fall back to `body`.
- Five mixed-case malformed request payloads assert exact canonical Thai `fieldErrors` keys and messages.
- Direct handler tests reject library IDs, ordinary uploaded keys, empty keys, whitespace keys, and the already-covered mutual combination; no untrusted identifier can reach persistence.
- The deterministic image chronology fixture now explicitly creates the image before its review timestamp; production chronology checks are unchanged.

## Fix Round 2 — serializable trigger and frozen migration remediation

### Root cause

The initial trigger read the parent exercise through an unlocked `EXISTS` query. Under PostgreSQL MVCC that permits the performance insert and principal-mode update to validate against different snapshots and both commit. A foreign-key key-share lock alone is insufficient because non-key principal mode updates may remain compatible.

The performance trigger now selects the parent exercise row `FOR UPDATE` before comparing modes. A principal mode update already owns that row lock before its history trigger checks performance. Thus exactly one writer proceeds first; the second rechecks committed state after the lock and rejects when it would break the invariant. The trigger applies to both performance INSERT and relevant UPDATE operations.

The unmerged Task 3 mode migration was squashed into `20260815112000_AddCustomExerciseNameUniqueness`; the dynamic designers were removed. Its standard frozen target model includes the simple exercise-ID FK, the active-owner partial unique index, and all persisted entities.

### RED evidence

`dotnet test tests/TrackZ.Infrastructure.Tests --filter FullyQualifiedName~ExerciseTrackingModeConcurrencyTests --no-restore`

- Failed before the lock fix: both interleaving tests timed out waiting for their expected blocked statement, proving that neither the concurrent principal update nor concurrent performance insert serialized on the exercise row.

### GREEN / verification evidence

| Command | Result |
|---|---|
| `dotnet test tests/TrackZ.Infrastructure.Tests --filter 'FullyQualifiedName~ExerciseTrackingModeConcurrencyTests|FullyQualifiedName~Custom_exercise_migration_designer_contains_the_complete_target_model|FullyQualifiedName~Custom_exercise_migration_round_trip_preserves_valid_history' --no-restore` | PASS — 4/4, real PostgreSQL |
| `dotnet test tests/TrackZ.Domain.Tests --no-restore` | PASS — 50/50 |
| `dotnet test tests/TrackZ.Application.Tests --no-restore` | PASS — 28/28 |
| `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore` | PASS — 23/23 |
| `dotnet test tests/TrackZ.Api.Tests --filter FullyQualifiedName~Custom_tracking_mode_changes_before_any_history_exists --no-restore` | PASS — 1/1 |
| `dotnet test tests/TrackZ.Api.Tests --filter FullyQualifiedName~Custom_tracking_mode_is_immutable_after_performance_history_but_other_fields_remain_updatable --no-restore` | PASS — 1/1 |
| `dotnet test tests/TrackZ.Api.Tests --filter FullyQualifiedName~Custom_json_paths_canonicalize_only_known_root_properties --no-restore` | PASS — 9/9 |
| `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --disable-build-servers` (outside sandbox) | PASS — 0 warnings, 0 errors |

### Fix Round 2 sensitivity coverage

- Two independent DbContexts/transactions use `pg_stat_activity` lock observation with five-second deadlines. In interleaving A, an uncommitted performance insert holds the parent lock, the concurrent principal update is observed blocked, then rejects after the insert commits. In interleaving B, an uncommitted principal mode update holds the parent lock, the old-mode insert is observed blocked, then rejects after the update commits. Both assert final persisted state.
- Direct PostgreSQL mismatched-mode performance inserts and direct principal mode changes with history remain rejected.
- Frozen migration target metadata asserts entity count, active-owner index uniqueness/filter, simple performance FK/principal key, single-column performance index, and absence of the old mutable composite alternate key.
- A fresh real PostgreSQL database migrates current -> `AddExerciseCatalog` (Down) -> current (Up), preserves valid history, restores trigger integrity, and has no pending model changes.

## Fix Round 3 — frozen designer metadata remediation

### Root cause

Although the custom-exercise migration designer exposed the expected entities, it was manually rebuilt with CLR lambdas. That omitted scaffolded relational metadata such as column types, Npgsql value-generation strategy, and generated-value annotations, so it was not a faithful frozen migration target.

`20260815112000_AddCustomExerciseNameUniqueness.Designer.cs` now uses the same standard string-based EF/Npgsql target-model shape as the current snapshot. It carries the provider annotations, identifier limit, generated UUID properties, relational column types, precision, checks, indexes, filters, and relationships directly in the frozen model.

### RED evidence

`dotnet test tests/TrackZ.Infrastructure.Tests --filter FullyQualifiedName~Custom_exercise_migration_target_matches_current_snapshot_relational_metadata --no-restore --disable-build-servers`

- Failed as intended against the previous hand-built CLR-lambda designer: the target and snapshot relational metadata collections differed immediately after the entity record, exposing missing property metadata.

### GREEN / verification evidence

| Command | Result |
|---|---|
| `dotnet test tests/TrackZ.Infrastructure.Tests --filter 'FullyQualifiedName~Custom_exercise_migration_designer_contains_the_complete_target_model\|FullyQualifiedName~Custom_exercise_migration_target_matches_current_snapshot_relational_metadata\|FullyQualifiedName~Custom_exercise_migration_round_trip_preserves_valid_history_and_restores_trigger_integrity' --no-restore --disable-build-servers` (outside sandbox) | PASS — 3/3, including real PostgreSQL Down/Up integrity |
| `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore --disable-build-servers` (outside sandbox) | PASS — 24/24 |
| `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --disable-build-servers` (outside sandbox) | PASS — 0 warnings, 0 errors |

### Fix Round 3 sensitivity coverage

- The migration parity test serializes every entity's relational table/schema and annotations, every property (CLR type, nullability, generated value, column type, length, precision, scale, annotations), keys, foreign keys, indexes, and check constraints from both the migration target and current snapshot. The prior lambda reconstruction fails this comparison; a target that only has the right entity names or partial-index filter cannot pass.
- The existing real PostgreSQL migration Down/Up test remains in the focused group, proving the frozen target change did not alter the executable migration or its trigger-backed integrity guarantees.
