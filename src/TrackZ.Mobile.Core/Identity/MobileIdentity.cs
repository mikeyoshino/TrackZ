using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Services;

namespace TrackZ.Mobile.Identity;

public static class MobileTokenKeys
{
    public const string AccessToken = "trackz_access_token";
    public const string RefreshToken = "trackz_refresh_token";
    public const string SessionId = "trackz_session_id";
    public const string UserId = "trackz_user_id";
}

public interface IMobileTokenStorage
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public interface IMobilePrivateDataCleaner
{
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class MobileTokenStore(IMobileTokenStorage storage) : IAccessTokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        storage.GetAsync(MobileTokenKeys.AccessToken, cancellationToken);

    public Task<string?> GetRefreshTokenAsync(CancellationToken cancellationToken = default) =>
        storage.GetAsync(MobileTokenKeys.RefreshToken, cancellationToken);

    public Task<string?> GetSessionIdAsync(CancellationToken cancellationToken = default) =>
        storage.GetAsync(MobileTokenKeys.SessionId, cancellationToken);

    public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
        storage.GetAsync(MobileTokenKeys.UserId, cancellationToken);

    public async Task SaveAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        var (userId, sessionId) = ParseIdentity(accessToken);
        await storage.SetAsync(MobileTokenKeys.AccessToken, accessToken, cancellationToken);
        await storage.SetAsync(MobileTokenKeys.RefreshToken, refreshToken, cancellationToken);
        await storage.SetAsync(MobileTokenKeys.SessionId, sessionId.ToString("D"), cancellationToken);
        await storage.SetAsync(MobileTokenKeys.UserId, userId.ToString("D"), cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await storage.RemoveAsync(MobileTokenKeys.AccessToken, cancellationToken);
        await storage.RemoveAsync(MobileTokenKeys.RefreshToken, cancellationToken);
        await storage.RemoveAsync(MobileTokenKeys.SessionId, cancellationToken);
        await storage.RemoveAsync(MobileTokenKeys.UserId, cancellationToken);
    }

    public static Guid ReadUserId(string accessToken) => ParseIdentity(accessToken).UserId;

    private static (Guid UserId, Guid SessionId) ParseIdentity(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length != 3) throw new FormatException();
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            if (document.RootElement.TryGetProperty("sub", out var subject)
                && Guid.TryParse(subject.GetString(), out var userId)
                && userId != Guid.Empty
                && document.RootElement.TryGetProperty("sid", out var value)
                && Guid.TryParse(value.GetString(), out var sessionId)
                && sessionId != Guid.Empty) return (userId, sessionId);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
        }
        throw new MobileApiException(BusinessErrorCode.InternalServerError, "The identity response was invalid.");
    }
}

public sealed class TrackZIdentityApiClient(
    HttpClient httpClient,
    MobileTokenStore tokenStore,
    IMobilePrivateDataCleaner privateDataCleaner)
{
    public async Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password, deviceName }, cancellationToken);
        var tokens = await ReadTokensAsync(response, cancellationToken);
        var previousUserId = await tokenStore.GetUserIdAsync(cancellationToken);
        var incomingUserId = MobileTokenStore.ReadUserId(tokens.AccessToken);
        if (!Guid.TryParse(previousUserId, out var previous) || previous != incomingUserId)
            await privateDataCleaner.ClearAsync(cancellationToken);
        await tokenStore.SaveAsync(tokens.AccessToken, tokens.RefreshToken, cancellationToken);
    }

    public async Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        var refreshToken = await tokenStore.GetRefreshTokenAsync(cancellationToken)
            ?? throw new MobileApiException(BusinessErrorCode.InvalidRequest, "No refresh token is available.");
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken, deviceName }, cancellationToken);
        var tokens = await ReadTokensAsync(response, cancellationToken);
        await tokenStore.SaveAsync(tokens.AccessToken, tokens.RefreshToken, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionId = await tokenStore.GetSessionIdAsync(cancellationToken);
            if (Guid.TryParse(sessionId, out var parsed) && parsed != Guid.Empty)
            {
                using var response = await httpClient.PostAsJsonAsync(
                    "/api/v1/auth/logout", new { sessionId = parsed }, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw new MobileApiException(BusinessErrorCode.InternalServerError, "Logout failed.");
            }
        }
        finally
        {
            try
            {
                await privateDataCleaner.ClearAsync(CancellationToken.None);
            }
            finally
            {
                await tokenStore.ClearAsync(CancellationToken.None);
            }
        }
    }

    private static async Task<TokenResponse> ReadTokensAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new MobileApiException(BusinessErrorCode.InternalServerError, "Authentication failed.");
        try
        {
            var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            if (result is null || string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.RefreshToken))
                throw new JsonException("Required token properties are missing.");
            return result;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new MobileApiException(BusinessErrorCode.InternalServerError, "The identity response was invalid.", innerException: exception);
        }
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
}
