using System.Net;
using System.Text;
using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Identity;

public sealed class AuthGateCoordinatorTests
{
    public static TheoryData<SessionCase, AuthGateState, bool> BootstrapCases => new()
    {
        { SessionCase.None, AuthGateState.SignedOut, false },
        { SessionCase.Valid, AuthGateState.SignedIn, false },
        { SessionCase.ExpiredRefreshSucceeds, AuthGateState.SignedIn, false },
        { SessionCase.ExpiredRefreshRejected, AuthGateState.SignedOut, true },
        { SessionCase.ExpiredRefreshOffline, AuthGateState.SignedIn, false }
    };

    [Theory]
    [MemberData(nameof(BootstrapCases))]
    public async Task Bootstrap_has_one_honest_terminal_state(
        SessionCase scenario,
        AuthGateState expected,
        bool expectedPrivateClear)
    {
        var fixture = Fixture.For(scenario);

        await fixture.Coordinator.InitializeAsync();

        Assert.Equal(expected, fixture.Coordinator.Snapshot.State);
        Assert.Equal(expectedPrivateClear, fixture.Cleaner.ClearCount != 0);
    }

    [Fact]
    public async Task Bootstrap_starts_checking_and_serializes_concurrent_calls()
    {
        var fixture = Fixture.For(SessionCase.ExpiredRefreshSucceeds);
        var first = fixture.Coordinator.InitializeAsync();
        var second = fixture.Coordinator.InitializeAsync();

        await Task.WhenAll(first, second);

        Assert.Equal(AuthGateState.CheckingSession, fixture.InitialState);
        Assert.Equal(1, fixture.Identity.RefreshCalls);
        Assert.Equal(AuthGateState.SignedIn, fixture.Coordinator.Snapshot.State);
    }

    [Fact]
    public async Task Sign_in_rejects_duplicate_submit_and_emits_signed_in()
    {
        var fixture = Fixture.For(SessionCase.None);
        fixture.Identity.GateLogin();
        var first = fixture.Coordinator.SignInAsync("lift@example.com", "Correct-Horse-9");
        await fixture.Identity.LoginEntered;
        var duplicate = fixture.Coordinator.SignInAsync("lift@example.com", "Correct-Horse-9");
        fixture.Identity.ReleaseLogin();

        await Task.WhenAll(first, duplicate);

        Assert.Equal(1, fixture.Identity.LoginCalls);
        Assert.Equal(AuthGateState.SignedIn, fixture.Coordinator.Snapshot.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_concrete_identity_transition_publishes_signed_in_after_its_account_reset(bool register)
    {
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        var boundary = new AccountSessionBoundary();
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var responses = register
            ? new[]
            {
                Json(HttpStatusCode.Created, "{\"userId\":\"99999999-9999-9999-9999-999999999999\",\"email\":\"lift@example.com\"}"),
                TokenResponse(now.AddMinutes(15))
            }
            : [TokenResponse(now.AddMinutes(15))];
        var identity = new TrackZIdentityApiClient(
            new HttpClient(new ResponseQueueHandler(responses)) { BaseAddress = new Uri("https://trackz.test") },
            store,
            new RecordingCleaner(),
            boundary);
        var gate = new AuthGateCoordinator(store, identity, new RecordingCleaner(), boundary,
            new TestDeviceNameProvider(), new OfflineConnectivity(), new FixedTimeProvider(now));

        if (register)
            await gate.RegisterAsync("lift@example.com", "Correct-Horse-9");
        else
            await gate.SignInAsync("lift@example.com", "Correct-Horse-9");

        Assert.Equal(AuthGateState.SignedIn, gate.Snapshot.State);
        Assert.NotNull(await store.GetAccessTokenAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_reset_during_identity_success_without_boundary_cooperation_cannot_publish_signed_in(bool register)
    {
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        var boundary = new AccountSessionBoundary();
        var identity = new BoundaryIgnoringIdentity();
        var gate = new AuthGateCoordinator(store, identity, new RecordingCleaner(), boundary,
            new TestDeviceNameProvider(), new OfflineConnectivity(), new FixedTimeProvider(DateTimeOffset.UtcNow));

        var transition = register
            ? gate.RegisterAsync("lift@example.com", "Correct-Horse-9")
            : gate.SignInAsync("lift@example.com", "Correct-Horse-9");
        await identity.Entered;
        await boundary.ResetAsync(token => store.ClearAsync(token));
        identity.Release();
        await transition;

        Assert.NotEqual(AuthGateState.SignedIn, gate.Snapshot.State);
        Assert.Null(await store.GetAccessTokenAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_installed_identity_receipt_after_external_reset_cannot_publish_signed_in(bool register)
    {
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        var boundary = new AccountSessionBoundary();
        var identity = new StaleReceiptIdentity(store, boundary);
        var gate = new AuthGateCoordinator(store, identity, new RecordingCleaner(), boundary,
            new TestDeviceNameProvider(), new OfflineConnectivity(), new FixedTimeProvider(DateTimeOffset.UtcNow));

        var transition = register
            ? gate.RegisterAsync("lift@example.com", "Correct-Horse-9")
            : gate.SignInAsync("lift@example.com", "Correct-Horse-9");
        await identity.ReceiptReady;
        await boundary.ResetAsync(token => store.ClearAsync(token));
        identity.ReleaseReceipt();
        await transition;

        Assert.NotEqual(AuthGateState.SignedIn, gate.Snapshot.State);
        Assert.Null(await store.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Reset_while_refresh_is_delayed_cannot_publish_a_stale_signed_in_state()
    {
        var fixture = Fixture.For(SessionCase.ExpiredRefreshSucceeds);
        fixture.Identity.GateRefresh();
        var initialize = fixture.Coordinator.InitializeAsync();
        await fixture.Identity.RefreshEntered;

        await fixture.Boundary.ResetAsync(token => fixture.Store.ClearAsync(token));
        fixture.Identity.ReleaseRefresh();
        await initialize;

        Assert.NotEqual(AuthGateState.SignedIn, fixture.Coordinator.Snapshot.State);
        Assert.Null(await fixture.Store.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Timeout_shaped_refresh_cancellation_preserves_the_complete_offline_session()
    {
        var fixture = Fixture.For(SessionCase.ExpiredRefreshSucceeds);
        fixture.Identity.RefreshFailure = new TaskCanceledException("The request timed out.");

        await fixture.Coordinator.InitializeAsync();

        Assert.Equal(new AuthGateSnapshot(AuthGateState.SignedIn, IsOfflineSession: true), fixture.Coordinator.Snapshot);
        Assert.NotNull(await fixture.Store.GetAccessTokenAsync());
        Assert.Equal(0, fixture.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Register_and_logout_publish_terminal_states_even_when_revoke_fails()
    {
        var fixture = Fixture.For(SessionCase.None);
        await fixture.Coordinator.RegisterAsync("lift@example.com", "Correct-Horse-9");
        fixture.Identity.LogoutFailure = new HttpRequestException("offline");

        await fixture.Coordinator.SignOutAsync();

        Assert.Equal(1, fixture.Identity.RegisterCalls);
        Assert.Equal(AuthGateState.SignedOut, fixture.Coordinator.Snapshot.State);
    }

    [Fact]
    public async Task Require_sign_in_after_initialization_clears_account_and_publishes_signed_out_once()
    {
        var fixture = Fixture.For(SessionCase.Valid);
        await fixture.Coordinator.InitializeAsync();
        var signedOutEvents = 0;
        fixture.Coordinator.Changed += (_, snapshot) =>
        {
            if (snapshot.State == AuthGateState.SignedOut) signedOutEvents++;
        };

        await ((IAuthEntryPoint)fixture.Coordinator).RequireSignInAsync();

        Assert.Equal(AuthGateState.SignedOut, fixture.Coordinator.Snapshot.State);
        Assert.Equal(1, signedOutEvents);
        Assert.Equal(1, fixture.Cleaner.ClearCount);
        Assert.Null(await fixture.Store.GetAccessTokenAsync());
        Assert.Null(await fixture.Store.GetRefreshTokenAsync());
        Assert.Equal(0, fixture.Identity.LogoutCalls);
    }

    [Fact]
    public async Task Delayed_require_sign_in_from_an_old_generation_cannot_clear_a_new_login()
    {
        var fixture = Fixture.For(SessionCase.Valid);
        await fixture.Coordinator.InitializeAsync();
        fixture.Identity.GateLogin();
        var login = fixture.Coordinator.SignInAsync("new@example.com", "Correct-Horse-9");
        await fixture.Identity.LoginEntered;

        var staleRequireSignIn = ((IAuthEntryPoint)fixture.Coordinator).RequireSignInAsync();
        fixture.Identity.ReleaseLogin();
        await Task.WhenAll(login, staleRequireSignIn);

        Assert.Equal(AuthGateState.SignedIn, fixture.Coordinator.Snapshot.State);
        Assert.NotNull(await fixture.Store.GetAccessTokenAsync());
        Assert.Equal(0, fixture.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Expected_generation_require_sign_in_cannot_clear_a_replaced_account()
    {
        var fixture = Fixture.For(SessionCase.Valid);
        await fixture.Coordinator.InitializeAsync();
        var oldGeneration = fixture.Boundary.Capture();
        await fixture.Boundary.ResetAsync(_ => Task.CompletedTask);

        await ((IAuthEntryPoint)fixture.Coordinator).RequireSignInAsync(oldGeneration);

        Assert.Equal(AuthGateState.SignedIn, fixture.Coordinator.Snapshot.State);
        Assert.NotNull(await fixture.Store.GetAccessTokenAsync());
        Assert.NotNull(await fixture.Store.GetRefreshTokenAsync());
        Assert.Equal(0, fixture.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Malformed_stored_identity_clears_private_account_data_and_signs_out()
    {
        var fixture = Fixture.For(SessionCase.Valid);
        await fixture.Store.ClearAsync();
        await fixture.Storage.SetAsync(MobileTokenKeys.AccessToken, "malformed");
        await fixture.Storage.SetAsync(MobileTokenKeys.RefreshToken, "refresh");
        await fixture.Storage.SetAsync(MobileTokenKeys.UserId, Guid.NewGuid().ToString());
        await fixture.Storage.SetAsync(MobileTokenKeys.SessionId, Guid.NewGuid().ToString());

        await fixture.Coordinator.InitializeAsync();

        Assert.Equal(AuthGateState.SignedOut, fixture.Coordinator.Snapshot.State);
        Assert.Equal(1, fixture.Cleaner.ClearCount);
        Assert.Null(await fixture.Store.GetAccessTokenAsync());
    }

    [Theory]
    [InlineData(BusinessErrorCode.EmailAlreadyExists, "Use another email address.", null, null)]
    [InlineData(BusinessErrorCode.PasswordPolicyViolation, null, "Use a password that meets the requirements.", null)]
    [InlineData(BusinessErrorCode.InvalidCredentials, null, null, "Email or password is incorrect.")]
    public async Task Auth_form_maps_stable_business_errors_to_the_right_visible_slot(
        BusinessErrorCode errorCode,
        string? expectedEmail,
        string? expectedPassword,
        string? expectedForm)
    {
        var fixture = Fixture.For(SessionCase.None);
        fixture.Identity.LoginFailure = new MobileApiException(errorCode, "Server message");
        var form = new AuthFormViewModel(fixture.Coordinator, AuthTextSet.English);

        await form.SubmitCommand.ExecuteAsync();

        Assert.Equal(expectedEmail, form.EmailError);
        Assert.Equal(expectedPassword, form.PasswordError);
        Assert.Equal(expectedForm, form.FormError);
    }

    [Fact]
    public async Task Auth_form_silently_ignores_session_reset_cancellation()
    {
        var fixture = Fixture.For(SessionCase.None);
        fixture.Identity.GateLogin();
        var form = new AuthFormViewModel(fixture.Coordinator, AuthTextSet.English);

        var submit = form.SubmitCommand.ExecuteAsync();
        await fixture.Identity.LoginEntered;
        await fixture.Boundary.ResetAsync(_ => Task.CompletedTask);
        fixture.Identity.CancelLoginAfterGate();
        fixture.Identity.ReleaseLogin();
        await submit;

        Assert.False(form.IsSubmitting);
        Assert.Null(form.FormError);
        Assert.Null(form.EmailError);
        Assert.Null(form.PasswordError);
        Assert.NotEqual(AuthGateState.SignedIn, fixture.Coordinator.Snapshot.State);
    }

    private sealed class Fixture
    {
        private Fixture(MemoryTokenStorage storage, MobileTokenStore store, RecordingIdentity identity,
            RecordingCleaner cleaner, AccountSessionBoundary boundary, AuthGateCoordinator coordinator, AuthGateState initialState)
        {
            Storage = storage;
            Store = store;
            Identity = identity;
            Cleaner = cleaner;
            Boundary = boundary;
            Coordinator = coordinator;
            InitialState = initialState;
        }

        public MemoryTokenStorage Storage { get; }
        public MobileTokenStore Store { get; }
        public RecordingIdentity Identity { get; }
        public RecordingCleaner Cleaner { get; }
        public AccountSessionBoundary Boundary { get; }
        public AuthGateCoordinator Coordinator { get; }
        public AuthGateState InitialState { get; }

        public static Fixture For(SessionCase scenario)
        {
            var storage = new MemoryTokenStorage();
            var store = new MobileTokenStore(storage);
            var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
            var boundary = new AccountSessionBoundary();
            var identity = new RecordingIdentity(store, boundary);
            var cleaner = new RecordingCleaner();
            var coordinator = new AuthGateCoordinator(store, identity, cleaner, boundary,
                new TestDeviceNameProvider(), new OfflineConnectivity(), new FixedTimeProvider(now));
            if (scenario is not SessionCase.None)
            {
                var expiry = scenario == SessionCase.Valid ? now.AddMinutes(15) : now.AddMinutes(-1);
                store.SaveAsync(CreateToken(expiry), "refresh").GetAwaiter().GetResult();
            }
            if (scenario == SessionCase.ExpiredRefreshRejected)
                identity.RefreshFailure = new MobileApiException(BusinessErrorCode.RefreshTokenInvalid, "Refresh token is invalid.");
            if (scenario == SessionCase.ExpiredRefreshOffline)
                identity.RefreshFailure = new HttpRequestException("offline");
            return new Fixture(storage, store, identity, cleaner, boundary, coordinator, coordinator.Snapshot.State);
        }

    }

    public enum SessionCase { None, Valid, ExpiredRefreshSucceeds, ExpiredRefreshRejected, ExpiredRefreshOffline }

    private sealed class MemoryTokenStorage : IMobileTokenStorage
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) { _values.Remove(key); return Task.CompletedTask; }
    }

    private sealed class RecordingCleaner : IMobilePrivateDataCleaner
    {
        public int ClearCount { get; private set; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { ClearCount++; return Task.CompletedTask; }
    }

    private sealed class RecordingIdentity(MobileTokenStore store, IAccountSessionBoundary boundary) : IIdentitySessionApi
    {
        private readonly TaskCompletionSource _loginEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseLogin = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _refreshEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _gateLogin;
        private bool _gateRefresh;
        private bool _cancelLoginAfterGate;
        public int LoginCalls { get; private set; }
        public int RegisterCalls { get; private set; }
        public int RefreshCalls { get; private set; }
        public int LogoutCalls { get; private set; }
        public Exception? RefreshFailure { get; set; }
        public Exception? LoginFailure { get; set; }
        public Exception? LogoutFailure { get; set; }
        public Task LoginEntered => _loginEntered.Task;
        public Task RefreshEntered => _refreshEntered.Task;
        public void GateLogin() => _gateLogin = true;
        public void CancelLoginAfterGate() => _cancelLoginAfterGate = true;
        public void GateRefresh() => _gateRefresh = true;
        public void ReleaseLogin() => _releaseLogin.TrySetResult();
        public void ReleaseRefresh() => _releaseRefresh.TrySetResult();
        public async Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default)
        {
            LoginCalls++;
            if (LoginFailure is not null) throw LoginFailure;
            if (_gateLogin)
            {
                _loginEntered.TrySetResult();
                await _releaseLogin.Task.WaitAsync(cancellationToken);
                if (_cancelLoginAfterGate) throw new OperationCanceledException("The account session changed.");
            }
        }
        public async Task<IdentityTransitionReceipt?> LoginWithReceiptAsync(
            string email,
            string password,
            string deviceName,
            CancellationToken cancellationToken = default)
        {
            await LoginAsync(email, password, deviceName, cancellationToken);
            return await InstallIdentityAsync(cancellationToken);
        }
        public Task RegisterAndLoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) { RegisterCalls++; return Task.CompletedTask; }
        public async Task<IdentityTransitionReceipt?> RegisterAndLoginWithReceiptAsync(
            string email,
            string password,
            string deviceName,
            CancellationToken cancellationToken = default)
        {
            await RegisterAndLoginAsync(email, password, deviceName, cancellationToken);
            return await InstallIdentityAsync(cancellationToken);
        }
        public async Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            if (RefreshFailure is not null) throw RefreshFailure;
            if (_gateRefresh)
            {
                _refreshEntered.TrySetResult();
                await _releaseRefresh.Task.WaitAsync(cancellationToken);
            }
        }
        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            LogoutCalls++;
            return LogoutFailure is null ? Task.CompletedTask : Task.FromException(LogoutFailure);
        }

        private async Task<IdentityTransitionReceipt> InstallIdentityAsync(CancellationToken cancellationToken)
        {
            var accessToken = CreateToken(DateTimeOffset.UtcNow.AddMinutes(15));
            var snapshot = MobileTokenStore.CreateSnapshot(accessToken, "refresh-one");
            await boundary.ResetAsync(token => store.SaveAsync(accessToken, "refresh-one", token), cancellationToken);
            return new IdentityTransitionReceipt(boundary.Capture(), snapshot);
        }
    }

    private sealed class TestDeviceNameProvider : IDeviceNameProvider { public string DeviceName => "iPhone Simulator"; }
    private sealed class OfflineConnectivity : IConnectivityService { public bool IsOnline => false; public event EventHandler? ConnectivityChanged { add { } remove { } } }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static HttpResponseMessage TokenResponse(DateTimeOffset expiresAt) => Json(HttpStatusCode.OK,
        $$"""{"accessToken":"{{CreateToken(expiresAt)}}","refreshToken":"refresh-one","expiresAt":"{{expiresAt:O}}"}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string CreateToken(DateTimeOffset expiresAt)
    {
        static string Part(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part("{\"alg\":\"none\"}")}.{Part($"{{\"sub\":\"99999999-9999-9999-9999-999999999999\",\"sid\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"exp\":{expiresAt.ToUnixTimeSeconds()}}}")}.signature";
    }

    private sealed class ResponseQueueHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Dequeue());
    }

    private sealed class BoundaryIgnoringIdentity : IIdentitySessionApi
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();
        public async Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task;
        }
        public async Task RegisterAndLoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task;
        }
        public Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StaleReceiptIdentity(MobileTokenStore store, IAccountSessionBoundary boundary) : IIdentitySessionApi
    {
        private readonly TaskCompletionSource _receiptReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseReceipt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ReceiptReady => _receiptReady.Task;
        public void ReleaseReceipt() => _releaseReceipt.TrySetResult();
        public Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterAndLoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IdentityTransitionReceipt?> LoginWithReceiptAsync(
            string email,
            string password,
            string deviceName,
            CancellationToken cancellationToken = default) => CreateStaleReceiptAsync(cancellationToken);
        public Task<IdentityTransitionReceipt?> RegisterAndLoginWithReceiptAsync(
            string email,
            string password,
            string deviceName,
            CancellationToken cancellationToken = default) => CreateStaleReceiptAsync(cancellationToken);

        private async Task<IdentityTransitionReceipt?> CreateStaleReceiptAsync(CancellationToken cancellationToken)
        {
            var accessToken = CreateToken(DateTimeOffset.UtcNow.AddMinutes(15));
            var snapshot = MobileTokenStore.CreateSnapshot(accessToken, "refresh-one");
            await boundary.ResetAsync(token => store.SaveAsync(accessToken, "refresh-one", token), cancellationToken);
            var receipt = new IdentityTransitionReceipt(boundary.Capture(), snapshot);
            _receiptReady.TrySetResult();
            await _releaseReceipt.Task;
            return receipt;
        }
    }
}
