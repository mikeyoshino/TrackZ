using System.Net;
using System.Net.Http.Headers;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Networking;

namespace TrackZ.Mobile.Tests.Networking;

public sealed class AuthenticatedApiHandlerTests
{
    private static readonly Uri ApiOrigin = new("https://api.trackz.test/mobile/");

    [Fact]
    public async Task Exact_origin_get_refreshes_once_and_replays_once_after_401()
    {
        var transport = new SequenceHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        var recovery = new RecordingAuthenticationRecovery(succeeds: true);
        using var client = ClientWithHandler(transport, recovery, new RotatingTokenProvider(), ApiOrigin);

        using var response = await client.GetAsync("exercises?pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, recovery.RefreshCount);
        Assert.Equal(["Bearer old-token", "Bearer new-token"], transport.AuthorizationValues);
        Assert.Equal(2, transport.RequestCount);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Unsafe_request_is_never_replayed(string method)
    {
        var transport = new SequenceHandler(HttpStatusCode.Unauthorized);
        var recovery = new RecordingAuthenticationRecovery(succeeds: true);
        using var client = ClientWithHandler(transport, recovery, new RotatingTokenProvider(), ApiOrigin);
        using var request = new HttpRequestMessage(new HttpMethod(method), "exercises");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, recovery.RefreshCount);
        Assert.Equal(0, recovery.RequireSignInCount);
        Assert.Equal(1, transport.RequestCount);
    }

    [Theory]
    [InlineData("http://api.trackz.test/mobile/exercises")]
    [InlineData("https://api.trackz.test:444/mobile/exercises")]
    [InlineData("https://evil.example/mobile/exercises")]
    [InlineData("https://api.trackz.test/exercises")]
    [InlineData("https://api.trackz.test/mobile-other/exercises")]
    public async Task Non_exact_origin_has_no_bearer_and_no_recovery(string address)
    {
        var transport = new SequenceHandler(HttpStatusCode.Unauthorized);
        var recovery = new RecordingAuthenticationRecovery(succeeds: true);
        using var client = ClientWithHandler(transport, recovery, new RotatingTokenProvider(), ApiOrigin);

        using var response = await client.GetAsync(address);

        Assert.Null(Assert.Single(transport.AuthorizationValues));
        Assert.Equal(0, recovery.RefreshCount);
        Assert.Equal(0, recovery.RequireSignInCount);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task Foreign_origin_strips_a_preexisting_bearer_header()
    {
        var transport = new SequenceHandler(HttpStatusCode.OK);
        using var client = ClientWithHandler(
            transport,
            new RecordingAuthenticationRecovery(true),
            new RotatingTokenProvider(),
            ApiOrigin);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/steal");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "must-not-leak");

        using var response = await client.SendAsync(request);

        Assert.Null(Assert.Single(transport.AuthorizationValues));
    }

    [Fact]
    public async Task Redirect_response_is_returned_without_a_follow_up_request()
    {
        var transport = new RedirectHandler();
        using var client = ClientWithHandler(
            transport, new RecordingAuthenticationRecovery(true), new RotatingTokenProvider(), ApiOrigin);

        using var response = await client.GetAsync("exercises");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task Second_401_requires_sign_in_once_and_returns_final_response()
    {
        var transport = new SequenceHandler(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);
        var recovery = new RecordingAuthenticationRecovery(succeeds: true);
        using var client = ClientWithHandler(transport, recovery, new RotatingTokenProvider(), ApiOrigin);

        using var response = await client.GetAsync("exercises");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, recovery.RefreshCount);
        Assert.Equal(1, recovery.RequireSignInCount);
        Assert.Equal(2, transport.RequestCount);
    }

    [Fact]
    public async Task Concurrent_401_responses_share_one_refresh()
    {
        var transport = new ConcurrentUnauthorizedHandler(expectedInitialRequests: 2);
        var tokens = new MutableTokenProvider();
        var recovery = new GatedAuthenticationRecovery(() => tokens.Token = "new-token");
        using var client = ClientWithHandler(transport, recovery, tokens, ApiOrigin);

        var first = client.GetAsync("exercises?request=1");
        var second = client.GetAsync("exercises?request=2");
        await transport.InitialRequestsEntered.WaitAsync(TimeSpan.FromSeconds(2));
        transport.ReleaseInitialResponses();
        await recovery.RefreshEntered.WaitAsync(TimeSpan.FromSeconds(2));
        recovery.ReleaseRefresh();

        using var firstResponse = await first;
        using var secondResponse = await second;

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(1, recovery.RefreshCount);
        Assert.Equal(4, transport.RequestCount);
    }

    [Fact]
    public async Task Old_generation_401_after_new_login_never_refreshes_replays_or_clears_new_account()
    {
        var boundary = new AccountSessionBoundary();
        var tokens = new MutableTokenProvider();
        var transport = new PausedUnauthorizedHandler();
        var recovery = new RecordingAuthenticationRecovery(succeeds: true);
        using var client = ClientWithHandler(transport, recovery, tokens, ApiOrigin, boundary);

        var oldRequest = client.GetAsync("exercises?account=A");
        await transport.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        await boundary.ResetAsync(_ =>
        {
            tokens.Token = "account-B-token";
            return Task.CompletedTask;
        });
        transport.Release();

        using var response = await oldRequest;

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(["Bearer old-token"], transport.AuthorizationValues);
        Assert.Equal(1, transport.RequestCount);
        Assert.Equal(0, recovery.RefreshCount);
        Assert.Equal(0, recovery.RequireSignInCount);
        Assert.Equal("account-B-token", tokens.Token);
    }

    [Theory]
    [InlineData("transport")]
    [InlineData("malformed")]
    public async Task Concurrent_401_responses_share_one_refresh_exception_and_later_request_can_retry(
        string failureKind)
    {
        Exception failure = failureKind == "transport"
            ? new HttpRequestException("refresh transport failed")
            : new TrackZ.Mobile.Features.Exercises.MobileApiException(
                TrackZ.Contracts.Errors.BusinessErrorCode.InternalServerError,
                "The server returned an invalid response.");
        var transport = new ConcurrentUnauthorizedHandler(expectedInitialRequests: 2);
        var recovery = new GatedExceptionRecovery(failure);
        using var client = ClientWithHandler(
            transport, recovery, new MutableTokenProvider(), ApiOrigin);

        var first = client.GetAsync("exercises?request=1");
        var second = client.GetAsync("exercises?request=2");
        await transport.InitialRequestsEntered.WaitAsync(TimeSpan.FromSeconds(2));
        transport.ReleaseInitialResponses();
        await recovery.RefreshEntered.WaitAsync(TimeSpan.FromSeconds(2));
        recovery.ReleaseRefresh();

        var firstError = await Record.ExceptionAsync(() => first);
        var secondError = await Record.ExceptionAsync(() => second);

        Assert.Same(failure, firstError);
        Assert.Same(failure, secondError);
        Assert.Equal(1, recovery.RefreshCount);
        Assert.Equal(2, transport.RequestCount);

        var laterError = await Record.ExceptionAsync(() =>
            client.GetAsync("exercises?request=later"));

        Assert.Same(failure, laterError);
        Assert.Equal(2, recovery.RefreshCount);
        Assert.Equal(3, transport.RequestCount);
    }

    [Fact]
    public async Task Leader_caller_cancellation_does_not_cancel_shared_refresh_for_live_waiter()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        var transport = new OrderedConcurrentUnauthorizedHandler();
        var tokens = new MutableTokenProvider();
        var recovery = new CancellationOwnershipRecovery(() => tokens.Token = "new-token");
        using var client = ClientWithHandler(transport, recovery, tokens, ApiOrigin, boundary);
        using var leaderCancellation = new CancellationTokenSource();
        using var waiterCancellation = new CancellationTokenSource();

        var leader = client.GetAsync("exercises?request=leader", leaderCancellation.Token);
        await transport.LeaderEntered.WaitAsync(TimeSpan.FromSeconds(2));
        transport.ReleaseLeaderResponse();
        await recovery.RefreshEntered.WaitAsync(TimeSpan.FromSeconds(2));

        var waiter = client.GetAsync("exercises?request=waiter", waiterCancellation.Token);
        await transport.WaiterEntered.WaitAsync(TimeSpan.FromSeconds(2));
        transport.ReleaseWaiterResponse();
        await transport.WaiterResponseReturned.WaitAsync(TimeSpan.FromSeconds(2));

        leaderCancellation.Cancel();
        recovery.InspectRefreshCancellation();
        await recovery.CancellationInspected.WaitAsync(TimeSpan.FromSeconds(2));
        recovery.ReleaseRefresh();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leader);
        using var waiterResponse = await waiter;

        Assert.False(recovery.RefreshCancellationWasRequested);
        Assert.Equal(HttpStatusCode.OK, waiterResponse.StatusCode);
        Assert.Equal(1, recovery.RefreshCount);
        Assert.Equal(0, recovery.RequireSignInCount);
        Assert.Equal(3, transport.RequestCount);
        Assert.Equal(generation, boundary.Capture());
        Assert.Equal("new-token", tokens.Token);
    }

    [Fact]
    public async Task Waiter_caller_cancellation_does_not_cancel_leader_or_shared_refresh()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        var transport = new OrderedConcurrentUnauthorizedHandler();
        var tokens = new MutableTokenProvider();
        var recovery = new CancellationOwnershipRecovery(() => tokens.Token = "new-token");
        using var client = ClientWithHandler(transport, recovery, tokens, ApiOrigin, boundary);
        using var leaderCancellation = new CancellationTokenSource();
        using var waiterCancellation = new CancellationTokenSource();

        var leader = client.GetAsync("exercises?request=leader", leaderCancellation.Token);
        await transport.LeaderEntered.WaitAsync(TimeSpan.FromSeconds(2));
        transport.ReleaseLeaderResponse();
        await recovery.RefreshEntered.WaitAsync(TimeSpan.FromSeconds(2));

        var waiter = client.GetAsync("exercises?request=waiter", waiterCancellation.Token);
        await transport.WaiterEntered.WaitAsync(TimeSpan.FromSeconds(2));
        transport.ReleaseWaiterResponse();
        await transport.WaiterResponseReturned.WaitAsync(TimeSpan.FromSeconds(2));

        waiterCancellation.Cancel();
        recovery.InspectRefreshCancellation();
        await recovery.CancellationInspected.WaitAsync(TimeSpan.FromSeconds(2));
        recovery.ReleaseRefresh();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        using var leaderResponse = await leader;

        Assert.False(recovery.RefreshCancellationWasRequested);
        Assert.Equal(HttpStatusCode.OK, leaderResponse.StatusCode);
        Assert.Equal(1, recovery.RefreshCount);
        Assert.Equal(0, recovery.RequireSignInCount);
        Assert.Equal(3, transport.RequestCount);
        Assert.Equal(generation, boundary.Capture());
        Assert.Equal("new-token", tokens.Token);
    }

    [Fact]
    public async Task Rejected_refresh_returns_the_original_401_with_problem_details_and_headers_untouched()
    {
        const string problemJson =
            "{\"code\":\"InvalidCredentials\",\"message\":\"Session expired.\",\"fieldErrors\":{\"email\":[\"Sign in again.\"]}}";
        var original = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(problemJson)
        };
        original.Headers.TryAddWithoutValidation("X-Auth-Reason", "expired");
        var transport = new ReturningResponseHandler(original);
        var recovery = new RecordingAuthenticationRecovery(succeeds: false);
        using var client = ClientWithHandler(
            transport, recovery, new MutableTokenProvider(), ApiOrigin);

        using var response = await client.GetAsync("exercises");

        Assert.Same(original, response);
        Assert.Equal("expired", Assert.Single(response.Headers.GetValues("X-Auth-Reason")));
        Assert.Equal(problemJson, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, recovery.RefreshCount);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved_without_canceling_shared_refresh()
    {
        var transport = new SequenceHandler(HttpStatusCode.Unauthorized);
        var recovery = new CancellationObservingRecovery();
        using var client = ClientWithHandler(transport, recovery, new RotatingTokenProvider(), ApiOrigin);
        using var cancellation = new CancellationTokenSource();

        var pending = client.GetAsync("exercises", cancellation.Token);
        await recovery.RefreshEntered.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(recovery.RefreshCancellationWasRequested);
        recovery.ReleaseRefresh();
        await recovery.RefreshCompleted.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task Replay_clones_safe_request_metadata_without_content()
    {
        var transport = new MetadataRecordingHandler();
        using var client = ClientWithHandler(
            transport, new RecordingAuthenticationRecovery(true), new RotatingTokenProvider(), ApiOrigin);
        using var request = new HttpRequestMessage(HttpMethod.Head, "exercises")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        request.Headers.TryAddWithoutValidation("X-Trace", "trace-value");
        request.Options.Set(new HttpRequestOptionsKey<string>("test-option"), "option-value");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, value =>
        {
            Assert.Equal(HttpMethod.Head, value.Method);
            Assert.Equal(HttpVersion.Version20, value.Version);
            Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, value.VersionPolicy);
            Assert.Equal("trace-value", value.Trace);
            Assert.Equal("option-value", value.Option);
            Assert.False(value.HadContent);
        });
    }

    [Fact]
    public async Task Identity_refresh_uses_raw_transport_and_cannot_recurse_through_protected_handler()
    {
        var rawTransport = new PathRecordingHandler(HttpStatusCode.Unauthorized);
        using var rawClient = new HttpClient(rawTransport) { BaseAddress = ApiOrigin };
        var storage = new MemoryTokenStorage();
        var tokenStore = new MobileTokenStore(storage);
        await tokenStore.SaveAsync(TestJwt(), "refresh-token");
        var refresh = new TrackZIdentityRefreshClient(rawClient, tokenStore, new AccountSessionBoundary());

        await Assert.ThrowsAsync<TrackZ.Mobile.Features.Exercises.MobileApiException>(() =>
            refresh.RefreshAsync("test-device"));

        Assert.Equal(1, rawTransport.RequestCount);
        Assert.Equal("/mobile/api/v1/auth/refresh", rawTransport.RequestUri!.AbsolutePath);
        Assert.Null(rawTransport.Authorization);
    }

    [Fact]
    public async Task Rejected_refresh_can_reset_its_generation_and_return_false_without_self_cancellation()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        var tokenStore = new MobileTokenStore(new MemoryTokenStorage());
        await tokenStore.SaveAsync(TestJwt(), "refresh-token");
        using var rawClient = new HttpClient(new ReturningResponseHandler(new HttpResponseMessage(
            HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """
                {"type":"https://trackz.test/problem","title":"Request failed","status":401,"errorCode":10003,"message":"Refresh token is invalid.","traceId":"trace-1","fieldErrors":null}
                """,
                System.Text.Encoding.UTF8,
                "application/json")
        })) { BaseAddress = ApiOrigin };
        var entryPoint = new ResettingAuthEntryPoint(boundary);
        var recovery = new ProtectedRequestAuthentication(
            new TrackZIdentityRefreshClient(rawClient, tokenStore, boundary),
            new StubDeviceNameProvider(),
            entryPoint,
            boundary);
        using var sessionCancellation = boundary.CreateCancellationLease(generation);

        var refreshed = await recovery.TryRefreshAsync(generation, sessionCancellation.Token);

        Assert.False(refreshed);
        Assert.Equal(1, entryPoint.CallCount);
        Assert.True(boundary.IsCancellationRequested(generation));
    }

    private static HttpClient ClientWithHandler(
        HttpMessageHandler transport,
        IProtectedRequestAuthentication recovery,
        IAccessTokenProvider tokenProvider,
        Uri apiOrigin,
        IAccountSessionBoundary? boundary = null)
    {
        var handler = new AuthenticatedApiHandler(
            tokenProvider,
            recovery,
            apiOrigin,
            boundary ?? new AccountSessionBoundary())
        {
            InnerHandler = transport
        };
        return new HttpClient(handler) { BaseAddress = apiOrigin };
    }

    private static string TestJwt()
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                $$"""{"sub":"{{Guid.Parse("11111111-1111-1111-1111-111111111111")}}","sid":"{{Guid.Parse("22222222-2222-2222-2222-222222222222")}}","exp":2000000000}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"header.{payload}.signature";
    }

    private sealed class RotatingTokenProvider : IAccessTokenProvider
    {
        private int _reads;
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(Interlocked.Increment(ref _reads) == 1 ? "old-token" : "new-token");
    }

    private sealed class MutableTokenProvider : IAccessTokenProvider
    {
        public string Token { get; set; } = "old-token";
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(Token);
    }

    private sealed class RecordingAuthenticationRecovery(bool succeeds) : IProtectedRequestAuthentication
    {
        public int RefreshCount { get; private set; }
        public int RequireSignInCount { get; private set; }

        public Task<bool> TryRefreshAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.FromResult(succeeds);
        }

        public Task RequireSignInAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken)
        {
            RequireSignInCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class GatedAuthenticationRecovery(Action onRefresh) : IProtectedRequestAuthentication
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RefreshEntered => _entered.Task;
        public int RefreshCount { get; private set; }

        public async Task<bool> TryRefreshAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken)
        {
            RefreshCount++;
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            onRefresh();
            return true;
        }

        public Task RequireSignInAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public void ReleaseRefresh() => _release.TrySetResult();
    }

    private sealed class CancellationObservingRecovery : IProtectedRequestAuthentication
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationToken _refreshCancellationToken;
        public Task RefreshEntered => _entered.Task;
        public Task RefreshCompleted => _completed.Task;
        public bool RefreshCancellationWasRequested => _refreshCancellationToken.IsCancellationRequested;

        public async Task<bool> TryRefreshAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken)
        {
            _refreshCancellationToken = cancellationToken;
            _entered.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                return false;
            }
            finally
            {
                _completed.TrySetResult();
            }
        }

        public Task RequireSignInAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void ReleaseRefresh() => _release.TrySetResult();
    }

    private sealed class GatedExceptionRecovery(Exception failure) : IProtectedRequestAuthentication
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RefreshEntered => _entered.Task;
        public int RefreshCount { get; private set; }

        public async Task<bool> TryRefreshAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken)
        {
            RefreshCount++;
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            throw failure;
        }

        public Task RequireSignInAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void ReleaseRefresh() => _release.TrySetResult();
    }

    private sealed class CancellationOwnershipRecovery(Action onRefresh) : IProtectedRequestAuthentication
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _inspect = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _inspected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RefreshEntered => _entered.Task;
        public Task CancellationInspected => _inspected.Task;
        public bool RefreshCancellationWasRequested { get; private set; }
        public int RefreshCount { get; private set; }
        public int RequireSignInCount { get; private set; }

        public async Task<bool> TryRefreshAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken)
        {
            RefreshCount++;
            _entered.TrySetResult();
            await _inspect.Task;
            RefreshCancellationWasRequested = cancellationToken.IsCancellationRequested;
            _inspected.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            onRefresh();
            return true;
        }

        public Task RequireSignInAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken)
        {
            RequireSignInCount++;
            return Task.CompletedTask;
        }

        public void InspectRefreshCancellation() => _inspect.TrySetResult();
        public void ReleaseRefresh() => _release.TrySetResult();
    }

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);
        public int RequestCount { get; private set; }
        public List<string?> AuthorizationValues { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            AuthorizationValues.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(_statuses.Dequeue()));
        }
    }

    private sealed class ReturningResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://evil.example/steal");
            return Task.FromResult(response);
        }
    }

    private sealed class PausedUnauthorizedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public int RequestCount { get; private set; }
        public List<string?> AuthorizationValues { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            AuthorizationValues.Add(request.Headers.Authorization?.ToString());
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class OrderedConcurrentUnauthorizedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _leaderEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseLeader = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _waiterEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _waiterReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;
        public Task LeaderEntered => _leaderEntered.Task;
        public Task WaiterEntered => _waiterEntered.Task;
        public Task WaiterResponseReturned => _waiterReturned.Task;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            if (request.Headers.Authorization?.Parameter != "old-token")
                return new HttpResponseMessage(HttpStatusCode.OK);

            if (request.RequestUri!.Query.Contains("request=leader", StringComparison.Ordinal))
            {
                _leaderEntered.TrySetResult();
                await _releaseLeader.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            _waiterEntered.TrySetResult();
            await _releaseWaiter.Task.WaitAsync(cancellationToken);
            _waiterReturned.TrySetResult();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        public void ReleaseLeaderResponse() => _releaseLeader.TrySetResult();
        public void ReleaseWaiterResponse() => _releaseWaiter.TrySetResult();
    }

    private sealed class ConcurrentUnauthorizedHandler(int expectedInitialRequests) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _allEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _initialCount;
        private int _requestCount;
        public Task InitialRequestsEntered => _allEntered.Task;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            if (request.Headers.Authorization?.Parameter == "old-token")
            {
                if (Interlocked.Increment(ref _initialCount) == expectedInitialRequests) _allEntered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public void ReleaseInitialResponses() => _release.TrySetResult();
    }

    private sealed class MetadataRecordingHandler : HttpMessageHandler
    {
        public List<RequestMetadata> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Options.TryGetValue(new HttpRequestOptionsKey<string>("test-option"), out var option);
            Requests.Add(new RequestMetadata(
                request.Method,
                request.Version,
                request.VersionPolicy,
                request.Headers.GetValues("X-Trace").Single(),
                option,
                request.Content is not null));
            return Task.FromResult(new HttpResponseMessage(
                Requests.Count == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK));
        }
    }

    private sealed record RequestMetadata(
        HttpMethod Method,
        Version Version,
        HttpVersionPolicy VersionPolicy,
        string Trace,
        string? Option,
        bool HadContent);

    private sealed class PathRecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    private sealed class MemoryTokenStorage : IMobileTokenStorage
    {
        private readonly Dictionary<string, string> _values = [];
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

    private sealed class StubDeviceNameProvider : IDeviceNameProvider
    {
        public string DeviceName => "test-device";
    }

    private sealed class ResettingAuthEntryPoint(IAccountSessionBoundary boundary) : IAuthEntryPoint
    {
        public int CallCount { get; private set; }

        public Task RequireSignInAsync(CancellationToken cancellationToken = default) =>
            RequireSignInAsync(boundary.Capture(), cancellationToken);

        public async Task RequireSignInAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            _ = await boundary.TryResetAsync(
                expectedGeneration,
                _ => Task.CompletedTask,
                cancellationToken);
        }
    }
}
