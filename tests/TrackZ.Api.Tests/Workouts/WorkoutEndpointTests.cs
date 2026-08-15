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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TrackZ.Api.Tests.Workouts;

public sealed class WorkoutEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private WorkoutApiFactory? _factory;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("trackz_workout_api_tests")
            .WithUsername("trackz")
            .WithPassword("trackz_workout_api_tests_only")
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

        _factory = new WorkoutApiFactory(_container.GetConnectionString());
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
    public async Task Workout_routes_are_authorization_first()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/workouts?cursor=bad&pageSize=0")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync($"/api/v1/workouts/{Guid.NewGuid():D}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync($"/api/v1/exercises/{Guid.NewGuid():D}/history?cursor=bad")).StatusCode);
    }

    [Fact]
    public async Task Detail_returns_exact_order_and_foreign_or_unknown_are_indistinguishable()
    {
        var owner = await AuthenticateAsync($"detail-owner-{Guid.NewGuid():N}@example.com");
        var other = await AuthenticateAsync($"detail-other-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateSystem("Exact Press", BodyPart.Chest, TrackingMode.Weighted);
        var workout = CompletedWorkout(owner.UserId, exercise, DateTimeOffset.UtcNow,
            (60m, 10), (62.5m, 8), (65m, 6));
        await SeedAsync(exercise, workout);

        var detail = await SendAsync(owner.Token, $"/api/v1/workouts/{workout.Id:D}");
        var foreign = await SendAsync(other.Token, $"/api/v1/workouts/{workout.Id:D}", "th-TH");
        var unknown = await SendAsync(other.Token, $"/api/v1/workouts/{Guid.NewGuid():D}", "th-TH");
        var json = await detail.Content.ReadFromJsonAsync<JsonDocument>();
        var foreignProblem = await foreign.Content.ReadFromJsonAsync<ApiProblemDetails>();
        var unknownProblem = await unknown.Content.ReadFromJsonAsync<ApiProblemDetails>();

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal([60m, 62.5m, 65m], json!.RootElement.GetProperty("exercises")[0].GetProperty("sets").EnumerateArray().Select(set => set.GetProperty("weightKg").GetDecimal()));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(BusinessErrorCode.WorkoutNotFound, foreignProblem!.ErrorCode);
        Assert.Equal(foreignProblem.ErrorCode, unknownProblem!.ErrorCode);
        Assert.Equal(foreignProblem.Message, unknownProblem.Message);
        Assert.Equal("ไม่พบรายการออกกำลังกาย", unknownProblem.Message);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Equal_timestamp_history_pages_have_no_duplicates_and_tampered_cursor_is_localized()
    {
        var owner = await AuthenticateAsync($"paging-owner-{Guid.NewGuid():N}@example.com");
        var other = await AuthenticateAsync($"paging-other-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateSystem("Paging Press", BodyPart.Chest, TrackingMode.Weighted);
        var completedAt = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var workouts = Enumerable.Range(0, 3)
            .Select(_ => CompletedWorkout(owner.UserId, exercise, completedAt, (70m, 8)))
            .ToArray();
        await SeedAsync(exercise, workouts);

        var ids = new List<Guid>();
        string? cursor = null;
        string? firstCursor = null;
        do
        {
            var path = "/api/v1/workouts?pageSize=1" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await SendAsync(owner.Token, path);
            var page = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ids.Add(page!.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            firstCursor ??= cursor;
        } while (cursor is not null);

        Assert.Equal(3, ids.Count);
        Assert.Equal(3, ids.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(id => id), ids);
        Assert.NotNull(firstCursor);

        async Task AssertCursorRejectedAsync(string token, string path)
        {
            var rejected = await SendAsync(token, path);
            var rejectedProblem = await rejected.Content.ReadFromJsonAsync<ApiProblemDetails>();
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(BusinessErrorCode.InvalidRequest, rejectedProblem!.ErrorCode);
            Assert.True(rejectedProblem.FieldErrors!.ContainsKey("cursor"));
        }

        await AssertCursorRejectedAsync(other.Token,
            $"/api/v1/workouts?cursor={Uri.EscapeDataString(firstCursor!)}");
        await AssertCursorRejectedAsync(owner.Token,
            $"/api/v1/exercises/{exercise.Id:D}/history?cursor={Uri.EscapeDataString(firstCursor!)}");

        var exercisePage = await (await SendAsync(owner.Token,
            $"/api/v1/exercises/{exercise.Id:D}/history?pageSize=1")).Content.ReadFromJsonAsync<JsonDocument>();
        var exerciseCursor = exercisePage!.RootElement.GetProperty("nextCursor").GetString();
        Assert.NotNull(exerciseCursor);
        await AssertCursorRejectedAsync(owner.Token,
            $"/api/v1/exercises/{Guid.NewGuid():D}/history?cursor={Uri.EscapeDataString(exerciseCursor!)}");

        _factory!.Time.UtcNow = _factory.Time.UtcNow.AddMinutes(61);
        await AssertCursorRejectedAsync(owner.Token,
            $"/api/v1/workouts?cursor={Uri.EscapeDataString(firstCursor!)}");

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/workouts?cursor=not-a-cursor");
        invalidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        invalidRequest.Headers.AcceptLanguage.ParseAdd("th-TH");
        var invalid = await _client.SendAsync(invalidRequest);
        var problem = await invalid.Content.ReadFromJsonAsync<ApiProblemDetails>();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(BusinessErrorCode.InvalidRequest, problem!.ErrorCode);
        Assert.Equal("ข้อมูล cursor ไม่ถูกต้อง", Assert.Single(problem.FieldErrors!["cursor"]));
    }

    [Fact]
    public async Task Exercise_history_keeps_mode_values_and_counts_only_weighted_volume()
    {
        var owner = await AuthenticateAsync($"exercise-history-{Guid.NewGuid():N}@example.com");
        var weighted = ExerciseDefinition.CreateSystem("Volume Press", BodyPart.Chest, TrackingMode.Weighted);
        var bodyweight = ExerciseDefinition.CreateSystem("Pushup", BodyPart.Chest, TrackingMode.Bodyweight);
        var completedAt = DateTimeOffset.UtcNow;
        var weightedWorkout = CompletedWorkout(owner.UserId, weighted, completedAt, (70m, 10), (60m, 5));
        var bodyweightWorkout = CompletedBodyweightWorkout(owner.UserId, bodyweight, completedAt.AddMinutes(-1), 20);
        await SeedAsync([weighted, bodyweight], [weightedWorkout, bodyweightWorkout]);

        var weightedResponse = await SendAsync(owner.Token, $"/api/v1/exercises/{weighted.Id:D}/history");
        var weightedJson = await weightedResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var bodyweightResponse = await SendAsync(owner.Token, $"/api/v1/exercises/{bodyweight.Id:D}/history");
        var bodyweightJson = await bodyweightResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(1000m, weightedJson!.RootElement.GetProperty("items")[0].GetProperty("weightedVolumeKg").GetDecimal());
        Assert.Equal(0m, bodyweightJson!.RootElement.GetProperty("items")[0].GetProperty("weightedVolumeKg").GetDecimal());
        Assert.Equal(20, bodyweightJson.RootElement.GetProperty("items")[0].GetProperty("sets")[0].GetProperty("reps").GetInt32());
        Assert.Equal(JsonValueKind.Null, bodyweightJson.RootElement.GetProperty("items")[0].GetProperty("sets")[0].GetProperty("weightKg").ValueKind);
    }

    private async Task<(Guid UserId, string Token)> AuthenticateAsync(string email)
    {
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "ValidPassword!42" });
        var registration = await register.Content.ReadFromJsonAsync<RegistrationResponse>();
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "ValidPassword!42", deviceName = "workout-tests" });
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        return (registration!.UserId, token!.AccessToken);
    }

    private async Task<HttpResponseMessage> SendAsync(string token, string path, string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (language is not null) request.Headers.AcceptLanguage.ParseAdd(language);
        return await _client.SendAsync(request);
    }

    private async Task SeedAsync(ExerciseDefinition exercise, params WorkoutSession[] workouts) =>
        await SeedAsync([exercise], workouts);

    private async Task SeedAsync(IReadOnlyList<ExerciseDefinition> exercises, IReadOnlyList<WorkoutSession> workouts)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Exercises.AddRangeAsync(exercises);
        await database.WorkoutSessions.AddRangeAsync(workouts);
        await database.SaveChangesAsync();
    }

    private static WorkoutSession CompletedWorkout(Guid ownerId, ExerciseDefinition definition, DateTimeOffset completedAt, params (decimal WeightKg, int Reps)[] sets)
    {
        var workout = WorkoutSession.Start(ownerId, Guid.NewGuid(), completedAt.AddMinutes(-10));
        var itemId = Guid.NewGuid();
        workout.AddExercise(itemId, definition.Id, TrackingMode.Weighted, 0);
        for (var index = 0; index < sets.Length; index++)
            workout.CompleteSet(itemId, Guid.NewGuid(), new SetMeasurement(sets[index].WeightKg, null, sets[index].Reps), completedAt.AddMinutes(-sets.Length + index));
        workout.Complete(completedAt);
        return workout;
    }

    private static WorkoutSession CompletedBodyweightWorkout(Guid ownerId, ExerciseDefinition definition, DateTimeOffset completedAt, int reps)
    {
        var workout = WorkoutSession.Start(ownerId, Guid.NewGuid(), completedAt.AddMinutes(-10));
        var itemId = Guid.NewGuid();
        workout.AddExercise(itemId, definition.Id, TrackingMode.Bodyweight, 0);
        workout.CompleteSet(itemId, Guid.NewGuid(), new SetMeasurement(null, null, reps), completedAt.AddMinutes(-1));
        workout.Complete(completedAt);
        return workout;
    }

    private sealed record RegistrationResponse(Guid UserId, string Email);
    private sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed class WorkoutApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public MutableTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

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
            }))
            .ConfigureServices(services =>
            {
                services.RemoveAll<IObjectStorage>();
                services.AddSingleton<IObjectStorage, NoOpStorage>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Time);
                services.ReplaceStagingLifecycleWithNoOpForTests();
            });
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class NoOpStorage : IObjectStorage
    {
        public Task<ObjectStorageObject?> GetAsync(string ownerPrefix, string key, CancellationToken cancellationToken) => Task.FromResult<ObjectStorageObject?>(null);
        public Task PutAsync(string ownerPrefix, string key, Stream content, string contentType, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string ownerPrefix, string key, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
