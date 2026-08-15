# Task 6 Report: Native Workout and Set Logger UI

## Status

Implemented Task 6 from base `39f083e3795996f12d824a60401048c9aed274dd`. No Task 7 work was started.

## Delivered

- Added native MAUI `WorkoutPage` and `SetLoggerPage` plus code-native `LastSetTable`, `WeightStepper`, `RepsStepper`, and `SyncStatusPill` components.
- Added nonvisual `WorkoutViewModel`, `SetLoggerViewModel`, serialized `AsyncCommand`, workout resources, exact previous-session cache/source, and sync runner in `TrackZ.Mobile.Core`.
- Moved the existing nonvisual exercise/cache/view-model and sync sources into `TrackZ.Mobile.Core`, removing the linked-source project workaround. MAUI-only pages, code-behind, connectivity, secure storage, motion, and haptics remain in `TrackZ.Mobile`.
- Added English and Thai resource-backed workout copy. Numeric input has 44 px controls, numeric keyboards, semantic descriptions, mode-specific captions, and canonical kilogram state with pounds as display conversion only.
- Wired DI and Shell routes without removing the exercise catalog. Workout thumbnails come only from the existing authenticated/local cache path.

## Behavior and boundaries

- `MATCH LAST` targets `TodaySets.Count`, so the next set receives the corresponding previous-session set by index. Weighted, Bodyweight, and Assisted shapes remain distinct and exact previous/today order is retained.
- Set validation mirrors Domain bounds: reps `1..999`, kilograms `0.001..99999.999`, scale at most 3, and tracking-mode null/value shapes.
- Completion is serialized and local-first: await `ActiveWorkoutCoordinator.SaveSetAsync` (SQLite graph plus outbox), update Today and sync state, then invoke MAUI haptic/motion feedback. Network sync starts afterward and is not awaited by Complete Set.
- Rapid taps are ignored while the command is executing. Save failure, pre-commit account reset, stale account generation, or invalid measurement cannot invoke feedback. A haptic/motion adapter failure after the durable commit is isolated as best-effort feedback: Today remains saved, no save-error retry prompt is shown, and no duplicate local write occurs.
- Offline is represented by the pill, not an error toast. Pill states cover offline, pending, conflicted, syncing, and synced. The Task 5 conflict barrier remains authoritative because all writes still pass through `ActiveWorkoutCoordinator`.
- Exact previous-session sets use the authorized `/api/v1/exercises/{id}/history?pageSize=1` endpoint, validate and cache the full ordered session in private SQLite, and fall back to that cache offline. Account cleanup clears this cache.
- Exercise add/remove/reorder is intentionally pre-start only. Stable selected IDs have explicit order, and a single existing `StartWorkout` operation preserves it. No active-session reorder command or rejected `ReorderExercises` outbox action is emitted.
- No XP, levels, streaks, badges, poster-derived images, or other server-owned/fake achievements were added.

## TDD evidence

1. Initial logger tests failed to compile because `SetLoggerViewModel`, `IExerciseHistorySource`, and `ISetSavedFeedback` did not exist; the implemented slice then passed 17/17.
2. Exact-history HTTP/cache test failed because `IExerciseHistoryApi` did not exist; after implementation the logger suite passed 18/18.
3. Stable selection-order test failed with actual HashSet order `[first, second]` instead of reselected `[second, first]`; explicit ordered selection made it green.
4. Sync sequencing test failed because `IWorkoutSyncRunner` did not exist; the implemented runner now proves sync begins after feedback and does not delay command completion.
5. Malformed-history test failed by escaping `InvalidDataException`; localized safe load state now passes.
6. Account-reset-before-commit, save failure, rapid-tap, offline, restore, Thai localization, Domain validation, unit conversion, and stable StartWorkout order all exercise real `SetLoggerViewModel`/`WorkoutViewModel` boundaries backed by `ActiveWorkoutCoordinator`, SQLite, and the real outbox repository.
7. A post-commit feedback-failure regression first failed because the VM displayed `Could not save this set on your device` despite the SQLite row existing; after isolating best-effort feedback it passes with exactly one persisted row, one Today row, no save error, and released busy state.

## Verification

- `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter Workout --no-restore -m:1 -nr:false` — PASS, 52/52.
- `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore -m:1 -nr:false` — PASS, 193/193.
- `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter FullyQualifiedName~Architecture --no-restore -m:1 -nr:false` — PASS, 2/2.
- `dotnet build src/TrackZ.Mobile.Core/TrackZ.Mobile.Core.csproj --no-restore -m:1 -nr:false` — PASS, 0 warnings / 0 errors.
- `dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -nr:false -v:minimal` — PASS, including MAUI XAML source generation.
- Full iOS build was attempted and reached `actool`, then was environment-gated: no iPhone simulator runtime and CoreSimulatorService unavailable.
- Android build was attempted and environment-gated with `XA5300`: Android SDK directory not found.
- Server projects were not changed.

## Notes

- Mobile tests now disable test-collection parallelism, matching the requested sequential verification. This also removes a reproducible false failure where the existing gated thumbnail test saw only two of four workers during full-suite parallel contention; the test passed in isolation before this setting.
- Full device packaging still requires an installed Android SDK and an available iOS simulator runtime. The platform-independent Core build and iOS MAUI/XAML Compile target are green.
