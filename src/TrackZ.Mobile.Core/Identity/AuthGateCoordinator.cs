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

public sealed record IdentityTransitionReceipt(
    AccountSessionGeneration Generation,
    MobileIdentitySnapshot Identity);

public interface IIdentitySessionApi
{
    Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default);
    Task RegisterAndLoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default);
    Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);

    async Task<IdentityTransitionReceipt?> LoginWithReceiptAsync(
        string email,
        string password,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        await LoginAsync(email, password, deviceName, cancellationToken);
        return null;
    }

    async Task<IdentityTransitionReceipt?> RegisterAndLoginWithReceiptAsync(
        string email,
        string password,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        await RegisterAndLoginAsync(email, password, deviceName, cancellationToken);
        return null;
    }
}

public interface IAuthEntryPoint
{
    Task RequireSignInAsync(CancellationToken cancellationToken = default);
    Task RequireSignInAsync(
        AccountSessionGeneration expectedGeneration,
        CancellationToken cancellationToken = default);
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

    public Task RequireSignInAsync(CancellationToken cancellationToken = default) =>
        RequireSignInAsync(_sessionBoundary.Capture(), cancellationToken);

    public async Task RequireSignInAsync(
        AccountSessionGeneration expectedGeneration,
        CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _initialised) != 0 && Snapshot.State == AuthGateState.SignedOut) return;
            var resetStarted = false;
            try
            {
                if (!await _sessionBoundary.TryResetAsync(expectedGeneration, async token =>
                {
                    resetStarted = true;
                    await ClearStoredAccountAsync(token);
                }, cancellationToken)) return;
            }
            finally
            {
                if (resetStarted)
                {
                    Publish(new AuthGateSnapshot(AuthGateState.SignedOut));
                    Volatile.Write(ref _initialised, 1);
                }
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async Task SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _submitting, 1, 0) != 0) return;
        try
        {
            await _transitionGate.WaitAsync(cancellationToken);
            try
            {
                var receipt = await _identity.LoginWithReceiptAsync(
                    email, password, _deviceName.DeviceName, cancellationToken);
                if (await TryPublishSignedInAsync(receipt, cancellationToken))
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
                var receipt = await _identity.RegisterAndLoginWithReceiptAsync(
                    email, password, _deviceName.DeviceName, cancellationToken);
                if (await TryPublishSignedInAsync(receipt, cancellationToken))
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
            using var lease = _sessionBoundary.CreateCancellationLease(generation, cancellationToken);
            await _identity.RefreshAsync(_deviceName.DeviceName, lease.Token);
            if (_sessionBoundary.IsCancellationRequested(generation)) return;
            Publish(new AuthGateSnapshot(AuthGateState.SignedIn));
        }
        catch (OperationCanceledException) when (_sessionBoundary.IsCancellationRequested(generation))
        {
            // The newer account transition owns terminal state.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Publish(new AuthGateSnapshot(AuthGateState.SignedIn, IsOfflineSession: true));
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

    private async Task<bool> TryPublishSignedInAsync(
        IdentityTransitionReceipt? receipt,
        CancellationToken cancellationToken)
    {
        if (receipt is null) return false;
        var published = false;
        try
        {
            var committed = await _sessionBoundary.TryCommitAsync(receipt.Generation, async token =>
            {
                var snapshot = await _tokenStore.GetSnapshotAsync(token);
                if (snapshot != receipt.Identity) return;
                Publish(new AuthGateSnapshot(AuthGateState.SignedIn));
                published = true;
            }, cancellationToken);
            return committed && published;
        }
        catch (MobileApiException)
        {
            return false;
        }
    }

    private async Task ClearAccountAsync(CancellationToken cancellationToken)
    {
        await _sessionBoundary.ResetAsync(ClearStoredAccountAsync, cancellationToken);
    }

    private async Task ClearStoredAccountAsync(CancellationToken token)
    {
        try
        {
            await _privateDataCleaner.ClearAsync(token);
        }
        finally
        {
            await _tokenStore.ClearAsync(CancellationToken.None);
        }
    }

    private void Publish(AuthGateSnapshot snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke(this, snapshot);
    }
}
