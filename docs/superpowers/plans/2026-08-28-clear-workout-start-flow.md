# Clear Workout Start Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make starting a workout unmistakable by opening the first exercise's set logger immediately and presenting a visually distinct active-workout action state when the user returns.

**Architecture:** Keep workout persistence and state ownership in `WorkoutViewModel`. Expose navigation intent through a workout-exercise event so the MAUI page remains responsible for Shell routing, and bind the draft/active presentation directly to existing `IsDraft` and `HasStarted` state.

**Tech Stack:** .NET 9, C#, .NET MAUI XAML, xUnit

**Spec:** Approved bounded design in the 2026-08-28 conversation; no separate spec file was created.

## Global Constraints

- After a successful start, open the set logger for the first selected exercise immediately.
- Draft and active states must use different primary actions without a redundant workout-state badge.
- The active primary action opens the next unlogged exercise, falling back to the first exercise when every exercise has a logged set.
- `Finish workout` must not replace `Start workout` in the same primary-button slot.
- Preserve all existing uncommitted plate-count work and avoid unrelated changes.
- Provide English and Thai copy for every new user-facing string.

---

### Task 1: Workout navigation intent and active-state presentation

**Files:**
- Modify: `tests/TrackZ.Mobile.Tests/Workout/WorkoutViewModelTests.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutViewModel.cs`

**Interfaces:**
- Produces: `event EventHandler<WorkoutExerciseDraftItem>? ExerciseLoggingRequested`
- Produces: `AsyncCommand LogNextSetCommand`

- [ ] **Step 1: Write failing start-navigation test**

Add a test that creates a draft with two exercises, subscribes to `ExerciseLoggingRequested`, executes `StartWorkoutCommand`, and asserts the event carries the first selected exercise only after `HasStarted` becomes `true`.

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter FullyQualifiedName~WorkoutViewModelTests.Successful_start_requests_logging_for_the_first_exercise`

Expected: compilation failure because `ExerciseLoggingRequested` does not exist.

- [ ] **Step 3: Implement the minimal start-navigation intent**

Add `ExerciseLoggingRequested` and raise it with `Exercises[0]` immediately after the durable start has populated workout-exercise IDs and set `HasStarted = true`. Do not raise it on failure or cancellation.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Step 2 command and expect one passing test.

- [ ] **Step 5: Write failing next-exercise tests**

Add one test proving `LogNextSetCommand` selects the first exercise with `LoggedSetCount == 0`, and one proving it falls back to the first exercise when all exercises have logged sets.

- [ ] **Step 6: Run the next-exercise tests and verify RED**

Run: `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter FullyQualifiedName~WorkoutViewModelTests.Log_next_set`

Expected: compilation failure because `LogNextSetCommand` does not exist.

- [ ] **Step 7: Implement the minimal active primary action**

Create `LogNextSetCommand`, enable it only when `HasStarted && Exercises.Count > 0 && !IsBusy`, and raise `ExerciseLoggingRequested` with `Exercises.FirstOrDefault(x => x.LoggedSetCount == 0) ?? Exercises[0]`. Raise its command state alongside the existing workout commands.

- [ ] **Step 8: Verify workout behavior regressions**

Run all `WorkoutViewModelTests` and expect them to pass.

### Task 2: Distinct draft and active workout UI

**Files:**
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/TodayWorkoutVisualContractTests.cs`
- Modify: `src/TrackZ.Mobile/Features/Workout/WorkoutPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Workout/WorkoutPage.xaml.cs`

**Interfaces:**
- Consumes: `WorkoutViewModel.ExerciseLoggingRequested`
- Consumes: `WorkoutViewModel.LogNextSetCommand`

- [ ] **Step 1: Write the failing visual-contract test**

Assert that the page contains no `WorkoutStateBadge`, a draft primary button bound to `StartFirstExercise`, an active primary button bound to `LogNextSetCommand` and `LogNextWorkoutSet`, and a separate `FinishWorkoutButton` using the secondary button style.

- [ ] **Step 2: Run the visual-contract test and verify RED**

Run: `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter FullyQualifiedName~TodayWorkoutVisualContractTests.Native_page_distinguishes_preparation_from_active_training`

Expected: failure because the named controls and bindings are absent.

- [ ] **Step 3: Implement the distinct XAML states**

Keep only the exercise/set context near the top, with no redundant state badge. Keep `Add exercise` available in both states. Show `Start first exercise` only for drafts; show `Log next set` as the primary active action; place `Finish workout` on its own row with `TrackZSecondaryButtonStyle`; retain the quiet destructive discard action below it.

- [ ] **Step 4: Connect navigation once in the page lifecycle**

Subscribe to `ExerciseLoggingRequested` in the constructor and route it through the existing set-logger navigation method. Unsubscribe from this and the existing view-model events in `OnDisappearing` only if the page is permanently disposed; otherwise retain the page-scoped subscriptions consistently with the current Shell lifetime.

- [ ] **Step 5: Run visual and view-model tests and verify GREEN**

Run: `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter "FullyQualifiedName~TodayWorkoutVisualContractTests|FullyQualifiedName~WorkoutViewModelTests"`

Expected: all selected tests pass.

### Task 3: Localized action copy and regression verification

**Files:**
- Modify: `tests/TrackZ.Mobile.Tests/Workout/WorkoutViewModelTests.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx`

**Interfaces:**
- Produces: `WorkoutTextSet.StartFirstExercise`
- Produces: `WorkoutTextSet.LogNextWorkoutSet`

- [ ] **Step 1: Write the failing localization test**

Assert literal English values `Start first exercise` and `Log next set`, plus Thai values `เริ่มท่าแรก` and `บันทึกเซ็ตถัดไป` from `WorkoutResources.ForCulture`. Use the new key `LogNextWorkoutSet` because the existing `LogNextSet` key belongs to the set logger's `+ Add set` action.

- [ ] **Step 2: Run the localization test and verify RED**

Run: `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter FullyQualifiedName~WorkoutViewModelTests.Workout_start_flow_copy_is_localized_in_english_and_thai`

Expected: compilation failure because the new `WorkoutTextSet` properties do not exist.

- [ ] **Step 3: Add the localized resources and resource mappings**

Add the three keys to both RESX files, append the three values to `WorkoutTextSet`, and map them in `WorkoutResources.Load` without changing existing plate-count keys or values.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run all workout view-model, visual-contract, and localization-audit tests. Expect all to pass.

- [ ] **Step 5: Run the mobile regression suite**

Run: `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj`

Expected: zero failed tests and zero build errors.
