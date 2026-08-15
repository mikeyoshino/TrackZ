using System.Net;
using System.Text;
using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests;

public sealed class IdentityTokenIntegrationTests
{
    [Fact]
    public async Task Login_output_is_read_by_real_bearer_provider_and_logout_clears_shared_keys()
    {
        var sessionId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var accessToken = JwtWithSession(sessionId);
        var refreshedAccessToken = JwtWithSession(sessionId);
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, $$"""{"accessToken":"{{accessToken}}","refreshToken":"refresh-one","expiresAt":"2026-08-15T12:00:00Z"}"""),
            Json(HttpStatusCode.OK, $$"""{"accessToken":"{{refreshedAccessToken}}","refreshToken":"refresh-two","expiresAt":"2026-08-15T12:15:00Z"}"""),
            new HttpResponseMessage(HttpStatusCode.NoContent));
        var storage = new MemoryTokenStorage();
        var tokenStore = new MobileTokenStore(storage);
        var cleaner = new RecordingPrivateDataCleaner();
        var identity = new TrackZIdentityApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") }, tokenStore, cleaner,
            new AccountSessionBoundary());

        await identity.LoginAsync("person@example.com", "Password!42", "phone");

        Assert.Equal(accessToken, await tokenStore.GetAccessTokenAsync());
        Assert.Equal(accessToken, await storage.GetAsync(MobileTokenKeys.AccessToken));
        Assert.Equal("refresh-one", await storage.GetAsync(MobileTokenKeys.RefreshToken));
        await identity.RefreshAsync("phone");
        Assert.Equal(refreshedAccessToken, await tokenStore.GetAccessTokenAsync());
        Assert.Equal("refresh-two", await storage.GetAsync(MobileTokenKeys.RefreshToken));
        await identity.LogoutAsync();
        Assert.Null(await tokenStore.GetAccessTokenAsync());
        Assert.Null(await storage.GetAsync(MobileTokenKeys.AccessToken));
        Assert.Null(await storage.GetAsync(MobileTokenKeys.RefreshToken));
        Assert.Equal(2, cleaner.ClearCount);
        Assert.Contains("refresh-one", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Contains(sessionId.ToString("D"), handler.RequestBodies[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_preserves_business_code_message_and_field_errors()
    {
        var handler = new QueueHandler(Problem(HttpStatusCode.BadRequest, BusinessErrorCode.InvalidCredentials,
            "Email or password is incorrect.", new Dictionary<string, string[]> { ["email"] = ["Check the email address."] }));
        var identity = Identity(handler, out _, out _);

        var error = await Assert.ThrowsAsync<MobileApiException>(() =>
            identity.LoginAsync("person@example.com", "wrong", "phone"));

        Assert.Equal(BusinessErrorCode.InvalidCredentials, error.ErrorCode);
        Assert.Equal("Email or password is incorrect.", error.Message);
        Assert.Equal(["Check the email address."], error.FieldErrors!["email"]);
    }

    [Fact]
    public async Task Refresh_preserves_refresh_token_error_instead_of_internal_server_error()
    {
        var sessionId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var accessToken = JwtWithSession(sessionId);
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, $$"""{"accessToken":"{{accessToken}}","refreshToken":"refresh-one","expiresAt":"2026-08-15T12:00:00Z"}"""),
            Problem(HttpStatusCode.Unauthorized, BusinessErrorCode.RefreshTokenInvalid, "Refresh token is invalid."));
        var identity = Identity(handler, out _, out _);
        await identity.LoginAsync("person@example.com", "Password!42", "phone");

        var error = await Assert.ThrowsAsync<MobileApiException>(() => identity.RefreshAsync("phone"));

        Assert.Equal(BusinessErrorCode.RefreshTokenInvalid, error.ErrorCode);
        Assert.Equal("Refresh token is invalid.", error.Message);
    }

    [Fact]
    public async Task Auth_first_logout_preserves_problem_and_still_clears_private_session()
    {
        var sessionId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var accessToken = JwtWithSession(sessionId);
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, $$"""{"accessToken":"{{accessToken}}","refreshToken":"refresh-one","expiresAt":"2026-08-15T12:00:00Z"}"""),
            Problem(HttpStatusCode.Unauthorized, BusinessErrorCode.InvalidRequest, "Authentication is required."));
        var identity = Identity(handler, out var store, out var cleaner);
        await identity.LoginAsync("person@example.com", "Password!42", "phone");

        var error = await Assert.ThrowsAsync<MobileApiException>(() => identity.LogoutAsync());

        Assert.Equal(BusinessErrorCode.InvalidRequest, error.ErrorCode);
        Assert.Equal("Authentication is required.", error.Message);
        Assert.Null(await store.GetAccessTokenAsync());
        Assert.Equal(2, cleaner.ClearCount);
    }

    [Fact]
    public async Task Malformed_identity_problem_uses_stable_internal_server_error()
    {
        var identity = Identity(new QueueHandler(Json(HttpStatusCode.BadRequest, "{not-json")), out _, out _);

        var error = await Assert.ThrowsAsync<MobileApiException>(() =>
            identity.LoginAsync("person@example.com", "wrong", "phone"));

        Assert.Equal(BusinessErrorCode.InternalServerError, error.ErrorCode);
        Assert.Equal("The server returned an invalid response.", error.Message);
    }

    [Fact]
    public async Task Refresh_started_in_old_session_cannot_replace_new_account_tokens()
    {
        var oldUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var newUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var storage = new GatedRefreshTokenStorage();
        var store = new MobileTokenStore(storage);
        var boundary = new AccountSessionBoundary();
        await store.SaveAsync(JwtWithSession(Guid.NewGuid(), oldUser), "old-refresh");
        var handler = new GatedResponseHandler(Json(HttpStatusCode.OK,
            $$"""{"accessToken":"{{JwtWithSession(Guid.NewGuid(), oldUser)}}","refreshToken":"old-rotated","expiresAt":"2026-08-15T12:00:00Z"}"""));
        var identity = new TrackZIdentityApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") },
            store, new RecordingPrivateDataCleaner(), boundary);

        var refresh = identity.RefreshAsync("phone");
        await storage.RefreshReadEntered;
        var accountChange = boundary.ResetAsync(token => store.SaveAsync(
            JwtWithSession(Guid.NewGuid(), newUser), "new-refresh", token));
        storage.ReleaseRefreshRead();
        await handler.Entered;
        await accountChange;
        handler.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Equal(newUser.ToString("D"), await store.GetUserIdAsync());
        Assert.Equal("new-refresh", await store.GetRefreshTokenAsync());
    }

    [Fact]
    public async Task Delayed_old_logout_cannot_clear_a_newly_committed_account()
    {
        var oldUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var newUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        var boundary = new AccountSessionBoundary();
        await store.SaveAsync(JwtWithSession(Guid.NewGuid(), oldUser), "old-refresh");
        var handler = new GatedResponseHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var identity = new TrackZIdentityApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") },
            store, new RecordingPrivateDataCleaner(), boundary);

        var logout = identity.LogoutAsync();
        await handler.Entered;
        await boundary.ResetAsync(token => store.SaveAsync(
            JwtWithSession(Guid.NewGuid(), newUser), "new-refresh", token));
        handler.Release();
        await logout;

        Assert.Equal(newUser.ToString("D"), await store.GetUserIdAsync());
        Assert.Equal("new-refresh", await store.GetRefreshTokenAsync());
    }

    [Fact]
    public async Task Delayed_old_login_cannot_resurrect_account_after_logout_reset()
    {
        var oldUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        var boundary = new AccountSessionBoundary();
        var handler = new GatedResponseHandler(Json(HttpStatusCode.OK,
            $$"""{"accessToken":"{{JwtWithSession(Guid.NewGuid(), oldUser)}}","refreshToken":"old-refresh","expiresAt":"2026-08-15T12:00:00Z"}"""));
        var identity = new TrackZIdentityApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") },
            store, new RecordingPrivateDataCleaner(), boundary);

        var login = identity.LoginAsync("old@example.com", "Password!42", "phone");
        await handler.Entered;
        await boundary.ResetAsync(token => store.ClearAsync(token));
        handler.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
        Assert.Null(await store.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Logout_reads_old_session_under_boundary_and_never_sends_new_session_id()
    {
        var oldUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var newUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var oldSession = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var newSession = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var storage = new GatedSessionIdStorage();
        var store = new MobileTokenStore(storage);
        var boundary = new AccountSessionBoundary();
        await store.SaveAsync(JwtWithSession(oldSession, oldUser), "old-refresh");
        var handler = new GatedResponseHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var identity = new TrackZIdentityApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") },
            store, new RecordingPrivateDataCleaner(), boundary);

        var logout = identity.LogoutAsync();
        await storage.SessionReadEntered;
        var accountChange = boundary.ResetAsync(token => store.SaveAsync(
            JwtWithSession(newSession, newUser), "new-refresh", token));
        storage.ReleaseSessionRead();
        await handler.Entered;
        await accountChange;
        handler.Release();
        await logout;

        Assert.Contains(oldSession.ToString("D"), handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain(newSession.ToString("D"), handler.RequestBody, StringComparison.Ordinal);
        Assert.Equal(newUser.ToString("D"), await store.GetUserIdAsync());
    }

    private static TrackZIdentityApiClient Identity(
        QueueHandler handler,
        out MobileTokenStore store,
        out RecordingPrivateDataCleaner cleaner)
    {
        store = new MobileTokenStore(new MemoryTokenStorage());
        cleaner = new RecordingPrivateDataCleaner();
        return new TrackZIdentityApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") },
            store, cleaner, new AccountSessionBoundary());
    }

    private sealed class RecordingPrivateDataCleaner : IMobilePrivateDataCleaner
    {
        public int ClearCount { get; private set; }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private static string JwtWithSession(Guid sessionId, Guid? userId = null)
    {
        static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode($"{{\"sub\":\"{userId ?? Guid.Parse("99999999-9999-9999-9999-999999999999"):D}\",\"sid\":\"{sessionId:D}\"}}")}.signature";
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Problem(
        HttpStatusCode status,
        BusinessErrorCode code,
        string message,
        IReadOnlyDictionary<string, string[]>? fields = null)
    {
        var fieldJson = fields is null
            ? "null"
            : "{\"email\":[\"Check the email address.\"]}";
        return Json(status, $$"""
            {"type":"https://trackz.test/problem","title":"Request failed","status":{{(int)status}},"errorCode":{{(int)code}},"message":"{{message}}","traceId":"trace-1","fieldErrors":{{fieldJson}}}
            """);
    }

    private sealed class MemoryTokenStorage : IMobileTokenStorage
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<string> RequestBodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }
    }

    private sealed class GatedResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public string RequestBody { get; private set; } = string.Empty;
        public void Release() => _release.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(CancellationToken.None);
            _entered.TrySetResult();
            await _release.Task;
            return response;
        }
    }

    private sealed class GatedSessionIdStorage : IMobileTokenStorage
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SessionReadEntered => _entered.Task;
        public void ReleaseSessionRead() => _release.TrySetResult();
        public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            if (key == MobileTokenKeys.SessionId)
            {
                _entered.TrySetResult();
                await _release.Task;
            }
            return _values.GetValueOrDefault(key);
        }
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class GatedRefreshTokenStorage : IMobileTokenStorage
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RefreshReadEntered => _entered.Task;
        public void ReleaseRefreshRead() => _release.TrySetResult();
        public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var captured = _values.GetValueOrDefault(key);
            if (key == MobileTokenKeys.RefreshToken)
            {
                _entered.TrySetResult();
                await _release.Task;
            }
            return captured;
        }
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
