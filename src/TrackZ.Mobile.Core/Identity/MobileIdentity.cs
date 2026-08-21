using System.Net.Http.Json;
using System.Net.Http.Headers;
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

public sealed record MobileIdentitySnapshot(
    Guid UserId,
    Guid SessionId,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);

public sealed class IdentityHttpTransport(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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

    public async Task<MobileIdentitySnapshot?> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var accessToken = await storage.GetAsync(MobileTokenKeys.AccessToken, cancellationToken);
        var refreshToken = await storage.GetAsync(MobileTokenKeys.RefreshToken, cancellationToken);
        var storedSessionId = await storage.GetAsync(MobileTokenKeys.SessionId, cancellationToken);
        var storedUserId = await storage.GetAsync(MobileTokenKeys.UserId, cancellationToken);
        if (accessToken is null && refreshToken is null && storedSessionId is null && storedUserId is null) return null;
        if (string.IsNullOrWhiteSpace(accessToken)
            || string.IsNullOrWhiteSpace(refreshToken)
            || !Guid.TryParse(storedSessionId, out var sessionId)
            || sessionId == Guid.Empty
            || !Guid.TryParse(storedUserId, out var userId)
            || userId == Guid.Empty) throw InvalidIdentity();

        var parsed = ParseIdentity(accessToken);
        if (parsed.UserId != userId || parsed.SessionId != sessionId) throw InvalidIdentity();
        return new MobileIdentitySnapshot(parsed.UserId, parsed.SessionId, parsed.ExpiresAt, refreshToken);
    }

    public async Task SaveAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        var snapshot = CreateSnapshot(accessToken, refreshToken);
        await storage.SetAsync(MobileTokenKeys.AccessToken, accessToken, cancellationToken);
        await storage.SetAsync(MobileTokenKeys.RefreshToken, refreshToken, cancellationToken);
        await storage.SetAsync(MobileTokenKeys.SessionId, snapshot.SessionId.ToString("D"), cancellationToken);
        await storage.SetAsync(MobileTokenKeys.UserId, snapshot.UserId.ToString("D"), cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await storage.RemoveAsync(MobileTokenKeys.AccessToken, cancellationToken);
        await storage.RemoveAsync(MobileTokenKeys.RefreshToken, cancellationToken);
        await storage.RemoveAsync(MobileTokenKeys.SessionId, cancellationToken);
        await storage.RemoveAsync(MobileTokenKeys.UserId, cancellationToken);
    }

    public static Guid ReadUserId(string accessToken) => ParseIdentity(accessToken).UserId;

    internal static MobileIdentitySnapshot CreateSnapshot(string accessToken, string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw InvalidIdentity();
        var parsed = ParseIdentity(accessToken);
        return new MobileIdentitySnapshot(parsed.UserId, parsed.SessionId, parsed.ExpiresAt, refreshToken);
    }

    private static (Guid UserId, Guid SessionId, DateTimeOffset ExpiresAt) ParseIdentity(string accessToken)
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
                && sessionId != Guid.Empty
                && document.RootElement.TryGetProperty("exp", out var expiry)
                && expiry.ValueKind == JsonValueKind.Number
                && expiry.TryGetInt64(out var unixSeconds)
                && unixSeconds > 0) return (userId, sessionId, DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or FormatException or JsonException or InvalidOperationException)
        {
        }
        throw InvalidIdentity();
    }

    private static MobileApiException InvalidIdentity() => new(
        BusinessErrorCode.InternalServerError, "The identity response was invalid.");
}

public sealed class TrackZIdentityApiClient(
    HttpClient httpClient,
    MobileTokenStore tokenStore,
    IMobilePrivateDataCleaner privateDataCleaner,
    IAccountSessionBoundary sessionBoundary,
    TrackZIdentityRefreshClient? refreshClient = null,
    Uri? apiOrigin = null) : IIdentitySessionApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TrackZIdentityRefreshClient _refreshClient = refreshClient
        ?? new TrackZIdentityRefreshClient(httpClient, tokenStore, sessionBoundary);
    private readonly Uri? _apiOrigin = apiOrigin ?? httpClient.BaseAddress;

    public async Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) =>
        _ = await LoginWithReceiptAsync(email, password, deviceName, cancellationToken);

    public async Task<IdentityTransitionReceipt?> LoginWithReceiptAsync(
        string email,
        string password,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var generation = sessionBoundary.Capture();
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/auth/login", new { email, password, deviceName }, cancellationToken);
        var tokens = await ReadTokensAsync(response, cancellationToken);
        var snapshot = MobileTokenStore.CreateSnapshot(tokens.AccessToken, tokens.RefreshToken);
        if (!await sessionBoundary.TryResetAsync(generation, async token =>
        {
            await privateDataCleaner.ClearAsync(token);
            await tokenStore.SaveAsync(tokens.AccessToken, tokens.RefreshToken, token);
        }, cancellationToken)) throw new OperationCanceledException("The account session changed.");
        return new IdentityTransitionReceipt(sessionBoundary.Capture(), snapshot);
    }

    public async Task RegisterAndLoginAsync(
        string email,
        string password,
        string deviceName,
        CancellationToken cancellationToken = default) =>
        _ = await RegisterAndLoginWithReceiptAsync(email, password, deviceName, cancellationToken);

    public async Task<IdentityTransitionReceipt?> RegisterAndLoginWithReceiptAsync(
        string email,
        string password,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/auth/register", new { email, password }, cancellationToken);
        await ReadRegistrationAsync(response, cancellationToken);
        return await LoginWithReceiptAsync(email, password, deviceName, cancellationToken);
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
            string? accessToken = null;
            if (!await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                sessionId = await tokenStore.GetSessionIdAsync(token);
                accessToken = await tokenStore.GetAccessTokenAsync(token);
            }, cancellationToken)) return;
            if (Guid.TryParse(sessionId, out var parsed) && parsed != Guid.Empty)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/logout")
                {
                    Content = JsonContent.Create(new { sessionId = parsed })
                };
                var absoluteRequestUri = httpClient.BaseAddress is null
                    ? request.RequestUri
                    : new Uri(httpClient.BaseAddress, request.RequestUri!);
                if (!string.IsNullOrWhiteSpace(accessToken) && IsExactApiOrigin(absoluteRequestUri))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await httpClient.SendAsync(request, sessionCancellation.Token);
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

    private static async Task ReadRegistrationAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        try
        {
            var result = await response.Content.ReadFromJsonAsync<RegistrationResponse>(JsonOptions, cancellationToken);
            if (result is null || result.UserId == Guid.Empty || string.IsNullOrWhiteSpace(result.Email))
                throw new JsonException("Required registration properties are missing.");
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

    private bool IsExactApiOrigin(Uri? requestUri)
    {
        if (_apiOrigin is null || requestUri is not { IsAbsoluteUri: true }) return false;
        if (!string.Equals(requestUri.Scheme, _apiOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(requestUri.IdnHost, _apiOrigin.IdnHost, StringComparison.OrdinalIgnoreCase)
            || requestUri.Port != _apiOrigin.Port)
            return false;
        var root = _apiOrigin.AbsolutePath;
        if (root == "/") return requestUri.AbsolutePath.StartsWith("/", StringComparison.Ordinal);
        var normalizedRoot = root.EndsWith('/') ? root : root + "/";
        return requestUri.AbsolutePath.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    internal sealed record TokenResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset ExpiresAt);

    private sealed record RegistrationResponse(Guid UserId, string Email);
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
                "api/v1/auth/refresh",
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
