# Task 2 — Truthful Motivation and Unit-Aware Recent Momentum

## Scope and contract

- `TrainTodayViewModel` now commits local active/repeat state before it awaits cached or refreshed progress, then exposes authoritative weekly completion, goal, streak, Level/XP, progress fraction, and one recent momentum item.
- Cached progress is applied before an online refresh; a failed progress cache read or refresh leaves local training usable and never fabricates motivation values. A successful cached snapshot remains authoritative after refresh failure.
- Every asynchronous local/progress commit, error commit, and completion commit is generation-fenced. The singleton boundary reset clears active/repeat compatibility data, motivation metrics, and disposes the prior momentum item.
- `HomeMomentumItem` subscribes once to the shared unit preference, uses kg `0.###`, lb `0.00`, midpoint rounding away from zero, and unsubscribes on disposal.
- Recent momentum selects `LastPerformedAt` descending, then `ExerciseId` descending. Weighted, assisted, and bodyweight displays remain mode-specific.

## Persistent HTML comparison

Compared directly with `docs/design/momentum-home-reference.html`:

| HTML state / field | Native Task 2 state |
|---|---|
| Ready/Active hero local facts | `ActiveWorkout` and `RepeatWorkout`, committed before any progress network wait |
| `3/4`, streak `4`, Level `8 · 640 XP` | `WeeklyCompletedWorkouts`, `WeeklyGoal`, `CurrentStreakWeeks`, `Level`, `TotalXp`, `LevelProgress`, only visible later when `HasAuthoritativeProgress` is true |
| `Recent momentum` Last/Best kg/lb example | `RecentMomentum.LastText` / `BestText`, exact `70.125 kg` and `154.60 lb` formatting |
| hidden motivation/momentum with no data | `HasAuthoritativeProgress == false` and `HasRecentMomentum == false` |
| active state hides Train again | `RepeatWorkout` remains Task 1 data; Task 3/4 own its active-state command/visual hiding |

## Localization plan defect ruling

Task 2 requires localized Last/Best/assistance text while consuming `GamificationTextSet`, but that contract did not expose these fields and Task 4 only owns page/XAML-specific Workout resources. Per controller ruling, Task 2 expanded minimally to add `Last`, `Best`, and `Assistance` to `GamificationTextSet`/`GamificationResources`, with English and Thai literal-contract coverage. Task 4 must consume these labels rather than duplicate them. Cost accepted: one small gamification text contract change prevents a nonlocalizable interim API and later churn.

## RED evidence

1. Initial prescribed focused command:

   `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~HomeMomentumItemTests" --verbosity minimal -m:1`

   Result: compile RED — `HomeMomentumItem` and the required Home authoritative-progress properties/constructor did not exist (11 compiler errors).

2. Focused no-authority mutation test:

   `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter FullyQualifiedName~TrainTodayViewModelTests.Progress_cache_read_failure_keeps_local_workout_and_hides_unauthoritative_motivation --verbosity minimal -m:1`

   Result: RED — progress cache read leaked `"Could not load workout details"` into `ErrorText`; the expected presentation is local workout retained with hidden unauthoritative motivation and no technical error.

3. Focused cancellation regression:

   `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter FullyQualifiedName~TrainTodayViewModelTests.Caller_cancellation_during_progress_read_is_propagated --verbosity minimal -m:1`

   Result: RED — caller cancellation during cached-progress read was swallowed, so no `OperationCanceledException` reached the caller.

## GREEN evidence

1. Prescribed Task 2 focused suite:

   `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~HomeMomentumItemTests" --verbosity minimal -m:1`

   Result: 18 passed, 0 failed, 0 skipped.

2. Prescribed related regression:

   `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~ProgressDashboardViewModelTests|FullyQualifiedName~GamificationViewModelTests" --verbosity minimal -m:1`

   Result: 24 passed, 0 failed, 0 skipped.

The second command required the approved elevated local test environment because VSTest loopback socket binding was denied by the sandbox; it passed outside the sandbox. No API, Docker, container, simulator, or other service was started.

## Mutation-sensitive coverage

- Remove cached progress application or defer local state until refresh: `Local_state_commits_before_gated_progress_refresh_and_cached_values_survive_failure` fails.
- Replace reset fencing with an unconditional refresh commit: `Account_reset_after_cached_progress_prevents_delayed_refresh_from_restoring_home_state` fails.
- Choose first/ascending recent summary rather than latest then descending GUID: `Recent_momentum_uses_latest_time_then_descending_exercise_id` fails.
- Use incorrect conversion/precision or a generic mode formatter: literal weighted, assisted, and bodyweight tests fail.
- Retain a unit event subscription after disposal: `Dispose_unsubscribes_from_the_shared_weight_preference` fails.
- Surface progress-read errors as a local workout failure: `Progress_cache_read_failure_keeps_local_workout_and_hides_unauthoritative_motivation` fails.
- Swallow caller cancellation during progress read: `Caller_cancellation_during_progress_read_is_propagated` fails.

## Files changed

- `src/TrackZ.Mobile.Core/Features/Train/HomeMomentumItem.cs`
- `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- `src/TrackZ.Mobile.Core/Features/Gamification/GamificationViewModels.cs`
- `tests/TrackZ.Mobile.Tests/Train/HomeMomentumItemTests.cs`
- `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`

`git diff --check` is clean. Simulator acceptance remains pending by design.
