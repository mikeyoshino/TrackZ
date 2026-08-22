# Momentum Home — bounded final fix wave

Date: 2026-08-22
Starting HEAD: `886b22a86d5c5f3b71f46b37b4051de6fd9e1497`
Product acceptance: **simulator acceptance pending**

## Delivered fixes

- `TrainTodayViewModel.LoadAsync` now contains every non-caller-cancellation local dashboard/source
  failure, including a real `SqliteException`, so the page's `async void` appearance boundary does
  not receive an unhandled repository failure. The dashboard remains unknown, Start/Continue/Repeat
  mutations stay disabled, and localized `HomeLoadFailed` is shown.
- Home exposes `HasError`, load-specific `HasLoadRetry`, and a serialized `RetryCommand`.
  `TrainPage` renders the named native secondary retry button only for dashboard-load failure, with
  a shared 44-point target, localized text/semantics, and no additional primary action. A successful
  load retry clears the error and commits the recovered dashboard.
- Definite Train-again failure uses distinct actionable `HomeRepeatFailed` copy in English and Thai;
  the generic load-retry button stays hidden and the unchanged repeat row/command remains the retry
  path. A new repeat attempt clears the prior error before persistence; successful commit clears it
  again and creates only one active workout, Start outbox operation, and navigation.
- Persistence and navigation recovery are separate. If the repeat is durably committed but opening
  the workout fails, Home shows localized `HomeOpenWorkoutFailed`, retains the Active/Continue hero,
  hides repeat, and keeps the durable active workout/outbox. A subsequent Continue can navigate and
  clears the recovery error.
- `LocalTrainDashboardSource` rejects completed candidates with zero live exercises, falls back to
  the next exact candidate, and returns no shortcut when no non-empty exact candidate exists.
- Repeat completion weekday is converted through the injected device timezone and formatted with
  `CultureInfo.CurrentUICulture`, independently of `CurrentCulture`.
- The Train-again acceptance test now persists a real mixed-mode completed workout and sets, clears
  SQLite pools, recreates both workout database and exercise cache boundaries, loads through
  `LocalTrainDashboardSource` into `TrainTodayViewModel`, and executes concurrent Train-again.
  It proves exact IDs/modes/order, no copied sets, one active-workout Start operation/navigation,
  and unchanged completed history. Reset-during-save now reopens SQLite and proves no durable active
  workout or Start operation survived cancellation.
- `MauiTrainNavigator` follows the existing app-owned adapter pattern via injectable internal
  `ITrainNavigationHost`; a real adapter test asserts literal `active-workout` and
  `ExercisePickerPage?bodyPart=2` routes plus cancellation before mutation.
- Removed the explicitly temporary `RecentWorkoutItem`, `RecentWorkouts`, and `HasRecentWorkouts`
  projection. The now-unused legacy `Recent` and `NoRecentWorkouts` resources were removed; shared
  resources still used elsewhere were preserved.

## RED evidence

1. Real local SQLite failure, before broad source containment:

   ```text
   Real_local_sqlite_failure_is_contained_and_disables_dashboard_mutation
   Failed: 1, Passed: 0
   Actual: Microsoft.Data.Sqlite.SqliteException: SQLite Error 14: unable to open database file
   ```

2. Retry/error/localization/native UI contract, before implementation:

   ```text
   CS1061: TrainTodayViewModel has no HasError or RetryCommand
   CS1061: WorkoutTextSet has no HomeRepeatFailed
   ```

   After broad containment was introduced, the local caller-cancellation test exposed an incorrect
   catch-all branch: 1 of 4 focused tests failed because no `OperationCanceledException` propagated.
   The caller-token branch now rethrows before account-reset cancellation is contained.

3. Empty completed candidates, before rejection:

   ```text
   Source_skips_newest_completed_workout_with_only_tombstoned_exercises: expected older ID; got empty newest ID
   Source_returns_no_repeat_when_every_completed_workout_has_zero_live_exercises: expected null; got ExerciseCount = 0
   Failed: 2, Passed: 0
   ```

4. Bangkok near-midnight + mismatched culture boundary, before the timezone/UI-culture fix:

   ```text
   Expected: Monday / วันจันทร์
   Actual: วันอาทิตย์ / Sunday
   Failed: 2, Passed: 0
   ```

5. Production navigation adapter, before adding the host boundary:

   ```text
   CS0246: ITrainNavigationHost could not be found
   ```

6. Final re-review load-retry separation, before correction:

   ```text
   CS1061: TrainTodayViewModel has no HasLoadRetry
   ```

   After adding the state contract, binding `HomeRetryButton.IsVisible` back to `HasError` failed
   the inflated repeat-failure page: expected hidden, actual visible.

7. Repeat retry while its second persistence attempt was gated, before clearing prior error:

   ```text
   Expected: null
   Actual: Could not repeat that workout. Try again.
   Failed: 1, Passed: 0
   ```

8. Saved-but-navigation-failed recovery, before the split:

   ```text
   CS1061: WorkoutTextSet has no HomeOpenWorkoutFailed
   Expected: Workout saved. Could not open it. Tap Continue.
   Actual: Could not repeat that workout. Try again.
   ```

## Deliberate mutation evidence

- Changing the production active route to `active-exercise` failed the adapter test with literal
  expected `active-workout` versus actual `active-exercise`.
- Reversing `LocalTrainDashboardSource` exercise order failed the real restart acceptance at the
  view-model repeat boundary: expected weighted/assisted/bodyweight, actual
  bodyweight/assisted/weighted.
- Making the gated repository ignore its cancellation token failed the reset/restart assertion
  because a durable active workout remained in the freshly reopened database.
- Replacing `HomeRepeatFailed` with `HomeLoadFailed` failed the definite-failure test with exact
  expected actionable copy versus `Could not load Home`.
- Collapsing the post-commit navigation mapping back to `HomeRepeatFailed` failed the deterministic
  durable-navigation test with the exact saved-but-open-failed copy mismatch.
- Binding the native load retry to generic `HasError` failed the inflated repeat-failure test because
  the load action became visible when Train again was the required retry path.

Every mutation was restored before GREEN verification.

## GREEN verification

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~LocalTrainDashboardSourceTests|FullyQualifiedName~TrainAgainWorkoutTests|FullyQualifiedName~TrainNavigationTests|FullyQualifiedName~MomentumHomePresentationTests|FullyQualifiedName~MauiCompositionTests" \
  --verbosity minimal -m:1
Passed: 61, Failed: 0, Skipped: 0

dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --verbosity minimal -m:1
Passed: 590, Failed: 0, Skipped: 0

dotnet build src/TrackZ.Mobile.Core/TrackZ.Mobile.Core.csproj --no-restore -m:1 -v:minimal
Build succeeded: 0 warnings, 0 errors

dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile \
  -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -v:minimal
Exit code: 0; no product warning or error output

git diff --check
Exit code: 0; no output
```

The verification commands completed without starting an API, Docker, container, application, or
simulator device.

## Persistent HTML comparison

`docs/design/momentum-home-reference.html` was reopened after all fixes and every toggle was mapped
directly rather than recalled from memory:

| Reference state | Native comparison after fixes |
|---|---|
| Ready · EN · kg | Same header → lime Start hero → truthful metrics → Train again → kg momentum hierarchy; retry state hidden. |
| Ready · EN · lb | Same single primary and live shared-unit conversion; repeat weekday is device-local English. |
| Ready · TH · kg | Same Thai hierarchy/copy; repeat weekday uses Thai UI culture after timezone conversion. |
| Ready · TH · lb | Same Thai labels and shared lb values; no Home-only unit state. |
| Active · EN · kg | Same dark Continue hero, repeat hidden, metrics/momentum retained. |
| Active · EN · lb | Same active hierarchy and exact lb momentum with one primary. |
| Active · TH · kg | Same Thai active copy/progress, repeat hidden, one primary. |
| Active · TH · lb | Same Thai active copy and lb momentum; no extra primary action. |

The load-retry state is intentionally outside the eight normal reference combinations: it appears
only for dashboard-load failure, below normal content, uses the shared secondary style, and leaves
the disabled hero as the sole primary-style action. Definite repeat failure keeps that button hidden
and retries through the repeat card. Saved-but-open-failed state keeps the Active hero and Continue
as the recovery action. Production remains native MAUI XAML.

## Service and worktree audit

- `./scripts/trackz-dev status`: Docker Desktop stopped; TrackZ containers stopped; TrackZ API
  stopped; iOS Simulator device stopped.
- No testhost remained after verification. Pre-existing editor/compiler/UI processes were not
  started, stopped, or modified by this wave.
- Unrelated pre-existing dirty files and untracked `scripts/` / `tests/tooling/` remain preserved
  and are excluded from this commit.

## Concerns

No implementation concern remains from the five Important findings, the final re-review recovery
correction, or planned compatibility cleanup. Manual simulator acceptance remains pending until the
user explicitly asks to start it.
