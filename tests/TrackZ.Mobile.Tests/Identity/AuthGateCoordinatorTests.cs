using System.Net;
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
            var identity = new RecordingIdentity();
            var cleaner = new RecordingCleaner();
            var boundary = new AccountSessionBoundary();
            var coordinator = new AuthGateCoordinator(store, identity, cleaner, boundary,
                new TestDeviceNameProvider(), new OfflineConnectivity(), new FixedTimeProvider(now));
            if (scenario is not SessionCase.None)
            {
                var expiry = scenario == SessionCase.Valid ? now.AddMinutes(15) : now.AddMinutes(-1);
                store.SaveAsync(Token(expiry), "refresh").GetAwaiter().GetResult();
            }
            if (scenario == SessionCase.ExpiredRefreshRejected)
                identity.RefreshFailure = new MobileApiException(BusinessErrorCode.RefreshTokenInvalid, "Refresh token is invalid.");
            if (scenario == SessionCase.ExpiredRefreshOffline)
                identity.RefreshFailure = new HttpRequestException("offline");
            return new Fixture(storage, store, identity, cleaner, boundary, coordinator, coordinator.Snapshot.State);
        }

        private static string Token(DateTimeOffset expiresAt)
        {
            static string Part(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return $"{Part("{\"alg\":\"none\"}")}.{Part($"{{\"sub\":\"99999999-9999-9999-9999-999999999999\",\"sid\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"exp\":{expiresAt.ToUnixTimeSeconds()}}}")}.signature";
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

    private sealed class RecordingIdentity : IIdentitySessionApi
    {
        private readonly TaskCompletionSource _loginEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseLogin = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _gateLogin;
        public int LoginCalls { get; private set; }
        public int RegisterCalls { get; private set; }
        public int RefreshCalls { get; private set; }
        public Exception? RefreshFailure { get; set; }
        public Exception? LoginFailure { get; set; }
        public Exception? LogoutFailure { get; set; }
        public Task LoginEntered => _loginEntered.Task;
        public void GateLogin() => _gateLogin = true;
        public void ReleaseLogin() => _releaseLogin.TrySetResult();
        public async Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default)
        {
            LoginCalls++;
            if (LoginFailure is not null) throw LoginFailure;
            if (_gateLogin) { _loginEntered.TrySetResult(); await _releaseLogin.Task.WaitAsync(cancellationToken); }
        }
        public Task RegisterAndLoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) { RegisterCalls++; return Task.CompletedTask; }
        public Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return RefreshFailure is null ? Task.CompletedTask : Task.FromException(RefreshFailure);
        }
        public Task LogoutAsync(CancellationToken cancellationToken = default) =>
            LogoutFailure is null ? Task.CompletedTask : Task.FromException(LogoutFailure);
    }

    private sealed class TestDeviceNameProvider : IDeviceNameProvider { public string DeviceName => "iPhone Simulator"; }
    private sealed class OfflineConnectivity : IConnectivityService { public bool IsOnline => false; public event EventHandler? ConnectivityChanged { add { } remove { } } }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
