# Thai-Default Live Language Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Thai the first-install language, allow immediate Thai/English switching from Profile, and enforce localization across every shipped mobile page.

**Architecture:** Root data/session/sync services remain singleton while immutable text sets, Shells, pages, and page view models live in a disposable localized UI scope. A serialized language changer persists the choice, applies `th-TH` or `en-US`, builds a candidate scope, swaps the window root without resetting account state, and rolls back on failure.

**Tech Stack:** .NET 10, .NET MAUI native XAML, Microsoft.Extensions.DependencyInjection scopes, RESX/ResourceManager, xUnit, Microsoft.Maui.Storage preferences.

**Spec:** `docs/superpowers/specs/2026-08-22-thai-default-live-language-design.md`

## Global Constraints

- With no valid saved preference, use `th-TH` even when the device language is English.
- Support exactly Thai (`th-TH`) and English (`en-US`); unsupported stored values fall back to Thai.
- Switching language must be immediate and must preserve account generation, tokens, active workout, sets, outbox, caches, images, progress, kg/lb, haptics, and reduce-motion settings.
- App-owned copy must be localized; server-authored exercise names and user-authored custom names must not be translated.
- JSON, SQLite decimals, IDs, signatures, and API payloads remain culture-invariant.
- Language switching may reset the in-memory navigation stack only to the current root tab; it must not reopen a modal editor.
- No backend, database, or API contract changes.
- Keep services stopped unless the user explicitly requests `scripts/trackz-dev start` or `restart`.
- Preserve unrelated dirty worktree files and stage only files owned by each task.

---

## File Structure

### New files

- `src/TrackZ.Mobile.Core/Features/Localization/AppLanguage.cs` — stable language enum, culture mapping, store/changer contracts, option presentation.
- `src/TrackZ.Mobile.Core/Features/Localization/MobileResources.cs` — typed language/settings text set, extended with custom-exercise copy in Task 4.
- `src/TrackZ.Mobile.Core/Resources/MobileStrings.resx` — English language/settings copy, extended in Task 4.
- `src/TrackZ.Mobile.Core/Resources/MobileStrings.th.resx` — exact-key Thai language/settings copy, extended in Task 4.
- `src/TrackZ.Mobile/Localization/MauiAppLanguageStore.cs` — Microsoft.Maui.Storage preference adapter.
- `src/TrackZ.Mobile/Localization/LocalizedUiScopeManager.cs` — candidate/active localized scope lifetime and scoped route resolution.
- `src/TrackZ.Mobile/Localization/MauiAppLanguageChanger.cs` — serialized live-switch transaction and rollback.
- `tests/TrackZ.Mobile.Tests/Localization/AppLanguageTests.cs` — default, malformed, persistence, culture, and rollback tests.
- `tests/TrackZ.Mobile.Tests/Localization/LocalizedUiScopeTests.cs` — scoped composition, route, disposal, and state-preservation tests.
- `tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs` — page manifest, literal-copy, resource parity, enum-presentation, and DI lifetime audit.
- `tests/TrackZ.Mobile.Tests/Acceptance/LiveLanguageAcceptanceTests.cs` — real MAUI Thai/English switch and recreation acceptance.

### Existing files with focused modifications

- `src/TrackZ.Mobile/MauiProgram.cs` — apply startup culture before localized resolution; register root and scoped lifetimes.
- `src/TrackZ.Mobile/App.xaml.cs` — own active localized scope and atomically replace the root page.
- `src/TrackZ.Mobile/AppShell.xaml.cs` — consume scoped typed text and stop global type-based route registration.
- `src/TrackZ.Mobile/Features/Auth/AuthShell.cs` — resolve auth pages from the active scope.
- `src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml(.cs)` — native language selector and localized scope change command.
- `src/TrackZ.Mobile.Core/Features/Gamification/GamificationViewModels.cs` — Profile language state/error presentation.
- `src/TrackZ.Mobile/Features/Exercises/CustomExercisePage.xaml(.cs)` — remove hard-coded English and bind localized option objects.
- `src/TrackZ.Mobile.Core/Features/Exercises/CustomExerciseViewModel.cs` — localized body-part/tracking-mode option presentation.
- `src/TrackZ.Mobile/Features/Train/BodyAreaSheetPage.xaml.cs`, `src/TrackZ.Mobile/Features/History/HistorySetEditorSheetPage.xaml.cs`, `src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml.cs`, `src/TrackZ.Mobile/Features/Workout/MauiSetSavedFeedback.cs` — replace static `Resources.Current` reads with scoped injected text.
- `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`, `src/TrackZ.Mobile.Core/Features/Workout/WorkoutViewModel.cs`, `src/TrackZ.Mobile.Core/Features/Exercises/ExercisePickerViewModel.cs`, `src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryViewModel.cs`, `src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryDetailViewModel.cs`, `src/TrackZ.Mobile.Core/Features/Gamification/ProgressDashboardViewModel.cs` — dispose event subscriptions when a localized scope is replaced.
- `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs` — scoped lifetime and root-singleton preservation assertions.
- `tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs` — include language selector geometry and keep shipped-page manifest aligned.
- `docs/testing/native-ios-simulator-walkthrough.md` — Thai-default and live English-switch manual verification.

---

### Task 1: Stable Preference and Startup Culture

**Files:**
- Create: `src/TrackZ.Mobile.Core/Features/Localization/AppLanguage.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Localization/MobileResources.cs`
- Create: `src/TrackZ.Mobile.Core/Resources/MobileStrings.resx`
- Create: `src/TrackZ.Mobile.Core/Resources/MobileStrings.th.resx`
- Create: `src/TrackZ.Mobile/Localization/MauiAppLanguageStore.cs`
- Create: `tests/TrackZ.Mobile.Tests/Localization/AppLanguageTests.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`

**Interfaces:**
- Produces: `AppLanguage`, `AppLanguageCulture.For`, `AppLanguageCulture.Apply`, `IAppLanguageStore.Read`, `IAppLanguageStore.Write`, `MobileTextSet`, `MobileResources.ForCulture`.
- Consumes: `Microsoft.Maui.Storage.IPreferences` through the production adapter only.

- [ ] **Step 1: Write failing default, malformed, persisted, and culture tests**

```csharp
[Theory]
[InlineData(null, AppLanguage.Thai)]
[InlineData("", AppLanguage.Thai)]
[InlineData("ja-JP", AppLanguage.Thai)]
[InlineData("th-TH", AppLanguage.Thai)]
[InlineData("en-US", AppLanguage.English)]
public void Store_reads_only_the_two_stable_language_tags(string? stored, AppLanguage expected)
{
    var preferences = new MemoryPreferences();
    if (stored is not null) preferences.Set(MauiAppLanguageStore.PreferenceKey, stored);
    Assert.Equal(expected, new MauiAppLanguageStore(preferences).Read());
}

[Fact]
public void Applying_Thai_updates_current_and_default_thread_cultures()
{
    AppLanguageCulture.Apply(AppLanguage.Thai);
    Assert.Equal("th-TH", CultureInfo.CurrentCulture.Name);
    Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
    Assert.Equal("th-TH", CultureInfo.DefaultThreadCurrentCulture!.Name);
    Assert.Equal("th-TH", CultureInfo.DefaultThreadCurrentUICulture!.Name);
}
```

- [ ] **Step 2: Run the focused tests and observe RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter FullyQualifiedName~AppLanguageTests --verbosity minimal -m:1
```

Expected: compile failure because `AppLanguage`, `AppLanguageCulture`, and `MauiAppLanguageStore` do not exist.

- [ ] **Step 3: Add the framework-neutral contract and exact culture mapping**

```csharp
namespace TrackZ.Mobile.Features.Localization;

public enum AppLanguage { Thai = 1, English = 2 }

public static class AppLanguageCulture
{
    public static CultureInfo For(AppLanguage language) => language switch
    {
        AppLanguage.Thai => CultureInfo.GetCultureInfo("th-TH"),
        AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
        _ => CultureInfo.GetCultureInfo("th-TH")
    };

    public static void Apply(AppLanguage language)
    {
        var culture = For(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

public interface IAppLanguageStore
{
    AppLanguage Read();
    void Write(AppLanguage language);
}
```

- [ ] **Step 4: Add exact-key Thai/English language resources**

Create `MobileStrings.resx` and `MobileStrings.th.resx` with these exact non-empty keys: `Language`, `ThaiLanguage`, `EnglishLanguage`, and `LanguageSwitchFailed`. Add:

```csharp
public sealed record MobileTextSet(
    string Language,
    string ThaiLanguage,
    string EnglishLanguage,
    string LanguageSwitchFailed);

public static class MobileResources
{
    public static MobileTextSet ForCulture(CultureInfo culture) => new(
        Value("Language", culture),
        Value("ThaiLanguage", culture),
        Value("EnglishLanguage", culture),
        Value("LanguageSwitchFailed", culture));
}
```

Use `ResourceManager` with the same explicit lookup pattern and missing-key failure behavior as `WorkoutResources`.

- [ ] **Step 5: Add the MAUI preference adapter with Thai fail-closed behavior**

```csharp
public sealed class MauiAppLanguageStore(IPreferences preferences) : IAppLanguageStore
{
    internal const string PreferenceKey = "trackz_app_language_v1";

    public AppLanguage Read() => preferences.Get<string?>(PreferenceKey, null) switch
    {
        "en-US" => AppLanguage.English,
        "th-TH" => AppLanguage.Thai,
        _ => AppLanguage.Thai
    };

    public void Write(AppLanguage language) => preferences.Set(
        PreferenceKey,
        AppLanguageCulture.For(language).Name);
}
```

- [ ] **Step 6: Apply culture before any localized DI registration**

Change `MauiProgram.CreateMauiApp` to accept an optional store for tests without changing existing callers:

```csharp
public static MauiApp CreateMauiApp(
    Action<IServiceCollection>? configureTestServices = null,
    IAppLanguageStore? appLanguageStore = null)
{
    appLanguageStore ??= new MauiAppLanguageStore(Preferences.Default);
    AppLanguageCulture.Apply(appLanguageStore.Read());
    var builder = MauiApp.CreateBuilder();
    builder.Services.AddSingleton(appLanguageStore);
    builder.Services.AddSingleton<IAppLanguageStore>(appLanguageStore);
    // Execute the repository's existing builder and service registrations after these lines.
}
```

- [ ] **Step 7: Run focused and composition regressions**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~AppLanguageTests|FullyQualifiedName~MauiCompositionTests" --verbosity minimal -m:1
```

Expected: all selected tests pass; tests restore global cultures in `finally` blocks so ordering cannot leak.

- [ ] **Step 8: Commit Task 1**

```bash
git add src/TrackZ.Mobile.Core/Features/Localization/AppLanguage.cs src/TrackZ.Mobile.Core/Features/Localization/MobileResources.cs src/TrackZ.Mobile.Core/Resources/MobileStrings.resx src/TrackZ.Mobile.Core/Resources/MobileStrings.th.resx src/TrackZ.Mobile/Localization/MauiAppLanguageStore.cs src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/Localization/AppLanguageTests.cs
git commit -m "feat: default mobile language to Thai"
```

---

### Task 2: Disposable Localized UI Scope

**Files:**
- Create: `src/TrackZ.Mobile/Localization/LocalizedUiScopeManager.cs`
- Create: `tests/TrackZ.Mobile.Tests/Localization/LocalizedUiScopeTests.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `src/TrackZ.Mobile/App.xaml.cs`
- Modify: `src/TrackZ.Mobile/AppShell.xaml.cs`
- Modify: `src/TrackZ.Mobile/Features/Auth/AuthShell.cs`
- Modify: `src/TrackZ.Mobile/Features/Train/BodyAreaSheetPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/Features/History/HistorySetEditorSheetPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml.cs`
- Modify: `src/TrackZ.Mobile/Features/Workout/MauiSetSavedFeedback.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Exercises/ExercisePickerViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryDetailViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Gamification/ProgressDashboardViewModel.cs`
- Test: `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs`

**Interfaces:**
- Consumes: `IAppLanguageStore.Read`, `AppLanguageCulture.Apply`, `AuthGateCoordinator.Snapshot` from Task 1/current identity code.
- Produces: `LocalizedUiScopeManager.CreateCandidate`, `LocalizedUiScopeManager.Activate`, `LocalizedUiScopeManager.ActiveServices`, `ScopedRouteFactory`, `ILocalizedUiHost.Prepare`, `ILocalizedUiHost.InstallAsync`.

- [ ] **Step 1: Write RED tests for scoped identity and root-singleton preservation**

```csharp
[Fact]
public void New_localized_scope_recreates_text_pages_and_shell_but_preserves_root_state_services()
{
    using var app = MauiProgram.CreateMauiApp(appLanguageStore: new MemoryLanguageStore(AppLanguage.Thai));
    var manager = app.Services.GetRequiredService<LocalizedUiScopeManager>();
    using var first = manager.CreateCandidate();
    using var second = manager.CreateCandidate();

    Assert.NotSame(first.Services.GetRequiredService<WorkoutTextSet>(), second.Services.GetRequiredService<WorkoutTextSet>());
    Assert.NotSame(first.Services.GetRequiredService<ProfilePage>(), second.Services.GetRequiredService<ProfilePage>());
    Assert.Same(first.Services.GetRequiredService<LocalWorkoutRepository>(), second.Services.GetRequiredService<LocalWorkoutRepository>());
    Assert.Same(first.Services.GetRequiredService<SyncCoordinator>(), second.Services.GetRequiredService<SyncCoordinator>());
}
```

Add a weak-reference test that disposes the first scope, clears its strong references, forces GC, and asserts its `TrainPage`, `WorkoutPage`, `ExercisePickerPage`, `WorkoutHistoryPage`, `WorkoutHistoryDetailPage`, `ExerciseProgressPage`, `ProfilePage`, and `WorkoutTextSet` are collected while `LocalWorkoutRepository` remains alive. Recording boundary/connectivity/unit-preference fakes must return to their pre-scope subscription counts.

- [ ] **Step 2: Run the focused test and observe RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalizedUiScopeTests|FullyQualifiedName~MauiCompositionTests" --verbosity minimal -m:1
```

Expected: compile failure because the scope manager and scoped registrations do not exist.

- [ ] **Step 3: Implement candidate/active scope ownership**

```csharp
public sealed class LocalizedUiScope : IDisposable
{
    private readonly IServiceScope _scope;
    internal LocalizedUiScope(IServiceScope scope) => _scope = scope;
    public IServiceProvider Services => _scope.ServiceProvider;
    public void Dispose() => _scope.Dispose();
}

public sealed class LocalizedUiScopeManager(IServiceScopeFactory scopes)
{
    private readonly object _gate = new();
    private LocalizedUiScope? _active;
    public IServiceProvider ActiveServices =>
        Active().Services;

    public LocalizedUiScope CreateCandidate() => new(scopes.CreateScope());

    public LocalizedUiScope? Activate(LocalizedUiScope candidate)
    {
        lock (_gate)
        {
            var previous = _active;
            _active = candidate;
            return previous;
        }
    }

    private LocalizedUiScope Active()
    {
        lock (_gate) return _active
            ?? throw new InvalidOperationException("The localized UI scope is not active.");
    }
}
```

Add a route factory that resolves from the active provider:

```csharp
internal sealed class ScopedRouteFactory<TPage>(LocalizedUiScopeManager scopes) : RouteFactory
    where TPage : Element
{
    public override Element GetOrCreate() =>
        scopes.ActiveServices.GetRequiredService<TPage>();

    public override Element GetOrCreate(IServiceProvider services) => GetOrCreate();
}
```

Register all Shell routes exactly once during manager initialization; remove `Routing.RegisterRoute` calls from `AppShell` constructors so a language switch cannot double-register routes.

Define the UI-host boundary used by Task 3:

```csharp
public sealed class LocalizedUiInstallation(LocalizedUiScope scope, Page root) : IDisposable
{
    public LocalizedUiScope Scope { get; } = scope;
    public Page Root { get; } = root;
    public void Dispose() => Scope.Dispose();
}

public interface ILocalizedUiHost
{
    string? CurrentRootTabRoute { get; }
    LocalizedUiInstallation Prepare(AuthGateSnapshot snapshot);
    Task InstallAsync(
        LocalizedUiInstallation installation,
        string? rootTabRoute,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Move only localized UI objects into scopes**

Register the following with `AddScoped`: `AuthTextSet`, `WorkoutTextSet`, `GamificationTextSet`, `MobileTextSet` (introduced in Task 1), `AuthGatePage`, `AuthShell`, `AppShell`, `TrainTodayViewModel`, `TrainPage`, `WorkoutPage`, `ExercisePickerPage`, `ExerciseProgressPage`, and `ProfilePage`.

Keep pushed pages, sheet pages, and their page view models transient; they resolve scoped text sets from their owning scope. Keep `MobileTokenStore`, `IAccountSessionBoundary`, SQLite repositories/caches, coordinators, synchronization services, image caches, and preference services singleton.

Replace all production `WorkoutResources.Current` and `GamificationResources.Current` reads in page constructors/feedback with injected scoped `WorkoutTextSet` or `GamificationTextSet`.

Make every localized view model that subscribes to a root singleton implement `IDisposable`: unsubscribe `SessionReset`, `ConnectivityChanged`, and weight-unit events; dispose/deactivate child momentum/progress items; make disposal idempotent. Existing page `Deactivate` calls remain valid and delegate to the same idempotent cleanup.

- [ ] **Step 5: Make App own the active candidate and resolve every root from it**

```csharp
private LocalizedUiScope? _localizedUi;

private Page ResolveRoot(LocalizedUiScope scope, AuthGateSnapshot snapshot) => snapshot.State switch
{
    AuthGateState.SignedIn => scope.Services.GetRequiredService<AppShell>(),
    AuthGateState.SignedOut => scope.Services.GetRequiredService<AuthShell>(),
    _ => scope.Services.GetRequiredService<AuthGatePage>()
};

private void InstallInitialRoot(Window window)
{
    var candidate = _localizedScopes.CreateCandidate();
    window.Page = ResolveRoot(candidate, _authentication.Snapshot);
    _localizedUi = candidate;
    _localizedScopes.Activate(candidate)?.Dispose();
}
```

Authentication changes resolve a replacement root from the active scope rather than the root provider. Destroying the Window disposes the active scope exactly once.

- [ ] **Step 6: Prove routes and templates use the active scope**

Add a test that activates Thai scope A, resolves a routed `SetLoggerPage`, activates English scope B, resolves it again, and asserts the second page/text belong to B. Mutate the route factory in the test to use the root provider and verify the test fails before restoring production.

- [ ] **Step 7: Run scoped composition, auth, navigation, and leak tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalizedUiScopeTests|FullyQualifiedName~MauiCompositionTests|FullyQualifiedName~TrainNavigationTests|FullyQualifiedName~ExercisePickerInteractionTests|FullyQualifiedName~Auth" --verbosity minimal -m:1
```

Expected: all selected tests pass; one route registration exists per route and no localized page is a root singleton.

- [ ] **Step 8: Commit Task 2**

```bash
git add src/TrackZ.Mobile/Localization/LocalizedUiScopeManager.cs src/TrackZ.Mobile/MauiProgram.cs src/TrackZ.Mobile/App.xaml.cs src/TrackZ.Mobile/AppShell.xaml.cs src/TrackZ.Mobile/Features/Auth/AuthShell.cs src/TrackZ.Mobile/Features/Train/BodyAreaSheetPage.xaml.cs src/TrackZ.Mobile/Features/History/HistorySetEditorSheetPage.xaml.cs src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml.cs src/TrackZ.Mobile/Features/Workout/MauiSetSavedFeedback.cs src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs src/TrackZ.Mobile.Core/Features/Workout/WorkoutViewModel.cs src/TrackZ.Mobile.Core/Features/Exercises/ExercisePickerViewModel.cs src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryViewModel.cs src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryDetailViewModel.cs src/TrackZ.Mobile.Core/Features/Gamification/ProgressDashboardViewModel.cs tests/TrackZ.Mobile.Tests/Localization/LocalizedUiScopeTests.cs tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs
git commit -m "refactor: scope localized mobile UI"
```

---

### Task 3: Transactional Live Language Switch and Profile Selector

**Files:**
- Create: `src/TrackZ.Mobile/Localization/MauiAppLanguageChanger.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Localization/AppLanguage.cs`
- Modify: `src/TrackZ.Mobile/App.xaml.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Gamification/GamificationViewModels.cs`
- Modify: `src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml.cs`
- Test: `tests/TrackZ.Mobile.Tests/Localization/AppLanguageTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Localization/LocalizedUiScopeTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs`

**Interfaces:**
- Consumes: `LocalizedUiScopeManager.CreateCandidate/Activate`, `IAppLanguageStore`, `AppLanguageCulture`, `AuthGateCoordinator.Snapshot`.
- Produces: `IAppLanguageChanger.Current`, `IAppLanguageChanger.IsChanging`, `IAppLanguageChanger.ChangeAsync`, `LanguageOption`.

- [ ] **Step 1: Write RED tests for same-language no-op, serialization, rollback, and state preservation**

```csharp
[Fact]
public async Task English_switch_replaces_one_scope_and_preserves_root_state()
{
    var fixture = await LanguageFixture.CreateAsync(AppLanguage.Thai);
    var workout = await fixture.StartWorkoutAndSaveSetAsync();
    var session = fixture.SessionBoundary.Capture();
    var sync = fixture.Services.GetRequiredService<SyncCoordinator>();

    await Task.WhenAll(
        fixture.Language.ChangeAsync(AppLanguage.English),
        fixture.Language.ChangeAsync(AppLanguage.English));

    Assert.Equal(AppLanguage.English, fixture.Language.Current);
    Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
    Assert.Equal(1, fixture.UiHost.ReplacementCount);
    Assert.Equal(session, fixture.SessionBoundary.Capture());
    Assert.Same(sync, fixture.Services.GetRequiredService<SyncCoordinator>());
    Assert.Equal(workout.Id, (await fixture.Repository.GetActiveAsync())!.Id);
}
```

Add separate tests for preference-write failure, candidate-composition failure, UI-install failure, logout racing the candidate build, and caller cancellation. Each failure must leave the old root/culture/preference active; caller cancellation must rethrow `OperationCanceledException`.

- [ ] **Step 2: Run focused tests and observe RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~AppLanguageTests|FullyQualifiedName~LocalizedUiScopeTests" --verbosity minimal -m:1
```

Expected: compile failure for `IAppLanguageChanger`, `LanguageOption`, and `MauiAppLanguageChanger`.

- [ ] **Step 3: Add the language presentation and changer contract**

```csharp
public sealed record LanguageOption(
    AppLanguage Value,
    string Label,
    string AccessibilityLabel,
    bool IsSelected);

public interface IAppLanguageChanger
{
    AppLanguage Current { get; }
    bool IsChanging { get; }
    Task ChangeAsync(AppLanguage language, CancellationToken cancellationToken = default);
}

public sealed class AppLanguageChangeException : Exception
{
    public AppLanguageChangeException() : base("The localized UI replacement failed.") { }
}
```

- [ ] **Step 4: Implement one serialized transactional switch**

`MauiAppLanguageChanger` depends on `IAppLanguageStore`, `ILocalizedUiHost`, and `AuthGateCoordinator`. `ChangeAsync` uses one `SemaphoreSlim`. Inside the gate it returns immediately for the current language, captures the old language, authentication snapshot, and `CurrentRootTabRoute`, writes the target, applies target culture, calls `ILocalizedUiHost.Prepare`, rechecks `AuthGateCoordinator.Snapshot`, prepares a replacement for the newer auth state if it changed, then calls `InstallAsync`. Successful installation activates the candidate and disposes the old scope. After rollback, non-cancellation failures throw `AppLanguageChangeException` with no storage/platform exception message exposed to UI.

Its catch path disposes the candidate, restores the old preference and all four culture properties, leaves the old root active, rethrows caller cancellation, and maps non-cancellation failure to Profile's localized error state.

- [ ] **Step 5: Add Profile language state and command**

Extend `ProfileViewModel` with:

```csharp
public ObservableCollection<LanguageOption> Languages { get; } = [];
public AsyncCommand ChangeLanguageCommand { get; }
public string? LanguageError { get; private set; }

private async Task ChangeLanguageAsync(object? parameter)
{
    if (parameter is not AppLanguage language || _language.IsChanging) return;
    LanguageError = null;
    try { await _language.ChangeAsync(language); }
    catch (OperationCanceledException) { throw; }
    catch (AppLanguageChangeException)
    {
        LanguageError = _mobileText.LanguageSwitchFailed;
        OnPropertyChanged(nameof(LanguageError));
    }
}
```

Populate two options in stable Thai/English order and update selected state after successful replacement.

- [ ] **Step 6: Add the native Profile selector above kg/lb**

Use one `Border` card with a localized section label and a two-column Grid. Each Button uses `TrackZSecondaryButtonStyle`, a 44-point-or-larger target, exact selected-state DataTrigger, command parameter `AppLanguage`, and localized accessibility description. Bind a `TrackZInlineErrorStyle` Label to `LanguageError`.

- [ ] **Step 7: Add UI structure and mutation-sensitive tests**

Assert selector order before weight, exactly two options, shared styles, selected state for both cultures, no literal copy, and one exact `IAppLanguageChanger` instance. Deliberately move language below weight and remove the English selected trigger; observe both tests fail, then restore.

- [ ] **Step 8: Run focused language/Profile/composition tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~AppLanguage|FullyQualifiedName~LocalizedUiScope|FullyQualifiedName~Profile|FullyQualifiedName~MauiComposition|FullyQualifiedName~AppWideVisualConsistency" --verbosity minimal -m:1
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit Task 3**

```bash
git add src/TrackZ.Mobile.Core/Features/Localization/AppLanguage.cs src/TrackZ.Mobile/Localization/MauiAppLanguageChanger.cs src/TrackZ.Mobile/App.xaml.cs src/TrackZ.Mobile/MauiProgram.cs src/TrackZ.Mobile.Core/Features/Gamification/GamificationViewModels.cs src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml.cs tests/TrackZ.Mobile.Tests/Localization/AppLanguageTests.cs tests/TrackZ.Mobile.Tests/Localization/LocalizedUiScopeTests.cs tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs
git commit -m "feat: switch mobile language live"
```

---

### Task 4: Localize Custom Exercise and Mobile-Owned Copy

**Files:**
- Modify: `src/TrackZ.Mobile.Core/Features/Localization/MobileResources.cs`
- Modify: `src/TrackZ.Mobile.Core/Resources/MobileStrings.resx`
- Modify: `src/TrackZ.Mobile.Core/Resources/MobileStrings.th.resx`
- Modify: `src/TrackZ.Mobile.Core/Features/Exercises/CustomExerciseViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Exercises/Services/LocalExerciseImageSelectionCoordinator.cs`
- Modify: `src/TrackZ.Mobile/Features/Exercises/CustomExercisePage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Exercises/CustomExercisePage.xaml.cs`
- Modify: `src/TrackZ.Mobile/Features/Exercises/Services/MauiExerciseServices.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Test: `tests/TrackZ.Mobile.Tests/Exercises/CustomExerciseViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Localization/AppLanguageTests.cs`

**Interfaces:**
- Produces: `MobileTextSet`, `MobileResources.ForCulture`, `LocalizedBodyPartOption`, `LocalizedTrackingModeOption`.
- Consumes: scoped `CultureInfo.CurrentUICulture` established by Tasks 1–3.

- [ ] **Step 1: Write RED resource and localized-option tests**

```csharp
[Theory]
[InlineData("th-TH", "สร้างท่าเอง", "หน้าอก", "ใช้น้ำหนัก")]
[InlineData("en-US", "Custom exercise", "Chest", "Weight")]
public void Custom_exercise_copy_and_enum_options_follow_ui_culture(
    string cultureName, string title, string chest, string weighted)
{
    var text = MobileResources.ForCulture(CultureInfo.GetCultureInfo(cultureName));
    var vm = Fixture.Create(text);
    Assert.Equal(title, text.CustomExerciseTitle);
    Assert.Equal(chest, vm.BodyPartOptions.Single(x => x.Value == BodyPart.Chest).Label);
    Assert.Equal(weighted, vm.TrackingModeOptions.Single(x => x.Value == TrackingMode.Weighted).Label);
}
```

Add an XML key-parity test that compares all `<data name="...">` names in EN and TH files and rejects empty values.

- [ ] **Step 2: Run tests and observe RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~CustomExerciseViewModelTests|FullyQualifiedName~AppLanguageTests" --verbosity minimal -m:1
```

Expected: compile failure because `MobileResources` and localized option types do not exist.

- [ ] **Step 3: Add exact EN/TH resource keys and typed projection**

Both RESX files contain the same keys:

```text
Language, ThaiLanguage, EnglishLanguage, LanguageSwitchFailed,
CustomExerciseTitle, ExerciseName, BodyPart, TrackingMode,
ChooseExerciseImage, PublishedLibraryImages, ExerciseImageRetentionHelp,
SaveExercise, UnsupportedExerciseImage, ImageNotImported, ExerciseNotSaved,
ExerciseSaveFailedFormat, Okay, BodyPartChest, BodyPartBack, BodyPartShoulders,
BodyPartArms, BodyPartLegs, BodyPartCore,
TrackingWeighted, TrackingAssisted, TrackingBodyweight
```

Extend the Task 1 `MobileTextSet` and `MobileResources.ForCulture` to read every new key explicitly; it never falls back to literal English in code.

- [ ] **Step 4: Replace raw enum pickers with localized value objects**

```csharp
public sealed record LocalizedBodyPartOption(BodyPart Value, string Label);
public sealed record LocalizedTrackingModeOption(TrackingMode Value, string Label);

public IReadOnlyList<LocalizedBodyPartOption> BodyPartOptions { get; }
public IReadOnlyList<LocalizedTrackingModeOption> TrackingModeOptions { get; }
public LocalizedBodyPartOption? SelectedBodyPart
{
    get => BodyPartOptions.SingleOrDefault(x => x.Value == BodyPart);
    set => BodyPart = value?.Value;
}
```

Provide the corresponding `SelectedTrackingMode`. Persist only the enum `Value` in `CustomExerciseDraft`.

Make `CustomExerciseViewModel` implement idempotent `IDisposable` and unsubscribe `_boundary.SessionReset`; add a scope-disposal assertion that its boundary subscription returns to baseline after switching language away from an open Custom Exercise route.

- [ ] **Step 5: Bind all Custom Exercise copy and options**

Set the page `Title`, Entry `Placeholder`, Picker `Title`, buttons, section label, help label, alerts, and accessibility descriptions from `MobileTextSet`. Bind Picker display through the option's `Label`; do not use `Enum.ToString()`.

Change `ILocalExerciseImagePicker.PickAsync` to accept `pickerTitle`, and pass `MobileTextSet.ChooseExerciseImage` from `CustomExercisePage` through `LocalExerciseImageSelectionCoordinator`; this keeps the root image picker free of scoped text. Replace the unsupported-file literal exception with a typed `UnsupportedExerciseImageException`, and map it to `MobileTextSet.UnsupportedExerciseImage` in the page.

- [ ] **Step 6: Run Custom Exercise, picker, resource, and iOS XAML tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~CustomExercise|FullyQualifiedName~ExercisePicker|FullyQualifiedName~AppLanguage" --verbosity minimal -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -v:minimal
```

Expected: all tests pass and iOS XAML compile exits 0.

- [ ] **Step 7: Commit Task 4**

```bash
git add src/TrackZ.Mobile.Core/Features/Localization/MobileResources.cs src/TrackZ.Mobile.Core/Resources/MobileStrings.resx src/TrackZ.Mobile.Core/Resources/MobileStrings.th.resx src/TrackZ.Mobile.Core/Features/Exercises/CustomExerciseViewModel.cs src/TrackZ.Mobile.Core/Features/Exercises/Services/LocalExerciseImageSelectionCoordinator.cs src/TrackZ.Mobile/Features/Exercises/CustomExercisePage.xaml src/TrackZ.Mobile/Features/Exercises/CustomExercisePage.xaml.cs src/TrackZ.Mobile/Features/Exercises/Services/MauiExerciseServices.cs src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/Exercises/CustomExerciseViewModelTests.cs tests/TrackZ.Mobile.Tests/Localization/AppLanguageTests.cs
git commit -m "feat: localize custom exercise flow"
```

---

### Task 5: Enforce Localization Across Every Shipped Page

**Files:**
- Create: `tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryViewModel.cs`

**Interfaces:**
- Consumes: `MobileTextSet`, `WorkoutTextSet`, `GamificationTextSet`, `AuthTextSet` and the scoped lifetime contract.
- Produces: explicit `ShippedPages`/`UserFacingComponents` localization manifest and reusable audit helpers.

- [ ] **Step 1: Write a strict shipped-XAML literal-copy audit**

Use the same 17-page manifest as `AppWideVisualConsistencyTests`, plus shared components that expose `Text`, `Title`, `Placeholder`, `SemanticProperties.Description`, or `AutomationProperties.Name`. Parse XAML with `XDocument`; reject literal alphabetic Thai/English in those properties unless the value is a binding/resource or appears in this technical allowlist:

```csharp
private static readonly HashSet<string> AllowedTechnicalLiterals =
[
    "›", "—", "+", "−", "kg", "lb", "XP", "#0",
    "JPEG, PNG, WebP"
];
```

Do not allow full sentences or action labels in the allowlist.

- [ ] **Step 2: Write resource parity and typed-constructor coverage tests**

Compare key sets and non-empty values for `WorkoutStrings.resx/.th.resx` and `MobileStrings.resx/.th.resx`. Reflect each property of `WorkoutTextSet` and `MobileTextSet`; instantiate Thai and English and assert every string is non-empty. Assert Thai and English differ for semantic copy while stable abbreviations (`kg`, `lb`, `XP`) may match.

- [ ] **Step 3: Write raw-enum and lifetime audits**

Assert no user-facing Picker binds `ItemsSource` to raw `BodyParts` or `TrackingModes`. Inspect DI descriptors and assert localized text sets, Shells, pages, and page view models are scoped/transient while repositories, session boundary, and sync coordinators remain singleton.

- [ ] **Step 4: Run audit RED and record every exact violation**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalizationAuditTests|FullyQualifiedName~AppWideVisualConsistencyTests|FullyQualifiedName~NativeIosExperienceAcceptanceTests" --verbosity minimal -m:1
```

Expected: failures list exact file/property/literal or lifetime. Keep the failure message path-specific so one violation cannot mask others.

- [ ] **Step 5: Replace the confirmed user-facing C# literals through typed resources**

Add exact EN/TH `WorkoutStrings` format keys for local/server conflict summaries and exercise/set counts; use them in `SetLoggerViewModel` and `WorkoutHistoryViewModel` instead of `Local`, `Server version`, `exercise`, `set`, and `base` literals.

Do not localize exception messages that cannot reach UI, route names, MIME types, filenames, server-provided names, or invariant serialization formats.

- [ ] **Step 6: Prove mutation sensitivity**

In test copies of XAML/RESX/DI descriptors, make these mutations one at a time and assert the audit rejects each:

```text
Button Text="Save"
SemanticProperties.Description="Delete workout"
remove one Thai RESX key
bind Custom Exercise Picker to raw BodyParts
register ProfilePage as singleton
resolve a route from the root provider
```

Restore production after each mutation.

- [ ] **Step 7: Run the complete localization and visual-contract suite**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~Localization|FullyQualifiedName~AppWideVisualConsistency|FullyQualifiedName~NativeIosExperienceAcceptance|FullyQualifiedName~MauiComposition" --verbosity minimal -m:1
```

Expected: all selected tests pass with all 17 shipped pages still present.

- [ ] **Step 8: Commit Task 5**

```bash
git add tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs src/TrackZ.Mobile.Core/Features/History/WorkoutHistoryViewModel.cs
git diff --cached --name-only
git commit -m "test: audit mobile localization coverage"
```

Before commit, remove any unrelated path shown by `git diff --cached --name-only`; the expected list is exactly the eight paths in the `git add` command.

---

### Task 6: End-to-End Language Switching and Final Verification

**Files:**
- Create: `tests/TrackZ.Mobile.Tests/Acceptance/LiveLanguageAcceptanceTests.cs`
- Modify: `docs/testing/native-ios-simulator-walkthrough.md`
- Modify: task reports under `.superpowers/sdd/2026-08-22-trackz-thai-default-live-language/` when execution uses the repository SDD workflow.

**Interfaces:**
- Consumes: complete language store, scoped UI composition, live changer, Profile selector, and localization audit from Tasks 1–5.
- Produces: real-MAUI acceptance evidence and manual simulator checklist.

- [ ] **Step 1: Write real-MAUI fresh-install and persistence acceptance**

Create a temp language store with no value while setting the test device cultures to `en-US`. Resolve the first Auth page and assert Thai title/action/accessibility. Seed a signed-in identity and active SQLite workout, switch to English through the real Profile command, then assert:

```csharp
Assert.Equal("Progress", EnglishShell.Items[2].Title);
Assert.Equal("You", EnglishShell.Items[3].Title);
Assert.Equal(activeWorkoutId, (await repository.GetActiveAsync())!.Id);
Assert.Equal(sessionGeneration, sessionBoundary.Capture());
Assert.Same(syncCoordinator, services.GetRequiredService<SyncCoordinator>());
```

Dispose the Maui app, recreate it with the same preference store, and assert English appears before any navigation.

- [ ] **Step 2: Write Thai reversal, route, and accessibility acceptance**

Switch back to Thai and resolve Train, picker, set logger, history, progress, profile, auth, and custom exercise through the active scoped routes. Assert localized page/tab/action/state/accessibility copy and culture-formatted weekday/month values. Assert API exercise names and custom names remain byte-for-byte unchanged.

- [ ] **Step 3: Write account/reset/sync race acceptance**

Gate candidate composition, start English switching, trigger account reset/logout, then release composition. Assert the installed root is Thai or English according to the committed preference but is always `AuthShell`, never stale `AppShell`; one sync lifecycle is stopped and no pending data is deleted.

- [ ] **Step 4: Run full Mobile and architecture verification**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --verbosity minimal -m:1
dotnet build src/TrackZ.Mobile.Core/TrackZ.Mobile.Core.csproj --no-restore -m:1 -v:minimal
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -v:minimal
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 --no-restore -m:1 -v:minimal
```

Expected: full Mobile suite passes with zero skipped localization tests; Core and iOS commands exit 0 with zero warnings/errors.

- [ ] **Step 5: Attempt Android compile without installing tooling**

Run:

```bash
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-android -p:BuildProjectReferences=false -m:1 -v:minimal
```

Expected in the current machine state: exact external XA5300 missing-Android-SDK gate. Do not download an SDK or launch an emulator; if the SDK is present, require a clean compile.

- [ ] **Step 6: Update manual simulator checklist**

Document exact checks for fresh Thai launch, Thai auth, Thai signed-in tabs, Profile selector state, immediate English replacement, current-tab preservation, kg/lb preservation, active workout preservation, custom exercise copy/options, accessibility labels, app recreation persistence, and switching back to Thai.

- [ ] **Step 7: Relaunch only when explicitly authorized by the user**

Run after user asks to test:

```bash
./scripts/trackz-dev restart
./scripts/trackz-dev status
```

Expected: API reachable, PostgreSQL/MinIO running, iOS Simulator running, and the newly built app launched.

- [ ] **Step 8: Final diff and process audit**

Run:

```bash
git diff --check
git status --short
./scripts/trackz-dev status
```

Verify no unrelated user changes are staged. Stop services only if the user requests it.

- [ ] **Step 9: Commit Task 6**

```bash
git add tests/TrackZ.Mobile.Tests/Acceptance/LiveLanguageAcceptanceTests.cs docs/testing/native-ios-simulator-walkthrough.md
git add -f .superpowers/sdd/2026-08-22-trackz-thai-default-live-language
git commit -m "test: accept Thai-default live localization"
```

---

## Final Review Checklist

- [ ] Fresh install defaults to Thai independently of device culture.
- [ ] English and Thai switch immediately from Profile.
- [ ] Same-language taps are no-ops; concurrent taps install one scope.
- [ ] Failure and cancellation retain the previous UI/culture/preference.
- [ ] Authentication, account generation, sync identity, SQLite workout/outbox/cache state, images, kg/lb, haptics, and reduce-motion survive.
- [ ] Current root tab is preserved; modal/pushed navigation is intentionally reset.
- [ ] Custom Exercise uses localized copy and value objects, never raw enum display.
- [ ] All shipped pages/components are explicitly audited for UI literals and resource parity.
- [ ] Server/user-authored names and invariant persistence/network formats are unchanged.
- [ ] Full Mobile, Core, iOS XAML, and iOS simulator build evidence is fresh.
- [ ] Services are started/stopped only through `scripts/trackz-dev` and only when requested.
