using System.Net;
using System.Text;
using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests;

public sealed class IdentityTokenIntegrationTests
{
    [Fact]
    public async Task Complete_snapshot_requires_matching_structural_identity_and_expiry()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        await store.SaveAsync(Jwt(sessionId, userId, now.AddMinutes(15)), "refresh-one");

        var snapshot = await store.GetSnapshotAsync();

        Assert.Equal(new MobileIdentitySnapshot(userId, sessionId, now.AddMinutes(15), "refresh-one"), snapshot);
    }

    [Theory]
    [InlineData("{\"sub\":\"99999999-9999-9999-9999-999999999999\",\"sid\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\"}")]
    [InlineData("{\"sub\":\"99999999-9999-9999-9999-999999999999\",\"sid\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"exp\":\"not-a-number\"}")]
    [InlineData("{\"sub\":\"\",\"sid\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"exp\":1787314500}")]
    [InlineData("{\"sub\":\"99999999-9999-9999-9999-999999999999\",\"sid\":\"\",\"exp\":1787314500}")]
    public async Task Snapshot_rejects_missing_or_invalid_structural_token_claims(string payload)
    {
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        await storage.SetAsync(MobileTokenKeys.AccessToken, JwtPayload(payload));
        await storage.SetAsync(MobileTokenKeys.RefreshToken, "refresh-one");
        await storage.SetAsync(MobileTokenKeys.UserId, "99999999-9999-9999-9999-999999999999");
        await storage.SetAsync(MobileTokenKeys.SessionId, "aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");

        var error = await Assert.ThrowsAsync<MobileApiException>(() => store.GetSnapshotAsync());

        Assert.Equal(BusinessErrorCode.InternalServerError, error.ErrorCode);
        Assert.Equal("The identity response was invalid.", error.Message);
    }

    [Fact]
    public async Task Snapshot_rejects_malformed_base64url_and_stored_identity_mismatches()
    {
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        await storage.SetAsync(MobileTokenKeys.AccessToken, "header.%%%bad.payload");
        await storage.SetAsync(MobileTokenKeys.RefreshToken, "refresh-one");
        await storage.SetAsync(MobileTokenKeys.UserId, "99999999-9999-9999-9999-999999999999");
        await storage.SetAsync(MobileTokenKeys.SessionId, "aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");

        await Assert.ThrowsAsync<MobileApiException>(() => store.GetSnapshotAsync());

        await storage.SetAsync(MobileTokenKeys.AccessToken, Jwt(
            Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            DateTimeOffset.FromUnixTimeSeconds(1787314500)));
        await storage.SetAsync(MobileTokenKeys.UserId, "11111111-1111-1111-1111-111111111111");

        await Assert.ThrowsAsync<MobileApiException>(() => store.GetSnapshotAsync());
    }

    [Fact]
    public async Task Register_then_login_preserves_stable_problem_fields_and_uses_one_device_name()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.Created, "{\"userId\":\"99999999-9999-9999-9999-999999999999\",\"email\":\"lift@example.com\"}"),
            Json(HttpStatusCode.OK, $$"""{"accessToken":"{{JwtWithSession(Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"))}}","refreshToken":"refresh-one","expiresAt":"2026-08-15T12:00:00Z"}"""));
        var client = Identity(handler, out _, out _);

        await client.RegisterAndLoginAsync("lift@example.com", "Correct-Horse-9", "iPhone Simulator");

        Assert.Equal(["/api/v1/auth/register", "/api/v1/auth/login"], handler.RequestPaths);
        Assert.Contains("lift@example.com", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("Correct-Horse-9", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("iPhone Simulator", handler.RequestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_preserves_server_field_errors()
    {
        var handler = new QueueHandler(Problem(HttpStatusCode.BadRequest, BusinessErrorCode.EmailAlreadyExists,
            "Email already exists.", new Dictionary<string, string[]> { ["email"] = ["Use another email address."] }));
        var client = Identity(handler, out _, out _);

        var error = await Assert.ThrowsAsync<MobileApiException>(() =>
            client.RegisterAndLoginAsync("lift@example.com", "Correct-Horse-9", "iPhone Simulator"));

        Assert.Equal(BusinessErrorCode.EmailAlreadyExists, error.ErrorCode);
        Assert.Equal(["Use another email address."], error.FieldErrors!["email"]);
    }

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
        Assert.Equal([null, null, $"Bearer {refreshedAccessToken}"], handler.AuthorizationValues);
    }

    [Fact]
    public async Task Logout_never_attaches_the_session_bearer_outside_the_configured_api_origin()
    {
        var store = new MobileTokenStore(new MemoryTokenStorage());
        await store.SaveAsync(JwtWithSession(Guid.NewGuid()), "refresh-one");
        var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var identity = new TrackZIdentityApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://evil.example/") },
            store,
            new RecordingPrivateDataCleaner(),
            new AccountSessionBoundary(),
            apiOrigin: new Uri("https://api.trackz.test/"));

        await identity.LogoutAsync();

        Assert.Null(Assert.Single(handler.AuthorizationValues));
    }

    [Fact]
    public async Task Prepared_logout_captures_revocation_without_clearing_or_replacing_the_session()
    {
        var sessionId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var accessToken = JwtWithSession(sessionId);
        var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        await store.SaveAsync(accessToken, "refresh-one");
        var cleaner = new RecordingPrivateDataCleaner();
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        var identity = new TrackZIdentityApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") },
            store,
            cleaner,
            boundary);

        var prepared = await identity.PrepareLogoutAsync();

        Assert.Equal(generation, boundary.Capture());
        Assert.Equal(accessToken, await store.GetAccessTokenAsync());
        Assert.Equal(0, cleaner.ClearCount);

        await prepared.RevokeAsync();

        Assert.Equal(generation, boundary.Capture());
        Assert.Equal(accessToken, await store.GetAccessTokenAsync());
        Assert.Equal(0, cleaner.ClearCount);
        Assert.Contains(sessionId.ToString("D"), Assert.Single(handler.RequestBodies), StringComparison.Ordinal);
        Assert.Equal($"Bearer {accessToken}", Assert.Single(handler.AuthorizationValues));
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
    public async Task Concurrent_refresh_callers_share_one_rotating_token_exchange()
    {
        var sessionId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        await store.SaveAsync(JwtWithSession(sessionId), "refresh-one");
        var rotatedAccessToken = JwtWithSession(sessionId);
        var handler = new ConcurrentRefreshHandler(rotatedAccessToken);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") };
        var boundary = new AccountSessionBoundary();
        var refresh = new TrackZIdentityRefreshClient(
            httpClient,
            store,
            boundary);
        var identity = new TrackZIdentityApiClient(
            httpClient,
            store,
            new RecordingPrivateDataCleaner(),
            boundary,
            refresh);

        var first = identity.RefreshAsync("phone");
        await handler.FirstEntered.WaitAsync(TimeSpan.FromSeconds(2));
        var second = refresh.RefreshAsync("phone");
        await Task.Delay(100);
        handler.ReleaseFirst();

        var error = await Record.ExceptionAsync(() => Task.WhenAll(first, second));

        Assert.Null(error);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("refresh-two", await store.GetRefreshTokenAsync());
        Assert.Equal(rotatedAccessToken, await store.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Account_reset_cancels_inflight_refresh_and_prevents_queued_old_session_post()
    {
        var oldUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var newUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        var boundary = new AccountSessionBoundary();
        await store.SaveAsync(JwtWithSession(Guid.NewGuid(), oldUser), "old-refresh");
        var handler = new ConcurrentRefreshHandler(
            JwtWithSession(Guid.NewGuid(), oldUser));
        var refresh = new TrackZIdentityRefreshClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") },
            store,
            boundary);

        var first = refresh.RefreshAsync("phone");
        await handler.FirstEntered.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = refresh.RefreshAsync("phone");
        await boundary.ResetAsync(token => store.SaveAsync(
            JwtWithSession(Guid.NewGuid(), newUser), "new-refresh", token));
        handler.ReleaseFirst();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(newUser.ToString("D"), await store.GetUserIdAsync());
        Assert.Equal("new-refresh", await store.GetRefreshTokenAsync());
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

    [Fact]
    public async Task Login_canceled_after_session_invalidation_cannot_poison_later_login_or_commit()
    {
        var firstUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var firstToken = JwtWithSession(Guid.NewGuid(), firstUser);
        var secondToken = JwtWithSession(Guid.NewGuid(), secondUser);
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK,
                $$"""{"accessToken":"{{firstToken}}","refreshToken":"first-refresh","expiresAt":"2026-08-15T12:00:00Z"}"""),
            Json(HttpStatusCode.OK,
                $$"""{"accessToken":"{{secondToken}}","refreshToken":"second-refresh","expiresAt":"2026-08-15T12:15:00Z"}"""));
        var store = new MobileTokenStore(new MemoryTokenStorage());
        var boundary = new AccountSessionBoundary();
        var identity = new TrackZIdentityApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") },
            store, new RecordingPrivateDataCleaner(), boundary);
        var generation = boundary.Capture();
        var commitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generationCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeCommit = boundary.TryCommitAsync(generation, async token =>
        {
            commitEntered.TrySetResult();
            using var registration = token.Register(() => generationCanceled.TrySetResult());
            await releaseCommit.Task;
            token.ThrowIfCancellationRequested();
        });
        await commitEntered.Task;
        using var callerCancellation = new CancellationTokenSource();

        var firstLogin = identity.LoginAsync(
            "first@example.com", "Password!42", "phone", callerCancellation.Token);
        await generationCanceled.Task;
        callerCancellation.Cancel();

        Assert.False(firstLogin.IsCompleted);
        releaseCommit.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activeCommit);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstLogin);
        Assert.True(await boundary.TryCommitAsync(boundary.Capture(), _ => Task.CompletedTask));

        await identity.LoginAsync("second@example.com", "Password!42", "phone");

        Assert.Equal(secondToken, await store.GetAccessTokenAsync());
        Assert.Equal("second-refresh", await store.GetRefreshTokenAsync());
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

    private static string JwtWithSession(Guid sessionId, Guid? userId = null) =>
        Jwt(sessionId, userId ?? Guid.Parse("99999999-9999-9999-9999-999999999999"), DateTimeOffset.UtcNow.AddMinutes(15));

    private static string Jwt(Guid sessionId, Guid userId, DateTimeOffset expiresAt) =>
        JwtPayload($"{{\"sub\":\"{userId:D}\",\"sid\":\"{sessionId:D}\",\"exp\":{expiresAt.ToUnixTimeSeconds()}}}");

    private static string JwtPayload(string payload)
    {
        static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode(payload)}.signature";
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
            : System.Text.Json.JsonSerializer.Serialize(fields);
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
        public List<string> RequestPaths { get; } = [];
        public List<string?> AuthorizationValues { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri!.AbsolutePath);
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            AuthorizationValues.Add(request.Headers.Authorization?.ToString());
            return _responses.Dequeue();
        }
    }

    private sealed class ConcurrentRefreshHandler(string accessToken) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public Task FirstEntered => _firstEntered.Task;
        public int RequestCount => Volatile.Read(ref _requestCount);
        public void ReleaseFirst() => _releaseFirst.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _requestCount);
            if (attempt == 1)
            {
                _firstEntered.TrySetResult();
                await _releaseFirst.Task.WaitAsync(cancellationToken);
                return Json(HttpStatusCode.OK,
                    $$"""{"accessToken":"{{accessToken}}","refreshToken":"refresh-two","expiresAt":"2026-08-15T12:15:00Z"}""");
            }

            return Problem(
                HttpStatusCode.Unauthorized,
                BusinessErrorCode.RefreshTokenInvalid,
                "Refresh token is invalid.");
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
