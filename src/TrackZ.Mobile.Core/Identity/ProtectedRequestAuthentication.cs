using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises;

namespace TrackZ.Mobile.Identity;

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
