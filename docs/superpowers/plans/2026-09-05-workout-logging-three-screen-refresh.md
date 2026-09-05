# Workout Logging Three-Screen Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the active-workout screen and the empty/editor states of the set logger match the three approved mobile mockups while preserving workout and set persistence behavior.

**Architecture:** Keep navigation and domain workflows unchanged. Add small presentation-only properties to the existing workout and set-logger view models, then compose the approved hierarchy in MAUI XAML using existing commands and design tokens. Protect state and interaction behavior with view-model tests and protect the approved native hierarchy with XAML visual-contract tests.

**Tech Stack:** .NET 10, C#, .NET MAUI XAML, xUnit.

**Spec:** The three approved mock images in the 2026-09-05 conversation: active workout, empty set logger, and set editor.

**Global constraints:** Preserve unrelated dirty-worktree changes; do not change API services, persistence, or Docker resources; do not commit from this shared dirty workspace.

---

### Task 1: Lock the approved behavior in tests

**Files:**
- Modify: `tests/TrackZ.Mobile.Tests/Workout/WorkoutViewModelTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Workout/SetLoggerViewModelTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/TodayWorkoutVisualContractTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/TrackSetsVisualContractTests.cs`

**Steps:**
1. Add a workout-view-model test proving progress is derived from exercises with at least one logged set.
2. Add set-logger tests proving validation feedback is hidden before a save attempt and shown after an invalid save attempt.
3. Update native visual-contract expectations for compact cards, the progress strip, reordered workout actions, hidden set-logger tab bar, empty-state section, segmented unit control, numeric cards, and disabled invalid save action.
4. Run the focused tests and confirm they fail for the missing production behavior.

### Task 2: Add presentation state to the view models

**Files:**
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx`

**Steps:**
1. Add workout exercise/progress count properties and notify them whenever the materialized exercise list or set counts change.
2. Add explicit save-attempt state so draft validation is not announced before interaction.
3. Add display properties needed by the mock: today-set count, next-set action, unit selection state, and helper/empty-state copy.
4. Run the focused view-model tests and confirm they pass.

### Task 3: Implement the active-workout mock

**Files:**
- Modify: `src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.xaml`
- Modify: `src/TrackZ.Mobile/Features/Workout/WorkoutPage.xaml`
- Modify: `src/TrackZ.Mobile/Resources/Styles/TrackZControls.xaml` if a reusable compact token is required.

**Steps:**
1. Reduce the exercise row to the approved compact hierarchy with 72px artwork, two-line exercise name, status text, compact lime set badge, and chevron.
2. Add the compact progress label and track beneath the workout summary.
3. Reorder and resize the sticky actions so the next-set action is full width, followed by add/finish and the destructive cancel action.
4. Run the workout visual-contract and view-model tests.

### Task 4: Implement the empty and editing set-logger mocks

**Files:**
- Modify: `src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml.cs` only if focus/touched behavior cannot remain in the view model.

**Steps:**
1. Hide the bottom tab bar and build the compact exercise identity header.
2. Render the empty `เซ็ตวันนี้` section and sticky `เพิ่มเซ็ตที่ 1` action when no draft exists.
3. Restyle the editor card with a close icon, unit segmented buttons, two numeric cards, helper feedback, and a disabled save action until valid.
4. Keep previous-workout and saved-set information below the primary flow without changing its data behavior.
5. Run the set-logger visual-contract and view-model tests.

### Task 5: Verify the complete mobile application

**Files:**
- Verify only.

**Steps:**
1. Run `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore`.
2. Run the repository test suite if the focused mobile suite passes.
3. Build the iOS simulator target.
4. Install/launch the refreshed app on the existing simulator without touching any API process.
5. Report exact test/build results and the files changed for this refresh.
