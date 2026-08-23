# Insight-First Home Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Home immediately understandable using only the authoritative progress facts TrackZ already has, without introducing performance judgments.

**Architecture:** Keep `ITrainDashboardSource` as the local workout-state source and `IProgressSnapshotSource` as the cached-authoritative progress source. Replace the metric strip with plain-language weekly copy, present the newest exercise's exact latest/best values through a focused presentation object, and route the detail action through the existing app-owned train navigation boundary to the Progress tab.

**Tech Stack:** .NET 10, C#, .NET MAUI XAML, xUnit, SQLite-backed mobile repositories, RESX localization, iOS Simulator build.

**Spec:** `docs/superpowers/specs/2026-08-24-trackz-insight-first-home-design.md`

## Global Constraints

- Home remains action-first: Start/Continue is the only lime primary action.
- `ฝึกแบบครั้งก่อน` appears immediately below the hero and is hidden during an active workout.
- Home contains no Level/XP, emoji, decorative icons, or symbolic status arrows.
- Phase 1 reports exact latest/best facts only; it must not say improved, stable, lower, new this week, or summarize a muscle group.
- Weekly goal and streak render only from authoritative or cached-authoritative profile data.
- Thai and English copy are equally complete; canonical weights remain kilograms and shared kg/lb preference changes update live.
- Interactive targets are at least 44 points and Dynamic Type can wrap.
- Do not start Docker. This phase needs only Mobile tests and an iOS build.
- Do not start app services automatically. Install/launch the Simulator build only when the user asks to test.

---

## File Structure

### Modified production files

- `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx` — exact English Home copy and Home-specific performance formats.
- `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx` — exact Thai equivalents.
- `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs` — typed access to new resource keys.
- `src/TrackZ.Mobile.Core/Features/Train/HomeMomentumItem.cs` — factual latest/best value formatting only.
- `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs` — weekly sentence, latest-performance state, and progress navigation command.
- `src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs` — add the Progress navigation boundary method.
- `src/TrackZ.Mobile/Features/Train/TrainNavigation.cs` — route to the Progress tab with a focused exercise ID.
- `src/TrackZ.Mobile/Features/Train/TrainPage.xaml` — approved action-first/no-icons hierarchy.
- `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml` — use a scrollable exercise list that can focus an item.
- `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml.cs` — accept and apply the exercise focus query.

### Modified tests and documentation

- `tests/TrackZ.Mobile.Tests/Train/HomeMomentumItemTests.cs`
- `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`
- `tests/TrackZ.Mobile.Tests/NativeIos/TrainNavigationTests.cs`
- `tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs`
- `tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs`
- `tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs`
- `docs/testing/native-ios-simulator-walkthrough.md`

---

### Task 1: Plain-language Home presentation contracts

**Files:**
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Train/HomeMomentumItem.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- Test: `tests/TrackZ.Mobile.Tests/Train/HomeMomentumItemTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs`

**Interfaces:**
- Consumes: `ProgressSnapshot.Profile`, `ProgressSnapshot.Summary.PersonalRecords`, `IWeightUnitPreference`.
- Produces: `WeeklyGoalSentenceText`, `WeeklyStreakSentenceText`, `HasWeeklyStreak`, `LatestPerformanceTitle`, `LatestPerformanceValue`, `BestPerformanceValue`, and `HasRecentMomentum` on `TrainTodayViewModel`.

- [ ] **Step 1: Write failing copy and formatting tests**

Add exact English/Thai assertions to `LocalizationAuditTests`:

```csharp
[Fact]
public void Insight_first_home_copy_is_exact_in_English_and_Thai()
{
    var en = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("en-US"));
    var th = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

    Assert.Equal("This week you completed {0} of {1} workouts", en.HomeWeeklyGoalFormat);
    Assert.Equal("Goal met {0} weeks in a row", en.HomeWeeklyStreakFormat);
    Assert.Equal("Latest performance", en.HomeLatestPerformance);
    Assert.Equal("Latest", en.HomeLatestLabel);
    Assert.Equal("Best", en.HomeBestLabel);
    Assert.Equal("View all data", en.HomeViewAllData);
    Assert.Equal("Open", en.Open);

    Assert.Equal("สัปดาห์นี้ฝึกแล้ว {0} จากเป้าหมาย {1} ครั้ง", th.HomeWeeklyGoalFormat);
    Assert.Equal("ทำถึงเป้า {0} สัปดาห์ติด", th.HomeWeeklyStreakFormat);
    Assert.Equal("ผลงานท่าล่าสุด", th.HomeLatestPerformance);
    Assert.Equal("ครั้งล่าสุด", th.HomeLatestLabel);
    Assert.Equal("สถิติสูงสุด", th.HomeBestLabel);
    Assert.Equal("ดูข้อมูลทั้งหมด", th.HomeViewAllData);
    Assert.Equal("เปิด", th.Open);
}
```

Add `HomeMomentumItemTests` cases that assert:

```csharp
[Theory]
[InlineData("en-US", "75 kg × 8 reps", "80 kg × 8 reps")]
[InlineData("th-TH", "75 กก. × 8 ครั้ง", "80 กก. × 8 ครั้ง")]
public void Weighted_latest_and_best_are_factual_and_localized(
    string cultureName,
    string expectedLatest,
    string expectedBest)
{
    using var item = new HomeMomentumItem(
        new ExerciseProgressSummaryDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Bench Press",
            TrackingMode.Weighted,
            DateTimeOffset.Parse("2026-08-20T09:00:00Z"),
            75m,
            null,
            8,
            80m,
            null,
            8),
        new MutableWeightPreference(WeightDisplayUnit.Kilograms),
        WorkoutResources.ForCulture(CultureInfo.GetCultureInfo(cultureName)));

    Assert.Equal(expectedLatest, item.LatestValueText);
    Assert.Equal(expectedBest, item.BestValueText);
}
```

Add equivalent assisted/bodyweight and live kg→lb change cases. In `TrainTodayViewModelTests`, replace ratio/streak/XP assertions with:

```csharp
Assert.Equal("This week you completed 1 of 3 workouts", viewModel.WeeklyGoalSentenceText);
Assert.Equal("Goal met 4 weeks in a row", viewModel.WeeklyStreakSentenceText);
Assert.True(viewModel.HasWeeklyStreak);
Assert.Equal("Latest performance", viewModel.Text.HomeLatestPerformance);
Assert.Equal("70.125 kg × 8 reps", viewModel.LatestPerformanceValue);
Assert.Equal("72.5 kg × 6 reps", viewModel.BestPerformanceValue);
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~HomeMomentumItemTests|FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~LocalizationAuditTests" --verbosity minimal
```

Expected: FAIL because the new resource properties and view-model properties do not exist.

- [ ] **Step 3: Add the exact resource contract**

Append these typed fields to `WorkoutTextSet` together, then map the keys in the same order in `WorkoutResources.ForCulture`:

```csharp
string HomeWeeklyGoalFormat,
string HomeWeeklyStreakFormat,
string HomeLatestPerformance,
string HomeLatestLabel,
string HomeBestLabel,
string HomeViewAllData,
string HomeWeightedValueFormat,
string HomeAssistedValueFormat,
string HomeBodyweightValueFormat,
string Open,
```

Use these RESX values:

```text
HomeWeeklyGoalFormat: This week you completed {0} of {1} workouts | สัปดาห์นี้ฝึกแล้ว {0} จากเป้าหมาย {1} ครั้ง
HomeWeeklyStreakFormat: Goal met {0} weeks in a row | ทำถึงเป้า {0} สัปดาห์ติด
HomeLatestPerformance: Latest performance | ผลงานท่าล่าสุด
HomeLatestLabel: Latest | ครั้งล่าสุด
HomeBestLabel: Best | สถิติสูงสุด
HomeViewAllData: View all data | ดูข้อมูลทั้งหมด
HomeWeightedValueFormat: {0} {1} × {2} reps | {0} {1} × {2} ครั้ง
HomeAssistedValueFormat: {0} {1} assistance × {2} reps | แรงช่วย {0} {1} × {2} ครั้ง
HomeBodyweightValueFormat: {0} reps | {0} ครั้ง
Open: Open | เปิด
```

- [ ] **Step 4: Make `HomeMomentumItem` factual and unit-aware**

Change its constructor to consume `WorkoutTextSet` and expose unlabeled values:

```csharp
public HomeMomentumItem(
    ExerciseProgressSummaryDto source,
    IWeightUnitPreference preference,
    WorkoutTextSet text)
{
    Source = source;
    _preference = preference;
    _text = text;
    _preference.Changed += OnPreferenceChanged;
}

public string LatestValueText => Format(Source.LastWeightKg, Source.LastAssistedKg, Source.LastReps);
public string BestValueText => Format(Source.BestWeightKg, Source.BestAssistedKg, Source.BestReps);

private string Format(decimal? weightKg, decimal? assistedKg, int reps) => Source.TrackingMode switch
{
    TrackingMode.Weighted => string.Format(
        CultureInfo.CurrentCulture,
        _text.HomeWeightedValueFormat,
        FormatNumber(weightKg),
        UnitLabel,
        reps),
    TrackingMode.Assisted => string.Format(
        CultureInfo.CurrentCulture,
        _text.HomeAssistedValueFormat,
        FormatNumber(assistedKg),
        UnitLabel,
        reps),
    TrackingMode.Bodyweight => string.Format(
        CultureInfo.CurrentCulture,
        _text.HomeBodyweightValueFormat,
        reps),
    _ => throw new ArgumentOutOfRangeException(nameof(Source.TrackingMode))
};
```

Keep the established three-decimal kg and two-decimal lb rules. Raise property changes for `LatestValueText` and `BestValueText` on unit changes.

- [ ] **Step 5: Replace Home's ratio/XP presentation properties**

In `TrainTodayViewModel`:

```csharp
public string WeeklyGoalSentenceText => string.Format(
    CultureInfo.CurrentCulture,
    Text.HomeWeeklyGoalFormat,
    WeeklyCompletedWorkouts,
    WeeklyGoal);

public bool HasWeeklyStreak => CurrentStreakWeeks > 0;

public string WeeklyStreakSentenceText => HasWeeklyStreak
    ? string.Format(CultureInfo.CurrentCulture, Text.HomeWeeklyStreakFormat, CurrentStreakWeeks)
    : string.Empty;

public string LatestPerformanceTitle => RecentMomentum?.ExerciseName ?? string.Empty;
public string LatestPerformanceValue => RecentMomentum?.LatestValueText ?? string.Empty;
public string BestPerformanceValue => RecentMomentum?.BestValueText ?? string.Empty;
```

Remove `Level`, `TotalXp`, `LevelXpText`, and `LevelProgress` from this Home view model. Continue reading the profile for weekly goal and streak. Construct `HomeMomentumItem` with `WorkoutTextSet`. Update property-change notifications and account reset accordingly.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run the Step 2 command.

Expected: PASS for all selected tests.

- [ ] **Step 7: Commit Task 1**

```bash
git add src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs src/TrackZ.Mobile.Core/Features/Train/HomeMomentumItem.cs src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs tests/TrackZ.Mobile.Tests/Train/HomeMomentumItemTests.cs tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs
git commit -m "feat: clarify home progress facts"
```

---

### Task 2: Approved action-first Home hierarchy

**Files:**
- Modify: `src/TrackZ.Mobile/Features/Train/TrainPage.xaml`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs`

**Interfaces:**
- Consumes: Task 1 properties on `TrainTodayViewModel`.
- Produces: named native elements `WeeklyGoalCard`, `WeeklyGoalSentence`, `WeeklyStreakSentence`, `LatestPerformanceCard`, `LatestPerformanceValue`, and `BestPerformanceValue`.

- [ ] **Step 1: Write failing inflated-XAML hierarchy tests**

Update `MomentumHomePresentationTests` to assert this exact order under the root stack:

```csharp
var root = Assert.IsType<VerticalStackLayout>(
    Assert.IsType<ScrollView>(page.FindByName("MomentumHomeScroll")).Content);
var hero = Assert.IsAssignableFrom<IView>(page.FindByName("HomeHero"));
var repeat = Assert.IsAssignableFrom<IView>(page.FindByName("TrainAgainSection"));
var goal = Assert.IsAssignableFrom<IView>(page.FindByName("WeeklyGoalCard"));
var latest = Assert.IsAssignableFrom<IView>(page.FindByName("LatestPerformanceSection"));

var children = root.Children.ToList();
Assert.True(children.IndexOf(hero) < children.IndexOf(repeat));
Assert.True(children.IndexOf(repeat) < children.IndexOf(goal));
Assert.True(children.IndexOf(goal) < children.IndexOf(latest));
Assert.Null(page.FindByName("MotivationStrip"));
Assert.Null(page.FindByName("LevelMetric"));
```

Add exact binding and visibility assertions:

```csharp
Assert.Equal(viewModel.WeeklyGoalSentenceText,
    Assert.IsType<Label>(page.FindByName("WeeklyGoalSentence")).Text);
Assert.Equal(viewModel.HasWeeklyStreak,
    Assert.IsType<Label>(page.FindByName("WeeklyStreakSentence")).IsVisible);
Assert.Equal(viewModel.LatestPerformanceValue,
    Assert.IsType<Label>(page.FindByName("LatestPerformanceValue")).Text);
```

Update the acceptance audit to reject emoji and status-arrow glyphs in Home text and to assert one primary button.

- [ ] **Step 2: Run the native presentation tests and verify RED**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~MomentumHomePresentationTests|FullyQualifiedName~NativeIosExperienceAcceptanceTests" --verbosity minimal
```

Expected: FAIL because the old motivation strip and recent-momentum artwork still exist.

- [ ] **Step 3: Reorder and simplify `TrainPage.xaml`**

Use this direct-child order inside the existing root stack:

```text
Context header
HomeHero
TrainAgainSection
WeeklyGoalCard
LatestPerformanceSection
HomeErrorState
```

Replace `MotivationStrip` with:

```xml
<Border x:Name="WeeklyGoalCard"
        IsVisible="{Binding HasAuthoritativeProgress}"
        Style="{DynamicResource TrackZCardStyle}">
    <VerticalStackLayout Spacing="{DynamicResource TrackZSpace8}">
        <Label x:Name="WeeklyGoalSentence"
               Style="{DynamicResource TrackZSectionTitleStyle}"
               Text="{Binding WeeklyGoalSentenceText}" />
        <Label x:Name="WeeklyStreakSentence"
               IsVisible="{Binding HasWeeklyStreak}"
               Style="{DynamicResource TrackZSecondaryStyle}"
               Text="{Binding WeeklyStreakSentenceText}" />
        <ProgressBar Progress="{Binding WeeklyProgress}"
                     ProgressColor="{DynamicResource TrackZPrimary}" />
    </VerticalStackLayout>
</Border>
```

Expose `WeeklyProgress` on `TrainTodayViewModel` using the same clamped completed/goal calculation previously used by `ProgressDashboardViewModel`.

Replace the artwork-based recent card with a text-only two-row card. Use `HomeLatestLabel` and `HomeBestLabel` on the left and factual values on the right. Task 3 adds the interactive `ดูข้อมูลทั้งหมด` row after its command exists. Do not include `↗`, `›`, emoji, or a status arrow.

Add `x:Name="TrainAgainSection"` to the existing section and replace its chevron with a bound `Text.Open` label.

- [ ] **Step 4: Run native presentation tests and verify GREEN**

Run the Step 2 command.

Expected: PASS.

- [ ] **Step 5: Commit Task 2**

```bash
git add src/TrackZ.Mobile/Features/Train/TrainPage.xaml src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs
git commit -m "feat: simplify insight-first home layout"
```

---

### Task 3: Home-to-Progress exercise focus

**Files:**
- Modify: `src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- Modify: `src/TrackZ.Mobile/Features/Train/TrainNavigation.cs`
- Modify: `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml.cs`
- Test: `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/TrainNavigationTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs`
- Create: `tests/TrackZ.Mobile.Tests/NativeIos/ProgressExerciseFocusTests.cs`

**Interfaces:**
- Consumes: `RecentMomentum.Source.ExerciseId` and `ProgressDashboardViewModel.Exercises`.
- Produces: `ITrainNavigator.OpenProgressAsync(Guid, CancellationToken)` and `TrainTodayViewModel.OpenProgressCommand`.

- [ ] **Step 1: Write failing command and route tests**

Add to `TrainTodayViewModelTests`:

```csharp
[Fact]
public async Task View_all_progress_routes_only_when_recent_performance_exists()
{
    var navigator = new RecordingTrainNavigator();
    var viewModel = new TrainTodayViewModel(
        new RecordingTrainDashboardSource(new TrainDashboardSnapshot(null, null)),
        new AccountSessionBoundary(),
        WorkoutResources.English,
        new CachedProgressSource(Snapshot(3, 1, 4, 8, 640)),
        new FixedConnectivity(false),
        new MutableWeightPreference(),
        GamificationResources.English,
        navigator: navigator);
    await viewModel.LoadAsync();

    Assert.True(viewModel.OpenProgressCommand.CanExecute(null));
    await viewModel.OpenProgressCommand.ExecuteAsync();

    Assert.Equal(["progress:77777777-7777-7777-7777-777777777777"], navigator.Events);
}
```

Extend `TrainNavigationTests` expected routes with:

```csharp
await navigator.OpenProgressAsync(
    Guid.Parse("77777777-7777-7777-7777-777777777777"));

Assert.Contains(
    "//progress?exerciseId=77777777-7777-7777-7777-777777777777",
    host.Routes);
```

- [ ] **Step 2: Run focused navigation tests and verify RED**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainNavigationTests|FullyQualifiedName~View_all_progress_routes" --verbosity minimal
```

Expected: FAIL because the interface and command do not exist.

- [ ] **Step 3: Add the app-owned navigation method and serialized command**

Extend `ITrainNavigator`:

```csharp
Task OpenProgressAsync(Guid exerciseId, CancellationToken cancellationToken = default);
```

Implement it in `MauiTrainNavigator`:

```csharp
public Task OpenProgressAsync(Guid exerciseId, CancellationToken cancellationToken = default)
{
    ArgumentOutOfRangeException.ThrowIfEqual(exerciseId, Guid.Empty);
    cancellationToken.ThrowIfCancellationRequested();
    return _host.GoToAsync($"//progress?exerciseId={exerciseId:D}", cancellationToken);
}
```

Add `OpenProgressCommand` to `TrainTodayViewModel`, gated by `CanMutate && RecentMomentum is not null`. Capture the exercise ID before navigation and use the existing account-generation-fenced `NavigateAsync` path.

Update every test fake implementing `ITrainNavigator` with the new method. Recording fakes append `progress:{exerciseId:D}`; no-op fakes return `Task.CompletedTask`.

- [ ] **Step 4: Make Progress focus the requested exercise**

Change the exercise list to a named `CollectionView`:

```xml
<CollectionView x:Name="ExerciseProgressList"
                ItemsSource="{Binding Exercises}"
                SelectionMode="None">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="gamification:ExerciseProgressItem">
            <Border Style="{DynamicResource TrackZCardStyle}">
                <components:ExerciseProgressChart
                    ExerciseName="{Binding ExerciseName}"
                    PersonalRecordText="{Binding PersonalRecordText}" />
            </Border>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

Implement `IQueryAttributable` on the page:

```csharp
private Guid? _requestedExerciseId;

public void ApplyQueryAttributes(IDictionary<string, object> query)
{
    _requestedExerciseId = query.TryGetValue("exerciseId", out var raw)
        && Guid.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var parsed)
            ? parsed
            : null;
}

protected override async void OnAppearing()
{
    base.OnAppearing();
    await _viewModel.LoadAsync();
    var item = ConsumeRequestedExercise();
    if (item is not null) ExerciseProgressList.ScrollTo(item, position: ScrollToPosition.Start, animate: false);
}

internal ExerciseProgressItem? ConsumeRequestedExercise()
{
    var requested = _requestedExerciseId;
    _requestedExerciseId = null;
    return requested is { } id
        ? _viewModel.Exercises.SingleOrDefault(candidate => candidate.ExerciseId == id)
        : null;
}
```

Create `ProgressExerciseFocusTests` with a local fixed `IProgressSnapshotSource` that returns two `ExerciseProgressSummaryDto` rows. Construct and load a real `ProgressDashboardViewModel`, construct the page, apply the query, and assert the one-shot focus seam exactly:

```csharp
page.ApplyQueryAttributes(new Dictionary<string, object>
{
    ["exerciseId"] = requestedExerciseId.ToString("D", CultureInfo.InvariantCulture)
});
await viewModel.LoadAsync();

Assert.Equal(requestedExerciseId, page.ConsumeRequestedExercise()!.ExerciseId);
Assert.Null(page.ConsumeRequestedExercise());
```

Add invalid/missing query cases that return `null`. Keep actual scroll-position observation in the Simulator walkthrough because headless MAUI does not lay out a native scroll surface.

- [ ] **Step 5: Bind the Home text action and verify GREEN**

Bind `ViewAllProgressAction` to `OpenProgressCommand`, keep its minimum touch target at 44 points, and run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~TrainNavigationTests|FullyQualifiedName~MomentumHomePresentationTests|FullyQualifiedName~ProgressExerciseFocusTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit Task 3**

```bash
git add src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs src/TrackZ.Mobile/Features/Train/TrainNavigation.cs src/TrackZ.Mobile/Features/Train/TrainPage.xaml src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml.cs tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs tests/TrackZ.Mobile.Tests/NativeIos/TrainNavigationTests.cs tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs tests/TrackZ.Mobile.Tests/NativeIos/ProgressExerciseFocusTests.cs
git commit -m "feat: open progress from latest performance"
```

---

### Task 4: Phase 1 acceptance and handoff

**Files:**
- Modify: `docs/testing/native-ios-simulator-walkthrough.md`
- Modify: relevant expectation maps in `tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs`
- Modify: relevant localization lists in `tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs`

**Interfaces:**
- Consumes: completed Tasks 1–3.
- Produces: current automated evidence and a manual walkthrough for the approved Phase 1 scope.

- [ ] **Step 1: Add the Phase 1 acceptance matrix to the walkthrough**

Document these exact checks:

```text
Ready: Start is the only lime action; Train again is second; weekly sentence is third.
Active: Continue is the only lime action; Train again is hidden.
No profile authority: no weekly zero or empty ratio appears.
Weighted/assisted/bodyweight: latest and best labels remain factual.
Thai/English: no truncation and no emoji/icon/status arrow.
kg/lb: Home values change without app restart.
View all data: Progress opens and the latest exercise is brought into view.
Dynamic Type/VoiceOver: text wraps and every action remains at least 44 points.
```

- [ ] **Step 2: Run the complete Mobile suite**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --verbosity minimal
```

Expected: all tests PASS with zero failed tests.

- [ ] **Step 3: Run source and whitespace audits**

```bash
git diff --check
rg -n "[📈💪🔥⚠️🏆]|↑|↓|→|↗" src/TrackZ.Mobile/Features/Train src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx
```

Expected: `git diff --check` exits 0; the glyph search returns no Home status/decorative copy. Existing unrelated glyphs outside the listed Home paths are out of scope.

- [ ] **Step 4: Build iOS without starting Docker**

```bash
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios -r iossimulator-arm64 --no-restore -m:1 --verbosity minimal
```

Expected: `Build succeeded`, zero warnings, zero errors.

- [ ] **Step 5: Request review against the spec**

Use `superpowers:requesting-code-review` with the Phase 1 base/head range. Resolve all Critical and Important findings, rerun Steps 2–4, and record the final counts.

- [ ] **Step 6: Commit acceptance documentation**

```bash
git add docs/testing/native-ios-simulator-walkthrough.md tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs
git commit -m "test: verify insight-first home phase one"
```

- [ ] **Step 7: Offer Simulator acceptance without launching automatically**

Report the exact test count, iOS build result, branch/head, and that Docker remained off. Ask the user whether to install and launch the build. Do not run `xcrun simctl install` or `launch` until the user says yes.
