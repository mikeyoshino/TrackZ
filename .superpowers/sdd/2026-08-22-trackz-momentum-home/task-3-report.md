# Task 3 — Safe Start, Continue, and Train Again Commands

## Delivered contract

- `TrainTodayViewModel` now exposes one state-aware `HeroActionCommand` and a separate
  `TrainAgainCommand`, plus `HeroActionText`, `ShowStartHero`, `ShowContinueHero`,
  `ShowTrainAgain`, and `CanMutate`.
- Commands remain unavailable until a local dashboard snapshot commits, while an action is
  mutating, and after an account boundary reset. Every command captures the current generation
  and rechecks it through `IAccountSessionBoundary.TryCommitAsync` before navigation.
- The hero routes to body-area picking in Ready state and to the active workout in Active state.
  Train again passes the exact ordered `WorkoutExerciseSelection` list to
  `ActiveWorkoutCoordinator.StartAsync`, so its tracking modes/order are preserved and no old
  sets are copied.
- `MauiTrainNavigator` owns Shell/body-area routing. `TrainPage` contains only page lifetime
  loading and all previous direct Shell routing was removed. Its existing controls now bind the
  view-model command as a transitional compatibility path until Task 4 replaces the XAML
  hierarchy.
- The navigator is registered in the MAUI composition root.

## Persistent HTML comparison

`docs/design/momentum-home-reference.html` was re-opened before implementation and checked
against the command state contract:

| HTML reference state | Native Task 3 result |
| --- | --- |
| Ready hero has one lime Start workout action | `ShowStartHero`, `HeroActionText`, and one `HeroActionCommand` route to the picker. |
| Active hero has one Continue action | `ShowContinueHero` routes only to `active-workout`. |
| Train again appears only in Ready | `ShowTrainAgain` requires a committed ready snapshot and exact repeat; it is false for every active workout. |
| Active state has no competing lime repeat action | `ShowTrainAgain` becomes false immediately when repeat start commits an active card. |

## RED evidence

1. Before production implementation:

   ```text
   dotnet test ... --filter "FullyQualifiedName~TrainAgainWorkoutTests|FullyQualifiedName~Hero_opens_picker"
   error CS0246: ITrainNavigator could not be found
   ```

2. The focused repeat failure test initially surfaced an uncaught coordinator validation failure:

   ```text
   System.ArgumentException: A workout needs unique valid exercise selections.
   ```

   The command now leaves local state/outbox unchanged, reports its localized failure state, and
   never navigates for this failure.

## GREEN evidence

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TrainAgainWorkoutTests|FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~ActiveWorkoutCoordinatorTests" \
  --verbosity minimal -m:1
Passed: 45, Failed: 0, Skipped: 0
```

The repeated test run required the approved local test environment because VSTest loopback socket
binding is denied inside the sandbox. No API, Docker, container, service, or simulator was started.

## Additional regression observation

`MauiCompositionTests` was attempted after the focused green command. It produced 9 existing
reference-assembly failures before exercising the new navigation composition:

```text
Microsoft.Maui.ApplicationModel.NotImplementedInReferenceAssemblyException
at Microsoft.Maui.Storage.FileSystem.get_AppDataDirectory()
at TrackZ.Mobile.MauiProgram ... line 119
```

This is a host/platform test limitation (the test run is using MAUI reference assemblies without a
platform `FileSystem` implementation), not a Task 3 assertion failure. Task 5 owns production DI
acceptance and must rerun this in its supported native test host.

## Mutation-sensitive coverage

- Replacing the hero branch with a fixed route fails the literal picker/active navigation test.
- Enabling commands before snapshot commit fails the unknown-state command test.
- Starting repeat with generic IDs/modes/order fails the real SQLite restart assertion for the
  weighted, assisted, and bodyweight sequence.
- Removing the command serialization or active transition permits duplicate start/navigation and
  fails the one-outbox/one-navigation assertion.
- Removing the generation checks allows navigation after a reset-while-committing test.
- Allowing a coordinator failure to navigate or write a partial operation fails the no-active,
  no-outbox, no-navigation test.
- Retaining the previous shortcut after reload fails the unavailable-repeat test.

## Files changed

- `src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs`
- `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- `src/TrackZ.Mobile/Features/Train/TrainNavigation.cs`
- `src/TrackZ.Mobile/Features/Train/TrainPage.xaml`
- `src/TrackZ.Mobile/Features/Train/TrainPage.xaml.cs`
- `src/TrackZ.Mobile/MauiProgram.cs`
- `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`
- `tests/TrackZ.Mobile.Tests/Train/TrainAgainWorkoutTests.cs`

`git diff --check` is clean. Manual simulator verification remains deferred until the user
explicitly asks to start services.
