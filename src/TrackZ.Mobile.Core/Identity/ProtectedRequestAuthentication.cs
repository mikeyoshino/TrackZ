using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises;

namespace TrackZ.Mobile.Identity;

public interface IProtectedRequestAuthentication
{
    Task<bool> TryRefreshAsync(
        AccountSessionGeneration expectedGeneration,
        CancellationToken cancellationToken);
    Task RequireSignInAsync(
        AccountSessionGeneration expectedGeneration,
        CancellationToken cancellationToken);
}

public sealed class ProtectedRequestAuthentication(
    TrackZIdentityRefreshClient refreshClient,
    IDeviceNameProvider deviceName,
    IAuthEntryPoint authEntryPoint,
    IAccountSessionBoundary sessionBoundary) : IProtectedRequestAuthentication
{
    public async Task<bool> TryRefreshAsync(
        AccountSessionGeneration expectedGeneration,
        CancellationToken cancellationToken)
    {
        if (sessionBoundary.IsCancellationRequested(expectedGeneration)) return false;
        try
        {
            await refreshClient.RefreshAsync(
                deviceName.DeviceName, expectedGeneration, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (
            sessionBoundary.IsCancellationRequested(expectedGeneration))
        {
            return false;
        }
        catch (MobileApiException exception) when (
            exception.ErrorCode == BusinessErrorCode.RefreshTokenInvalid
            || exception.IsAuthenticationRequired)
        {
            await authEntryPoint.RequireSignInAsync(expectedGeneration, cancellationToken);
            return false;
        }
    }

    public Task RequireSignInAsync(
        AccountSessionGeneration expectedGeneration,
        CancellationToken cancellationToken) =>
        authEntryPoint.RequireSignInAsync(expectedGeneration, cancellationToken);
}
