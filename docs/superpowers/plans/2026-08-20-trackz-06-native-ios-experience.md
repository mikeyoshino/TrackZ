# TrackZ 06 Native iOS Experience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace TrackZ's web-like MAUI presentation with the approved Native Performance iOS experience while preserving the existing domain, offline-first storage, sync protocol, API media security, and gamification rules.

**Architecture:** Keep view models, SQLite, API clients, and synchronization in `TrackZ.Mobile.Core`; keep XAML, navigation, sheets, haptics, animation drivers, and iOS adapters in `TrackZ.Mobile`. Deliver the redesign as vertical slices that remain runnable after every commit, with native iOS presentation behind small platform interfaces and a non-iOS fallback that preserves shared-core compatibility.

**Tech Stack:** .NET 10, .NET MAUI XAML SourceGen, C# 14, iOS 15+, UIKit adapters where MAUI has no native surface, SQLite, xUnit, ASP.NET Core API, authenticated signed media, PostgreSQL/Testcontainers for existing API acceptance.

**Spec:** `docs/superpowers/specs/2026-08-20-trackz-native-ios-experience-design.md`

## Global Constraints

- iOS is the primary polished platform; do not force iOS presentation metaphors into `TrackZ.Mobile.Core`.
- Production UI is MAUI XAML and native controls; do not add HTML, a WebView, or runtime dependence on the Visual Companion mockups.
- `TrackZ.Mobile.Core` must not reference MAUI, UIKit, platform lifecycle types, or XAML assemblies.
- Runtime exercise artwork comes from API catalog metadata through the authenticated media capability flow and bounded local cache; bundled images are placeholders only.
- Workout, exercise, set, history, and completion mutations remain local-first and commit their outbox operation atomically before success feedback.
- TrackZ does not prescribe exercises, weights, or set counts. Never render planned/required set totals or completion percentages.
- Use the system font and Dynamic Type on iOS. Do not assign OpenSans to redesigned iOS controls.
- Every interactive target is at least 44 by 44 points and has localized VoiceOver semantics.
- User-facing copy is localized in Thai and English; raw business codes, server exception messages, media keys, and signatures never appear in the UI.
- Normal feedback remains brief; PR/workout celebrations are interruptible and no longer than approximately 1.2 seconds.
- Reduce Motion replaces transforms and count-ups with fades or immediate state; animation is never a business-state dependency.
- Existing account/session cancellation, auth refresh, offline operation, sync conflict, and ambiguous-outcome safety rules remain intact.
- Run tests sequentially with `-m:1`; do not launch an emulator or install platform SDKs as part of automated verification.

## File and Responsibility Map

- `src/TrackZ.Mobile/Resources/Styles/TrackZ*.xaml` — semantic color, typography, spacing, list, button, and material tokens.
- `src/TrackZ.Mobile/AppShell.*` — four native tabs and registered detail routes.
- `src/TrackZ.Mobile/Presentation/*` — native-sheet, motion, haptic, and navigation contracts/MAUI fallbacks.
- `src/TrackZ.Mobile/Platforms/iOS/*` — UIKit configuration for sheets, large titles, and system accessibility behavior.
- `src/TrackZ.Mobile.Core/Features/Train/*` — Today/dashboard state independent from MAUI.
- `src/TrackZ.Mobile/Features/Train/*` — Today page and body-area sheet.
- `src/TrackZ.Mobile.Core/Features/Exercises/*` — stable search/filter/selection and per-item artwork state.
- `src/TrackZ.Mobile/Features/Exercises/*` — native exercise library rows, search, chips, and bottom selection bar.
- `src/TrackZ.Mobile.Core/Features/Workout/*` — active-workout and set-logger state; no presentation dependencies.
- `src/TrackZ.Mobile/Features/Workout/*` — active list, native actions, set entry sheet, and durable feedback visuals.
- `src/TrackZ.Mobile.Core/Features/History/*` and `src/TrackZ.Mobile/Features/History/*` — grouped history, detail state, native edit/delete/conflict sheets.
- `src/TrackZ.Mobile.Core/Features/Gamification/*` and Progress/Summary/Profile pages — unified progress dashboard and completion reveal.
- `tests/TrackZ.Mobile.Tests/NativeIos/*` — headless XAML/composition/accessibility tests.
- Existing feature test folders — state, storage, media, sync, and lifecycle regressions closest to the code they cover.

## Test Fixture Contracts Used in Task Snippets

Keep these as private test helpers in the test file that first uses them; they are not production APIs:

- `RecordingTrainDashboardSource(TrainDashboardSnapshot snapshot)` implements `ITrainDashboardSource`, returns `snapshot`, and increments `LoadCount`.
- `CreateBodyAreaPicker()` builds a real `MauiBodyAreaPicker` with a recording `INativeSheetPresenter`; the returned fixture exposes the currently presented `BodyAreaSheetPage VisibleSheet`.
- `SelectiveThumbnailCache(string failFirstFor)` implements `IExerciseThumbnailCache`, records `Dictionary<string,int> Attempts`, throws once for `failFirstFor`, then returns a fixture PNG path.
- `CreatePicker(...)` builds an `ExercisePickerViewModel` with temporary SQLite, online connectivity, a fixed clock, an inline dispatcher, a fresh account boundary, and the supplied catalog/media fakes.
- `ActiveWorkoutFixture.CreateAsync()` creates temporary workout/catalog databases plus `ActiveWorkoutCoordinator` and `WorkoutViewModel`; it exposes `First`, `Second`, `Third`, `ViewModel`, and `RecreateViewModel()` and deletes the directory in `DisposeAsync`.
- `RecordingWorkoutRepository` uses the real SQLite repository and invokes an injected callback immediately after the transaction commits; `RecordingFeedback` appends the observed `SetSavedOutcome` to the same event list.
- `CreateLogger(...)` builds a `SetLoggerViewModel` with fixed history, unit preference, account boundary, inline dispatcher, and supplied repository/feedback fakes.
- `RecordingMotionDriver(bool reduceMotion)` records exact phase names; it records only `fade` when Reduce Motion is true.
- `CreateHistoryList(...)` and `CreateDetail(...)` build list/detail view models over temporary SQLite and fixed connectivity/account services. `ReconcilingWorkout()` persists an outbox row with `SendStartedAt` so the real reconciling barrier is exercised.
- `RecordingProgressSource` and `SequencedProgressSource` implement `IProgressSnapshotSource`; the first returns one snapshot, while the second returns its first snapshot from `GetCachedAsync` and second from `RefreshAsync`.
- `Snapshot(...)` creates complete `ProgressSnapshot` instances with fixed timestamps and explicit profile/PR/badge values; it never defaults business-significant fields implicitly.
- `NativeExperienceFixture` owns temporary exercise/history/workout/progress stores plus fake authenticated API/media clients; `KillAndRecreateAsync()` disposes the MAUI provider, clears SQLite pools, and creates a new provider over the same files.
- `NativeControlInventory.Create()` instantiates the redesigned weight/reps controls and primary actions under the headless dispatcher and returns their minimum sizes and semantic descriptions.

All temporary fixtures restore `DispatcherProvider.Current`, clear SQLite connection pools, dispose MAUI providers, and delete only their unique temporary directory in `finally`/`DisposeAsync`.

---

### Task 1: Native Shell and Presentation Foundation

**Files:**
- Create: `src/TrackZ.Mobile/Resources/Styles/TrackZColors.xaml`
- Create: `src/TrackZ.Mobile/Resources/Styles/TrackZTypography.xaml`
- Create: `src/TrackZ.Mobile/Resources/Styles/TrackZControls.xaml`
- Create: `src/TrackZ.Mobile/Presentation/INativeSheetPresenter.cs`
- Create: `src/TrackZ.Mobile/Presentation/MauiNativeSheetPresenter.cs`
- Create: `src/TrackZ.Mobile/Platforms/iOS/NativeSheetConfiguration.cs`
- Create: `src/TrackZ.Mobile/Platforms/iOS/NativeNavigationConfiguration.cs`
- Create: `src/TrackZ.Mobile/Resources/Images/tab_train.svg`
- Create: `src/TrackZ.Mobile/Resources/Images/tab_history.svg`
- Create: `src/TrackZ.Mobile/Resources/Images/tab_progress.svg`
- Create: `src/TrackZ.Mobile/Resources/Images/tab_you.svg`
- Modify: `src/TrackZ.Mobile/App.xaml`
- Modify: `src/TrackZ.Mobile/AppShell.xaml`
- Modify: `src/TrackZ.Mobile/AppShell.xaml.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/NativeShellTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/NativePresentationCompositionTests.cs`

**Interfaces:**
- Consumes: existing DI pages, `IReduceMotionPreference`, `App`, and `Shell`.
- Produces: `NativeSheetDetent`, `INativeSheetPresenter.ShowAsync(ContentPage, NativeSheetDetent, CancellationToken)`, `INativeSheetPresenter.DismissAsync(ContentPage, CancellationToken)`, four routes named `train`, `history`, `progress`, and `you`, and semantic resources used by all later tasks.

- [ ] **Step 1: Write failing shell and composition tests**

```csharp
[Fact]
public void Authenticated_shell_exposes_exactly_four_native_tabs()
{
    using var app = MauiProgram.CreateMauiApp();
    var shell = app.Services.GetRequiredService<AppShell>();
    var routes = shell.Items
        .SelectMany(item => item.Items)
        .SelectMany(section => section.Items)
        .Select(content => content.Route)
        .ToArray();

    Assert.Equal(["train", "history", "progress", "you"], routes);
    Assert.DoesNotContain("exercises", routes);
}

[Fact]
public void Native_sheet_presenter_is_singleton_and_resolvable()
{
    using var app = MauiProgram.CreateMauiApp();
    Assert.Same(
        app.Services.GetRequiredService<INativeSheetPresenter>(),
        app.Services.GetRequiredService<INativeSheetPresenter>());
}
```

- [ ] **Step 2: Run the new tests and record the strict RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeShellTests|FullyQualifiedName~NativePresentationCompositionTests" -m:1
```

Expected: FAIL because `INativeSheetPresenter` is absent and the current shell exposes five top-level destinations including Exercises.

- [ ] **Step 3: Add the exact native-sheet contract and MAUI fallback**

```csharp
namespace TrackZ.Mobile.Presentation;

public enum NativeSheetDetent
{
    Medium = 1,
    Large = 2
}

public interface INativeSheetPresenter
{
    Task ShowAsync(
        ContentPage page,
        NativeSheetDetent detent,
        CancellationToken cancellationToken = default);

    Task DismissAsync(
        ContentPage page,
        CancellationToken cancellationToken = default);
}
```

`MauiNativeSheetPresenter` must use `Shell.Current.Navigation.PushModalAsync`/`PopModalAsync` as the cross-platform fallback. On iOS, subscribe once to `HandlerChanged`, obtain the page's `UIViewController`, configure `UISheetPresentationController.Detents` to medium or large, set `PrefersGrabberVisible = true`, and detach the handler after configuration. Cancellation before presentation must not push a page; cancellation after presentation must not pop an unrelated page.

- [ ] **Step 4: Replace the shell with the exact four-tab hierarchy**

Build one `TabBar` with Train, History, Progress, and You. Keep Exercise Picker, Custom Exercise, Active Workout, Set Logger, History Detail, and Workout Summary as registered detail routes rather than tabs. Use the four SVG assets as `ShellContent.Icon`. Set `Shell.FlyoutBehavior="Disabled"` and native dark tab colors from semantic resources.

`NativeNavigationConfiguration` configures `UINavigationBarAppearance` once and enables large titles for tab-root navigation controllers. Each pushed detail page explicitly requests compact title mode. Do not draw replacement back arrows; retain the native interactive-pop gesture.

- [ ] **Step 5: Add semantic resources and remove global web-like defaults**

Define keys including `TrackZBackground`, `TrackZSurface`, `TrackZSurfaceRaised`, `TrackZPrimary`, `TrackZTextPrimary`, `TrackZTextSecondary`, `TrackZWarning`, `TrackZDanger`, `TrackZHairline`, `TrackZPageTitleStyle`, `TrackZSectionTitleStyle`, `TrackZPerformanceNumberStyle`, `TrackZPrimaryButtonStyle`, `TrackZListRowStyle`, and `TrackZStatusPillStyle`.

The redesigned styles must leave `FontFamily` unset so iOS uses the system font. Preserve old resource keys temporarily for pages not yet migrated; remove them only in Task 8.

- [ ] **Step 6: Run focused tests and native XAML compilation**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeShellTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~MauiCompositionTests" -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1
```

Expected: focused tests PASS; iOS XAML SourceGen/compile exits 0 with no TrackZ warnings.

- [ ] **Step 7: Commit the foundation**

```bash
git add src/TrackZ.Mobile tests/TrackZ.Mobile.Tests/NativeIos
git commit -m "feat: establish native iOS presentation foundation"
```

---

### Task 2: Train Today and Body-Area Sheet

**Files:**
- Create: `src/TrackZ.Mobile.Core/Features/Train/TrainDashboardModels.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Train/LocalTrainDashboardSource.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Train/TrainPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Train/TrainPage.xaml.cs`
- Create: `src/TrackZ.Mobile/Features/Train/BodyAreaSheetPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Train/BodyAreaSheetPage.xaml.cs`
- Create: `src/TrackZ.Mobile/Features/Train/MauiBodyAreaPicker.cs`
- Modify: `src/TrackZ.Mobile/AppShell.xaml.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Test: `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/BodyAreaSheetTests.cs`

**Interfaces:**
- Consumes: `ILocalWorkoutRepository.GetActiveAsync`, `ILocalWorkoutRepository.GetHistoryAsync`, `ExerciseCache.GetAllAsync`, `INativeSheetPresenter`, and `AccountSessionBoundary` behavior already used by other mobile screens.
- Produces:

```csharp
public sealed record ActiveWorkoutCard(
    Guid WorkoutId,
    DateTimeOffset StartedAt,
    int ExerciseCount,
    int LoggedSetCount);

public sealed record RecentWorkoutShortcut(
    Guid WorkoutId,
    IReadOnlyList<BodyPart> BodyParts,
    DateTimeOffset CompletedAt,
    int ExerciseCount,
    string? ThumbnailPath);

public sealed record TrainDashboardSnapshot(
    ActiveWorkoutCard? Active,
    IReadOnlyList<RecentWorkoutShortcut> Recent);

public interface ITrainDashboardSource
{
    Task<TrainDashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IBodyAreaPicker
{
    Task<BodyPart?> PickAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write RED tests for active restore, recent ordering, and body-area cancellation**

```csharp
[Fact]
public async Task Today_loads_active_workout_and_three_newest_completed_shortcuts()
{
    var source = new RecordingTrainDashboardSource(new(
        new(Guid.NewGuid(), At(9), 2, 4),
        [
            new(Guid.NewGuid(), [BodyPart.Shoulders, BodyPart.Back], At(8), 6, "/cache/shoulder.png"),
            new(Guid.NewGuid(), [BodyPart.Legs], At(7), 5, "/cache/leg.png")
        ]));
    var viewModel = new TrainTodayViewModel(source, WorkoutResources.English);

    await viewModel.LoadAsync();

    Assert.NotNull(viewModel.ActiveWorkout);
    Assert.Equal(4, viewModel.ActiveWorkout!.LoggedSetCount);
    Assert.Equal(2, viewModel.RecentWorkouts.Count);
    Assert.Equal("Shoulders + Back", viewModel.RecentWorkouts[0].Title);
}

[Fact]
public async Task Dismissing_body_area_sheet_returns_null_without_navigation()
{
    var picker = CreateBodyAreaPicker();
    var pending = picker.PickAsync();
    await picker.VisibleSheet.CancelAsync();
    Assert.Null(await pending);
}
```

- [ ] **Step 2: Run the focused RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~BodyAreaSheetTests" -m:1
```

Expected: FAIL because Train feature types and body-area picker do not exist.

- [ ] **Step 3: Implement the local dashboard source and MAUI-free view model**

`LocalTrainDashboardSource` must read one active workout and the newest three completed workouts. Join exercise IDs to `ExerciseCache` in memory, preserve workout exercise order, derive distinct body parts, and choose the first available cached thumbnail. Never invent a planned-set total; `LoggedSetCount` is the sum of persisted live set rows.

`TrainTodayViewModel` exposes `ActiveWorkout`, `ObservableCollection<RecentWorkoutItem> RecentWorkouts`, `IsBusy`, `IsOffline`, and localized empty/error text. It captures the account session generation before load and refuses stale commits after reset.

- [ ] **Step 4: Implement the native Today page and body-area sheet**

The Train tab root uses a large title, one **Choose workout** button near the bottom safe area, an active-workout resume row, and recent shortcuts. `MauiBodyAreaPicker` creates a fresh transient `BodyAreaSheetPage`, presents it through `INativeSheetPresenter`, and completes exactly once with a `BodyPart?`.

Selecting a body area navigates to:

```csharp
await Shell.Current.GoToAsync(
    $"{nameof(ExercisePickerPage)}?bodyPart={(int)selectedBodyPart}");
```

Do not start or mutate a workout until the exercise selection is confirmed.

- [ ] **Step 5: Add Thai/English body-area and Today copy**

Add exact resource values for Today, Choose workout, Resume workout, Recent, No recent workouts, Chest, Back, Shoulders, Arms, Legs, Core, Continue, and Cancel. Tests must switch `CurrentUICulture` between `en` and `th` and assert both the body-area labels and the combined recent title.

- [ ] **Step 6: Run Train, lifecycle, and XAML verification**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~BodyAreaSheetTests|FullyQualifiedName~AccountSessionBoundaryTests|FullyQualifiedName~MauiCompositionTests" -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1
```

Expected: all selected tests PASS and iOS compile exits 0.

- [ ] **Step 7: Commit the Train entry flow**

```bash
git add src/TrackZ.Mobile.Core/Features/Train src/TrackZ.Mobile/Features/Train src/TrackZ.Mobile/AppShell.xaml.cs src/TrackZ.Mobile/MauiProgram.cs src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs tests/TrackZ.Mobile.Tests/Train tests/TrackZ.Mobile.Tests/NativeIos/BodyAreaSheetTests.cs
git commit -m "feat: add native today workout flow"
```

---

### Task 3: API-Backed Exercise Library and Artwork States

**Files:**
- Create: `src/TrackZ.Mobile/Networking/MobileEndpointOrigins.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Exercises/Models/ExerciseArtworkState.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `src/TrackZ.Mobile/Platforms/iOS/Info.plist`
- Modify: `src/TrackZ.Api/Properties/launchSettings.json`
- Modify: `src/TrackZ.Api/appsettings.Development.json`
- Modify: `compose.yaml`
- Modify: `src/TrackZ.Mobile.Core/Features/Exercises/Models/CachedExercise.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Exercises/Models/ExercisePickerItem.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Exercises/Data/ExerciseCache.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Exercises/ExercisePickerViewModel.cs`
- Modify: `src/TrackZ.Mobile/Features/Exercises/ExercisePickerPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Exercises/ExercisePickerPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/Components/ExercisePerformanceCard.xaml`
- Modify: `src/TrackZ.Mobile/Components/ExercisePerformanceCard.xaml.cs`
- Test: `tests/TrackZ.Mobile.Tests/Networking/SimulatorApiConfigurationTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Exercises/ExerciseArtworkStateTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Exercises/ExercisePerformanceCardTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Exercises/ExercisePickerViewModelTests.cs`

**Interfaces:**
- Consumes: existing `IExerciseCatalogApi`, `IExerciseThumbnailCache`, signed-media client, `ExerciseCache`, `IWeightUnitPreference`, and Task 2's `bodyPart` route query.
- Produces: `MobileEndpointOrigins.Resolve(Func<string,string?>)`, persisted `RemoteThumbnailRoute`, `ExerciseArtworkState`, per-item `RetryArtworkCommand`, native library XAML, and correct simulator-to-local-API routing.

- [ ] **Step 1: Write RED tests for endpoint origin safety and API artwork behavior**

```csharp
[Fact]
public void Debug_simulator_origins_use_explicit_clean_http_origins()
{
    var values = new Dictionary<string, string?>
    {
        ["TRACKZ_API_ORIGIN"] = "http://127.0.0.1:5080",
        ["TRACKZ_MEDIA_ORIGIN"] = "http://127.0.0.1:9000"
    };
    var origins = MobileEndpointOrigins.Resolve(key => values.GetValueOrDefault(key));
    Assert.Equal(new Uri("http://127.0.0.1:5080/"), origins.ApiOrigin);
    Assert.Equal(new Uri("http://127.0.0.1:9000/"), origins.MediaOrigin);
}

[Fact]
public async Task One_failed_thumbnail_does_not_hide_other_results_and_can_retry_only_it()
{
    var cache = new SelectiveThumbnailCache(failFirstFor: ShoulderPressRoute);
    var viewModel = CreatePicker(cache, ShoulderPressDto, LateralRaiseDto);
    await viewModel.LoadAsync(BodyPart.Shoulders);
    await viewModel.RefreshCompletion;

    Assert.Equal(2, viewModel.Exercises.Count);
    Assert.Equal(ExerciseArtworkState.Failed, viewModel.Exercises[0].ArtworkState);
    Assert.Equal(ExerciseArtworkState.Ready, viewModel.Exercises[1].ArtworkState);

    await viewModel.Exercises[0].RetryArtworkCommand.ExecuteAsync(null);
    Assert.Equal(ExerciseArtworkState.Ready, viewModel.Exercises[0].ArtworkState);
    Assert.Equal(2, cache.Attempts[ShoulderPressRoute]);
    Assert.Equal(1, cache.Attempts[LateralRaiseRoute]);
}
```

- [ ] **Step 2: Run the focused RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~SimulatorApiConfigurationTests|FullyQualifiedName~ExerciseArtworkStateTests|FullyQualifiedName~ExercisePerformanceCardTests" -m:1
```

Expected: FAIL because endpoint configuration and artwork-state/retry types are absent.

- [ ] **Step 3: Add strict endpoint configuration for simulator and production**

`MobileEndpointOrigins.Resolve` must accept only clean absolute HTTP(S) origins with no user info, query, fragment, or non-root path. Defaults remain `https://api.trackz.app/` and `https://media.trackz.app/`. Debug environment overrides are explicit; release builds must not accept a development token shortcut. Add only the narrow iOS local-network/ATS allowance needed for the configured simulator development origin.

Align `launchSettings.json`, development appsettings, MinIO public endpoint, and Compose port bindings so API catalog thumbnail routes authorize URLs the simulator can reach. Do not expose object keys or attach the API bearer token to the signed-media client.

- [ ] **Step 4: Persist remote authorization routes separately from local thumbnail paths**

Add `RemoteThumbnailRoute TEXT NULL` to the exercise cache with an idempotent column upgrader. `CachedExercise` must contain both:

```csharp
public string? RemoteThumbnailRoute { get; init; }
public string? ThumbnailUri { get; init; }
```

Catalog refresh persists metadata and `RemoteThumbnailRoute` immediately, preserves a valid existing local `ThumbnailUri`, and fills local thumbnails independently. Account cleanup removes local files and private cache rows as before.

- [ ] **Step 5: Add isolated artwork state and retry**

```csharp
public enum ExerciseArtworkState
{
    Unavailable = 1,
    Loading = 2,
    Ready = 3,
    Failed = 4
}
```

Each `ExercisePickerItem` owns `ArtworkState`, `ThumbnailUri`, `HasArtwork`, `ShowsArtworkPlaceholder`, and an `AsyncCommand RetryArtworkCommand`. The view model provides one callback that caches only the item's `RemoteThumbnailRoute`, commits only under the current account generation, updates SQLite, then updates that item on the UI dispatcher. Search/filter rebuilds must preserve selection and current per-ID artwork state.

- [ ] **Step 6: Rebuild Exercise Picker as a native library screen**

Use native SearchBar behavior, horizontal body-area/equipment chips, list rows with `AspectFit` artwork, LAST, PR, tracking mode, and a 44-point selection affordance. Use a bottom safe-area selection bar. The list becomes interactive after cached metadata loads; do not wait for every thumbnail task.

The placeholder is a neutral anatomy/equipment silhouette with localized accessibility text. It must not substitute a bundled standard-exercise illustration for a failed API image.

- [ ] **Step 7: Run media, picker, security, and XAML verification**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~ExercisePickerViewModelTests|FullyQualifiedName~ExerciseArtworkStateTests|FullyQualifiedName~ExercisePerformanceCardTests|FullyQualifiedName~SimulatorApiConfigurationTests|FullyQualifiedName~LibraryMetadataRefreshTests|FullyQualifiedName~AccountSessionRaceTests|FullyQualifiedName~IdentityTokenIntegrationTests" -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1
```

Expected: all selected tests PASS; image failure remains isolated; no bearer token reaches the media client; iOS compile exits 0.

- [ ] **Step 8: Commit the API exercise library**

```bash
git add compose.yaml src/TrackZ.Api src/TrackZ.Mobile src/TrackZ.Mobile.Core/Features/Exercises tests/TrackZ.Mobile.Tests/Networking tests/TrackZ.Mobile.Tests/Exercises
git commit -m "feat: build native API exercise library"
```

---

### Task 4: Active Workout List and Native Exercise Actions

**Files:**
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Modify: `src/TrackZ.Mobile/Features/Workout/WorkoutPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Workout/WorkoutPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/AppShell.xaml.cs`
- Create: `src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.xaml`
- Create: `src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.xaml.cs`
- Test: `tests/TrackZ.Mobile.Tests/Workout/WorkoutViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/ActiveWorkoutExerciseRowTests.cs`

**Interfaces:**
- Consumes: `ActiveWorkoutCoordinator`, `ExerciseCache`, Task 3's local artwork paths, and existing Start/Add/Remove/Reorder/Finish operations.
- Produces: enriched `WorkoutExerciseDraftItem` with `LoggedSetCount`, tracking-mode-aware LAST text, artwork path, and native row commands. The active page route is `active-workout`.

- [ ] **Step 1: Write RED tests proving logged counts are descriptive, never prescriptive**

```csharp
[Fact]
public async Task Restored_active_workout_reports_only_sets_actually_logged()
{
    var viewModel = CreateWorkoutViewModel(ActiveWorkoutWithSetCounts(2, 0));
    await viewModel.RestoreAsync();

    Assert.Equal([2, 0], viewModel.Exercises.Select(item => item.LoggedSetCount));
    Assert.DoesNotContain(
        viewModel.Exercises,
        item => item.AccessibilitySummary.Contains("of", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public async Task Reorder_and_remove_remain_durable_after_restart()
{
    var fixture = await ActiveWorkoutFixture.CreateAsync();
    await fixture.ViewModel.MoveUpCommand.ExecuteAsync(fixture.Third);
    await fixture.ViewModel.MoveUpCommand.ExecuteAsync(fixture.Third);
    await fixture.ViewModel.RemoveExerciseCommand.ExecuteAsync(fixture.Second);
    var restored = fixture.RecreateViewModel();
    await restored.RestoreAsync();
    Assert.Equal(
        [fixture.Third.ExerciseDefinitionId, fixture.First.ExerciseDefinitionId],
        restored.Exercises.Select(x => x.ExerciseDefinitionId));
}
```

- [ ] **Step 2: Run focused RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutViewModelTests|FullyQualifiedName~ActiveWorkoutExerciseRowTests" -m:1
```

Expected: FAIL because the enriched item/accessibility properties and native row do not exist.

- [ ] **Step 3: Enrich the active-workout presentation state without changing the aggregate**

Build rows from the restored `LocalWorkout` and cached exercise metadata. Count only live persisted sets. Format LAST using the same shared unit preference and tracking-mode rules as Exercise Picker. Subscribe to unit changes and update existing rows. Keep stable client IDs and existing operation/base-version behavior unchanged.

- [ ] **Step 4: Replace up/down/remove button stacks with native actions**

`ActiveWorkoutExerciseRow` contains artwork, name, tracking mode, LAST, and `"{n} sets logged"`. A tap opens Set Logger. Swipe/context actions expose Move Up, Move Down, and Remove with localized semantics; impossible actions are disabled. Add Exercise remains available while active. A user-controlled **Finish workout** action is present without a progress percentage or required-set label.

- [ ] **Step 5: Preserve active-workout lifecycle and summary navigation**

Train's Resume action opens `active-workout`. Starting from the picker pushes the same page. Finishing must await the existing durable completion operation, navigate to `WorkoutSummaryPage?workoutId=...`, and leave the sync orchestrator independent of navigation.

- [ ] **Step 6: Run workout, outbox, sync, and XAML verification**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutViewModelTests|FullyQualifiedName~ActiveWorkoutExerciseRowTests|FullyQualifiedName~ActiveWorkoutCoordinatorTests|FullyQualifiedName~OfflineWorkoutAcceptanceTests|FullyQualifiedName~SyncCoordinatorTests" -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1
```

Expected: selected tests PASS; durable order survives restart; no planned-set copy exists; iOS compile exits 0.

- [ ] **Step 7: Commit active workout redesign**

```bash
git add src/TrackZ.Mobile.Core/Features/Workout src/TrackZ.Mobile/Features/Workout src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.* src/TrackZ.Mobile/AppShell.xaml.cs tests/TrackZ.Mobile.Tests/Workout tests/TrackZ.Mobile.Tests/NativeIos/ActiveWorkoutExerciseRowTests.cs
git commit -m "feat: redesign active workout for iOS"
```

---

### Task 5: Native Set Entry Sheet and Durable Reward Motion

**Files:**
- Create: `src/TrackZ.Mobile/Features/Workout/SetEntrySheetPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Workout/SetEntrySheetPage.xaml.cs`
- Create: `src/TrackZ.Mobile/Presentation/ITrackZMotion.cs`
- Create: `src/TrackZ.Mobile/Presentation/MauiTrackZMotion.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs`
- Modify: `src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/Features/Workout/MauiSetSavedFeedback.cs`
- Modify: `src/TrackZ.Mobile/Features/Workout/MauiSetSavedPulseDriver.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Test: `tests/TrackZ.Mobile.Tests/Workout/SetLoggerViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Workout/MauiSetSavedFeedbackTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Workout/ReduceMotionTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/SetEntrySheetTests.cs`

**Interfaces:**
- Consumes: existing `SetLoggerViewModel`, local-first `ActiveWorkoutCoordinator.RecordSetAsync`, `SetSavedFeedbackSession`, `IWeightUnitPreference`, `IReduceMotionPreference`, and Task 1's native sheet presenter.
- Produces:

```csharp
public enum SetSavedOutcome
{
    Saved = 1,
    MatchedPrevious = 2,
    PersonalRecord = 3
}

public sealed record SetSavedPresentation(
    LocalSet Set,
    SetSavedOutcome Outcome,
    string PrimaryText,
    string SecondaryText);

public interface ITrackZMotion
{
    Task PlaySetSavedAsync(
        VisualElement target,
        SetSavedOutcome outcome,
        CancellationToken cancellationToken);

    Task PlayWorkoutSummaryAsync(
        VisualElement target,
        CancellationToken cancellationToken);

    void Cancel(VisualElement target);
}
```

- [ ] **Step 1: Write RED tests for native entry, outcome classification, and feedback ordering**

```csharp
[Fact]
public async Task Feedback_starts_only_after_set_and_outbox_are_durable()
{
    var events = new List<string>();
    var repository = new RecordingWorkoutRepository(events);
    var feedback = new RecordingFeedback(events);
    var viewModel = CreateLogger(repository, feedback, previous: Set(45m, 8));

    viewModel.DisplayWeight = 47.5m;
    viewModel.Reps = 8;
    await viewModel.CompleteSetCommand.ExecuteAsync(null);

    Assert.Equal(["sqlite-commit", "feedback-PersonalRecord"], events);
}

[Fact]
public async Task Reduce_motion_uses_fade_without_scale_or_countup()
{
    var driver = new RecordingMotionDriver(reduceMotion: true);
    await driver.PlaySetSavedAsync(new Border(), SetSavedOutcome.PersonalRecord, default);
    Assert.Equal(["fade"], driver.Phases);
}
```

- [ ] **Step 2: Run the focused RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~SetEntrySheetTests|FullyQualifiedName~SetLoggerViewModelTests|FullyQualifiedName~MauiSetSavedFeedbackTests|FullyQualifiedName~ReduceMotionTests" -m:1
```

Expected: FAIL because outcome/presentation and native entry sheet types are absent.

- [ ] **Step 3: Separate comparison display from set-entry state**

Set Logger page displays API/cached artwork, exact previous-session rows, today's durable rows, previous best, and all-time PR. **Log next set** opens a fresh `SetEntrySheetPage` bound to the same transient logger view model. The sheet displays `NextSetNumber`, Match Last, mode-aware weight/assistance, reps, unit selection, validation, and Save.

Tapping the central numeric value may open the numeric keyboard; plus/minus controls remain 44 points and accessible. Bodyweight hides the weight section. Assisted copy says assistance and explains progress through lower assistance only in secondary text.

- [ ] **Step 4: Classify saved outcomes from authoritative known history**

Classify `PersonalRecord` only when the newly durable measurement outranks the currently known PR under weighted/bodyweight/assisted semantics. Classify `MatchedPrevious` when it equals the comparable previous set and is not a PR. Otherwise classify `Saved`. Do not award XP in mobile presentation; display server/provisional XP only from the existing progress snapshot.

- [ ] **Step 5: Implement cancellable native motion and haptics**

Normal save uses a light haptic and short row fade. PR uses one stronger success haptic plus a scale/glow under one second. Reduce Motion uses fade only. All phases run through `SetSavedFeedbackSession.TryStartPhase`, accept the session token, cancel on page deactivation/account reset, and never convert a post-commit animation exception into Save Failed.

- [ ] **Step 6: Verify repeated page lifetimes do not leak transient logger instances**

Add a test that opens/closes Set Logger eight times through the real MAUI provider, calls page deactivation, forces collection, and asserts all weak references are collected while singleton services remain alive. Assert connectivity/session subscriptions return to their original counts.

- [ ] **Step 7: Run logger, lifecycle, architecture, and iOS compile verification**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~SetLoggerViewModelTests|FullyQualifiedName~MauiSetSavedFeedbackTests|FullyQualifiedName~ReduceMotionTests|FullyQualifiedName~SetEntrySheetTests|FullyQualifiedName~AccountSessionBoundaryTests|FullyQualifiedName~MobileCoreDependencyTests" -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1
```

Expected: all selected tests PASS; no transient VM leak; iOS compile exits 0.

- [ ] **Step 8: Commit the native logger**

```bash
git add src/TrackZ.Mobile.Core/Features/Workout src/TrackZ.Mobile/Features/Workout src/TrackZ.Mobile/Presentation src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/Workout tests/TrackZ.Mobile.Tests/NativeIos/SetEntrySheetTests.cs
git commit -m "feat: add native durable set logger"
```

---

### Task 6: Native History, Detail, and Conflict Sheets

**Files:**
- Create: `src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryDetailViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/History/WorkoutHistoryDetailPage.xaml`
- Create: `src/TrackZ.Mobile/Features/History/WorkoutHistoryDetailPage.xaml.cs`
- Create: `src/TrackZ.Mobile/Features/History/HistorySetEditorSheetPage.xaml`
- Create: `src/TrackZ.Mobile/Features/History/HistorySetEditorSheetPage.xaml.cs`
- Create: `src/TrackZ.Mobile/Features/History/HistoryConflictSheetPage.xaml`
- Create: `src/TrackZ.Mobile/Features/History/HistoryConflictSheetPage.xaml.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryViewModel.cs`
- Modify: `src/TrackZ.Mobile/Features/History/WorkoutHistoryPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/History/WorkoutHistoryPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/AppShell.xaml.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Test: `tests/TrackZ.Mobile.Tests/History/WorkoutHistoryDetailViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/HistoryPresentationTests.cs`

**Interfaces:**
- Consumes: `WorkoutHistoryCoordinator`, `IHistoryConfirmation`, `IConflictResolution`, `IWeightUnitPreference`, current history mutation/outbox semantics, and Task 1's native sheets.
- Produces: route `workout-history-detail`, detail view model keyed by workout ID, native edit/delete confirmations, and local/server conflict comparison without changing sync semantics.

- [ ] **Step 1: Write RED tests for grouped list, detail lifetime, and safe conflict actions**

```csharp
[Fact]
public async Task History_list_opens_detail_without_eagerly_expanding_every_set()
{
    var viewModel = CreateHistoryList(ThreeCompletedWorkouts());
    await viewModel.LoadAsync();
    Assert.Equal(3, viewModel.Workouts.Count);
    Assert.All(viewModel.Workouts, item => Assert.False(item.IsExpanded));
}

[Fact]
public async Task Ambiguous_reconciling_history_disables_destructive_resolution()
{
    var viewModel = CreateDetail(ReconcilingWorkout());
    await viewModel.LoadAsync(viewModel.WorkoutId);
    Assert.True(viewModel.IsReconciling);
    Assert.False(viewModel.KeepServerCommand.CanExecute(null));
    Assert.False(viewModel.ApplyLocalCommand.CanExecute(null));
    Assert.False(viewModel.DeleteWorkoutCommand.CanExecute(null));
}
```

- [ ] **Step 2: Run focused RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutHistoryDetailViewModelTests|FullyQualifiedName~HistoryPresentationTests" -m:1
```

Expected: FAIL because the detail view model/pages and collapsed grouped-list state are absent.

- [ ] **Step 3: Split history list from detail state**

History root displays compact completed-workout rows grouped by month, with date, duration, exercise count, set count, volume, and sync status. Tapping pushes one detail page. The detail view model loads exactly one workout, preserves exercise/set order, formats names and kg/lb consistently, and exposes existing edit/delete/undo/conflict commands.

- [ ] **Step 4: Present edit, delete, and conflict operations as native sheets**

Edit Set reuses mode-aware numeric controls. Delete Set/Exercise/Workout uses localized native destructive confirmation. Conflict sheet shows local and server summaries, labels the applicable operation, and exposes only valid Keep Server/Apply Local actions. Reconciling and permanent-failure states must preserve existing safety barriers.

- [ ] **Step 5: Verify cancellation and private-state cleanup**

Navigate into a detail, start load, reset account, then assert no stale workout rows or confirmation sheets appear. Repeated push/pop must deactivate and collect transient detail view models. Thai and English destructive/action labels must be exact.

- [ ] **Step 6: Run history, sync, acceptance, and XAML verification**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutHistoryViewModelTests|FullyQualifiedName~WorkoutHistoryDetailViewModelTests|FullyQualifiedName~WorkoutHistoryCoordinatorTests|FullyQualifiedName~HistoryPresentationTests|FullyQualifiedName~SyncCoordinatorTests|FullyQualifiedName~OfflineWorkoutAcceptanceTests" -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1
```

Expected: selected tests PASS; historical mutations remain durable/idempotent; iOS compile exits 0.

- [ ] **Step 7: Commit native history**

```bash
git add src/TrackZ.Mobile.Core/Features/History src/TrackZ.Mobile/Features/History src/TrackZ.Mobile/AppShell.xaml.cs src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/History tests/TrackZ.Mobile.Tests/NativeIos/HistoryPresentationTests.cs
git commit -m "feat: redesign workout history for iOS"
```

---

### Task 7: Progress Dashboard, Completion Reveal, and You Settings

**Files:**
- Create: `src/TrackZ.Mobile.Core/Features/Gamification/ProgressDashboardViewModel.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Gamification/ProgressReveal.cs`
- Create: `src/TrackZ.Mobile/Components/WeeklyStreakView.xaml`
- Create: `src/TrackZ.Mobile/Components/WeeklyStreakView.xaml.cs`
- Create: `src/TrackZ.Mobile/Components/ExerciseProgressChart.xaml`
- Create: `src/TrackZ.Mobile/Components/ExerciseProgressChart.xaml.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Gamification/GamificationViewModels.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Gamification/ProgressSnapshotSource.cs`
- Modify: `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/Features/Summary/WorkoutSummaryPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Summary/WorkoutSummaryPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Test: `tests/TrackZ.Mobile.Tests/Gamification/ProgressDashboardViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Gamification/GamificationViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/MotivationPresentationTests.cs`

**Interfaces:**
- Consumes: `IProgressSnapshotSource`, `ICompletedWorkoutSummarySource`, `IWeightUnitPreference`, existing server-authoritative progress/gamification DTOs, and Task 5's `ITrackZMotion`.
- Produces: one Progress dashboard view model containing level, XP progress, weekly streak, badges, and exercise PR rows; `ProgressReveal`; interruptible completion reveal; You page limited to settings/account/sync state.

```csharp
public sealed record ProgressReveal(
    int XpDelta,
    int PreviousLevel,
    int CurrentLevel,
    IReadOnlyList<string> NewlyEarnedBadgeKeys,
    IReadOnlyList<Guid> ImprovedExerciseIds,
    bool IsProvisional);
```

Extend `GamificationTextSet` with exact Thai/English fields `StreakAccessibilityText`, `WorkoutComplete`, `XpEarnedFormat`, `LevelAdvancedFormat`, `BadgeUnlockedFormat`, and `ProgressPending`. Existing constructor call sites and localization tests must supply every field explicitly.

- [ ] **Step 1: Write RED tests for server-derived rewards and non-punitive streak presentation**

```csharp
[Fact]
public async Task Progress_dashboard_uses_one_authoritative_snapshot_for_level_streak_badges_and_prs()
{
    var source = new RecordingProgressSource(Snapshot(level: 12, xp: 720, streakWeeks: 4));
    var viewModel = CreateProgressDashboard(source);
    await viewModel.LoadAsync();
    Assert.Equal(12, viewModel.Level);
    Assert.Equal(720, viewModel.TotalXp);
    Assert.Equal(4, viewModel.CurrentStreakWeeks);
    Assert.NotEmpty(viewModel.Badges);
    Assert.NotEmpty(viewModel.Exercises);
    Assert.Equal(1, source.RefreshCount);
}

[Fact]
public void Streak_copy_refers_to_weekly_goal_not_daily_app_open()
{
    var text = GamificationResources.English;
    Assert.DoesNotContain("daily", text.StreakAccessibilityText, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("week", text.StreakAccessibilityText, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task Summary_computes_reward_delta_between_cached_baseline_and_authoritative_refresh()
{
    var source = new SequencedProgressSource(
        Snapshot(level: 11, xp: 640, streakWeeks: 3),
        Snapshot(level: 12, xp: 720, streakWeeks: 4, newlyEarned: ["consistent-4"]));
    var viewModel = CreateWorkoutSummary(source, CompletedShoulderWorkout());
    await viewModel.LoadAsync(viewModel.WorkoutId);
    Assert.Equal(80, viewModel.Reveal.XpDelta);
    Assert.Equal(11, viewModel.Reveal.PreviousLevel);
    Assert.Equal(12, viewModel.Reveal.CurrentLevel);
    Assert.Equal(["consistent-4"], viewModel.Reveal.NewlyEarnedBadgeKeys);
}
```

- [ ] **Step 2: Run focused RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~ProgressDashboardViewModelTests|FullyQualifiedName~MotivationPresentationTests" -m:1
```

Expected: FAIL because the combined dashboard and new presentation components are absent.

- [ ] **Step 3: Build one snapshot-driven Progress dashboard**

Combine the current Profile and Exercise Progress presentation state without issuing duplicate refreshes. Apply cached data immediately, mark it provisional, then apply one online refresh. Expose level, total XP, level progress, weekly goal/completed count, current/best streak, badges, and unit-aware exercise PR rows. Account reset cancels and clears the whole dashboard atomically.

Extend `CompletedWorkoutSummary` with start/completion timestamps and the completed workout's exercise IDs/best measurements. `WorkoutSummaryViewModel` captures the cached progress snapshot as its baseline before refresh, compares the refreshed snapshot, and publishes `ProgressReveal`. Clamp `XpDelta` to zero when no trustworthy baseline exists; label the reveal provisional while offline rather than inventing XP. Use `NewlyEarnedBadgeKeys` only from the authoritative profile contract.

- [ ] **Step 4: Rebuild Progress and You tab content**

Progress shows level/XP, weekly streak, PR trend chart, and earned/locked badges in that order. Charts are native XAML paths/shapes or deterministic MAUI drawing—not generated bitmap UI. You contains units, language, weekly goal, haptics, Reduce Motion, sync/account status, and sign-out. Remove badges/streak hero content from You so the tabs have distinct purposes.

- [ ] **Step 5: Implement the interruptible completion reveal**

Workout Summary renders local set/repetition/volume totals first, then cached/server progress. Reveal duration remains below 1.2 seconds and accepts cancellation on dismiss/account reset. Reduce Motion shows all values immediately with one fade. Provisional progress is labeled but never blocks Done.

- [ ] **Step 6: Verify Thai/English, kg/lb, Reduce Motion, and error isolation**

Tests must assert existing cards update in place when unit changes, Thai labels are resource-backed, a progress refresh failure retains cached values with a quiet status, and motion failure does not change completed workout state or show Save Failed.

- [ ] **Step 7: Run gamification, progress, architecture, and iOS compile verification**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~ProgressDashboardViewModelTests|FullyQualifiedName~GamificationViewModelTests|FullyQualifiedName~MotivationPresentationTests|FullyQualifiedName~MobileCoreDependencyTests|FullyQualifiedName~MauiCompositionTests" -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1
```

Expected: all selected tests PASS and iOS compile exits 0.

- [ ] **Step 8: Commit Progress and motivation redesign**

```bash
git add src/TrackZ.Mobile.Core/Features/Gamification src/TrackZ.Mobile/Features/Progress src/TrackZ.Mobile/Features/Summary src/TrackZ.Mobile/Features/Profile src/TrackZ.Mobile/Components src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/Gamification tests/TrackZ.Mobile.Tests/NativeIos/MotivationPresentationTests.cs
git commit -m "feat: add native progress and reward experience"
```

---

### Task 8: Accessibility, Simulator Acceptance, and Obsolete UI Removal

**Files:**
- Create: `tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs`
- Create: `tests/TrackZ.Mobile.Tests/NativeIos/AccessibilitySemanticsTests.cs`
- Create: `tests/TrackZ.Mobile.Tests/NativeIos/BusinessErrorPresentationTests.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Shared/BusinessErrorPresentation.cs`
- Create: `docs/testing/native-ios-simulator-walkthrough.md`
- Modify: `src/TrackZ.Mobile/Resources/Styles/Styles.xaml`
- Modify: `src/TrackZ.Mobile/Resources/Styles/Colors.xaml`
- Modify: `src/TrackZ.Mobile/TrackZ.Mobile.csproj`
- Modify: redesigned XAML pages/components from Tasks 1–7
- Delete only after reference scan: obsolete generic card/button components and unused OpenSans font registrations/files

**Interfaces:**
- Consumes: every prior task, the existing local API/Compose stack, signed media, session lifecycle, and iOS simulator build.
- Produces: one release-level native acceptance suite, documented simulator walkthrough, accessibility guarantees, clean resource graph, and a reviewable screenshot checklist.

- [ ] **Step 1: Write RED acceptance and accessibility tests**

```csharp
[Fact]
public async Task Cached_catalog_to_active_workout_to_summary_survives_relaunch()
{
    await using var fixture = await NativeExperienceFixture.CreateAsync();
    await fixture.SeedApiExerciseAsync("Machine Shoulder Press", hasArtwork: true);

    await fixture.Train.LoadAsync();
    await fixture.Picker.LoadAsync(BodyPart.Shoulders);
    await fixture.Picker.RefreshCompletion;
    var exercise = Assert.Single(fixture.Picker.Exercises);
    Assert.Equal(ExerciseArtworkState.Ready, exercise.ArtworkState);

    await fixture.StartWorkoutAsync(exercise.Id);
    await fixture.LogSetAsync(45m, 8);
    fixture = await fixture.KillAndRecreateAsync();

    Assert.Single((await fixture.Workouts.GetActiveAsync())!.Exercises[0].Sets);
    Assert.True(File.Exists(fixture.CachedArtworkPath));
}

[Fact]
public void Every_increment_decrement_and_primary_action_has_distinct_semantics_and_44_point_target()
{
    var controls = NativeControlInventory.Create();
    Assert.All(controls, control => Assert.True(control.MinimumHeightRequest >= 44));
    Assert.NotEqual(controls.WeightDecrease.Description, controls.WeightIncrease.Description);
    Assert.NotEqual(controls.RepsDecrease.Description, controls.RepsIncrease.Description);
}

[Theory]
[InlineData(30001, "WorkoutNotFound")]
[InlineData(30004, "InvalidSetValue")]
[InlineData(60001, "SyncConflict")]
public void Stable_business_codes_map_to_localized_actions_without_rendering_raw_code(
    int errorCode,
    string expectedResourceKey)
{
    var presentation = BusinessErrorPresenter.Map((BusinessErrorCode)errorCode);
    var message = BusinessErrorText.Resolve(
        presentation.ResourceKey,
        CultureInfo.GetCultureInfo("th-TH"));
    Assert.Equal(expectedResourceKey, presentation.ResourceKey);
    Assert.DoesNotContain(errorCode.ToString(CultureInfo.InvariantCulture), message);
}
```

- [ ] **Step 2: Run RED acceptance tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeIosExperienceAcceptanceTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~BusinessErrorPresentationTests" -m:1
```

Expected: FAIL until the full flow fixture, semantics, and final resource cleanup are implemented.

- [ ] **Step 3: Complete semantic labels, touch targets, Dynamic Type, and safe areas**

Audit every redesigned page in visible task order. Give images concise exercise/body-area descriptions; distinguish weight, assistance, and repetition actions; ensure selection, PR, conflict, pending, and error states have non-color indicators. Replace fixed heights that clip large text with minimum heights and flexible rows. Keep bottom actions above the iPhone safe area. Add `BusinessErrorPresenter.Map(BusinessErrorCode)` in Mobile.Core as a pure mapping to `BusinessErrorPresentation(ResourceKey, Action)` and `BusinessErrorText.Resolve(string, CultureInfo)` for exact Thai/English copy; neither path includes the numeric code.

- [ ] **Step 4: Remove obsolete web-like presentation only after a reference scan**

Run:

```bash
rg -n "ExercisePerformanceCard|XpBar|BadgeTile|WeightStepper|RepsStepper|OpenSans" src/TrackZ.Mobile tests/TrackZ.Mobile.Tests
```

Delete a component/font registration only when the scan proves no redesigned page or test references it. Keep functional shared components that were restyled and still have one clear responsibility. Remove the old five-destination shell and duplicated inline hex colors from redesigned pages.

- [ ] **Step 5: Run complete mobile, server-media, and architecture regressions**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore -m:1
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --no-restore -m:1
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~Media|FullyQualifiedName~WorkoutPersistence|FullyQualifiedName~Migration" -m:1
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~MediaEndpointTests|FullyQualifiedName~WorkoutEndpointTests|FullyQualifiedName~Sync" -m:1
dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore -m:1
dotnet build src/TrackZ.Mobile.Core/TrackZ.Mobile.Core.csproj --no-restore -m:1
```

Expected: all commands exit 0 with no failed tests and no TrackZ warnings.

- [ ] **Step 6: Build the final iOS app and record the Android environment result separately**

Run:

```bash
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios -r iossimulator-arm64 --no-restore -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-android -p:BuildProjectReferences=false -m:1
```

Expected: iOS XAML compile and simulator build exit 0. If Android SDK remains absent, record exact XA5300 as an environment gate; do not download an SDK or claim Android compilation.

- [ ] **Step 7: Execute the documented simulator walkthrough**

Start only the local TrackZ API, PostgreSQL, and object storage required for the walkthrough. On the simulator:

1. sign in;
2. open Train and select Shoulders;
3. search `shoulder press`;
4. verify the per-exercise API image, LAST, and PR;
5. select two exercises and start;
6. log sets online, then with network disabled;
7. edit/delete through native actions;
8. finish and observe summary/PR/XP/streak/badge states;
9. relaunch and verify workout, sync, and image cache;
10. repeat the essential path with Reduce Motion and a large Dynamic Type setting.

Capture the smallest supported iPhone and a Pro Max screenshot set. Stop API, Compose services, simulator, and Docker Desktop after the walkthrough while retaining test volumes unless the user explicitly requests deletion.

- [ ] **Step 8: Perform final hygiene and commit**

Run:

```bash
git diff --check
git status --short
ps -axo pid,etime,%cpu,command
```

Confirm no TrackZ API, testhost, simulator app, Testcontainers, PostgreSQL, MinIO, Java, or aapt2 process remains from the task. Preserve unrelated user processes and worktree changes.

```bash
git add src/TrackZ.Mobile src/TrackZ.Mobile.Core/Features/Shared tests/TrackZ.Mobile.Tests docs/testing/native-ios-simulator-walkthrough.md
git commit -m "feat: complete native iOS experience"
```

## Final Review Gate

Before integration, compare the final implementation against every section of the spec and verify:

- four native tabs and independent navigation stacks;
- native body-area and set-entry sheets;
- API-backed artwork visible during search and offline from bounded cache;
- no prescribed set totals;
- durable-before-feedback logging;
- active workout add/remove/reorder;
- history edit/delete/conflict safety;
- XP/level/streak/badges derived from authoritative snapshots;
- Thai/English, kg/lb, Dynamic Type, VoiceOver, Reduce Motion, and 44-point targets;
- full iOS simulator walkthrough and process/container cleanup.
