using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises;

namespace TrackZ.Mobile.Identity;

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

public sealed class AuthGateCoordinator : IAuthEntryPoint
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
    private readonly MobileTokenStore _tokenStore;
    private readonly IIdentitySessionApi _identity;
    private readonly IMobilePrivateDataCleaner _privateDataCleaner;
    private readonly IAccountSessionBoundary _sessionBoundary;
    private readonly IDeviceNameProvider _deviceName;
    private readonly IConnectivityService _connectivity;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private int _initialised;
    private int _submitting;

    public AuthGateCoordinator(
        MobileTokenStore tokenStore,
        IIdentitySessionApi identity,
        IMobilePrivateDataCleaner privateDataCleaner,
        IAccountSessionBoundary sessionBoundary,
        IDeviceNameProvider deviceName,
        IConnectivityService connectivity,
        TimeProvider timeProvider)
    {
        _tokenStore = tokenStore;
        _identity = identity;
        _privateDataCleaner = privateDataCleaner;
        _sessionBoundary = sessionBoundary;
        _deviceName = deviceName;
        _connectivity = connectivity;
        _timeProvider = timeProvider;
    }

    public AuthGateSnapshot Snapshot { get; private set; } = new(AuthGateState.CheckingSession);
    public event EventHandler<AuthGateSnapshot>? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _initialised) != 0) return;
            await BootstrapAsync(cancellationToken);
            Volatile.Write(ref _initialised, 1);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public Task RequireSignInAsync(CancellationToken cancellationToken = default) => InitializeAsync(cancellationToken);

    public async Task SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _submitting, 1, 0) != 0) return;
        try
        {
            await _transitionGate.WaitAsync(cancellationToken);
            try
            {
                var generation = _sessionBoundary.Capture();
                using var lease = _sessionBoundary.CreateCancellationLease(generation, cancellationToken);
                await _identity.LoginAsync(email, password, _deviceName.DeviceName, lease.Token);
                if (_sessionBoundary.IsCancellationRequested(generation))
                    throw new OperationCanceledException("The account session changed.");
                Publish(new AuthGateSnapshot(AuthGateState.SignedIn));
                Volatile.Write(ref _initialised, 1);
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        finally
        {
            Volatile.Write(ref _submitting, 0);
        }
    }

    public async Task RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _submitting, 1, 0) != 0) return;
        try
        {
            await _transitionGate.WaitAsync(cancellationToken);
            try
            {
                var generation = _sessionBoundary.Capture();
                using var lease = _sessionBoundary.CreateCancellationLease(generation, cancellationToken);
                await _identity.RegisterAndLoginAsync(email, password, _deviceName.DeviceName, lease.Token);
                if (_sessionBoundary.IsCancellationRequested(generation))
                    throw new OperationCanceledException("The account session changed.");
                Publish(new AuthGateSnapshot(AuthGateState.SignedIn));
                Volatile.Write(ref _initialised, 1);
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        finally
        {
            Volatile.Write(ref _submitting, 0);
        }
    }

    public async Task RetryAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _initialised, 0);
        await InitializeAsync(cancellationToken);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await _identity.LogoutAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Local sign-out must complete even when server revocation cannot.
            }
            finally
            {
                await ClearAccountAsync(cancellationToken);
                Publish(new AuthGateSnapshot(AuthGateState.SignedOut));
                Volatile.Write(ref _initialised, 1);
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        Publish(new AuthGateSnapshot(AuthGateState.CheckingSession));
        MobileIdentitySnapshot? snapshot;
        try
        {
            snapshot = await _tokenStore.GetSnapshotAsync(cancellationToken);
        }
        catch (MobileApiException)
        {
            await ClearAccountAsync(cancellationToken);
            Publish(new AuthGateSnapshot(AuthGateState.SignedOut));
            return;
        }

        if (snapshot is null)
        {
            Publish(new AuthGateSnapshot(AuthGateState.SignedOut));
            return;
        }
        if (snapshot.AccessTokenExpiresAt > _timeProvider.GetUtcNow().Add(ClockSkew))
        {
            Publish(new AuthGateSnapshot(AuthGateState.SignedIn));
            return;
        }

        var generation = _sessionBoundary.Capture();
        Publish(new AuthGateSnapshot(AuthGateState.Refreshing));
        try
        {
            await _identity.RefreshAsync(_deviceName.DeviceName, cancellationToken);
            Publish(new AuthGateSnapshot(AuthGateState.SignedIn));
        }
        catch (OperationCanceledException) when (_sessionBoundary.IsCancellationRequested(generation))
        {
            // The newer account transition owns terminal state.
        }
        catch (Exception exception) when (IsOffline(exception))
        {
            Publish(new AuthGateSnapshot(AuthGateState.SignedIn, IsOfflineSession: true));
        }
        catch (MobileApiException exception)
        {
            await ClearAccountAsync(cancellationToken);
            Publish(new AuthGateSnapshot(AuthGateState.SignedOut, ErrorCode: exception.ErrorCode));
        }
    }

    private static bool IsOffline(Exception exception) => exception is HttpRequestException or IOException or TimeoutException;

    private async Task ClearAccountAsync(CancellationToken cancellationToken)
    {
        await _sessionBoundary.ResetAsync(async token =>
        {
            try
            {
                await _privateDataCleaner.ClearAsync(token);
            }
            finally
            {
                await _tokenStore.ClearAsync(CancellationToken.None);
            }
        }, cancellationToken);
    }

    private void Publish(AuthGateSnapshot snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke(this, snapshot);
    }
}
