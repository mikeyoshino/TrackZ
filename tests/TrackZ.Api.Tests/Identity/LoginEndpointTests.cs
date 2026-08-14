using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;
using TrackZ.Contracts.Errors;
using TrackZ.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TrackZ.Api.Tests.Identity;

public sealed class LoginEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private TrackZApiFactory? _factory;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("trackz_api_tests")
            .WithUsername("trackz")
            .WithPassword("trackz_api_tests_only")
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
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", null);
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", null);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", null);
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", null);
    }

    [Fact]
    public async Task Register_persists_only_a_password_hash_and_login_returns_a_signed_token_pair()
    {
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "athlete@example.com",
            password = "ValidPassword!42"
        });

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal("application/json", register.Content.Headers.ContentType?.MediaType);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await database.Users.SingleAsync();
            Assert.NotEqual("ValidPassword!42", user.PasswordHash);
            Assert.DoesNotContain("ValidPassword!42", user.PasswordHash, StringComparison.Ordinal);
        }

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = " ATHLETE@EXAMPLE.COM ",
            password = "ValidPassword!42",
            deviceName = "ios"
        });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("application/json", login.Content.Headers.ContentType?.MediaType);
        var tokenPair = await login.Content.ReadFromJsonAsync<TokenPairResponse>();
        Assert.NotNull(tokenPair);
        Assert.False(string.IsNullOrWhiteSpace(tokenPair.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokenPair.RefreshToken));
        Assert.True(tokenPair.ExpiresAt > DateTimeOffset.UtcNow);

        using var jwtPayload = ReadJwtPayload(tokenPair.AccessToken);
        Assert.Equal("trackz-api", jwtPayload.RootElement.GetProperty("iss").GetString());
        Assert.Equal("trackz-mobile", jwtPayload.RootElement.GetProperty("aud").GetString());
        Assert.False(string.IsNullOrWhiteSpace(jwtPayload.RootElement.GetProperty("sub").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(jwtPayload.RootElement.GetProperty("jti").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(jwtPayload.RootElement.GetProperty("sid").GetString()));

        await using var tokenScope = _factory.Services.CreateAsyncScope();
        var tokenDatabase = tokenScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshToken = await tokenDatabase.RefreshTokens.SingleAsync();
        Assert.NotEqual(tokenPair.RefreshToken, refreshToken.TokenHash);
        Assert.DoesNotContain(tokenPair.RefreshToken, refreshToken.TokenHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_login_for_unknown_user_and_bad_password_has_the_same_problem_contract()
    {
        var registered = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "athlete@example.com",
            password = "ValidPassword!42"
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var unknown = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "unknown@example.com",
            password = "ValidPassword!42",
            deviceName = "ios"
        });
        var wrongPassword = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "athlete@example.com",
            password = "wrong-password",
            deviceName = "ios"
        });

        var unknownProblem = await unknown.Content.ReadFromJsonAsync<ApiProblemDetails>();
        var wrongPasswordProblem = await wrongPassword.Content.ReadFromJsonAsync<ApiProblemDetails>();
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", wrongPassword.Content.Headers.ContentType?.MediaType);
        Assert.Equal(10001, (int)unknownProblem!.ErrorCode);
        Assert.Equal(unknownProblem.ErrorCode, wrongPasswordProblem!.ErrorCode);
        Assert.Equal(unknownProblem.Message, wrongPasswordProblem.Message);
    }

    [Fact]
    public async Task Thai_accept_language_localizes_the_invalid_credentials_message_without_changing_the_code()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "unknown@example.com",
                password = "ValidPassword!42",
                deviceName = "ios"
            })
        };
        request.Headers.AcceptLanguage.ParseAdd("th-TH");

        var response = await _client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(10001, (int)problem!.ErrorCode);
        Assert.Equal("อีเมลหรือรหัสผ่านไม่ถูกต้อง", problem.Message);
    }

    [Fact]
    public async Task Register_rejects_normalized_duplicates_and_password_policy_with_problem_details()
    {
        var first = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "athlete@example.com",
            password = "ValidPassword!42"
        });
        var duplicate = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = " ATHLETE@EXAMPLE.COM ",
            password = "AnotherPassword!42"
        });
        var policy = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "new@example.com",
            password = "short!A1"
        });

        var duplicateProblem = await duplicate.Content.ReadFromJsonAsync<ApiProblemDetails>();
        var policyProblem = await policy.Content.ReadFromJsonAsync<ApiProblemDetails>();
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(10002, (int)duplicateProblem!.ErrorCode);
        Assert.Equal(HttpStatusCode.BadRequest, policy.StatusCode);
        Assert.Equal(10006, (int)policyProblem!.ErrorCode);
    }

    private sealed record TokenPairResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private static JsonDocument ReadJwtPayload(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
    }

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
