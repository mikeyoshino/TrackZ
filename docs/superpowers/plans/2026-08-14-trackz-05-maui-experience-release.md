# TrackZ MAUI Experience and Release Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the native Android/iOS experience with auth, Home/body-part flow, Thai/English and kg/lb settings, game-quality motion/haptics, accessibility, privacy controls, and reproducible release verification.

**Architecture:** AppShell switches between an unauthenticated auth flow and four authenticated tabs. ViewModels call use cases/coordinators; code-behind contains only view lifecycle and animation hookup. Platform services wrap Secure Storage, haptics, system Reduce Motion, and locale so they remain unit-testable.

**Tech Stack:** .NET 10, .NET MAUI XAML, MVVM, platform Secure Storage/haptics, RESX localization, ASP.NET Core, xUnit, Android/iOS build tooling.

## Global Constraints

- Production UI is native XAML and keeps the approved dark charcoal/lime performance visual language.
- Game feel comes from meaningful motion and feedback, not an RPG/neon redesign.
- Press feedback is 120–180 ms, navigation 220–300 ms, and PR/summary celebration is interruptible and at most 1.2 seconds.
- Save SQLite state before animation/haptic feedback.
- Respect system Reduce Motion, expose a haptic toggle, and keep sound off by default.
- Support Thai and English, selected from device locale with a Settings override.
- Support kg and lb display while preserving decimal kilograms as canonical data.
- Access/refresh tokens live only in Secure Storage and never appear in logs.
- Meet screen-reader, dynamic text, contrast, offline-status, and 60 FPS acceptance criteria.

---

### Task 1: Auth Session, Secure Tokens, and Native Auth Flow

**Files:**
- Create: `src/TrackZ.Mobile/Auth/ISecureSessionStore.cs`
- Create: `src/TrackZ.Mobile/Auth/SecureSessionStore.cs`
- Create: `src/TrackZ.Mobile/Auth/AuthSessionCoordinator.cs`
- Create: `src/TrackZ.Mobile/Networking/AuthenticatedApiHandler.cs`
- Create: `src/TrackZ.Mobile/Features/Auth/LoginPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Auth/LoginViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Auth/RegisterPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Auth/RegisterViewModel.cs`
- Create: `src/TrackZ.Mobile/AppShell.xaml`
- Test: `tests/TrackZ.Mobile.Tests/Auth/AuthSessionCoordinatorTests.cs`

**Interfaces:**
- Consumes: Plan 1 auth endpoints and `AuthTokenPair`.
- Produces: `SignInAsync`, single-flight `RefreshAsync`, `SignOutAsync`, authenticated HTTP retry once, and Auth/Main shell routing.

- [ ] **Step 1: Write failing secure-session/refresh tests**

```csharp
[Fact]
public async Task Concurrent_401_responses_trigger_one_refresh()
{
    await Task.WhenAll(_sut.EnsureFreshTokenAsync(), _sut.EnsureFreshTokenAsync());
    await _api.Received(1).RefreshAsync(Arg.Any<string>(), default);
}

[Fact]
public async Task Error_10003_clears_session_and_routes_to_login()
{
    _api.RefreshAsync(Arg.Any<string>(), default).Throws(ApiErrors.RefreshTokenInvalid());
    await Assert.ThrowsAsync<SessionExpiredException>(() => _sut.EnsureFreshTokenAsync());
    await _store.Received(1).ClearAsync();
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter AuthSessionCoordinatorTests
```

- [ ] **Step 3: Implement secure session and XAML auth screens**

Store only access token, refresh token, expiry, and session ID through platform Secure Storage. Never persist passwords. The HTTP handler refreshes once and retries the original request once; a second 401 signs out. ViewModels expose localized field validation and never log request bodies.

- [ ] **Step 4: Run tests and platform builds**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter Auth
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios
```

- [ ] **Step 5: Commit auth UI/session**

```bash
git add src/TrackZ.Mobile/Auth src/TrackZ.Mobile/Networking src/TrackZ.Mobile/Features/Auth src/TrackZ.Mobile/AppShell.xaml tests/TrackZ.Mobile.Tests
git commit -m "feat: add secure native auth flow"
```

### Task 2: Thai/English Resources and kg/lb Presentation

**Files:**
- Create: `src/TrackZ.Mobile/Resources/Strings/AppResources.resx`
- Create: `src/TrackZ.Mobile/Resources/Strings/AppResources.th.resx`
- Create: `src/TrackZ.Mobile/Localization/LocalizationService.cs`
- Create: `src/TrackZ.Mobile/Units/WeightConverter.cs`
- Create: `src/TrackZ.Mobile/Localization/DeviceTimeZoneProvider.cs`
- Create: `src/TrackZ.Application/Identity/UpdatePreferences/UpdateUserPreferencesCommand.cs`
- Create: `src/TrackZ.Mobile/Features/Settings/SettingsPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Settings/SettingsViewModel.cs`
- Test: `tests/TrackZ.Mobile.Tests/Localization/LocalizationTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Units/WeightConverterTests.cs`

**Interfaces:**
- Consumes: device locale, user override, and canonical decimal kg.
- Produces: observable culture/unit/time-zone settings, `ToDisplay`/`ToCanonicalKg` conversion, and synchronized user preferences.

- [ ] **Step 1: Write failing localization and conversion tests**

```csharp
[Fact]
public void Seventy_kilograms_displays_as_154_3_pounds()
    => Assert.Equal(154.3m, WeightConverter.ToDisplayKg(70m, WeightUnit.Pounds, 1));

[Fact]
public void Thai_resource_contains_home_prompt()
{
    var culture = CultureInfo.GetCultureInfo("th-TH");
    Assert.Equal("วันนี้จะออกอะไร?", AppResources.ResourceManager.GetString("Home_TrainingPrompt", culture));
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter "Localization|WeightConverter"
```

- [ ] **Step 3: Implement resources, settings, and exact conversion**

Use `1 kg = 2.2046226218 lb`. Round only display values; convert edited lb back to decimal kg before local persistence. Settings values are `System|English|Thai` and `Kilograms|Pounds`. Update `CultureInfo.CurrentUICulture` through one localization service and recreate the visible shell after a language override. Read the device IANA time-zone ID on sign-in and when Settings resumes; send language, unit, weekly goal, and time zone through `UpdateUserPreferencesCommand` so server streak boundaries use the same preference snapshot.

- [ ] **Step 4: Run tests and inspect missing-key report**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter "Localization|Units|Settings"
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android
```

Expected: every neutral RESX key exists in Thai and English; conversion round-trips within `0.001 kg`.

- [ ] **Step 5: Commit localization and units**

```bash
git add src/TrackZ.Mobile/Resources/Strings src/TrackZ.Mobile/Localization src/TrackZ.Mobile/Units src/TrackZ.Mobile/Features/Settings tests/TrackZ.Mobile.Tests
git commit -m "feat: localize app and support kg lb"
```

### Task 3: Home, Body-Part Selection, and Main Navigation

**Files:**
- Create: `src/TrackZ.Mobile/Features/Home/HomePage.xaml`
- Create: `src/TrackZ.Mobile/Features/Home/HomeViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Exercises/BodyPartPickerPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Exercises/BodyPartPickerViewModel.cs`
- Create: `src/TrackZ.Mobile/Navigation/MainShell.xaml`
- Create: `src/TrackZ.Mobile/Components/AnatomyImage.xaml`
- Modify: `src/TrackZ.Mobile/AppShell.xaml`
- Test: `tests/TrackZ.Mobile.Tests/Home/HomeViewModelTests.cs`

**Interfaces:**
- Consumes: active-workout coordinator, exercise cache, progress snapshot, and user settings.
- Produces: Home prompt, quick resume, body-part multi-select, Exercise Picker navigation, and Home/Train/Progress/Profile tabs.

- [ ] **Step 1: Write failing resume/navigation test**

```csharp
[Fact]
public async Task Active_workout_changes_primary_action_to_resume()
{
    _workouts.RestoreActiveAsync(default).Returns(LocalWorkoutSamples.Active);
    await _sut.LoadAsync();
    Assert.Equal(HomePrimaryAction.ResumeWorkout, _sut.PrimaryAction);
    await _sut.PrimaryCommand.ExecuteAsync(null);
    await _navigation.Received(1).GoToAsync(Routes.ActiveWorkout);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter "HomeViewModel|BodyPartPicker"
```

- [ ] **Step 3: Implement native XAML hierarchy**

Home shows date, prompt, active/resumable workout, weekly progress, and recent exercise progress. Body Part Picker exposes six localized accessible cards and allows one or more selections. Exercise Picker receives selected enum values and retains multi-selection when navigating back.

- [ ] **Step 4: Run tests and platform builds**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter "Home|BodyPart|Navigation"
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios
```

- [ ] **Step 5: Commit main navigation**

```bash
git add src/TrackZ.Mobile/Features/Home src/TrackZ.Mobile/Features/Exercises/BodyPartPicker* src/TrackZ.Mobile/Navigation src/TrackZ.Mobile/Components/AnatomyImage.xaml src/TrackZ.Mobile/AppShell.xaml tests/TrackZ.Mobile.Tests
git commit -m "feat: add home and body part flow"
```

### Task 4: Motion, Haptics, and Reduced-Motion Behavior

**Files:**
- Create: `src/TrackZ.Mobile/Motion/IMotionPreferences.cs`
- Create: `src/TrackZ.Mobile/Motion/IHapticFeedback.cs`
- Create: `src/TrackZ.Mobile/Motion/PressFeedbackBehavior.cs`
- Create: `src/TrackZ.Mobile/Motion/PageTransitionService.cs`
- Create: `src/TrackZ.Mobile/Motion/CounterAnimation.cs`
- Create: `src/TrackZ.Mobile/Motion/SetSavedAnimation.cs`
- Create: `src/TrackZ.Mobile/Motion/CelebrationOverlay.xaml`
- Test: `tests/TrackZ.Mobile.Tests/Motion/MotionOrchestrationTests.cs`

**Interfaces:**
- Consumes: system Reduce Motion, app haptic preference, and persisted-domain events.
- Produces: cancelable animation tasks and no-motion fallbacks.

- [ ] **Step 1: Write failing persistence-order/reduced-motion tests**

```csharp
[Fact]
public async Task Reduced_motion_uses_fade_and_skips_particles()
{
    _preferences.ReduceMotion.Returns(true);
    await _sut.ShowPrAsync(CancellationToken.None);
    await _renderer.Received(1).FadeAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    await _renderer.DidNotReceive().ParticlesAsync(Arg.Any<CancellationToken>());
}

[Fact]
public async Task New_navigation_cancels_previous_transition()
{
    var first = _sut.NavigateAsync("workout");
    var second = _sut.NavigateAsync("logger");
    await second;
    Assert.True(first.IsCanceled || first.IsCompleted);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter MotionOrchestrationTests
```

- [ ] **Step 3: Implement timing tokens and behaviors**

Define tokens `Press=150ms`, `Navigation=260ms`, `Counter=450ms`, `CelebrationMax=1200ms`. Haptics run only after successful local persistence and only when enabled. All animations accept cancellation tokens, avoid layout-heavy per-frame changes, and use opacity/translation/scale transforms.

- [ ] **Step 4: Run tests and profile representative flows**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter Motion
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android -c Release
```

Profile Save Set, PR overlay, and Summary on a representative mid-range Android device; record frame timing under `docs/performance/motion-baseline.md` and require sustained 60 FPS with no blocking network call.

- [ ] **Step 5: Commit motion system**

```bash
git add src/TrackZ.Mobile/Motion tests/TrackZ.Mobile.Tests docs/performance
git commit -m "feat: add accessible game quality motion"
```

### Task 5: Email Recovery, Account Deletion, and Privacy Hardening

**Files:**
- Create: `src/TrackZ.Application/Identity/VerifyEmail/VerifyEmailCommand.cs`
- Create: `src/TrackZ.Application/Identity/ForgotPassword/ForgotPasswordCommand.cs`
- Create: `src/TrackZ.Application/Identity/ResetPassword/ResetPasswordCommand.cs`
- Create: `src/TrackZ.Application/Identity/DeleteAccount/DeleteAccountCommand.cs`
- Create: `src/TrackZ.Mobile/Features/Auth/ForgotPasswordPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Auth/ResetPasswordPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Settings/DeleteAccountViewModel.cs`
- Test: `tests/TrackZ.Api.Tests/Identity/AccountLifecycleTests.cs`

**Interfaces:**
- Consumes: email sender abstraction, token hashing, object storage, authenticated session.
- Produces: verify/reset flows, `DELETE /api/v1/account`, token revocation, user-data/media cleanup job.

- [ ] **Step 1: Write failing privacy/security tests**

```csharp
[Fact]
public async Task Forgot_password_does_not_reveal_email_existence()
{
    var known = await ForgotAsync("known@example.com");
    var unknown = await ForgotAsync("unknown@example.com");
    Assert.Equal(known.StatusCode, unknown.StatusCode);
    Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
}

[Fact]
public async Task Delete_account_revokes_sessions_and_removes_private_media_access()
{
    await DeleteAccountAsync();
    Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync()).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await GetPrivateImageAsync()).StatusCode);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Api.Tests --filter AccountLifecycleTests
```

- [ ] **Step 3: Implement lifecycle flows and destructive confirmation**

Verification/reset tokens are random, hashed at rest, single-use, and expiring. Forgot always returns the same localized response. Account deletion requires password re-authentication, revokes all sessions in the transaction, tombstones user-owned data, queues private-media deletion, and returns a deletion receipt ID. Mobile requires typed confirmation plus a final destructive button.

- [ ] **Step 4: Run security and log-redaction tests**

```bash
dotnet test tests/TrackZ.Api.Tests --filter "Identity|AccountLifecycle|LogRedaction"
dotnet test tests/TrackZ.Mobile.Tests --filter "ForgotPassword|DeleteAccount"
```

- [ ] **Step 5: Commit account lifecycle**

```bash
git add src/TrackZ.Application/Identity src/TrackZ.Mobile/Features src/TrackZ.Api tests
git commit -m "feat: complete secure account lifecycle"
```

### Task 6: Accessibility and Critical UI Automation

**Files:**
- Create: `tests/TrackZ.Mobile.UITests/TrackZ.Mobile.UITests.csproj`
- Create: `tests/TrackZ.Mobile.UITests/AppiumFixture.cs`
- Create: `tests/TrackZ.Mobile.UITests/Flows/OfflineWorkoutFlowTests.cs`
- Create: `tests/TrackZ.Mobile.UITests/Accessibility/AccessibilitySmokeTests.cs`
- Create: `docs/accessibility/checklist.md`
- Modify: all primary XAML pages to add semantic descriptions and automation IDs.

**Interfaces:**
- Consumes: complete native screen flow.
- Produces: repeatable UI automation for Android/iOS and documented manual accessibility checks.

- [ ] **Step 1: Write failing critical-flow UI test**

```csharp
[Fact]
public async Task User_can_choose_chest_log_offline_restore_and_finish()
{
    await App.LoginAsync();
    await App.SetConnectivityAsync(false);
    await App.TapAsync("home.startWorkout");
    await App.TapAsync("bodyPart.chest");
    await App.TapAsync("exercise.inclineBarbellBenchPress");
    await App.TapAsync("exercisePicker.continue");
    await App.EnterSetAsync(weight: "70", reps: "10");
    await App.RestartAsync();
    Assert.Equal("70", await App.TextAsync("activeWorkout.firstSet.weight"));
}
```

- [ ] **Step 2: Run and verify missing automation IDs**

Install/start Appium 2 with the Android driver in one terminal, install the debug app, then run the UI tests in another:

```bash
npx appium driver install uiautomator2
npx appium --base-path /wd/hub
```

```bash
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android -t:Install
TRACKZ_PLATFORM=Android TRACKZ_APP_PACKAGE=com.trackz.app dotnet test tests/TrackZ.Mobile.UITests -f net10.0
```

Expected: failure locating `home.startWorkout`.

- [ ] **Step 3: Add semantics and stable automation IDs**

Every interactive element receives a localized semantic label and stable English automation ID. Weight/reps announce value and unit; LAST/PR cards announce meaning, not color; sync states announce Pending/Syncing/Synced/Failed/Conflict; dynamic text does not clip at 200% scaling.

- [ ] **Step 4: Run UI and manual accessibility checks**

Run critical flows on Android and iOS, then verify screen reader, contrast, dynamic text, focus order, Reduce Motion, keyboard dismissal, and destructive confirmations using `docs/accessibility/checklist.md`.

- [ ] **Step 5: Commit accessibility coverage**

```bash
git add tests/TrackZ.Mobile.UITests docs/accessibility src/TrackZ.Mobile
git commit -m "test: cover critical mobile accessibility flows"
```

### Task 7: Reproducible Verification and Release Gate

**Files:**
- Create: `eng/verify.sh`
- Create: `.github/workflows/ci.yml`
- Create: `docs/runbook/local-development.md`
- Create: `docs/runbook/release-checklist.md`

**Interfaces:**
- Consumes: complete solution, Docker, Android/iOS toolchains.
- Produces: one verification command and documented release evidence.

- [ ] **Step 1: Write the verification script contract**

`eng/verify.sh` must run, in order:

```bash
dotnet format TrackZ.slnx --verify-no-changes
dotnet build TrackZ.slnx -c Release
dotnet test TrackZ.slnx -c Release --no-build
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android -c Release
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios -c Release
```

It exits on the first failure and prints no environment secrets.

- [ ] **Step 2: Run the script and capture the first real failure**

```bash
chmod +x eng/verify.sh
./eng/verify.sh
```

Expected before final cleanup: failure identifies an actual format/build/test issue rather than a missing command.

- [ ] **Step 3: Add CI and runbooks**

CI restores the pinned SDK/workload, starts PostgreSQL 17 for integration tests, caches NuGet/workloads, runs the verification sequence, and uploads test results only. The local runbook documents approved workload install, `docker compose up -d postgres`, migrations, API start, Android start, and iOS start without including credentials.

- [ ] **Step 4: Fix failures and rerun the complete gate**

```bash
./eng/verify.sh
git status --short
```

Expected: verification exits `0`; only intended source/docs/config changes remain.

- [ ] **Step 5: Commit release hardening**

```bash
git add eng .github docs src tests
git commit -m "build: add TrackZ release verification gate"
```

Plan 5 is complete when the full native app flow works in Thai/English and kg/lb, motion is accessible and performant, account recovery/deletion is secure, Android/iOS builds pass, automated tests are green, migrations run against PostgreSQL, and logs contain no tokens or PII.
