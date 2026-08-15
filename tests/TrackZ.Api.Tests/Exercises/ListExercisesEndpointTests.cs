using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;
using TrackZ.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TrackZ.Api.Tests.Exercises;

public sealed class ListExercisesEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private TrackZApiFactory? _factory;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("trackz_exercise_api_tests")
            .WithUsername("trackz")
            .WithPassword("trackz_exercise_api_tests_only")
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
    public async Task Authenticated_list_returns_system_and_owners_custom_exercises_with_performance_only()
    {
        var account = await AuthenticateAsync("catalog-owner@example.com");
        var system = ExerciseDefinition.CreateSystem("Incline Barbell Bench Press", BodyPart.Chest, TrackingMode.Weighted);
        var mine = ExerciseDefinition.CreateCustom(account.UserId, "My Cable Press", BodyPart.Chest, TrackingMode.Weighted);
        var other = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "Other Cable Press", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.Exercises.AddRangeAsync(system, mine, other);
            await database.ExercisePerformances.AddAsync(ExercisePerformance.Create(
                account.UserId, system.Id, TrackingMode.Weighted, new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero),
                new ExercisePerformanceSet(70m, null, 8), new ExercisePerformanceSet(75m, null, 5)));
            await database.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?bodyPart=Chest");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = json!.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, item => item.GetProperty("name").GetString() == "Other Cable Press");
        var press = Assert.Single(items, item => item.GetProperty("id").GetGuid() == system.Id);
        Assert.Equal(70m, press.GetProperty("lastBestSet").GetProperty("weightKg").GetDecimal());
        Assert.Equal(75m, press.GetProperty("allTimeBest").GetProperty("weightKg").GetDecimal());
        Assert.Equal("2026-08-14T09:00:00+00:00", press.GetProperty("lastPerformedAt").GetString());
    }

    [Fact]
    public async Task List_normalizes_search_applies_filters_and_uses_an_opaque_cursor_without_duplicate_equal_names()
    {
        var account = await AuthenticateAsync("catalog-pagination@example.com");
        var first = ExerciseDefinition.CreateSystem("Same Name", BodyPart.Chest, TrackingMode.Weighted);
        var second = ExerciseDefinition.CreateSystem("Same Name", BodyPart.Chest, TrackingMode.Weighted);
        var third = ExerciseDefinition.CreateSystem("Shoulder Press", BodyPart.Shoulders, TrackingMode.Weighted);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.Exercises.AddRangeAsync(first, second, third);
            await database.SaveChangesAsync();
        }

        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?bodyPart=Chest&search=%20same%20&pageSize=1");
        firstRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var firstResponse = await _client.SendAsync(firstRequest);
        var firstPage = await firstResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var cursor = firstPage!.RootElement.GetProperty("nextCursor").GetString();

        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/exercises?bodyPart=1&search=SAME&pageSize=1&cursor={Uri.EscapeDataString(cursor!)}");
        secondRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var secondResponse = await _client.SendAsync(secondRequest);
        var secondPage = await secondResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Contains('.', cursor!);
        Assert.NotEqual(firstPage.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid(), secondPage!.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Null(secondPage.RootElement.GetProperty("nextCursor").GetString());
    }

    [Theory]
    [InlineData("/api/v1/exercises?bodyPart=Nope")]
    [InlineData("/api/v1/exercises?pageSize=0")]
    [InlineData("/api/v1/exercises?cursor=not-a-cursor")]
    public async Task Invalid_list_parameters_return_localized_stable_validation_problem(string path)
    {
        var account = await AuthenticateAsync($"catalog-invalid-{Guid.NewGuid():N}@example.com");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        request.Headers.AcceptLanguage.ParseAdd("th-TH");

        var response = await _client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<TrackZ.Contracts.Errors.ApiProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10009, (int)problem!.ErrorCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Positive_page_size_above_the_limit_is_capped_at_fifty()
    {
        var account = await AuthenticateAsync("catalog-page-cap@example.com");
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.Exercises.AddRangeAsync(Enumerable.Range(1, 51)
                .Select(index => ExerciseDefinition.CreateSystem($"Cap exercise {index:D2}", BodyPart.Chest, TrackingMode.Weighted)));
            await database.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?pageSize=999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var response = await _client.SendAsync(request);
        var page = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(50, page!.RootElement.GetProperty("items").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(page.RootElement.GetProperty("nextCursor").GetString()));
    }

    [Fact]
    public async Task Unauthenticated_list_is_rejected()
    {
        var response = await _client.GetAsync("/api/v1/exercises");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_cursor_returns_thai_validation_field_error_without_internal_details()
    {
        var account = await AuthenticateAsync("catalog-cursor-thai@example.com");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?cursor=not-a-cursor");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        request.Headers.AcceptLanguage.ParseAdd("th-TH");

        var response = await _client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<TrackZ.Contracts.Errors.ApiProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10009, (int)problem!.ErrorCode);
        Assert.Equal("ข้อมูลคำขอไม่ถูกต้อง", problem.Message);
        Assert.Equal("ข้อมูล cursor ไม่ถูกต้อง", Assert.Single(problem.FieldErrors!["cursor"]));
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<(Guid UserId, string Token)> AuthenticateAsync(string email)
    {
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "ValidPassword!42" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registration = await register.Content.ReadFromJsonAsync<RegistrationResponse>();
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "ValidPassword!42", deviceName = "catalog-tests" });
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        return (registration!.UserId, token!.AccessToken);
    }

    private sealed record RegistrationResponse(Guid UserId, string Email);
    private sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed class TrackZApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
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
