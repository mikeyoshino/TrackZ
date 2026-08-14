# TrackZ Workout and Offline Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver crash-safe workout logging, exact previous-set display, editable history, and idempotent two-way synchronization that never silently loses local data.

**Architecture:** `WorkoutSession` is the server aggregate root. Mobile writes every workout mutation to SQLite and a transactional outbox before updating UI feedback. The server processes typed sync operations through MediatR, records operation IDs, uses aggregate versions for conflicts, and exposes cursor-based pull changes/tombstones.

**Tech Stack:** .NET 10, MediatR, EF Core/Npgsql, PostgreSQL, .NET MAUI XAML/MVVM, SQLite, xUnit.

## Global Constraints

- Supported modes are Weighted, Bodyweight, and Assisted; cardio/duration/distance are excluded.
- Save local state before animation or haptic feedback.
- Every mutation has a stable client `OperationId`; retries must return the prior result and must not duplicate records.
- Server owns workout state, LAST/PR, XP, streak, and badge truth.
- Version conflicts use `VersionConflict = 60001`; local unsynchronized data is never silently discarded.
- Users may edit or delete historical workouts/sets at any time; dependent projections must be recalculated.
- Network absence is a normal state, not an error toast.

---

### Task 1: Workout Aggregate and Set Validation

**Files:**
- Create: `src/TrackZ.Domain/Workouts/WorkoutStatus.cs`
- Create: `src/TrackZ.Domain/Workouts/WorkoutSession.cs`
- Create: `src/TrackZ.Domain/Workouts/WorkoutExercise.cs`
- Create: `src/TrackZ.Domain/Workouts/SetEntry.cs`
- Create: `src/TrackZ.Domain/Workouts/SetMeasurement.cs`
- Test: `tests/TrackZ.Domain.Tests/Workouts/WorkoutSessionTests.cs`

**Interfaces:**
- Consumes: exercise ID/tracking mode and authenticated owner ID.
- Produces: aggregate methods `Start`, `AddExercise`, `ReorderExercises`, `CompleteSet`, `EditSet`, `DeleteSet`, `Complete`, and `Delete`.

- [ ] **Step 1: Write failing aggregate tests**

```csharp
[Fact]
public void Completing_weighted_set_requires_positive_weight_and_reps()
{
    var workout = WorkoutSession.Start(_userId, _workoutId, _startedAt);
    var itemId = workout.AddExercise(_exerciseId, TrackingMode.Weighted, 0);
    var ex = Assert.Throws<BusinessException>(() =>
        workout.CompleteSet(itemId, _setId, new SetMeasurement(null, null, 10), _now));
    Assert.Equal(BusinessErrorCode.InvalidSetValue, ex.Code);
}

[Fact]
public void Completed_workout_rejects_new_sets()
{
    var workout = CompletedWorkout();
    var ex = Assert.Throws<BusinessException>(() => workout.CompleteSet(
        _itemId, Guid.NewGuid(), new SetMeasurement(70m, null, 10), _now));
    Assert.Equal(BusinessErrorCode.WorkoutAlreadyCompleted, ex.Code);
}
```

- [ ] **Step 2: Run and verify missing aggregate failure**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter WorkoutSessionTests
```

- [ ] **Step 3: Implement aggregate rules**

```csharp
public enum WorkoutStatus { Draft = 1, Active = 2, Completed = 3 }
public sealed record SetMeasurement(decimal? WeightKg, decimal? AssistedKg, int Reps);
```

Weighted requires `WeightKg > 0`, Bodyweight requires both weight fields null, Assisted requires `AssistedKg > 0`, and all modes require reps `1..999`. Exercise order and set order are zero-based unique integers. Mutations increment aggregate `Version` through optimistic concurrency.

- [ ] **Step 4: Run domain tests**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter Workouts
```

Expected: state, ordering, ownership-independent invariants, and validation tests pass.

- [ ] **Step 5: Commit workout domain**

```bash
git add src/TrackZ.Domain/Workouts tests/TrackZ.Domain.Tests/Workouts
git commit -m "feat: model workout aggregate"
```

### Task 2: Workout Persistence and Read Models

**Files:**
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/WorkoutSessionConfiguration.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/WorkoutExerciseConfiguration.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/SetEntryConfiguration.cs`
- Create: `src/TrackZ.Contracts/Workouts/WorkoutDetailDto.cs`
- Create: `src/TrackZ.Application/Workouts/GetWorkout/GetWorkoutQuery.cs`
- Create: `src/TrackZ.Application/Workouts/ListHistory/ListWorkoutHistoryQuery.cs`
- Create: `src/TrackZ.Application/Exercises/GetHistory/GetExerciseHistoryQuery.cs`
- Create: `src/TrackZ.Api/Endpoints/WorkoutEndpoints.cs`
- Test: `tests/TrackZ.Application.Tests/Workouts/WorkoutQueryTests.cs`

**Interfaces:**
- Consumes: workout aggregate and current user ID.
- Produces: exact-set workout detail, cursor-paged workout history, and per-exercise session history.

- [ ] **Step 1: Write a failing exact-previous-sets query test**

```csharp
[Fact]
public async Task Exercise_history_returns_every_set_in_original_order()
{
    await SeedCompletedWorkoutAsync((70m, 10), (70m, 9), (67.5m, 10));
    var page = await _handler.Handle(new GetExerciseHistoryQuery(_exerciseId, null, 20), default);
    Assert.Collection(page.Items[0].Sets,
        x => Assert.Equal((70m, 10), (x.WeightKg, x.Reps)),
        x => Assert.Equal((70m, 9), (x.WeightKg, x.Reps)),
        x => Assert.Equal((67.5m, 10), (x.WeightKg, x.Reps)));
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Application.Tests --filter WorkoutQueryTests
```

- [ ] **Step 3: Implement mappings and queries**

Configure decimal kilograms as `numeric(8,3)`, enforce ownership in every query, order sessions by `CompletedAt DESC, Id DESC`, and use opaque cursor pagination. Total volume is the sum of `WeightKg * Reps` for Weighted sets only; bodyweight and assisted sets remain visible but do not add kilogram volume.

- [ ] **Step 4: Add migration and run query tests**

```bash
dotnet ef migrations add AddWorkouts --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --output-dir Persistence/Migrations
dotnet test tests/TrackZ.Application.Tests --filter Workout
dotnet test tests/TrackZ.Api.Tests --filter Workout
```

- [ ] **Step 5: Commit workout read models**

```bash
git add src/TrackZ.Infrastructure src/TrackZ.Contracts/Workouts src/TrackZ.Application/Workouts src/TrackZ.Application/Exercises/GetHistory src/TrackZ.Api/Endpoints tests
git commit -m "feat: persist and query workouts"
```

### Task 3: Mobile SQLite Store, Outbox, and Active-Workout Restore

**Files:**
- Create: `src/TrackZ.Mobile/Data/TrackZLocalDatabase.cs`
- Create: `src/TrackZ.Mobile/Data/LocalWorkoutRepository.cs`
- Create: `src/TrackZ.Mobile/Data/Models/LocalWorkout.cs`
- Create: `src/TrackZ.Mobile/Data/Models/LocalWorkoutExercise.cs`
- Create: `src/TrackZ.Mobile/Data/Models/LocalSet.cs`
- Create: `src/TrackZ.Mobile/Data/Models/SyncCursor.cs`
- Create: `src/TrackZ.Mobile/Sync/OutboxOperation.cs`
- Create: `src/TrackZ.Mobile/Sync/OutboxRepository.cs`
- Create: `src/TrackZ.Mobile/Features/Workout/ActiveWorkoutCoordinator.cs`
- Test: `tests/TrackZ.Mobile.Tests/Workout/ActiveWorkoutCoordinatorTests.cs`

**Interfaces:**
- Consumes: selected exercise IDs from Plan 2.
- Produces: atomic `SaveWorkoutAndEnqueueAsync(LocalWorkout, OutboxOperation, CancellationToken)` and `RestoreActiveAsync`.

- [ ] **Step 1: Write failing crash-recovery test**

```csharp
[Fact]
public async Task Save_set_persists_workout_and_outbox_before_returning()
{
    await _sut.StartAsync([_exerciseId]);
    await _sut.SaveSetAsync(_exerciseId, new LocalSet(70m, null, 10));
    var restored = await NewCoordinatorUsingSameDatabase().RestoreActiveAsync();
    Assert.Equal(70m, restored!.Exercises.Single().Sets.Single().WeightKg);
    Assert.Single(await _outbox.PendingAsync());
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter ActiveWorkoutCoordinatorTests
```

- [ ] **Step 3: Implement transactional local persistence**

Use SQLite tables `LocalWorkout`, `LocalWorkoutExercise`, `LocalSet`, `OutboxOperation`, and `SyncCursor`. Generate IDs and operation IDs on the client. One SQLite transaction writes the changed entity and serialized typed operation. `SaveSetAsync` completes before the ViewModel triggers animation.

```csharp
public interface ILocalWorkoutRepository
{
    Task SaveAndEnqueueAsync(LocalWorkout workout, OutboxOperation operation, CancellationToken ct);
    Task<LocalWorkout?> GetActiveAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Run restore/concurrency tests**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter "ActiveWorkout|Outbox"
```

Expected: restore, transaction rollback, and duplicate-operation-ID tests pass.

- [ ] **Step 5: Commit local workout persistence**

```bash
git add src/TrackZ.Mobile/Data src/TrackZ.Mobile/Sync src/TrackZ.Mobile/Features/Workout tests/TrackZ.Mobile.Tests
git commit -m "feat: persist offline workouts and outbox"
```

### Task 4: Idempotent Sync Push

**Files:**
- Create: `src/TrackZ.Contracts/Sync/SyncPushRequest.cs`
- Create: `src/TrackZ.Contracts/Sync/SyncPushResponse.cs`
- Create: `src/TrackZ.Application/Sync/Push/PushSyncCommand.cs`
- Create: `src/TrackZ.Application/Sync/Push/PushSyncHandler.cs`
- Create: `src/TrackZ.Domain/Sync/ProcessedClientOperation.cs`
- Create: `src/TrackZ.Api/Endpoints/SyncEndpoints.cs`
- Test: `tests/TrackZ.Api.Tests/Sync/SyncPushTests.cs`

**Interfaces:**
- Consumes: batches of `SyncOperationDto(OperationId, EntityType, Action, Payload, BaseVersion)`.
- Produces: per-operation `Applied`, `Rejected`, `Conflict`, or `Retryable` results and stable server versions.

- [ ] **Step 1: Write failing duplicate-push test**

```csharp
[Fact]
public async Task Retrying_same_operation_returns_prior_result_without_duplicate_set()
{
    var operation = SyncSamples.AddSet(operationId: Guid.NewGuid());
    var first = await PushAsync(operation);
    var second = await PushAsync(operation);
    Assert.Equal(first.Results[0], second.Results[0]);
    Assert.Equal(1, await CountSetsAsync(operation.EntityId));
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Api.Tests --filter SyncPushTests
```

- [ ] **Step 3: Implement typed operation dispatch and idempotency**

Within one database transaction per operation, first look up `(UserId, OperationId)`. Return the stored serialized result if found. Otherwise dispatch the typed MediatR command, persist the aggregate and `ProcessedClientOperation`, then commit. A malformed/unknown operation is permanently rejected; transient database failure is retryable.

- [ ] **Step 4: Run retry and partial-batch tests**

```bash
dotnet test tests/TrackZ.Api.Tests --filter SyncPush
```

Expected: duplicate, partial success, permanent business failure, and transactional rollback pass.

- [ ] **Step 5: Commit sync push**

```bash
git add src/TrackZ.Contracts/Sync src/TrackZ.Application/Sync src/TrackZ.Domain/Sync src/TrackZ.Api/Endpoints tests/TrackZ.Api.Tests
git commit -m "feat: process idempotent sync pushes"
```

### Task 5: Sync Pull, Tombstones, and Conflict Resolution

**Files:**
- Create: `src/TrackZ.Domain/Sync/SyncChange.cs`
- Create: `src/TrackZ.Contracts/Sync/SyncPullResponse.cs`
- Create: `src/TrackZ.Application/Sync/Pull/PullSyncQuery.cs`
- Create: `src/TrackZ.Mobile/Sync/SyncCoordinator.cs`
- Create: `src/TrackZ.Mobile/Sync/ConflictResolution.cs`
- Test: `tests/TrackZ.Api.Tests/Sync/SyncPullTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Sync/SyncCoordinatorTests.cs`

**Interfaces:**
- Consumes: opaque pull cursor, change log/tombstones, and push results.
- Produces: atomic cache application, cursor advancement, retry scheduling, and explicit conflict state.

- [ ] **Step 1: Write failing conflict/no-data-loss tests**

```csharp
[Fact]
public async Task Conflict_keeps_local_payload_until_user_resolves()
{
    _api.PushAsync(Arg.Any<SyncPushRequest>(), default)
        .Returns(SyncSamples.VersionConflict(60001, serverVersion: 4));
    await _sut.RunOnceAsync();
    var operation = Assert.Single(await _outbox.ConflictedAsync());
    Assert.NotNull(operation.Payload);
    Assert.Equal(4, operation.ServerVersion);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter SyncCoordinatorTests
dotnet test tests/TrackZ.Api.Tests --filter SyncPullTests
```

- [ ] **Step 3: Implement ordered push-then-pull**

Push pending operations in creation order, mark per-result state, then pull changes after the current cursor. Apply changed rows and tombstones in one SQLite transaction and advance the cursor only in that transaction. For `60001`, retain payload and expose `KeepServerAsync` and `ApplyLocalAgainstVersionAsync(long serverVersion)`.

- [ ] **Step 4: Run sync tests**

```bash
dotnet test tests/TrackZ.Api.Tests --filter Sync
dotnet test tests/TrackZ.Mobile.Tests --filter Sync
```

Expected: cursor replay, tombstone, conflict, retry backoff, and partial-result tests pass.

- [ ] **Step 5: Commit two-way sync**

```bash
git add src/TrackZ.Domain/Sync src/TrackZ.Contracts/Sync src/TrackZ.Application/Sync src/TrackZ.Mobile/Sync tests
git commit -m "feat: add pull sync and conflict resolution"
```

### Task 6: Native Workout and Set Logger UI

**Files:**
- Create: `src/TrackZ.Mobile/Features/Workout/WorkoutPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Workout/WorkoutViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Workout/SetLoggerViewModel.cs`
- Create: `src/TrackZ.Mobile/Components/LastSetTable.xaml`
- Create: `src/TrackZ.Mobile/Components/WeightStepper.xaml`
- Create: `src/TrackZ.Mobile/Components/RepsStepper.xaml`
- Create: `src/TrackZ.Mobile/Components/SyncStatusPill.xaml`
- Test: `tests/TrackZ.Mobile.Tests/Workout/SetLoggerViewModelTests.cs`

**Interfaces:**
- Consumes: `ActiveWorkoutCoordinator`, cached previous-session sets, and `SyncCoordinator` status.
- Produces: add/remove/reorder exercise UI, weight/reps entry, `MATCH LAST`, exact previous sets, and local-first completion.

- [ ] **Step 1: Write failing MATCH LAST and save-order tests**

```csharp
[Fact]
public async Task Match_last_uses_corresponding_previous_set()
{
    await _sut.LoadAsync(ExerciseSamples.PreviousSets70x10_70x9_67_5x10);
    _sut.MatchLastCommand.Execute(null);
    Assert.Equal(70m, _sut.WeightKg);
    Assert.Equal(10, _sut.Reps);
}

[Fact]
public async Task Celebration_starts_only_after_local_save()
{
    await _sut.CompleteSetCommand.ExecuteAsync(null);
    Received.InOrder(() => { _store.SaveSetAsync(Arg.Any<LocalSet>(), default); _motion.SetSavedAsync(); });
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter SetLoggerViewModelTests
```

- [ ] **Step 3: Implement ViewModels and XAML**

Bind numeric controls to canonical kg/reps state, convert only for display, and show Last vs Today rows. Disable Complete Set only for locally invalid measurement; do not wait for network. Reorder uses stable exercise IDs and creates one outbox operation.

- [ ] **Step 4: Run tests and platform builds**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter Workout
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios
```

- [ ] **Step 5: Commit workout UI**

```bash
git add src/TrackZ.Mobile/Features/Workout src/TrackZ.Mobile/Components tests/TrackZ.Mobile.Tests
git commit -m "feat: add native offline workout logger"
```

### Task 7: History Editing and Offline End-to-End Acceptance

**Files:**
- Create: `src/TrackZ.Mobile/Features/History/WorkoutHistoryPage.xaml`
- Create: `src/TrackZ.Mobile/Features/History/WorkoutHistoryViewModel.cs`
- Create: `tests/TrackZ.Api.Tests/Workouts/EditHistoryTests.cs`
- Create: `tests/TrackZ.Mobile.Tests/Acceptance/OfflineWorkoutAcceptanceTests.cs`

**Interfaces:**
- Consumes: history queries, local repository, outbox, and conflict UX.
- Produces: edit/delete with confirmation, tombstones, and verified offline kill/resume/sync behavior.

- [ ] **Step 1: Write failing acceptance test**

```csharp
[Fact]
public async Task Offline_log_kill_restore_and_retry_sync_creates_each_set_once()
{
    await _app.GoOfflineAsync();
    await _app.LogWorkoutAsync((70m, 10), (70m, 9));
    await _app.KillAndRestoreAsync();
    Assert.Equal(2, _app.ActiveWorkout.SetCount);
    await _app.GoOnlineAsync();
    await _app.SyncTwiceAsync();
    Assert.Equal(2, await _server.CountSetsAsync());
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter OfflineWorkoutAcceptanceTests
```

- [ ] **Step 3: Implement history edit/delete flow**

Editing creates a versioned upsert operation; deleting creates a tombstone operation after confirmation. Keep an undoable local snapshot until server acknowledgement. History UI exposes pending/conflicted status and never hides failed changes.

- [ ] **Step 4: Run the complete workout/sync suite**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter Workouts
dotnet test tests/TrackZ.Application.Tests --filter Workout
dotnet test tests/TrackZ.Api.Tests --filter "Workout|Sync"
dotnet test tests/TrackZ.Mobile.Tests --filter "Workout|Sync|History|OfflineWorkout"
```

- [ ] **Step 5: Commit the workout/sync milestone**

```bash
git add src/TrackZ.Mobile/Features/History tests
git commit -m "feat: edit history and verify offline sync"
```

Plan 3 is complete when a user can log without connectivity, kill/reopen the app, sync repeated batches without duplicates, resolve two-device conflicts, and edit/delete history without silent data loss.
