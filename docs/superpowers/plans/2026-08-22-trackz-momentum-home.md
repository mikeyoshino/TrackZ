# TrackZ Momentum Home Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the approved native Momentum Home with a state-aware Start/Continue hero, truthful weekly motivation, exact kg/lb performance memory, and an offline-first Train again action.

**Architecture:** `TrainTodayViewModel` composes local workout state from `ITrainDashboardSource` with cached/refreshed progress from the existing `IProgressSnapshotSource`. Repeat creation delegates to `ActiveWorkoutCoordinator`, while a new app-owned navigation boundary keeps Shell mechanics out of Core. `TrainPage` renders one primary button whose command and copy change with state, so Start and Continue can never coexist as two lime actions.

**Tech Stack:** .NET 10, C# 14, .NET MAUI native XAML, SQLite via `Microsoft.Data.Sqlite`, xUnit, existing Clean Architecture boundaries, existing progress cache/API and workout outbox.

**Spec:** `docs/superpowers/specs/2026-08-22-trackz-momentum-home-design.md`

**Approved UI Reference:** `docs/design/momentum-home-reference.html`

## Global Constraints

- Do not add a Home-specific backend endpoint or database migration.
- Do not recommend exercises, generate workout plans, or describe TrackZ as a coach.
- Start/Continue is the only lime primary action on Home.
- Continue opens `active-workout`, never an individual exercise.
- The motivation strip displays the authoritative Weekly goal, current weekly Streak, and Level/XP selected in brainstorming.
- Recent momentum displays exact Last and Best values rather than claiming a newly achieved PR.
- Train again preserves every live exercise definition ID, tracking mode, and order; it never silently drops an unavailable exercise.
- Canonical stored/API weights remain kilograms; Home displays kg with up to three decimals and lb with exactly two decimals.
- Use authoritative cached/refreshed progress only; never fabricate zeros, streaks, XP, levels, or personal records.
- Treat `docs/design/momentum-home-reference.html` as the visual source of truth during implementation and review; production remains native MAUI XAML.
- Preserve cached progress on refresh failure and keep local training independent of network latency.
- All asynchronous state commits and navigation are fenced by `IAccountSessionBoundary` generation.
- All new user copy and accessibility descriptions ship in English and Thai.
- Interactive targets remain at least 44 points and use shared native visual tokens.
- Honor the existing Reduce Motion preference and add no mandatory decorative animation.
- Do not start API, Docker, containers, or Simulator during automated implementation. Start them only after the user explicitly requests the manual walkthrough.
- Each task records RED and GREEN commands/counts in `.superpowers/sdd/2026-08-22-trackz-momentum-home/`.

## File Structure

- Modify `src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs` — local active/repeat snapshot contracts and navigation interface.
- Modify `src/TrackZ.Mobile.Core/Features/Train/LocalTrainDashboardSource.cs` — derive exact active and repeat facts from SQLite/cache data.
- Create `src/TrackZ.Mobile.Core/Features/Train/HomeMomentumItem.cs` — exact Last/Best presentation with shared kg/lb preference lifecycle.
- Modify `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs` — two-stage local/progress loading, state-aware hero, safe commands, and account fencing.
- Create `src/TrackZ.Mobile/Features/Train/TrainNavigation.cs` — MAUI body-area and active-workout route adapter.
- Modify `src/TrackZ.Mobile/Features/Train/TrainPage.xaml` — approved Momentum Home hierarchy.
- Modify `src/TrackZ.Mobile/Features/Train/TrainPage.xaml.cs` — page lifetime only; no direct Shell routing.
- Modify `src/TrackZ.Mobile/Resources/Styles/TrackZControls.xaml` — shared Home hero/metric styles if the page would otherwise repeat setters.
- Modify `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs` — typed access to the new localized Home copy.
- Modify `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx` — English Home copy.
- Modify `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx` — Thai Home copy.
- Modify `src/TrackZ.Mobile/MauiProgram.cs` — navigation and expanded view-model composition.
- Reference `docs/design/momentum-home-reference.html` — persistent approved ready/active, kg/lb, and EN/TH visual states.
- Modify `tests/TrackZ.Mobile.Tests/Train/LocalTrainDashboardSourceTests.cs` — exact local snapshot and repeat eligibility.
- Create `tests/TrackZ.Mobile.Tests/Train/HomeMomentumItemTests.cs` — weighted, assisted, bodyweight, and live kg/lb presentation.
- Modify `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs` — truthful state, cache/refresh, units, and command state matrix.
- Create `tests/TrackZ.Mobile.Tests/Train/TrainAgainWorkoutTests.cs` — real SQLite repeat transaction/idempotency/restart coverage.
- Create `tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs` — inflated MAUI hierarchy, states, targets, semantics, and localization.
- Modify `tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs` — Train-specific structural and primary-action mutation checks.
- Modify `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs` — real provider lifetimes and navigation composition.
- Create `.superpowers/sdd/2026-08-22-trackz-momentum-home/momentum-home-report.md` — requirement-to-evidence ledger and simulator-pending status.

---

### Task 1: Exact Local Active and Repeat Snapshot

**Files:**
- Modify: `src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Train/LocalTrainDashboardSource.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Train/LocalTrainDashboardSourceTests.cs`
- Create: `.superpowers/sdd/2026-08-22-trackz-momentum-home/task-1-report.md`

**Interfaces:**
- Consumes: `ILocalWorkoutRepository.GetActiveAsync`, `ILocalWorkoutRepository.GetHistoryAsync`, `ExerciseCache.GetAllAsync`, and `WorkoutExerciseSelection(Guid ExerciseDefinitionId, TrackingMode TrackingMode)`.
- Produces:

```csharp
public sealed record ActiveWorkoutCard(
    Guid WorkoutId,
    DateTimeOffset StartedAt,
    IReadOnlyList<BodyPart> BodyParts,
    int ExerciseCount,
    int LoggedExerciseCount,
    int LoggedSetCount);

public sealed record RepeatWorkoutShortcut(
    Guid SourceWorkoutId,
    IReadOnlyList<BodyPart> BodyParts,
    DateTimeOffset CompletedAt,
    int ExerciseCount,
    int LoggedSetCount,
    string? ThumbnailPath,
    IReadOnlyList<WorkoutExerciseSelection> Selections);

public sealed record TrainDashboardSnapshot(
    ActiveWorkoutCard? Active,
    RepeatWorkoutShortcut? Repeat);
```

- [ ] **Step 1: Write the failing exact active/repeat tests**

Add tests with literal expected values. One graph contains a tombstoned exercise and set; one completed workout contains weighted, assisted, and bodyweight exercises in order `2, 0, 1` as stored input but sorted to `0, 1, 2` for repeat.

```csharp
[Fact]
public async Task Source_returns_exact_active_progress_and_newest_repeatable_selection_order()
{
    var snapshot = await CreateSourceWithMixedModes().LoadAsync();

    Assert.Equal([BodyPart.Chest, BodyPart.Back], snapshot.Active!.BodyParts);
    Assert.Equal(3, snapshot.Active.ExerciseCount);
    Assert.Equal(2, snapshot.Active.LoggedExerciseCount);
    Assert.Equal(4, snapshot.Active.LoggedSetCount);
    Assert.Equal([weightedId, assistedId, bodyweightId],
        snapshot.Repeat!.Selections.Select(x => x.ExerciseDefinitionId));
    Assert.Equal([TrackingMode.Weighted, TrackingMode.Assisted, TrackingMode.Bodyweight],
        snapshot.Repeat!.Selections.Select(x => x.TrackingMode));
    Assert.Equal(5, snapshot.Repeat.LoggedSetCount);
}

[Fact]
public async Task Source_refuses_partial_repeat_when_any_live_definition_is_missing_or_mode_changed()
{
    var missing = await CreateSourceWithMissingDefinition().LoadAsync();
    var changedMode = await CreateSourceWithChangedDefinitionMode().LoadAsync();

    Assert.Null(missing.Repeat);
    Assert.Null(changedMode.Repeat);
}

[Fact]
public async Task Source_skips_a_newer_inexact_workout_and_returns_the_next_exact_repeat()
{
    var snapshot = await CreateSourceWithNewerInexactAndOlderExactWorkouts().LoadAsync();

    Assert.Equal(olderExactWorkoutId, snapshot.Repeat!.SourceWorkoutId);
    Assert.Equal([weightedId, assistedId], snapshot.Repeat.Selections.Select(x => x.ExerciseDefinitionId));
}
```

- [ ] **Step 2: Run Task 1 tests and capture RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter FullyQualifiedName~LocalTrainDashboardSourceTests --verbosity minimal -m:1
```

Expected: compilation fails because `Repeat`, `BodyParts`, `LoggedExerciseCount`, and `Selections` do not exist.

- [ ] **Step 3: Replace the recent-list contract with exact active/repeat records**

Implement the signatures in the Interfaces block. Do not retain a three-item `Recent` list solely for the removed layout.

- [ ] **Step 4: Derive active facts only from live rows**

Use ordered live exercises and live sets:

```csharp
var liveActive = active?.Exercises
    .Where(x => x.DeletedAt is null)
    .OrderBy(x => x.Order)
    .ToArray() ?? [];
var loggedExerciseCount = liveActive.Count(x => x.Sets.Any(set => set.DeletedAt is null));
var loggedSetCount = liveActive.Sum(x => x.Sets.Count(set => set.DeletedAt is null));
```

Resolve body areas from the complete definition dictionary and keep first-occurrence order with `Distinct()`.

- [ ] **Step 5: Select the newest exactly repeatable completed workout**

Order completed live workouts by `CompletedAt` descending and `Id` descending. For each candidate, resolve every live exercise in exercise order. Accept the first candidate only when every definition exists and `definition.TrackingMode == workoutExercise.TrackingMode`; otherwise continue to the next completed candidate. Build `WorkoutExerciseSelection` without copying sets.

- [ ] **Step 6: Run Task 1 tests and the existing Train tests for GREEN**

Run the Task 1 command. Expected: all `LocalTrainDashboardSourceTests` pass. Then run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~LocalTrainDashboardSourceTests" --verbosity minimal -m:1
```

Update existing view-model fixtures from `new(activeId, At(9), 2, 4)` to `new(activeId, At(9), [BodyPart.Chest], 2, 1, 4)` and replace recent arrays with one `RepeatWorkoutShortcut` carrying literal ordered selections. Expected: all selected tests pass without changing their behavior assertions.

- [ ] **Step 7: Record evidence and commit Task 1**

Write RED/GREEN output, mutation targets, and changed contracts to `task-1-report.md`, then commit only Task 1 files:

```bash
git add src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs src/TrackZ.Mobile.Core/Features/Train/LocalTrainDashboardSource.cs tests/TrackZ.Mobile.Tests/Train/LocalTrainDashboardSourceTests.cs
git add -f .superpowers/sdd/2026-08-22-trackz-momentum-home/task-1-report.md
git commit -m "feat: expose repeatable train dashboard state"
```

---

### Task 2: Truthful Motivation and Unit-Aware Recent Momentum

**Files:**
- Create: `src/TrackZ.Mobile.Core/Features/Train/HomeMomentumItem.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- Create: `tests/TrackZ.Mobile.Tests/Train/HomeMomentumItemTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`
- Create: `.superpowers/sdd/2026-08-22-trackz-momentum-home/task-2-report.md`

**Interfaces:**
- Consumes: `IProgressSnapshotSource`, `IConnectivityService`, `IWeightUnitPreference`, `GamificationTextSet`, `ProgressSnapshot`, and Task 1 `TrainDashboardSnapshot`.
- Produces:

```csharp
public sealed class HomeMomentumItem : INotifyPropertyChanged, IDisposable
{
    public ExerciseProgressSummaryDto Source { get; }
    public string ExerciseName { get; }
    public string LastText { get; }
    public string BestText { get; }
    public void Dispose();
}
```

`TrainTodayViewModel` adds literal state properties: `HasAuthoritativeProgress`, `WeeklyCompletedWorkouts`, `WeeklyGoal`, `CurrentStreakWeeks`, `Level`, `TotalXp`, `LevelProgress`, `HomeMomentumItem? RecentMomentum`, `HasRecentMomentum`, `IsProgressLoading`, and `RepeatWorkoutShortcut? RepeatWorkout`.

- [ ] **Step 1: Write failing cached/refresh/no-authority and recent-momentum tests**

```csharp
[Fact]
public async Task Local_state_commits_before_gated_progress_refresh_and_cached_values_survive_failure()
{
    var progress = new CachedThenGatedFailingProgressSource(Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640));
    var vm = CreateViewModel(progress, online: true);

    var load = vm.LoadAsync();
    await progress.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.NotNull(vm.ActiveWorkout);
    Assert.Equal(3, vm.WeeklyCompletedWorkouts);
    Assert.Equal(8, vm.Level);
    progress.FailRefresh();
    await load;

    Assert.True(vm.HasAuthoritativeProgress);
    Assert.Equal(640, vm.TotalXp);
}

[Fact]
public async Task No_cache_offline_hides_motivation_instead_of_fabricating_zeroes()
{
    var vm = CreateViewModel(new EmptyProgressSource(), online: false);
    await vm.LoadAsync();

    Assert.False(vm.HasAuthoritativeProgress);
    Assert.False(vm.HasRecentMomentum);
}

[Theory]
[InlineData(WeightDisplayUnit.Kilograms, "Last 70.125 kg × 8", "Best 72.5 kg × 6")]
[InlineData(WeightDisplayUnit.Pounds, "Last 154.60 lb × 8", "Best 159.84 lb × 6")]
public void Recent_momentum_formats_exact_weighted_last_and_best(
    WeightDisplayUnit unit, string expectedLast, string expectedBest)
{
    using var item = WeightedMomentum(unit, 70.125m, 8, 72.5m, 6);
    Assert.Equal(expectedLast, item.LastText);
    Assert.Equal(expectedBest, item.BestText);
}
```

Add equivalent literal assertions for assisted and bodyweight modes. Add a preference-change test that creates one item, calls `preference.Set(Pounds)`, and asserts `PropertyChanged` raises for both `LastText` and `BestText` without replacing the item.

- [ ] **Step 2: Run Task 2 tests and capture RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~HomeMomentumItemTests" --verbosity minimal -m:1
```

Expected: compilation fails because `HomeMomentumItem` and authoritative progress properties are absent.

- [ ] **Step 3: Implement `HomeMomentumItem` with exact mode-aware formatting**

Use `2.204622621848775807m`, `MidpointRounding.AwayFromZero`, `0.###` for kg, and `0.00` for lb. Weighted uses `LastWeightKg`/`BestWeightKg`; assisted uses `LastAssistedKg`/`BestAssistedKg` plus localized assistance copy; bodyweight uses reps only. Subscribe once to `IWeightUnitPreference.Changed`; `Dispose` unsubscribes.

- [ ] **Step 4: Implement two-stage load with generation fencing**

In `LoadAsync`, load and commit `ITrainDashboardSource` first. Independently read `GetCachedAsync`, apply it when non-null, then call `RefreshAsync` only when online. Wrap each `TryCommitAsync` with the captured generation. A refresh exception preserves applied cached values. Subscribe the singleton view model once to `IAccountSessionBoundary.SessionReset`; reset clears active, repeat, metrics, and the disposed momentum item even when no load is running.

Choose recent momentum with:

```csharp
var recent = snapshot.Summary.PersonalRecords
    .OrderByDescending(x => x.LastPerformedAt)
    .ThenByDescending(x => x.ExerciseId)
    .FirstOrDefault();
```

Dispose the old item before replacement or clear.

- [ ] **Step 5: Prove stale-account and backward refresh cannot overwrite Home**

Add one test that resets `AccountSessionBoundary` after cached application but before refresh release. Assert the delayed refresh does not restore level, XP, repeat, active workout, or recent momentum.

- [ ] **Step 6: Run Task 2 GREEN and related gamification regression**

Run the Task 2 command, then:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~ProgressDashboardViewModelTests|FullyQualifiedName~GamificationViewModelTests" --verbosity minimal -m:1
```

Expected: all selected tests pass and cached-success → refresh-failure behavior remains authoritative in both Home and Progress.

- [ ] **Step 7: Record evidence and commit Task 2**

```bash
git add src/TrackZ.Mobile.Core/Features/Train/HomeMomentumItem.cs src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs tests/TrackZ.Mobile.Tests/Train/HomeMomentumItemTests.cs tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs
git add -f .superpowers/sdd/2026-08-22-trackz-momentum-home/task-2-report.md
git commit -m "feat: present truthful home momentum"
```

---

### Task 3: Safe Start, Continue, and Train Again Commands

**Files:**
- Modify: `src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Train/TrainNavigation.cs`
- Modify: `src/TrackZ.Mobile/Features/Train/TrainPage.xaml.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`
- Create: `tests/TrackZ.Mobile.Tests/Train/TrainAgainWorkoutTests.cs`
- Create: `.superpowers/sdd/2026-08-22-trackz-momentum-home/task-3-report.md`

**Interfaces:**
- Consumes: `ActiveWorkoutCoordinator.StartAsync(IReadOnlyList<WorkoutExerciseSelection>, Guid?, Guid?, CancellationToken)` and Task 1 `RepeatWorkoutShortcut.Selections`.
- Produces:

```csharp
public interface ITrainNavigator
{
    Task OpenWorkoutPickerAsync(CancellationToken cancellationToken = default);
    Task OpenActiveWorkoutAsync(CancellationToken cancellationToken = default);
}

public sealed class MauiTrainNavigator(
    IBodyAreaPicker bodyAreaPicker) : ITrainNavigator;
```

`TrainTodayViewModel` produces `AsyncCommand HeroActionCommand`, `AsyncCommand TrainAgainCommand`, `string HeroActionText`, `bool ShowStartHero`, `bool ShowContinueHero`, `bool ShowTrainAgain`, and `bool CanMutate`.

- [ ] **Step 1: Write failing command routing and state tests**

```csharp
[Fact]
public async Task Hero_opens_picker_without_active_workout_and_workout_list_when_active()
{
    var noActive = CreateCommandViewModel(active: null);
    await noActive.LoadAsync();
    await noActive.HeroActionCommand.ExecuteAsync();
    Assert.Equal(["picker"], noActive.Navigator.Events);

    var active = CreateCommandViewModel(active: ActiveCard());
    await active.LoadAsync();
    await active.HeroActionCommand.ExecuteAsync();
    Assert.Equal(["active-workout"], active.Navigator.Events);
}
```

Assert `ShowStartHero` and `ShowContinueHero` are mutually exclusive and `ShowTrainAgain` is false whenever active exists.

- [ ] **Step 2: Write the real SQLite repeat RED**

Create a completed workout with weighted, assisted, and bodyweight selections, close all SQLite pools, recreate `TrackZLocalDatabase`, then execute `TrainAgainCommand` twice concurrently. Assert before implementation that the command/type is missing. The final literal assertions are:

```csharp
Assert.Equal([weightedId, assistedId, bodyweightId],
    active.Exercises.OrderBy(x => x.Order).Select(x => x.ExerciseDefinitionId));
Assert.Equal([TrackingMode.Weighted, TrackingMode.Assisted, TrackingMode.Bodyweight],
    active.Exercises.OrderBy(x => x.Order).Select(x => x.TrackingMode));
Assert.Empty(active.Exercises.SelectMany(x => x.Sets));
Assert.Single((await outbox.PendingAsync()).Where(x => x.Type == OutboxOperationType.StartWorkout));
Assert.Equal(1, navigator.OpenActiveWorkoutCount);
```

- [ ] **Step 3: Run Task 3 tests and capture RED**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainAgainWorkoutTests|FullyQualifiedName~Hero_opens_picker" --verbosity minimal -m:1
```

Expected: compilation fails on missing `ITrainNavigator`, commands, and repeat behavior.

- [ ] **Step 4: Implement serialized state-aware commands**

Construct one `HeroActionCommand` whose handler captures the current generation at invocation and branches only on the committed `ActiveWorkout` value. `TrainAgainCommand` passes the exact ordered selections to `ActiveWorkoutCoordinator.StartAsync`. Disable both commands while local dashboard state is unknown, mutation is running, or the boundary is resetting. Re-check generation before calling navigation.

- [ ] **Step 5: Implement the MAUI navigation adapter**

`OpenWorkoutPickerAsync` calls `IBodyAreaPicker.PickAsync`; on a selected body part it routes to `ExercisePickerPage?bodyPart={(int)selected}`. `OpenActiveWorkoutAsync` routes to `active-workout`. Move all direct routing out of `TrainPage.xaml.cs`; the page only calls `LoadAsync` on appearance and `Deactivate` on disappearance when required by the final view-model lifetime.

- [ ] **Step 6: Add failure, reset, and missing-definition assertions**

Cover definite coordinator failure, boundary reset while Start is committing, and repeat becoming unavailable after reload. Assert no stale navigation, one-active-workout invariant, unchanged source history, and no partial outbox write.

- [ ] **Step 7: Run Task 3 GREEN and workout/outbox regressions**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainAgainWorkoutTests|FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~ActiveWorkoutCoordinatorTests" --verbosity minimal -m:1
```

Expected: all selected tests pass across SQLite restart and concurrent double execution.

- [ ] **Step 8: Record evidence and commit Task 3**

```bash
git add src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs src/TrackZ.Mobile/Features/Train/TrainNavigation.cs src/TrackZ.Mobile/Features/Train/TrainPage.xaml.cs tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs tests/TrackZ.Mobile.Tests/Train/TrainAgainWorkoutTests.cs
git add -f .superpowers/sdd/2026-08-22-trackz-momentum-home/task-3-report.md
git commit -m "feat: add safe momentum home actions"
```

---

### Task 4: Native Momentum Home Visual Hierarchy and Localization

**Files:**
- Modify: `src/TrackZ.Mobile/Features/Train/TrainPage.xaml`
- Modify: `src/TrackZ.Mobile/Resources/Styles/TrackZControls.xaml`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx`
- Create: `tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs`
- Create: `.superpowers/sdd/2026-08-22-trackz-momentum-home/task-4-report.md`

**Interfaces:**
- Consumes: Task 2 motivation properties and Task 3 commands/state properties.
- Produces named real-MAUI elements: `MomentumHomeScroll`, `HomeHero`, `HeroActionButton`, `MotivationStrip`, `WeeklyGoalMetric`, `StreakMetric`, `LevelMetric`, `TrainAgainCard`, `TrainAgainArtwork`, and `RecentMomentumCard`.

- [ ] **Step 1: Write exact English and Thai resource contract tests**

Write tests that reference these required new typed properties before they exist: `ReadyWhenYouAre`, `YouAreInMotion`, `StartTraining`, `ChooseTodaysWorkout`, `ChooseWorkoutSupporting`, `WorkoutInProgress`, `ContinueWorkout`, `ThisWeek`, `WeekStreak`, `LevelXpFormat`, `TrainAgain`, `RecentMomentum`, `LastBestFormat`, `HomeExerciseProgressFormat`, `RepeatWorkoutAccessibilityFormat`, and `HomeLoadFailed`. The tests also assert the existing `StartWorkout` and `TryAgain` values are reused rather than duplicated.

Test `WorkoutResources.English` and `WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"))` contain non-empty, culturally distinct values and exact English primary actions `Start workout` / `Continue workout`.

- [ ] **Step 2: Write the inflated page RED tests**

Resolve `TrainPage` from a real `MauiApp`, inflate the page, and assert:

```csharp
var primary = Assert.IsType<Button>(page.FindByName("HeroActionButton"));
Assert.True(primary.MinimumHeightRequest >= 44);
Assert.Same(vm.HeroActionCommand, primary.Command);
Assert.Single(Descendants(page).OfType<Button>()
    .Where(button => ReferenceEquals(button.Style, app.Resources["TrackZPrimaryButtonStyle"])));
Assert.True(page.FindByName<Grid>("MotivationStrip").IsVisible);
Assert.True(page.FindByName<Border>("TrainAgainCard").IsVisible);
Assert.True(page.FindByName<Border>("RecentMomentumCard").IsVisible);
```

Add active/no-authority/empty-repeat state rows and assert mutually exclusive visibility with one unchanged primary button instance.

- [ ] **Step 3: Run Task 4 tests and capture RED**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~MomentumHomePresentationTests|FullyQualifiedName~AppWideVisualConsistencyTests" --verbosity minimal -m:1
```

Expected: compilation or `FindByName` assertions fail because the Momentum Home resources and named hierarchy do not exist.

- [ ] **Step 4: Implement localized resources and replace `TrainPage` with the approved hierarchy**

Open `docs/design/momentum-home-reference.html` and compare both state toggles before editing XAML. Add all new Step 1 keys to `WorkoutTextSet`, `WorkoutResources.ForCulture`, and both RESX files with reviewed English/Thai copy. Then use a `ScrollView` and direct page-padding owner. Place header, one hero `Border`, one `HeroActionButton`, equal-height metric cards, Train again, and Recent momentum in that order. Bind one button to `HeroActionText` and `HeroActionCommand`. Use state triggers to change hero emphasis without changing padding, minimum height, or text baseline. Do not add another primary button to a hidden branch.

- [ ] **Step 5: Apply shared visual tokens and semantics**

Use `TrackZPageTitleStyle`, `TrackZSectionTitleStyle`, `TrackZBodyStyle`, `TrackZSecondaryStyle`, `TrackZPrimaryButtonStyle`, `TrackZCardStyle` or one shared `TrackZHomeMetricCardStyle`, `TrackZPageHorizontalPadding`, and 4-point spacing tokens. Bind action-specific `SemanticProperties.Description`. Artwork reserves `TrackZExerciseArtworkColumnWidth`, `TrackZExerciseArtworkSize`, and the shared gap.

- [ ] **Step 6: Harden structural mutation tests**

Update `AppWideVisualConsistencyTests` so Train is allowed to place its primary action inside `HomeHero` rather than the removed sticky footer. Add deliberate mutations that must fail: duplicate the primary button, move it outside `HomeHero`, set its static opacity to zero, change metric card minimum height, change artwork width from the semantic token, and place Train again above the hero.

- [ ] **Step 7: Run Task 4 GREEN and iOS XAML Compile**

Run the Task 4 test command, then:

```bash
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -v:minimal
```

Expected: focused tests pass and XAML Compile exits 0 with no product warning/error.

- [ ] **Step 8: Record evidence and commit Task 4**

```bash
git add src/TrackZ.Mobile/Features/Train/TrainPage.xaml src/TrackZ.Mobile/Resources/Styles/TrackZControls.xaml src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs
git add -f .superpowers/sdd/2026-08-22-trackz-momentum-home/task-4-report.md
git commit -m "feat: build native momentum home"
```

---

### Task 5: Production Composition and End-to-End Acceptance Contract

**Files:**
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/AccessibilitySemanticsTests.cs`
- Create: `.superpowers/sdd/2026-08-22-trackz-momentum-home/task-5-report.md`

**Interfaces:**
- Consumes: `ITrainNavigator`, `MauiTrainNavigator`, expanded `TrainTodayViewModel`, existing singleton `IProgressSnapshotSource`, `IWeightUnitPreference`, `ActiveWorkoutCoordinator`, and `IAccountSessionBoundary`.
- Produces: one real provider graph where both `TrainPage` and `TrainTodayViewModel` are explicit singletons, and all mutable account-scoped collaborators retain their existing singleton identities.

- [ ] **Step 1: Write the real-provider composition RED**

Create a temp-data `MauiApp`, replace only external API/connectivity services, resolve `TrainPage`, and assert its view model can load cached progress plus local workout state. Resolve the page twice and assert the page identity is the same while no duplicate weight-preference or boundary subscription is added across repeated appearance/disappearance cycles.

- [ ] **Step 2: Run composition test and capture RED**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~MauiCompositionTests|FullyQualifiedName~NativeIosExperienceAcceptanceTests" --verbosity minimal -m:1
```

Expected: provider activation fails because `ITrainNavigator` and the expanded constructor are not registered.

- [ ] **Step 3: Register the production graph with explicit lifetimes**

Register `ITrainNavigator` as singleton `MauiTrainNavigator` and register `TrainTodayViewModel` as singleton to match the existing singleton `TrainPage`. Keep `IProgressSnapshotSource`, `IWeightUnitPreference`, `ActiveWorkoutCoordinator`, and `IAccountSessionBoundary` singleton. The view model owns no page-cycle subscription: `HomeMomentumItem.Dispose` releases its unit subscription on replacement/account reset, and the singleton view model keeps only the existing singleton account boundary reference.

- [ ] **Step 4: Extend native acceptance and accessibility assertions**

Assert the shipped page contains the approved named hierarchy, one action-capable primary button, localized semantics, 44-point targets, no literal technical offline copy, and no words equivalent to “recommended exercise” in English or Thai Home resources.

- [ ] **Step 5: Run Task 5 GREEN and architecture regression**

Run the Task 5 command and:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~Architecture|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~MomentumHomePresentationTests" --verbosity minimal -m:1
```

Expected: all selected tests pass; no service process or simulator is launched.

- [ ] **Step 6: Record evidence and commit Task 5**

```bash
git add src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs tests/TrackZ.Mobile.Tests/NativeIos/AccessibilitySemanticsTests.cs
git add -f .superpowers/sdd/2026-08-22-trackz-momentum-home/task-5-report.md
git commit -m "test: enforce momentum home composition"
```

---

### Task 6: Discussion Traceability, Full Verification, and Simulator Handoff

**Files:**
- Create: `.superpowers/sdd/2026-08-22-trackz-momentum-home/momentum-home-report.md`
- Modify: `docs/testing/native-ios-simulator-walkthrough.md`

**Interfaces:**
- Consumes: Task 1–5 reports and the Discussion Acceptance Matrix in the spec.
- Produces: one final matrix mapping each approved requirement to an exact test name/result and a manual status of `pending` until the user requests simulator startup.

- [ ] **Step 1: Build the traceability report before the final run**

Copy every matrix row from the spec. For each row record: production files, test names, initial RED evidence, current verification command, and manual result. Use `pending — services intentionally stopped` for every simulator item; never write `pass` without observation.

- [ ] **Step 2: Run focused Momentum Home verification fresh**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~LocalTrainDashboardSourceTests|FullyQualifiedName~TrainAgainWorkoutTests|FullyQualifiedName~MomentumHomePresentationTests|FullyQualifiedName~MauiCompositionTests" --verbosity minimal -m:1
```

Expected: zero failures and zero skipped tests.

- [ ] **Step 3: Run the full Mobile regression fresh**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --verbosity minimal -m:1
```

Expected: zero failures. Record the exact passed/skipped counts from this run rather than copying an earlier count.

- [ ] **Step 4: Compile Core and native iOS XAML fresh**

```bash
dotnet build src/TrackZ.Mobile.Core/TrackZ.Mobile.Core.csproj --no-restore -m:1 -v:minimal
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -v:minimal
```

Expected: both exit 0 with zero product warnings/errors.

- [ ] **Step 5: Audit the exact diff and stopped-service state**

```bash
git diff --check
git status --short
./scripts/trackz-dev status
```

Expected service status: Docker Desktop stopped, TrackZ containers stopped, TrackZ API stopped, and iOS Simulator stopped. Preserve unrelated pre-existing worktree changes and list them separately in the report.

- [ ] **Step 6: Self-review against the approved discussion**

Read `docs/superpowers/specs/2026-08-22-trackz-momentum-home-design.md` line by line and compare the inflated MAUI page against every toggle in `docs/design/momentum-home-reference.html`. For each requirement, open the production element and its behavior-sensitive test. Deliberately check these regressions: Home collapsing to only Resume, two lime buttons, Continue opening an exercise, fake zero metrics, partial repeat, lost cached state, kg/lb mismatch, English literals in Thai, and stale account state.

- [ ] **Step 7: Update the simulator walkthrough without starting services**

Add a Momentum Home section to `docs/testing/native-ios-simulator-walkthrough.md` covering no-active state, active state, Continue destination, Train again double tap, offline cached state, kg/lb toggle, English/Thai, Dynamic Type, and screenshots. State that `./scripts/trackz-dev start` runs only after the user asks.

- [ ] **Step 8: Commit verification artifacts**

```bash
git add docs/testing/native-ios-simulator-walkthrough.md
git add -f .superpowers/sdd/2026-08-22-trackz-momentum-home/momentum-home-report.md
git commit -m "docs: add momentum home acceptance ledger"
```

- [ ] **Step 9: Handoff without claiming product acceptance**

Report automated counts and say: `Implementation verified; simulator acceptance pending.` Ask the user whether to run `./scripts/trackz-dev start`. Only after the user says yes, run the script, execute every manual matrix row, save screenshot paths in the report, and ask the user to confirm the simulator result.
