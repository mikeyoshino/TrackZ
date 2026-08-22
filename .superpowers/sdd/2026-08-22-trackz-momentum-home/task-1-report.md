# Task 1 — Exact Local Active and Repeat Snapshot

## Scope and changed contracts

- Replaced the three-item `RecentWorkoutShortcut` contract with `RepeatWorkoutShortcut`.
- `ActiveWorkoutCard` now carries ordered resolved body parts, exact live exercise count, logged-exercise count, and exact live set count.
- `TrainDashboardSnapshot` now exposes `Active` plus one optional exact `Repeat` candidate.
- `RepeatWorkoutShortcut` carries source ID, resolved body parts, completion time, live exercise/set counts, first ordered local thumbnail, and ordered `WorkoutExerciseSelection` values. Sets are never copied.
- `LocalTrainDashboardSource` resolves active body parts from the complete cached-definition dictionary but always counts live workout rows and live sets, including when a definition is unavailable. Completed candidates are ordered by `CompletedAt` then ID descending and are accepted only when every live exercise definition exists and retains the recorded tracking mode.
- `TrainTodayViewModel.RepeatWorkout` stores the exact snapshot value. `RecentWorkouts` remains a temporary zero-or-one projection of that value for the current XAML binding; it must be removed when Task 4 replaces the legacy layout.

## Reference mapping

| Snapshot fact | Momentum Home reference consumer |
| --- | --- |
| `Active == null` | Ready hero: Start action; the repeat row may be shown if `Repeat` exists. |
| `Active.BodyParts` | Active hero title/body-area summary. Missing active definitions are omitted from this label so later UI can use its generic title, while counts remain exact. |
| `Active.ExerciseCount`, `LoggedExerciseCount`, `LoggedSetCount` | Active hero copy and progress: live logged exercises over live exercises, plus logged sets. This maps to the reference’s “3 of 5 exercises logged · 8 sets” form without inventing a planned-set total. |
| `Repeat.BodyParts`, `CompletedAt`, `ExerciseCount`, `LoggedSetCount`, `ThumbnailPath` | The single Ready-state **Train again** row: localized area summary, completion date, exercise/set metadata, and first ordered cached artwork. |
| `Repeat.Selections` | The later Train again action’s exact ordered workout input, preserving tracking modes and never copying historical sets. |

The no-silent-drop contract is enforced before a repeat record is created: a missing live definition or changed tracking mode rejects that candidate and scans the next newest completed workout. It never filters unavailable exercises from an otherwise “repeatable” shortcut.

## RED evidence

Command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter FullyQualifiedName~LocalTrainDashboardSourceTests --verbosity minimal -m:1
```

Before the implementation, compilation failed as intended: `ActiveWorkoutCard` had no `BodyParts` or `LoggedExerciseCount`, and `TrainDashboardSnapshot` had no `Repeat`.

## GREEN evidence

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter FullyQualifiedName~LocalTrainDashboardSourceTests --verbosity minimal -m:1
Passed: 3, Failed: 0, Skipped: 0

dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~LocalTrainDashboardSourceTests" --verbosity minimal -m:1
Passed: 7, Failed: 0, Skipped: 0
```

The first sandboxed combined invocation was aborted before tests ran because the test host could not bind its local loopback listener (`SocketException (13): Permission denied`); the required command was then rerun successfully in the permitted execution environment.

## Mutation targets covered

- Active graph: three live exercises, two with live sets, four live sets total; it also contains a tombstoned set and a tombstoned exercise.
- Exact repeat graph: weighted, assisted, and bodyweight entries are stored in order values `2, 0, 1` and become the deterministic `0, 1, 2` selection order with matching modes; it contains five live sets.
- Inexact repeat graphs: one missing definition and one changed tracking mode each yield no repeat shortcut.
- Fallback selection: a newer inexact completed workout is skipped in favor of the next newest exact candidate.
