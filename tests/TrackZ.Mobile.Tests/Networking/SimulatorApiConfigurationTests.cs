using TrackZ.Mobile.Networking;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Mobile.Tests.Networking;

public sealed class SimulatorApiConfigurationTests
{
    [Fact]
    public void Resolve_uses_production_origins_when_no_override_exists()
    {
        var origins = MobileEndpointOrigins.Resolve(_ => null);

        Assert.Equal("https://api.trackz.app/", origins.ApiOrigin.AbsoluteUri);
        Assert.Equal("https://media.trackz.app/", origins.MediaOrigin.AbsoluteUri);
    }

    [Fact]
    public void Resolve_uses_the_local_api_for_api_and_media_when_only_api_is_overridden()
    {
        var origins = MobileEndpointOrigins.Resolve(name =>
            name == MobileEndpointOrigins.ApiOriginEnvironmentVariable
                ? "http://localhost:5042"
                : null);

        Assert.Equal("http://localhost:5042/", origins.ApiOrigin.AbsoluteUri);
        Assert.Equal(origins.ApiOrigin, origins.MediaOrigin);
    }

    [Theory]
    [InlineData("file:///tmp/trackz")]
    [InlineData("http://localhost:5042/api")]
    [InlineData("http://user:password@localhost:5042")]
    public void Resolve_rejects_an_unsafe_api_origin(string value)
    {
        Assert.Throws<InvalidOperationException>(() => MobileEndpointOrigins.Resolve(name =>
            name == MobileEndpointOrigins.ApiOriginEnvironmentVariable ? value : null));
    }

    [Fact]
    public async Task Development_provider_prefers_secure_storage_and_only_falls_back_to_the_supplied_token()
    {
        var stored = new StubAccessTokenProvider("stored-token");
        var provider = new DevelopmentAccessTokenProvider(stored, () => "simulator-token");

        Assert.Equal("stored-token", await provider.GetAccessTokenAsync());

        stored.Token = null;
        Assert.Equal("simulator-token", await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Development_provider_ignores_an_empty_fallback_token()
    {
        var provider = new DevelopmentAccessTokenProvider(
            new StubAccessTokenProvider(null),
            () => "   ");

        Assert.Null(await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Maui_composition_uses_the_simulator_api_origin_and_bearer_token()
    {
        using var app = MauiProgram.CreateMauiApp(
            services => services.AddSingleton<IMobileTokenStorage>(new EmptyTokenStorage()),
            name => name switch
            {
                MobileEndpointOrigins.ApiOriginEnvironmentVariable => "http://localhost:5042",
                DevelopmentAccessTokenProvider.AccessTokenEnvironmentVariable => "api-issued-token",
                _ => null
            });

        var client = app.Services.GetRequiredService<HttpClient>();
        var tokenProvider = app.Services.GetRequiredService<IAccessTokenProvider>();

        Assert.Equal("http://localhost:5042/", client.BaseAddress!.AbsoluteUri);
        Assert.Equal("api-issued-token", await tokenProvider.GetAccessTokenAsync());
    }

    private sealed class StubAccessTokenProvider(string? token) : IAccessTokenProvider
    {
        public string? Token { get; set; } = token;

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Token);
        }
    }

    private sealed class EmptyTokenStorage : IMobileTokenStorage
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
