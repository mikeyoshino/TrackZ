using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TrackZ.Contracts.Errors;
using TrackZ.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TrackZ.Api.Tests.Identity;

public sealed class RefreshRotationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private TrackZApiFactory? _factory;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("trackz_refresh_tests")
            .WithUsername("trackz")
            .WithPassword("trackz_refresh_tests_only")
            .Build();

        try
        {
            await _container.StartAsync();
        }
        catch (DockerUnavailableException exception)
        {
            await _container.DisposeAsync();
            throw SkipException.ForSkip($"Docker is unavailable; API integration tests require Docker. {exception.Message}");
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Issuer", "trackz-api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "trackz-mobile");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-signing-key-that-is-at-least-thirty-two-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", "14");
        _factory = new TrackZApiFactory(_container.GetConnectionString());
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", null);
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", null);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", null);
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", null);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_returns_10003()
    {
        var original = await RegisterAndLoginAsync("replay@example.com");
        var rotated = await RefreshAsync(original.RefreshToken);
        var replay = await PostRefreshAsync(original.RefreshToken);

        var problem = await replay.Content.ReadFromJsonAsync<ApiProblemDetails>();
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(10003, (int)problem!.ErrorCode);
        Assert.NotEqual(original.RefreshToken, rotated.RefreshToken);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens
            .OrderBy(token => token.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].RevokedAt);
        Assert.Null(tokens[1].RevokedAt);
        Assert.All(tokens, token => Assert.Equal(TimeSpan.Zero, token.CreatedAt.Offset));
    }

    [Fact]
    public async Task Concurrent_refresh_requests_allow_only_one_rotation()
    {
        var original = await RegisterAndLoginAsync("concurrent@example.com");
        var responses = await Task.WhenAll(PostRefreshAsync(original.RefreshToken), PostRefreshAsync(original.RefreshToken));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        var problem = await rejected.Content.ReadFromJsonAsync<ApiProblemDetails>();
        Assert.Equal(10003, (int)problem!.ErrorCode);
    }

    [Fact]
    public async Task Logout_requires_a_valid_signed_token_and_only_revokes_its_own_session()
    {
        var first = await RegisterAndLoginAsync("first@example.com");
        var second = await RegisterAndLoginAsync("second@example.com");
        var firstSession = ReadSessionId(first.AccessToken);
        var secondSession = ReadSessionId(second.AccessToken);

        using var tampered = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/auth/sessions/{firstSession:D}");
        tampered.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TamperToken(first.AccessToken));
        var tamperedResponse = await _client.SendAsync(tampered);
        Assert.Equal(HttpStatusCode.Unauthorized, tamperedResponse.StatusCode);

        using var crossUser = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/auth/sessions/{secondSession:D}");
        crossUser.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        var crossUserResponse = await _client.SendAsync(crossUser);
        Assert.Equal(HttpStatusCode.NotFound, crossUserResponse.StatusCode);

        using var logout = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/auth/sessions/{firstSession:D}");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(logout)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostRefreshAsync(first.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostRefreshAsync(second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_logout_and_refresh_do_not_leave_a_usable_session_token()
    {
        var original = await RegisterAndLoginAsync("logout-race@example.com");
        var sessionId = ReadSessionId(original.AccessToken);
        using var logout = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/auth/sessions/{sessionId:D}");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", original.AccessToken);

        var operations = await Task.WhenAll(_client.SendAsync(logout), PostRefreshAsync(original.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, operations[0].StatusCode);
        Assert.True(operations[1].StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized);

        if (operations[1].StatusCode == HttpStatusCode.OK)
        {
            var replacement = (await operations[1].Content.ReadFromJsonAsync<TokenPairResponse>())!;
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostRefreshAsync(replacement.RefreshToken)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostRefreshAsync(original.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Identity_routes_are_rate_limited_without_echoing_credentials()
    {
        var responses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            responses.Add(await _client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                email = "rate@example.com",
                password = "SecretPassword!42",
                deviceName = "ios"
            }));
        }

        var limited = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.TooManyRequests);
        var body = await limited.Content.ReadAsStringAsync();
        var problem = await limited.Content.ReadFromJsonAsync<ApiProblemDetails>();
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.Equal(10008, (int)problem!.ErrorCode);
        Assert.DoesNotContain("rate@example.com", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretPassword!42", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_duplicate_registration_has_one_success_and_one_10002_response()
    {
        var request = new { email = "race@example.com", password = "ValidPassword!42" };
        var responses = await Task.WhenAll(
            _client.PostAsJsonAsync("/api/v1/auth/register", request),
            _client.PostAsJsonAsync("/api/v1/auth/register", request));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var duplicate = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var problem = await duplicate.Content.ReadFromJsonAsync<ApiProblemDetails>();
        Assert.Equal(10002, (int)problem!.ErrorCode);
    }

    private async Task<TokenPairResponse> RegisterAndLoginAsync(string email)
    {
        var registration = await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "ValidPassword!42" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "ValidPassword!42", deviceName = "ios" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<TokenPairResponse>())!;
    }

    private async Task<TokenPairResponse> RefreshAsync(string refreshToken)
    {
        var response = await PostRefreshAsync(refreshToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;
    }

    private Task<HttpResponseMessage> PostRefreshAsync(string refreshToken) =>
        _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken, deviceName = "ios" });

    private static Guid ReadSessionId(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
        using var document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
        return Guid.Parse(document.RootElement.GetProperty("sid").GetString()!);
    }

    private static string TamperToken(string token) => token[..^1] + (token[^1] == 'a' ? "b" : "a");

    private sealed record TokenPairResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed class TrackZApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TrackZ"] = connectionString,
                ["Jwt:Issuer"] = "trackz-api",
                ["Jwt:Audience"] = "trackz-mobile",
                ["Jwt:SigningKey"] = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "14"
            }));
        }
    }
}
