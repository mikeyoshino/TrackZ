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
    IMobilePrivateDataCleaner privateDataCleaner,
    IAccountSessionBoundary sessionBoundary,
    TrackZIdentityRefreshClient? refreshClient = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TrackZIdentityRefreshClient _refreshClient = refreshClient
        ?? new TrackZIdentityRefreshClient(httpClient, tokenStore, sessionBoundary);

    public async Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default)
    {
        var generation = sessionBoundary.Capture();
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password, deviceName }, cancellationToken);
        var tokens = await ReadTokensAsync(response, cancellationToken);
        _ = MobileTokenStore.ReadUserId(tokens.AccessToken);
        if (!await sessionBoundary.TryResetAsync(generation, async token =>
        {
            await privateDataCleaner.ClearAsync(token);
            await tokenStore.SaveAsync(tokens.AccessToken, tokens.RefreshToken, token);
        }, cancellationToken)) throw new OperationCanceledException("The account session changed.");
    }

    public Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default) =>
        _refreshClient.RefreshAsync(deviceName, cancellationToken);

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var generation = sessionBoundary.Capture();
        using var sessionCancellation = sessionBoundary.CreateCancellationLease(
            generation, cancellationToken);
        try
        {
            string? sessionId = null;
            if (!await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                sessionId = await tokenStore.GetSessionIdAsync(token);
            }, cancellationToken)) return;
            if (Guid.TryParse(sessionId, out var parsed) && parsed != Guid.Empty)
            {
                using var response = await httpClient.PostAsJsonAsync(
                    "/api/v1/auth/logout", new { sessionId = parsed }, sessionCancellation.Token);
                await EnsureSuccessAsync(response, sessionCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && sessionBoundary.IsCancellationRequested(generation))
        {
            // A newer identity transition owns the current session.
        }
        finally
        {
            await sessionBoundary.TryResetAsync(generation, async token =>
            {
                try
                {
                    await privateDataCleaner.ClearAsync(token);
                }
                finally
                {
                    await tokenStore.ClearAsync(CancellationToken.None);
                }
            }, CancellationToken.None);
        }
    }

    internal static async Task<TokenResponse> ReadTokensAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        try
        {
            var result = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);
            if (result is null || string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.RefreshToken))
                throw new JsonException("Required token properties are missing.");
            return result;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new MobileApiException(BusinessErrorCode.InternalServerError, "The identity response was invalid.", innerException: exception);
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(JsonOptions, cancellationToken);
            if (problem is not null
                && Enum.IsDefined(problem.ErrorCode)
                && !string.IsNullOrWhiteSpace(problem.Message))
                throw new MobileApiException(problem.ErrorCode, problem.Message, problem.FieldErrors);
        }
        catch (MobileApiException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw InvalidResponse(exception);
        }
        throw InvalidResponse();
    }

    private static MobileApiException InvalidResponse(Exception? exception = null) => new(
        BusinessErrorCode.InternalServerError,
        "The server returned an invalid response.",
        innerException: exception);

    internal sealed record TokenResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset ExpiresAt);
}

public sealed class TrackZIdentityRefreshClient(
    HttpClient httpClient,
    MobileTokenStore tokenStore,
    IAccountSessionBoundary sessionBoundary)
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private long _refreshEpoch;

    public async Task RefreshAsync(
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var generation = sessionBoundary.Capture();
        var observedEpoch = Volatile.Read(ref _refreshEpoch);
        string? observedRefreshToken = null;
        if (!await sessionBoundary.TryCommitAsync(generation, async token =>
        {
            observedRefreshToken = await tokenStore.GetRefreshTokenAsync(token);
        }, cancellationToken)) throw new OperationCanceledException("The account session changed.");
        if (observedRefreshToken is null)
            throw new MobileApiException(
                BusinessErrorCode.InvalidRequest, "No refresh token is available.");
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _refreshEpoch) != observedEpoch) return;
            using var sessionCancellation = sessionBoundary.CreateCancellationLease(
                generation, cancellationToken);
            sessionCancellation.Token.ThrowIfCancellationRequested();

            using var response = await httpClient.PostAsJsonAsync(
                "/api/v1/auth/refresh",
                new { refreshToken = observedRefreshToken, deviceName },
                sessionCancellation.Token);
            var tokens = await TrackZIdentityApiClient.ReadTokensAsync(
                response, sessionCancellation.Token);
            if (!await sessionBoundary.TryCommitAsync(generation, token =>
                tokenStore.SaveAsync(tokens.AccessToken, tokens.RefreshToken, token),
                sessionCancellation.Token))
                throw new OperationCanceledException("The account session changed.");
            Interlocked.Increment(ref _refreshEpoch);
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
