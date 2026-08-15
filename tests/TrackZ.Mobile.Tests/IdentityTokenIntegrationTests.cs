using System.Net;
using System.Text;
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
            new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") }, tokenStore, cleaner);

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

    private sealed class RecordingPrivateDataCleaner : IMobilePrivateDataCleaner
    {
        public int ClearCount { get; private set; }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private static string JwtWithSession(Guid sessionId)
    {
        static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode($"{{\"sub\":\"99999999-9999-9999-9999-999999999999\",\"sid\":\"{sessionId:D}\"}}")}.signature";
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

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
}
