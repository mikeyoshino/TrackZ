using System.Net;
using System.Net.Http.Json;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Sync;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Sync;

public sealed class SyncApiClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SyncApiFailureKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, SyncApiFailureKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, SyncApiFailureKind.Retryable)]
    [InlineData(HttpStatusCode.InternalServerError, SyncApiFailureKind.Retryable)]
    public async Task Http_statuses_are_typed_instead_of_collapsed_to_offline(
        HttpStatusCode status,
        SyncApiFailureKind expectedKind)
    {
        var client = Client(Response(status, status == HttpStatusCode.TooManyRequests
            ? BusinessErrorCode.RateLimitExceeded
            : BusinessErrorCode.InternalServerError));

        var failure = await Assert.ThrowsAsync<SyncApiException>(() =>
            client.PushAsync(new SyncPushRequest([])));

        Assert.Equal(expectedKind, failure.Kind);
        Assert.Equal(status, failure.StatusCode);
    }

    [Fact]
    public async Task Invalid_persisted_pull_cursor_is_distinct_from_a_permanent_push_rejection()
    {
        var pull = Client(Response(HttpStatusCode.BadRequest, BusinessErrorCode.InvalidRequest));
        var pullFailure = await Assert.ThrowsAsync<SyncApiException>(() => pull.PullAsync("tampered"));
        var push = Client(Response(HttpStatusCode.BadRequest, BusinessErrorCode.InvalidRequest));
        var pushFailure = await Assert.ThrowsAsync<SyncApiException>(() =>
            push.PushAsync(new SyncPushRequest([])));

        Assert.Equal(SyncApiFailureKind.InvalidCursor, pullFailure.Kind);
        Assert.Equal(SyncApiFailureKind.Permanent, pushFailure.Kind);
        Assert.Equal(BusinessErrorCode.InvalidRequest, pullFailure.ErrorCode);
    }

    private static TrackZSyncApiClient Client(HttpResponseMessage response) =>
        new(new HttpClient(new SingleResponseHandler(response))
        {
            BaseAddress = new Uri("https://trackz.invalid")
        });

    private static HttpResponseMessage Response(HttpStatusCode status, BusinessErrorCode code) => new(status)
    {
        Content = JsonContent.Create(new ApiProblemDetails(
            "about:blank", "failure", (int)status, code, "failure", "trace", null))
    };

    private sealed class SingleResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
