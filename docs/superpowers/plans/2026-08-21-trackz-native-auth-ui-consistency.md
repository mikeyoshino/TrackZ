# TrackZ Native Authentication and UI Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a native iOS authentication entry flow, honest protected-catalog states, a consistent app-wide visual system, and an explicit Development-only publication path so the Simulator can show the 48 checked-in exercise images delivered through the API.

**Architecture:** A framework-neutral `AuthGateCoordinator` in Mobile.Core owns session bootstrap, registration/login, refresh, and signed-out transitions; the MAUI `App` alone swaps lazy `AuthShell` and `AppShell` roots. A bounded exact-origin HTTP handler refreshes and retries one safe protected request, while `ExercisePickerViewModel` exposes an explicit presentation-state machine. Shared XAML resources become the sole presentation contract, and an Infrastructure publication service crosses the existing Draft → Reviewed → Published domain lifecycle only through an explicit Development command after exact database/object verification.

**Tech Stack:** .NET 10, .NET MAUI XAML native controls, C# async/await, xUnit, ASP.NET Core minimal API hosting, EF Core, PostgreSQL, S3-compatible MinIO, ImageSharp, Testcontainers.

**Spec:** `docs/superpowers/specs/2026-08-21-trackz-native-auth-ui-consistency-design.md`

## Global Constraints

- Keep the approved **Native Performance** direction: iOS system typography, deep neutral surfaces, and lime only for primary action, selection, progress, and success.
- Use the exact spacing scale 4, 8, 12, 16, 24, and 32 points; page side margins are 17–18 points.
- Primary actions are 50–52 points high with 15-point radius; secondary actions and steppers are at least 44 points; fields are 50 points high with 13–15-point radius; cards use 16-point radius.
- Every interactive target is at least 44 by 44 points and remains usable with Dynamic Type, VoiceOver, safe areas, and Reduce Motion.
- Keep `Mobile.Core` framework-neutral: it must not reference MAUI, EF Core, Npgsql, UIKit, or platform assemblies.
- Keep access/refresh tokens in SecureStorage; never persist or log passwords.
- Attach bearer tokens only to the exact configured API origin. Never attach bearer tokens to signed-media requests, follow media redirects, or expose object/repository paths.
- Preserve `IAccountSessionBoundary` generation fencing and clear account-scoped private data on account changes or rejected refresh.
- Retry a protected safe request after refresh at most once; never loop authentication recovery.
- Render API business semantics through localized English/Thai copy; never show numeric business codes to users.
- Keep catalog deployment Draft-by-default. Publication must be an explicit `Development` command, transactional, exact-48, fail-closed, and absent from normal startup/migration/deployment.
- Exercise artwork displayed in the app must come from authenticated API catalog metadata and signed media bytes, never directly from `assets/exercises/images`.
- Follow strict RED → GREEN TDD for every task and run tests sequentially (`-m:1`) to avoid unnecessary heat.
- Do not install an Android SDK or start an emulator as part of implementation. Treat an exact `XA5300` result as an environment gate and verify iOS Simulator instead.

## File and Responsibility Map

### Mobile.Core identity and presentation

- Create `src/TrackZ.Mobile.Core/Identity/AuthGateCoordinator.cs`: session snapshot, bootstrap, refresh policy, login/register/logout transitions, auth-entry contract.
- Create `src/TrackZ.Mobile.Core/Identity/AuthPresentation.cs`: localized EN/TH auth text and form view-model state.
- Modify `src/TrackZ.Mobile.Core/Identity/MobileIdentity.cs`: registration call, structural JWT identity/expiry parsing, complete token snapshot reads.
- Create `src/TrackZ.Mobile.Core/Identity/ProtectedRequestAuthentication.cs`: bounded refresh/retry coordination contract used by the HTTP handler.
- Modify `src/TrackZ.Mobile.Core/Features/Exercises/ExercisePickerViewModel.cs`: explicit picker state and retry/sign-in actions.

### MAUI composition and native UI

- Create `src/TrackZ.Mobile/Features/Auth/AuthGatePage.xaml(.cs)`: neutral checking/refreshing root.
- Create `src/TrackZ.Mobile/Features/Auth/AuthShell.cs`: native navigation container for signed-out screens.
- Create `src/TrackZ.Mobile/Features/Auth/SignInPage.xaml(.cs)` and `CreateAccountPage.xaml(.cs)`: native forms.
- Create `src/TrackZ.Mobile/Identity/MauiDeviceNameProvider.cs`: non-sensitive iOS device label.
- Create `src/TrackZ.Mobile/Networking/AuthenticatedApiHandler.cs`: exact-origin bearer attachment and one safe replay.
- Modify `src/TrackZ.Mobile/App.xaml.cs` and `MauiProgram.cs`: lazy root resolution and signed-in-only sync lifecycle.
- Modify semantic dictionaries in `src/TrackZ.Mobile/Resources/Styles/` and audit every shipped page/component listed in Tasks 4–5.

### Server-side local publication

- Create `src/TrackZ.Infrastructure/Persistence/Seed/ExerciseCatalogPublicationService.cs`: exact preflight and transactional lifecycle crossing.
- Modify `ObjectStorageExerciseCatalogAssetDeployment.cs`: expose read-only exact object verification without uploading.
- Create `src/TrackZ.Api/ExerciseCatalogPublicationCommand.cs`: strict CLI contract and Development guard.
- Modify `src/TrackZ.Api/Program.cs` and Infrastructure DI registration.
- Create `docs/development/simulator-exercise-catalog.md`: reproducible local deploy, publish, API, and Simulator procedure.

---

### Task 1: Identity Snapshot, Registration, and Auth Gate Core

**Files:**
- Create: `src/TrackZ.Mobile.Core/Identity/AuthGateCoordinator.cs`
- Create: `src/TrackZ.Mobile.Core/Identity/AuthPresentation.cs`
- Modify: `src/TrackZ.Mobile.Core/Identity/MobileIdentity.cs`
- Test: `tests/TrackZ.Mobile.Tests/IdentityTokenIntegrationTests.cs`
- Create test: `tests/TrackZ.Mobile.Tests/Identity/AuthGateCoordinatorTests.cs`

**Interfaces:**
- Consumes: `MobileTokenStore`, `TrackZIdentityApiClient`, `TrackZIdentityRefreshClient`, `IMobilePrivateDataCleaner`, `IAccountSessionBoundary`, `IConnectivityService`, `TimeProvider`.
- Produces: `MobileIdentitySnapshot`, `IIdentitySessionApi`, `AuthGateState`, `AuthGateSnapshot`, `IAuthEntryPoint`, `IDeviceNameProvider`, `AuthGateCoordinator`, `AuthFormMode`, `AuthFormViewModel`, and `AuthTextSet`.

- [ ] **Step 1: Write failing token-snapshot and registration tests**

Add tests that require an `exp` claim, reject mismatched stored `userId`/`sessionId`, preserve server `FieldErrors`, and prove register-then-login uses the same credentials and device name:

```csharp
[Fact]
public async Task Complete_snapshot_requires_matching_structural_identity_and_expiry()
{
    var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    var userId = Guid.NewGuid();
    var sessionId = Guid.NewGuid();
    var storage = new MemoryTokenStorage();
    var store = new MobileTokenStore(storage);
    await store.SaveAsync(Jwt(userId, sessionId, now.AddMinutes(15)), "refresh-one");

    var snapshot = await store.GetSnapshotAsync();

    Assert.Equal(new MobileIdentitySnapshot(userId, sessionId, now.AddMinutes(15), "refresh-one"), snapshot);
}

[Fact]
public async Task Register_then_login_preserves_stable_problem_fields_and_uses_one_device_name()
{
    var handler = new RecordingHandler(
        Json(HttpStatusCode.Created, new { userId = Guid.NewGuid(), email = "lift@example.com" }),
        Json(HttpStatusCode.OK, Tokens(Jwt(UserId, SessionId, Now.AddMinutes(15)))));
    var client = IdentityClient(handler);

    await client.RegisterAndLoginAsync("lift@example.com", "Correct-Horse-9", "iPhone Simulator");

    Assert.Equal(["/api/v1/auth/register", "/api/v1/auth/login"], handler.Paths);
    Assert.Contains("iPhone Simulator", handler.Bodies[1], StringComparison.Ordinal);
}
```

Update the shared JWT helper to include numeric Unix `exp`; add separate tests for missing/non-numeric `exp`, malformed base64url, empty `sub`/`sid`, and stored IDs that do not match the token claims.

- [ ] **Step 2: Run the identity tests and verify RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~IdentityTokenIntegrationTests|FullyQualifiedName~AuthGateCoordinatorTests" --verbosity minimal -m:1
```

Expected: compile failure naming missing `MobileIdentitySnapshot`, `GetSnapshotAsync`, `RegisterAndLoginAsync`, and auth-gate types.

- [ ] **Step 3: Implement strict identity parsing and registration**

Add these exact public contracts to `MobileIdentity.cs`:

```csharp
public sealed record MobileIdentitySnapshot(
    Guid UserId,
    Guid SessionId,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);

public Task<MobileIdentitySnapshot?> GetSnapshotAsync(
    CancellationToken cancellationToken = default);

public Task RegisterAndLoginAsync(
    string email,
    string password,
    string deviceName,
    CancellationToken cancellationToken = default);
```

Parse JWT payload properties `sub`, `sid`, and numeric `exp`; require non-empty GUIDs and a UTC expiry after Unix epoch. `GetSnapshotAsync` must read access token, refresh token, stored session ID, and stored user ID under one caller operation, return `null` only when all four are absent, and throw the existing invalid-identity `MobileApiException` for partial or contradictory data. `RegisterAndLoginAsync` must POST `{ email, password }` to `/api/v1/auth/register`, validate success/problem JSON with the existing strict parser, then call `LoginAsync(email, password, deviceName)`.

- [ ] **Step 4: Write failing auth-gate transition tests**

Use a fake identity client boundary instead of HTTP details and cover this table exactly:

```csharp
[Theory]
[InlineData(SessionCase.None, AuthGateState.SignedOut, false)]
[InlineData(SessionCase.Valid, AuthGateState.SignedIn, false)]
[InlineData(SessionCase.ExpiredRefreshSucceeds, AuthGateState.SignedIn, false)]
[InlineData(SessionCase.ExpiredRefreshRejected, AuthGateState.SignedOut, true)]
[InlineData(SessionCase.ExpiredRefreshOffline, AuthGateState.SignedIn, false)]
public async Task Bootstrap_has_one_honest_terminal_state(
    SessionCase scenario,
    AuthGateState expected,
    bool expectedPrivateClear)
```

Also assert: initial state is `CheckingSession`; only one concurrent bootstrap runs; duplicate submit is rejected; cancellation caused by session reset produces no form error; login/register success emits `SignedIn`; logout emits `SignedOut` even when server revocation fails; malformed stored identity clears tokens/private data and signs out.

- [ ] **Step 5: Implement the framework-neutral auth gate and form view model**

Create these contracts:

```csharp
public enum AuthGateState
{
    CheckingSession = 1,
    SignedOut = 2,
    Refreshing = 3,
    SignedIn = 4
}

public sealed record AuthGateSnapshot(
    AuthGateState State,
    bool IsOfflineSession = false,
    BusinessErrorCode? ErrorCode = null);

public interface IDeviceNameProvider
{
    string DeviceName { get; }
}

public interface IIdentitySessionApi
{
    Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default);
    Task RegisterAndLoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default);
    Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

public interface IAuthEntryPoint
{
    Task RequireSignInAsync(CancellationToken cancellationToken = default);
}
```

Make `TrackZIdentityApiClient` implement `IIdentitySessionApi`. `AuthGateCoordinator` consumes that interface and exposes `Snapshot`, `event EventHandler<AuthGateSnapshot>? Changed`, `InitializeAsync`, `SignInAsync`, `RegisterAsync`, `RetryAsync`, `SignOutAsync`, and implements `IAuthEntryPoint`. Serialize transitions with one `SemaphoreSlim`. Treat an unexpired token as signed in using a 30-second clock skew. On expired token: enter `Refreshing`; refresh once; `RefreshTokenInvalid` or an authentication-required exception clears account data and signs out; `HttpRequestException`, `IOException`, and timeout preserve the complete identity and enter `SignedIn(IsOfflineSession: true)`.

Define `AuthFormMode.SignIn=1` and `AuthFormMode.CreateAccount=2`. `AuthFormViewModel` owns `Text`, `Mode`, `Email`, `Password`, `IsSubmitting`, `FormError`, `EmailError`, `PasswordError`, async `SubmitCommand`, and `SetMode(AuthFormMode mode)`. Each transient page sets its mode once in its constructor. It maps `EmailAlreadyExists` to the email field, `PasswordPolicyViolation` to password, `InvalidCredentials` to form, and transport failure to localized retry copy. Create an immutable `AuthTextSet` with explicit English and Thai values for every visible string.

- [ ] **Step 6: Run the focused suite and full Mobile.Core build**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~IdentityTokenIntegrationTests|FullyQualifiedName~AuthGateCoordinatorTests" --verbosity minimal -m:1
dotnet build src/TrackZ.Mobile.Core/TrackZ.Mobile.Core.csproj --no-restore --verbosity minimal -m:1
```

Expected: all focused tests pass; build has zero warnings and zero errors.

- [ ] **Step 7: Commit Task 1**

```bash
git add src/TrackZ.Mobile.Core/Identity tests/TrackZ.Mobile.Tests/IdentityTokenIntegrationTests.cs tests/TrackZ.Mobile.Tests/Identity
git commit -m "feat: add native authentication gate"
```

---

### Task 2: Native Auth Root and Lazy Protected Shell

**Files:**
- Create: `src/TrackZ.Mobile/Features/Auth/AuthGatePage.xaml`
- Create: `src/TrackZ.Mobile/Features/Auth/AuthGatePage.xaml.cs`
- Create: `src/TrackZ.Mobile/Features/Auth/AuthShell.cs`
- Create: `src/TrackZ.Mobile/Features/Auth/SignInPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Auth/SignInPage.xaml.cs`
- Create: `src/TrackZ.Mobile/Features/Auth/CreateAccountPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Auth/CreateAccountPage.xaml.cs`
- Create: `src/TrackZ.Mobile/Identity/MauiDeviceNameProvider.cs`
- Create: `src/TrackZ.Mobile/Properties/AssemblyInfo.cs`
- Modify: `src/TrackZ.Mobile/App.xaml.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/NativeShellTests.cs`
- Create test: `tests/TrackZ.Mobile.Tests/NativeIos/AuthPresentationTests.cs`

**Interfaces:**
- Consumes: Task 1 `AuthGateCoordinator`, `AuthFormViewModel`, `AuthTextSet`, `IDeviceNameProvider`; existing `AppShell` and `IWorkoutSyncLifecycle`.
- Produces: lazy root switching where `AppShell` is resolved only after `SignedIn`, `AuthShell` only for `SignedOut`, and sync lifecycle only runs while signed in.

- [ ] **Step 1: Write failing MAUI composition and root-transition tests**

Add a service-provider probe that counts `AppShell` resolution and a fake sync lifecycle:

```csharp
[Fact]
public async Task Fresh_launch_never_resolves_protected_shell_before_sign_in()
{
    using var app = MauiProgram.CreateMauiApp(services =>
        services.Replace(ServiceDescriptor.Singleton<IWorkoutSyncLifecycle, RecordingSyncLifecycle>()));
    var application = app.Services.GetRequiredService<App>();

    var window = application.CreateTestWindow();
    await application.Initialization;

    Assert.IsType<AuthShell>(window.Page);
    Assert.Equal(0, app.Services.GetRequiredService<ProtectedShellProbe>().ResolutionCount);
    Assert.Equal(0, app.Services.GetRequiredService<RecordingSyncLifecycle>().StartCount);
}
```

Cover signed-in bootstrap → `AppShell` and one sync start, sign-out → `AuthShell` and sync stop, resume while signed out → no sync resume, repeated state event → no duplicate root, and all created auth buttons/entries with target heights at least 44.

- [ ] **Step 2: Run the native composition tests and verify RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~MauiCompositionTests|FullyQualifiedName~NativeShellTests|FullyQualifiedName~AuthPresentationTests" --verbosity minimal -m:1
```

Expected: compile failure for missing auth pages/shell/device provider and the old `App(AppShell, IWorkoutSyncLifecycle)` behavior.

- [ ] **Step 3: Implement lazy root switching in `App`**

Replace eager `AppShell` injection with:

```csharp
public App(
    IServiceProvider services,
    AuthGateCoordinator authentication,
    IWorkoutSyncLifecycle synchronization)
```

`CreateWindow` starts with `services.GetRequiredService<AuthGatePage>()`, subscribes to `authentication.Changed`, assigns the returned task from `authentication.InitializeAsync()` to `internal Task Initialization { get; private set; }`, and runs initialization once. On the UI thread map `SignedOut` to a lazily resolved `AuthShell`, `SignedIn` to a lazily resolved `AppShell`, and checking/refreshing to the stable `AuthGatePage`. Stop sync before leaving `AppShell`; start/resume it only after signed-in root activation. Unsubscribe/cancel on `Window.Destroying`. Add `internal Window CreateTestWindow() => CreateWindow(null);` and expose both internal members through `src/TrackZ.Mobile/Properties/AssemblyInfo.cs` containing `[assembly: InternalsVisibleTo("TrackZ.Mobile.Tests")]`.

- [ ] **Step 4: Implement native auth pages with stable geometry**

Use this exact layout contract for both modes; the create page adds requirement copy but keeps the same field/action positions:

```xml
<Grid Padding="18,24" RowDefinitions="Auto,*,Auto" BackgroundColor="{DynamicResource TrackZBackground}">
  <VerticalStackLayout Spacing="12">
    <Label Style="{DynamicResource TrackZPageTitleStyle}" Text="{Binding Text.WelcomeTitle}" />
    <Label Style="{DynamicResource TrackZBodyStyle}" Text="{Binding Text.WelcomeBody}" />
  </VerticalStackLayout>
  <VerticalStackLayout Grid.Row="1" Spacing="12" VerticalOptions="Center">
    <Border Style="{DynamicResource TrackZFieldContainerStyle}">
      <Entry Style="{DynamicResource TrackZFieldStyle}" Text="{Binding Email}" Keyboard="Email" />
    </Border>
    <Label Style="{DynamicResource TrackZFieldErrorStyle}" Text="{Binding EmailError}" />
    <Border Style="{DynamicResource TrackZFieldContainerStyle}">
      <Entry Style="{DynamicResource TrackZFieldStyle}" Text="{Binding Password}" IsPassword="True" />
    </Border>
    <Label Style="{DynamicResource TrackZFieldErrorStyle}" Text="{Binding PasswordError}" />
    <Label Style="{DynamicResource TrackZFieldErrorStyle}" Text="{Binding FormError}" />
  </VerticalStackLayout>
  <VerticalStackLayout Grid.Row="2" Spacing="8">
    <Button Style="{DynamicResource TrackZPrimaryButtonStyle}" Command="{Binding SubmitCommand}" />
    <Button Style="{DynamicResource TrackZQuietButtonStyle}" Clicked="OnSwitchModeClicked" />
  </VerticalStackLayout>
</Grid>
```

Set iOS automation/content semantics in code-behind: email keyboard, `ReturnType=Next`, password `ReturnType=Go`, focus email→password→submit, keyboard dismissal on submit, safe-area use, and no dead Terms/Privacy links. The Sign in switch handler pushes `CreateAccountPage`; the Create account switch handler pops to Sign in. Each page calls `SetMode` with its exact mode before setting `BindingContext`. `AuthShell` uses native `Shell` navigation without a tab bar. `MauiDeviceNameProvider.DeviceName` returns a trimmed `DeviceInfo.Name`, falling back to `"iOS"`, with maximum 100 characters.

- [ ] **Step 5: Register exact lifetimes and verify root ownership**

Register `AuthGateCoordinator`, `AuthTextSet`, and `IDeviceNameProvider` as singletons; `AuthFormViewModel`, `SignInPage`, and `CreateAccountPage` as transients; `AuthGatePage` and `AuthShell` as singletons. Keep `AppShell` singleton but resolve it lazily from `App`. Tests must resolve the real provider, activate each auth page, and prove the two pages share the same coordinator but do not retain transient form instances after navigation.

- [ ] **Step 6: Run native composition and iOS XAML compile**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~MauiCompositionTests|FullyQualifiedName~NativeShellTests|FullyQualifiedName~AuthPresentationTests" --verbosity minimal -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -verbosity:minimal
```

Expected: tests pass; XAML source generation and managed compile exit 0.

- [ ] **Step 7: Commit Task 2**

```bash
git add src/TrackZ.Mobile/App.xaml.cs src/TrackZ.Mobile/MauiProgram.cs src/TrackZ.Mobile/Features/Auth src/TrackZ.Mobile/Identity src/TrackZ.Mobile/Properties tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs tests/TrackZ.Mobile.Tests/NativeIos
git commit -m "feat: add native sign in and registration flow"
```

---

### Task 3: Bounded Protected-Request Recovery and Honest Picker States

**Files:**
- Create: `src/TrackZ.Mobile.Core/Identity/ProtectedRequestAuthentication.cs`
- Create: `src/TrackZ.Mobile/Networking/AuthenticatedApiHandler.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Exercises/ExercisePickerViewModel.cs`
- Modify: `src/TrackZ.Mobile/Features/Exercises/ExercisePickerPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Exercises/ExercisePickerPage.xaml.cs`
- Create: `src/TrackZ.Mobile/Components/TrackZStateView.xaml`
- Create: `src/TrackZ.Mobile/Components/TrackZStateView.xaml.cs`
- Create: `src/TrackZ.Mobile/Components/ExerciseListSkeleton.xaml`
- Create: `src/TrackZ.Mobile/Components/ExerciseListSkeleton.xaml.cs`
- Test: `tests/TrackZ.Mobile.Tests/IdentityTokenIntegrationTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Exercises/ExercisePickerViewModelTests.cs`
- Create test: `tests/TrackZ.Mobile.Tests/Networking/AuthenticatedApiHandlerTests.cs`
- Modify test: `tests/TrackZ.Mobile.Tests/NativeIos/NativePresentationCompositionTests.cs`

**Interfaces:**
- Consumes: Task 1 `AuthGateCoordinator`, raw identity transport, `MobileTokenStore`, `IAuthEntryPoint`; existing `TrackZExerciseApiClient`, `ExerciseCache`, `IConnectivityService`.
- Produces: `IProtectedRequestAuthentication`, `AuthenticatedApiHandler`, `ExercisePickerPresentationState`, retry and sign-in commands, and stable picker state views.

- [ ] **Step 1: Write failing handler security/retry tests**

Cover exact-origin attachment, signed-media separation, one safe replay, and no replay of mutations:

```csharp
[Fact]
public async Task Exact_origin_get_refreshes_once_and_replays_once_after_401()
{
    var transport = new SequenceHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
    var recovery = new RecordingAuthenticationRecovery(succeeds: true);
    var client = ClientWithHandler(transport, recovery, "old-token", ApiOrigin);

    using var response = await client.GetAsync("/api/v1/exercises?pageSize=50");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(1, recovery.RefreshCount);
    Assert.Equal(["Bearer old-token", "Bearer new-token"], transport.AuthorizationValues);
    Assert.Equal(2, transport.RequestCount);
}

[Theory]
[InlineData("POST")]
[InlineData("PUT")]
[InlineData("DELETE")]
public async Task Unsafe_request_is_never_replayed(string method)
```

Add: foreign origin has no bearer and no recovery; redirect response is not followed by handler; second 401 calls `RequireSignInAsync` and returns the final 401; concurrent 401s share one refresh; caller cancellation is preserved; refresh endpoint uses raw identity transport and cannot recurse through the protected handler.

- [ ] **Step 2: Run handler tests and verify RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~AuthenticatedApiHandlerTests" --verbosity minimal -m:1
```

Expected: compile failure for missing recovery contract and handler.

- [ ] **Step 3: Implement raw identity transport and bounded protected handler**

Define:

```csharp
public interface IProtectedRequestAuthentication
{
    Task<bool> TryRefreshAsync(CancellationToken cancellationToken);
    Task RequireSignInAsync(CancellationToken cancellationToken);
}

public sealed class ProtectedRequestAuthentication(
    TrackZIdentityRefreshClient refreshClient,
    IDeviceNameProvider deviceName,
    IAuthEntryPoint authEntryPoint) : IProtectedRequestAuthentication
{
    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await refreshClient.RefreshAsync(deviceName.DeviceName, cancellationToken);
            return true;
        }
        catch (MobileApiException exception) when (
            exception.ErrorCode == BusinessErrorCode.RefreshTokenInvalid
            || exception.IsAuthenticationRequired)
        {
            await authEntryPoint.RequireSignInAsync(cancellationToken);
            return false;
        }
    }

    public Task RequireSignInAsync(CancellationToken cancellationToken) =>
        authEntryPoint.RequireSignInAsync(cancellationToken);
}
```

Register one raw `IdentityHttpTransport` wrapping a plain `HttpClient` for register/login/refresh/logout. Because logout itself is authenticated, `TrackZIdentityApiClient.LogoutAsync` reads the current access token from `MobileTokenStore` and attaches it directly to its exact-origin raw logout request; register/login/refresh remain credential-free. Register the protected API `HttpClient` with `AuthenticatedApiHandler` for exercise/sync/progress calls. The handler must:

1. Compare scheme, host, effective port, and root path to the configured API origin before attaching bearer.
2. Send once.
3. On `401` for GET or HEAD, dispose the first response, call one single-flight refresh, clone method/URI/version/headers/options without content, attach the rotated token, and send once more.
4. If refresh rejects or the replay returns `401`, call `RequireSignInAsync` once.
5. Return all other statuses untouched so `TrackZExerciseApiClient.EnsureSuccessAsync` preserves `ApiProblemDetails`.

The signed-media `HttpClient` stays a separate plain handler with `AllowAutoRedirect=false` and never passes through `AuthenticatedApiHandler`.

- [ ] **Step 4: Write the picker presentation-state matrix first**

Add the exact enum and a theory whose expected state cannot be inferred only from item count:

```csharp
public enum ExercisePickerPresentationState
{
    InitialLoading = 1,
    AuthenticationRequired = 2,
    OfflineWithCache = 3,
    OfflineWithoutCache = 4,
    RequestFailure = 5,
    NoFilterMatches = 6,
    Results = 7
}

[Theory]
[MemberData(nameof(PickerCases))]
public async Task Picker_exposes_honest_state(PickerCase value)
{
    var vm = value.CreateViewModel();
    await vm.LoadAsync(value.BodyPart);
    await vm.RefreshCompletion;
    Assert.Equal(value.ExpectedState, vm.PresentationState);
    Assert.Equal(value.ExpectedCount, vm.Exercises.Count);
}
```

Cases: loading before cache returns; online authenticated results; online 401 with empty cache; online 500 with empty cache; offline empty; offline cached results; successful catalog followed by a filter with no matches; thumbnail failure with card still present; retry success; account reset during refresh with no stale state mutation.

- [ ] **Step 5: Implement state properties and commands**

Expose `PresentationState`, `HasResults`, `ShowLoading`, `ShowAuthenticationRequired`, `ShowOfflineEmpty`, `ShowRequestFailure`, `ShowNoMatches`, `ShowOfflineBanner`, `StateTitle`, `StateMessage`, `RetryCommand`, and `SignInCommand`. Preserve the full caught `MobileApiException`, including `IsAuthenticationRequired`, rather than only `LastErrorCode`. Compute state in this priority order: active initial load; auth required; transport/API failure with cached rows; offline cached rows; offline no rows; retryable request failure; filter has no matches after a successful/cache-backed catalog; results.

`RetryCommand` reruns the refresh under a fresh account generation. `SignInCommand` invokes `IAuthEntryPoint.RequireSignInAsync`. Thumbnail failure remains per-card `ExerciseArtworkState.Failed` and does not change the page state.

- [ ] **Step 6: Replace ambiguous EmptyView with explicit native states**

Use a single Grid overlay whose visibility properties are mutually exclusive:

```xml
<Grid Grid.Row="3">
  <CollectionView IsVisible="{Binding HasResults}" ItemsSource="{Binding Exercises}" />
  <components:ExerciseListSkeleton IsVisible="{Binding ShowLoading}" />
  <components:TrackZStateView
      IsVisible="{Binding ShowAuthenticationRequired}"
      Title="{Binding StateTitle}"
      Message="{Binding StateMessage}"
      ActionText="{Binding Text.SignIn}"
      ActionCommand="{Binding SignInCommand}" />
  <components:TrackZStateView
      IsVisible="{Binding ShowOfflineEmpty}"
      Title="{Binding StateTitle}"
      Message="{Binding StateMessage}"
      ActionText="{Binding Text.TryAgain}"
      ActionCommand="{Binding RetryCommand}" />
  <components:TrackZStateView
      IsVisible="{Binding ShowRequestFailure}"
      Title="{Binding StateTitle}"
      Message="{Binding StateMessage}"
      ActionText="{Binding Text.TryAgain}"
      ActionCommand="{Binding RetryCommand}" />
  <components:TrackZStateView
      IsVisible="{Binding ShowNoMatches}"
      Title="{Binding StateTitle}"
      Message="{Binding StateMessage}" />
</Grid>
```

Create `TrackZStateView` with bindable `Title`, `Message`, `ActionText`, and `ICommand? ActionCommand`, using the existing body/secondary/primary styles. Create `ExerciseListSkeleton` as three non-animated fixed-height card silhouettes using `TrackZCardStyle` and `TrackZSurfaceRaised`. Task 4 will move their geometry to the expanded semantic resource contract. Do not use `CollectionView.EmptyView` for transport/auth state.

- [ ] **Step 7: Run handler, picker, and composition tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~AuthenticatedApiHandlerTests|FullyQualifiedName~ExercisePickerViewModelTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~IdentityTokenIntegrationTests" --verbosity minimal -m:1
```

Expected: all focused tests pass with no skipped rows.

- [ ] **Step 8: Commit Task 3**

```bash
git add src/TrackZ.Mobile.Core/Identity src/TrackZ.Mobile.Core/Features/Exercises/ExercisePickerViewModel.cs src/TrackZ.Mobile/Networking src/TrackZ.Mobile/MauiProgram.cs src/TrackZ.Mobile/Features/Exercises src/TrackZ.Mobile/Components/TrackZStateView.xaml src/TrackZ.Mobile/Components/TrackZStateView.xaml.cs src/TrackZ.Mobile/Components/ExerciseListSkeleton.xaml src/TrackZ.Mobile/Components/ExerciseListSkeleton.xaml.cs tests/TrackZ.Mobile.Tests
git commit -m "fix: recover protected catalog requests honestly"
```

---

### Task 4: Semantic Native Performance Resource System

**Files:**
- Modify: `src/TrackZ.Mobile/Resources/Styles/TrackZColors.xaml`
- Modify: `src/TrackZ.Mobile/Resources/Styles/TrackZTypography.xaml`
- Modify: `src/TrackZ.Mobile/Resources/Styles/TrackZControls.xaml`
- Modify: `src/TrackZ.Mobile/Resources/Styles/Styles.xaml`
- Modify: `src/TrackZ.Mobile/Components/TrackZStateView.xaml`
- Modify: `src/TrackZ.Mobile/Components/TrackZStateView.xaml.cs`
- Modify: `src/TrackZ.Mobile/Components/ExerciseListSkeleton.xaml`
- Modify: `src/TrackZ.Mobile/Components/ExerciseListSkeleton.xaml.cs`
- Modify: `src/TrackZ.Mobile/Components/ExercisePerformanceCard.xaml`
- Modify: `src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.xaml`
- Modify: `src/TrackZ.Mobile/Components/RepsStepper.xaml`
- Modify: `src/TrackZ.Mobile/Components/WeightStepper.xaml`
- Modify: `src/TrackZ.Mobile/Components/SyncStatusPill.xaml`
- Test: `tests/TrackZ.Mobile.Tests/NativeIos/AccessibilitySemanticsTests.cs`
- Create test: `tests/TrackZ.Mobile.Tests/NativeIos/NativeVisualTokenTests.cs`
- Modify test: `tests/TrackZ.Mobile.Tests/NativeIos/NativePresentationCompositionTests.cs`

**Interfaces:**
- Consumes: approved visual metrics in the design spec and Task 3 picker visibility/action bindings.
- Produces: semantic spacing/color/type/control resources plus reusable state and skeleton components used by every screen.

- [ ] **Step 1: Write failing token-value and component-geometry tests**

Load merged MAUI resources through the real app and assert exact keys/values:

```csharp
[Theory]
[InlineData("TrackZSpace4", 4d)]
[InlineData("TrackZSpace8", 8d)]
[InlineData("TrackZSpace12", 12d)]
[InlineData("TrackZSpace16", 16d)]
[InlineData("TrackZSpace24", 24d)]
[InlineData("TrackZSpace32", 32d)]
public void Spacing_tokens_are_exact(string key, double expected)

[Fact]
public void Shared_controls_meet_native_geometry()
{
    AssertStyle<Button>("TrackZPrimaryButtonStyle", ("MinimumHeightRequest", 52d), ("CornerRadius", 15));
    AssertStyle<Button>("TrackZSecondaryButtonStyle", ("MinimumHeightRequest", 44d));
    AssertStyle<Entry>("TrackZFieldStyle", ("MinimumHeightRequest", 50d));
    AssertStyle<Border>("TrackZCardStyle", ("StrokeShape", "RoundRectangle 16"));
}
```

Static XAML assertions must reject literal hex colors, lime body-copy labels, `FontAutoScalingEnabled="False"`, button targets below 44, card radii other than 16, and stepper buttons lacking action-specific semantic descriptions.

- [ ] **Step 2: Run visual-token tests and verify RED**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests" --verbosity minimal -m:1
```

Expected: failures for missing spacing/field/secondary/destructive/quiet/state/skeleton resources and the existing 20-point card radius/17-point section heading.

- [ ] **Step 3: Define the complete semantic resource contract**

Add `x:Double` spacing and geometry resources and these styles:

```xml
<x:Double x:Key="TrackZSpace4">4</x:Double>
<x:Double x:Key="TrackZSpace8">8</x:Double>
<x:Double x:Key="TrackZSpace12">12</x:Double>
<x:Double x:Key="TrackZSpace16">16</x:Double>
<x:Double x:Key="TrackZSpace24">24</x:Double>
<x:Double x:Key="TrackZSpace32">32</x:Double>
<x:Double x:Key="TrackZPageMargin">18</x:Double>
<x:Double x:Key="TrackZCardRadius">16</x:Double>
<x:Double x:Key="TrackZPrimaryActionHeight">52</x:Double>
<x:Double x:Key="TrackZMinimumTarget">44</x:Double>
<x:Double x:Key="TrackZFieldHeight">50</x:Double>
```

Typography metrics must be: page title 32 bold, navigation title 17 semibold, section heading 20 semibold, body 15 regular, metadata 13 regular, field error 12 regular. Add `TrackZPrimaryButtonStyle`, `TrackZSecondaryButtonStyle`, `TrackZDestructiveButtonStyle`, `TrackZQuietButtonStyle`, `TrackZFieldContainerStyle`, `TrackZFieldStyle`, `TrackZCardStyle`, `TrackZListRowStyle`, `TrackZSelectionChipStyle`, `TrackZStickyActionContainerStyle`, `TrackZInlineErrorStyle`, and `TrackZSkeletonStyle`. Change card radius from 20 to 16. Base old implicit `Button`, `Entry`, and `SearchBar` styles in `Styles.xaml` on the semantic contract so an unkeyed control cannot regress to 8-point radius/14-point text.

- [ ] **Step 4: Implement state and skeleton components**

`TrackZStateView` exposes bindable `Title`, `Message`, `ActionText`, and `ICommand? ActionCommand`; it uses centered semantic text and a 44-point secondary action only when command/text exist. `ExerciseListSkeleton` renders three fixed-height card silhouettes with `TrackZSkeletonStyle`; use reduced opacity rather than an animation, so Reduce Motion needs no branch and content remains stable.

- [ ] **Step 5: Normalize the high-frequency shared components**

Apply the shared card/text/button styles to exercise cards, active workout rows, sync pill, and both steppers. Preserve stable columns for 88–96 point artwork, name, metadata, LAST, PR, and selection control. Every decrement/increment target must be 44×44 and expose distinct localized semantic descriptions for weight/assistance/reps. Failed artwork keeps its card dimensions and retry control.

- [ ] **Step 6: Run component tests and iOS XAML compile**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~ExercisePickerViewModelTests" --verbosity minimal -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -verbosity:minimal
```

Expected: tests pass and XAML compile exits 0.

- [ ] **Step 7: Commit Task 4**

```bash
git add src/TrackZ.Mobile/Resources/Styles src/TrackZ.Mobile/Components tests/TrackZ.Mobile.Tests/NativeIos
git commit -m "style: unify native performance components"
```

---

### Task 5: App-Wide Native Page Consistency Audit

**Files:**
- Modify: `src/TrackZ.Mobile/Features/Train/TrainPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Train/BodyAreaSheetPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Exercises/ExercisePickerPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Exercises/CustomExercisePage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Workout/WorkoutPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Workout/SetEntrySheetPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/History/WorkoutHistoryPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/History/WorkoutHistoryDetailPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/History/HistorySetEditorSheetPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/History/HistoryConflictSheetPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Summary/WorkoutSummaryPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml`
- Modify: remaining components `BadgeTile.xaml`, `ExerciseProgressChart.xaml`, `LastSetTable.xaml`, `WeeklyStreakView.xaml`, `XpBar.xaml`
- Create test: `tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs`
- Modify test: `tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs`

**Interfaces:**
- Consumes: Task 4 semantic styles/components and existing view-model bindings; no domain/application behavior changes.
- Produces: all shipped pages using one title hierarchy, spacing rhythm, control geometry, safe-area action pattern, and semantic color roles.

- [ ] **Step 1: Write a failing static audit over the exact shipped XAML set**

Use an explicit file array so newly audited pages cannot silently disappear:

```csharp
private static readonly string[] ShippedPages =
[
    "Features/Auth/SignInPage.xaml",
    "Features/Auth/CreateAccountPage.xaml",
    "Features/Train/TrainPage.xaml",
    "Features/Train/BodyAreaSheetPage.xaml",
    "Features/Exercises/ExercisePickerPage.xaml",
    "Features/Exercises/CustomExercisePage.xaml",
    "Features/Workout/WorkoutPage.xaml",
    "Features/Workout/SetLoggerPage.xaml",
    "Features/Workout/SetEntrySheetPage.xaml",
    "Features/History/WorkoutHistoryPage.xaml",
    "Features/History/WorkoutHistoryDetailPage.xaml",
    "Features/History/HistorySetEditorSheetPage.xaml",
    "Features/History/HistoryConflictSheetPage.xaml",
    "Features/Summary/WorkoutSummaryPage.xaml",
    "Features/Progress/ExerciseProgressPage.xaml",
    "Features/Profile/ProfilePage.xaml"
];
```

For every file assert: semantic page background; side padding uses `TrackZPageMargin`; no literal hex colors; no literal `FontSize`, `CornerRadius`, or sub-44 button minimum; primary/secondary/destructive buttons use corresponding shared styles; page title uses page-title style only on root destinations and navigation-title style on pushed pages/sheets; destructive text uses the destructive role; bottom actions use sticky safe-area container.

- [ ] **Step 2: Run audit and record the concrete RED list**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~AppWideVisualConsistencyTests|FullyQualifiedName~NativeIosExperienceAcceptanceTests" --verbosity minimal -m:1
```

Expected: failures name every page with one-off dimensions/colors/styles; retain the output in the task report before editing.

- [ ] **Step 3: Normalize root destinations and selection flow**

Apply this hierarchy to Today, Picker, Progress, and Profile:

```xml
<Grid Padding="{DynamicResource TrackZPageMargin},16,18,0"
      RowDefinitions="Auto,Auto,*,Auto"
      RowSpacing="{DynamicResource TrackZSpace16}">
  <Label Style="{DynamicResource TrackZPageTitleStyle}" />
  <Label Grid.Row="1" Style="{DynamicResource TrackZBodyStyle}" />
  <ScrollView Grid.Row="2" />
  <Border Grid.Row="3" Style="{DynamicResource TrackZStickyActionContainerStyle}" />
</Grid>
```

Keep one lime primary action per screen. Chips use selection style only while selected. Search, filter count, card artwork/name/LAST/PR, and bottom Done action stay in fixed positions when states change.

- [ ] **Step 4: Normalize workout, logger, history, detail, and summary**

Use navigation title for pushed pages/sheets, section heading for groups, metadata for LAST/PR/time/mode, and shared steppers/cards/actions. Keep set logger’s durable-save feedback behavior unchanged. Use red only for delete/discard. History conflict actions retain distinct Keep Server/Apply Local hierarchy; offline/reconciling/permanent failure pills keep semantic status colors without lime body text.

- [ ] **Step 5: Normalize remaining gamification components without fabricating rewards**

Apply semantic cards/type/spacing to XP, level, streak, badges, and charts. Preserve actual server/cache values and Reduce Motion behavior. Do not add placeholder XP, fake badges, or decorative animations. Chart labels use metadata color and retain Dynamic Type legibility.

- [ ] **Step 6: Run the complete native UI and Mobile regression suites**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeIos|FullyQualifiedName~NativePresentation|FullyQualifiedName~Acceptance" --verbosity minimal -m:1
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --verbosity minimal -m:1
dotnet build src/TrackZ.Mobile.Core/TrackZ.Mobile.Core.csproj --no-restore --verbosity minimal -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -verbosity:minimal
```

Expected: all tests pass; Core and iOS managed/XAML compile have zero warnings/errors. If the known scheduling-sensitive thumbnail concurrency test fails only in the full suite, run it isolated three times and fix only if the current diff changed its behavior; document exact evidence rather than hiding the failure.

- [ ] **Step 7: Commit Task 5**

```bash
git add src/TrackZ.Mobile/Features src/TrackZ.Mobile/Components tests/TrackZ.Mobile.Tests/NativeIos tests/TrackZ.Mobile.Tests/Acceptance
git commit -m "style: align all native iOS screens"
```

---

### Task 6: Exact Development-Only Exercise Artwork Publication

**Files:**
- Modify: `src/TrackZ.Infrastructure/Persistence/Seed/ObjectStorageExerciseCatalogAssetDeployment.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Seed/ExerciseCatalogPublicationService.cs`
- Modify: `src/TrackZ.Infrastructure/DependencyInjection.cs`
- Create: `src/TrackZ.Api/ExerciseCatalogPublicationCommand.cs`
- Modify: `src/TrackZ.Api/Program.cs`
- Create test: `tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogPublicationTests.cs`
- Modify test: `tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogDeploymentTests.cs`
- Create test: `tests/TrackZ.Api.Tests/Exercises/ExerciseCatalogPublicationCommandTests.cs`
- Modify test: `tests/TrackZ.Api.Tests/Exercises/ExerciseCatalogDeploymentCommandTests.cs`

**Interfaces:**
- Consumes: `ExerciseManifest`, `ExerciseImage.Review`, `ExerciseImage.Publish`, `AppDbContext`, `ObjectStorageExerciseCatalogAssetDeployment`, `TimeProvider`, and exact existing object keys.
- Produces: `VerifyExactAsync`, `ExerciseCatalogPublicationService.PublishAsync`, and CLI command `publish-exercise-catalog --manifest ... --reviewer-id ... --rights-reference ...`.

- [ ] **Step 1: Write failing command parser/environment tests**

```csharp
[Fact]
public void Publication_command_requires_exact_contract()
{
    var reviewer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var parsed = ExerciseCatalogPublicationCommand.Parse(
    [
        "publish-exercise-catalog",
        "--manifest", "assets/exercises/catalog.json",
        "--reviewer-id", reviewer.ToString("D"),
        "--rights-reference", "local-simulator-review-2026-08-21"
    ]);

    Assert.Equal(reviewer, parsed!.ReviewerId);
    Assert.Equal("local-simulator-review-2026-08-21", parsed.RightsReference);
}

[Fact]
public async Task Publication_command_fails_before_database_access_outside_development()
```

Reject missing, duplicate, reordered, extra, empty, or invalid arguments; zero reviewer GUID; rights reference outside 1–512 trimmed characters; and any environment name other than exact `Development` via `IHostEnvironment.IsDevelopment()`.

- [ ] **Step 2: Run command tests and verify RED**

Run:

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationCommandTests|FullyQualifiedName~ExerciseCatalogDeploymentCommandTests" --verbosity minimal -m:1
```

Expected: compile failure for missing publication command.

- [ ] **Step 3: Write failing publication service tests against real PostgreSQL**

Create a fixture that first runs existing deploy-and-seed. Then cover:

```csharp
[Fact]
public async Task Exact_48_drafts_publish_atomically_with_complete_review_metadata()
{
    await deployment.DeployAndSeedAsync(CatalogPath);
    await publication.PublishAsync(CatalogPath, ReviewerId, RightsReference);

    var images = await db.ExerciseImages.AsNoTracking().OrderBy(x => x.ExerciseDefinitionId).ToArrayAsync();
    Assert.Equal(48, images.Length);
    Assert.All(images, image =>
    {
        Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
        Assert.Equal(ReviewerId, image.ReviewedByUserId);
        Assert.Equal(RightsReference, image.RightsReference);
        Assert.True(image.AnatomyApproved && image.MovementApproved && image.RightsApproved);
        Assert.True(image.IsReadyForUse);
        Assert.True(image.PublishedAt >= image.ReviewedAt);
    });
}
```

Add exact tests for: one missing object; one byte/content-type mismatch; only 47 image rows; unexpected version/key/source reference; Reviewed row; mixed Draft/Published rows; conflicting reviewer/rights on idempotent rerun; same reviewer/rights idempotent rerun with unchanged timestamps; normal deployment still leaves all 48 Draft. Prove rollback by installing a PostgreSQL trigger that raises on the 24th deterministic `exercise_images` update to `ReviewState=Published`, invoking `PublishAsync`, disposing that context, and verifying from a fresh context that all 48 rows remain Draft; drop the trigger in fixture cleanup.

- [ ] **Step 4: Run publication service tests and verify RED**

Run:

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests|FullyQualifiedName~ExerciseCatalogDeploymentTests" --verbosity minimal -m:1
```

Expected: compile failure for missing verification/publication service.

- [ ] **Step 5: Refactor object verification without changing deployment behavior**

Add:

```csharp
public Task VerifyExactAsync(
    string catalogPath,
    CancellationToken cancellationToken = default);
```

It loads/validates the exact manifest, deterministically processes master/thumbnail renditions, reads all 96 private objects, and fails for missing bytes, extra bytes, wrong content type, or byte mismatch. It performs no `PutAsync` or `DeleteAsync`. Refactor `DeployAsync` to share rendition generation and matching while preserving its current partial-upload retry semantics and final verification.

- [ ] **Step 6: Implement transactional publication with two-pass preflight**

Create:

```csharp
public sealed class ExerciseCatalogPublicationService(
    AppDbContext database,
    ObjectStorageExerciseCatalogAssetDeployment deployment,
    TimeProvider timeProvider)
{
    public Task PublishAsync(
        string catalogPath,
        Guid reviewerId,
        string rightsReference,
        CancellationToken cancellationToken = default);
}
```

First call `VerifyExactAsync`. Start one database transaction and load exact manifest definitions/images. Preflight every definition and image before mutating any entity: exact IDs/names/body parts/modes; system/unarchived definitions; version 1 public system artwork; exact master/thumbnail keys and source reference. Accept only either all 48 Draft with empty review metadata or all 48 Published with matching reviewer/rights/approvals and complete timestamps. Reject mixed states and Reviewed state. For all-Draft, capture one UTC instant, call `Review(reviewerId, normalizedRightsReference, true, true, true, instant)` then `Publish(instant)` on every image, save once, and commit once. For exact all-Published rerun, make no writes.

- [ ] **Step 7: Wire strict command selection without startup side effects**

In `Program.cs` parse deployment and publication commands before building:

```csharp
var deploymentCommand = ExerciseCatalogDeploymentCommand.Parse(args);
var publicationCommand = ExerciseCatalogPublicationCommand.Parse(args);
var startupCommand = deploymentCommand is not null || publicationCommand is not null;
var builder = WebApplication.CreateBuilder(startupCommand ? [] : args);
```

After `builder.Build()`, execute at most one parsed command and return before middleware/routes. `ExerciseCatalogPublicationCommand.ExecuteAsync` first resolves `IHostEnvironment` and rejects non-Development, then migrates and calls the publication service. Register the publication service scoped. Do not call it from deployment, seeding, migrations, hosted services, or normal startup.

- [ ] **Step 8: Run publication, migration, and server build verification**

Run:

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationCommandTests|FullyQualifiedName~ExerciseCatalogDeploymentCommandTests" --verbosity minimal -m:1
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests|FullyQualifiedName~ExerciseCatalogDeploymentTests|FullyQualifiedName~ObjectStorageIntegrationTests" --verbosity minimal -m:1
dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --verbosity minimal -m:1
```

Expected: all focused tests pass; deployment remains Draft; publication is exact/idempotent/fail-closed; API build has zero warnings/errors.

- [ ] **Step 9: Commit Task 6**

```bash
git add src/TrackZ.Infrastructure/Persistence/Seed src/TrackZ.Infrastructure/DependencyInjection.cs src/TrackZ.Api tests/TrackZ.Infrastructure.Tests/Seed tests/TrackZ.Api.Tests/Exercises
git commit -m "feat: publish reviewed catalog artwork locally"
```

---

### Task 7: Real API-to-Simulator Acceptance and Developer Runbook

**Files:**
- Create: `tests/TrackZ.Api.Tests/Exercises/PublishedCatalogAcceptanceTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs`
- Create: `docs/development/simulator-exercise-catalog.md`
- Create report: `docs/superpowers/reports/2026-08-21-trackz-native-auth-ui-consistency.md`

**Interfaces:**
- Consumes: all prior tasks, real authenticated API routes, PostgreSQL, MinIO, signed media capabilities, credential-free thumbnail client, native iOS app composition.
- Produces: one end-to-end proof and an exact local procedure that yields a signed-in Chest picker with eight named exercises and API-delivered artwork.

- [ ] **Step 1: Write the real server acceptance first**

Use TestServer with real PostgreSQL and MinIO. Execute deploy then publication through production services, register/login through HTTP, and assert:

```csharp
[Fact]
public async Task Authenticated_chest_catalog_returns_eight_published_thumbnails_and_bytes()
{
    var tokens = await RegisterAndLoginAsync();
    client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
    var page = await client.GetFromJsonAsync<CursorPage<ExerciseSummaryDto>>(
        "/api/v1/exercises?pageSize=50");
    var chest = page!.Items.Where(x => x.BodyPart == BodyPart.Chest).ToArray();

    Assert.Equal(8, chest.Length);
    Assert.All(chest, item => Assert.Matches(
        "^/api/v1/media/exercise-images/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/thumbnail$",
        item.ThumbnailUrl));
}
```

For each opaque route, request the signed capability with bearer, assert it contains no object key/repository path, then fetch bytes with a separate no-bearer client; assert `image/png`, non-empty content, no redirect, and zero Authorization headers at the media transport. Also assert anonymous catalog is 401 and Draft deployment without publication returns the 48 definitions with `thumbnailUrl=null`.

- [ ] **Step 2: Run the server acceptance and verify RED if integration is incomplete**

Run:

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~PublishedCatalogAcceptanceTests" --verbosity minimal -m:1
```

Expected before all wiring is complete: a focused failure at the missing publication/capability assertion; after implementation: pass with real containers disposed.

- [ ] **Step 3: Add the native acceptance flow**

Extend the MAUI acceptance test to resolve the real provider with fake native transports only at the network boundary and assert this sequence: fresh storage → `AuthShell`; register form submits once → tokens saved → `AppShell`; open Chest picker → initial loading skeleton → eight result cards; every card keeps non-empty name and local cached API-downloaded thumbnail; simulate offline relaunch → signed-in offline shell and eight cached cards; logout → `AuthShell`, empty private caches, and no protected shell sync.

- [ ] **Step 4: Write the exact Simulator runbook**

Document these commands from repository root, including the fixed local reviewer contract:

```bash
docker compose up -d postgres minio minio-bootstrap
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/TrackZ.Api/TrackZ.Api.csproj -- deploy-exercise-catalog --manifest "$PWD/assets/exercises/catalog.json"
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/TrackZ.Api/TrackZ.Api.csproj -- publish-exercise-catalog --manifest "$PWD/assets/exercises/catalog.json" --reviewer-id 11111111-1111-1111-1111-111111111111 --rights-reference local-simulator-review-2026-08-21
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/TrackZ.Api/TrackZ.Api.csproj --urls http://127.0.0.1:5080
```

Document the Simulator environment exactly:

```text
TRACKZ_API_ORIGIN=http://127.0.0.1:5080
TRACKZ_MEDIA_ORIGIN=http://127.0.0.1:5080
```

Explain that `assets/exercises/images` is deployment input only; the app receives opaque API routes and cached signed-media bytes. Include register/login steps, Chest expected count `8`, all-body expected count `48`, offline relaunch check, and commands to stop only TrackZ-owned API/build processes and `docker compose stop` when testing ends.

- [ ] **Step 5: Run final verification before claiming completion**

Run sequentially:

```bash
dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --no-restore --verbosity minimal -m:1
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --no-restore --verbosity minimal -m:1
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --verbosity minimal -m:1
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --verbosity minimal -m:1
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --verbosity minimal -m:1
dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --verbosity minimal -m:1
dotnet build src/TrackZ.Mobile.Core/TrackZ.Mobile.Core.csproj --no-restore --verbosity minimal -m:1
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -verbosity:minimal
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 --no-restore --verbosity minimal -m:1
git diff --check
```

Attempt Android once without installation:

```bash
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-android -p:BuildProjectReferences=false -m:1 -verbosity:minimal
```

Expected: all server/mobile tests and builds pass; iOS Simulator build exits 0; Android either passes with an existing SDK or reports exact external `XA5300`. Audit `ps` and `docker ps`; terminate only task-owned test/build/API processes and containers. Record exact test counts, platform result, and any unrelated pre-existing failure in the report.

- [ ] **Step 6: Manually verify native Simulator behavior**

Launch the built app on the available iPhone Simulator. Verify: native Sign in/Create account flow; keyboard/focus/disabled submit; Today after auth; consistent page margins/type/button geometry; Chest shows eight named exercise cards with images; no empty-state flash during load; auth/offline/no-match states are distinct; image failure affects one card only; logout returns to auth and clears private catalog. Capture screenshots for Sign in, Chest results, offline cached results, and one pushed workout page in the report.

- [ ] **Step 7: Commit Task 7**

```bash
git add tests/TrackZ.Api.Tests/Exercises/PublishedCatalogAcceptanceTests.cs tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs docs/development/simulator-exercise-catalog.md docs/superpowers/reports/2026-08-21-trackz-native-auth-ui-consistency.md
git commit -m "test: verify native authenticated catalog experience"
```

---

## Final Completion Gate

- The app launches into a neutral checking state and never exposes protected tabs before auth resolution.
- Fresh users can register, then land on Today with validated tokens stored through the account boundary.
- Returning users use a valid token, refresh an expired token once, or preserve a complete offline identity without destroying cached workouts/catalog.
- A protected GET retries once after refresh; foreign-origin and media requests never receive bearer credentials.
- Chest never displays a false zero-result state for auth/network failure; its eight published exercises and artwork arrive through API/signed media.
- Every shipped native page passes the semantic visual audit and 44-point accessibility target checks.
- Normal deployment remains 48 Draft records; the explicit Development publication command produces 48 complete Published records or no changes.
- All tests/builds, iOS Simulator verification, process/container audit, screenshots, and any Android `XA5300` gate are recorded before integration.
