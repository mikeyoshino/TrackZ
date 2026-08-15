using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Infrastructure.Persistence.Seed;
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
    public async Task Explicit_catalog_deployment_command_makes_all_48_draft_definitions_listable_without_thumbnails()
    {
        var account = await AuthenticateAsync($"catalog-deployment-{Guid.NewGuid():N}@example.com");
        await using (var before = _factory!.Services.CreateAsyncScope())
        {
            Assert.Empty(await before.ServiceProvider.GetRequiredService<AppDbContext>()
                .Exercises.ToArrayAsync());
        }
        await new ExerciseCatalogDeploymentCommand(CatalogPath)
            .ExecuteAsync(_factory!.Services);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?pageSize=50");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var response = await _client.SendAsync(request);
        var page = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = page!.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(48, items.Length);
        Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("thumbnailUrl").ValueKind));
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
        await using var verify = _factory.Services.CreateAsyncScope();
        var database = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(48, await database.ExerciseImages.CountAsync());
        Assert.All(await database.ExerciseImages.ToArrayAsync(), image =>
            Assert.Equal(ExerciseImageReviewState.Draft, image.ReviewState));
        Assert.Equal(96, verify.ServiceProvider.GetRequiredService<FakeObjectStorage>().Count);
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
    public async Task List_exposes_only_the_latest_ready_custom_thumbnail_as_an_opaque_media_route()
    {
        var account = await AuthenticateAsync("catalog-thumbnail@example.com");
        var custom = ExerciseDefinition.CreateCustom(account.UserId, "My image exercise", BodyPart.Chest, TrackingMode.Weighted);
        var first = ExerciseImage.CreateCustomUpload(custom, account.UserId, "private/not-for-client/first-master.jpg", "private/not-for-client/first-thumbnail.jpg", 1, "validated-upload");
        var latest = ExerciseImage.CreateCustomUpload(custom, account.UserId, "private/not-for-client/latest-master.jpg", "private/not-for-client/latest-thumbnail.jpg", 2, "validated-upload");
        var system = ExerciseDefinition.CreateSystem("Draft artwork", BodyPart.Chest, TrackingMode.Weighted);
        var draft = ExerciseImage.CreateSystem(system, "system/master.jpg", "system/thumbnail.jpg", 1, "generated");
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.Exercises.AddRangeAsync(custom, system);
            await database.ExerciseImages.AddRangeAsync(first, latest, draft);
            await database.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var page = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<JsonDocument>();

        var customItem = Assert.Single(page!.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == custom.Id);
        Assert.Equal($"/api/v1/media/exercise-images/{latest.Id:D}/thumbnail", customItem.GetProperty("thumbnailUrl").GetString());
        Assert.DoesNotContain("private/", customItem.GetRawText(), StringComparison.Ordinal);
        var systemItem = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == system.Id);
        Assert.Equal(JsonValueKind.Null, systemItem.GetProperty("thumbnailUrl").ValueKind);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tampered_or_unsupported_signed_cursor_returns_cursor_field_problem(bool unsupportedVersion)
    {
        var account = await AuthenticateAsync($"catalog-signed-cursor-{unsupportedVersion}@example.com");
        var cursor = unsupportedVersion
            ? SignedCursor(2, "Alpha", Guid.NewGuid())
            : SignedCursor(1, "Alpha", Guid.NewGuid()) + "A";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/exercises?cursor={Uri.EscapeDataString(cursor)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var response = await _client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<TrackZ.Contracts.Errors.ApiProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10009, (int)problem!.ErrorCode);
        Assert.NotNull(problem.FieldErrors);
        Assert.True(problem.FieldErrors!.ContainsKey("cursor"));
    }

    [Fact]
    public async Task Traverses_equal_names_without_duplicates_or_skips()
    {
        var account = await AuthenticateAsync("catalog-equal-traversal@example.com");
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddRangeAsync(Enumerable.Range(0, 7).Select(_ => ExerciseDefinition.CreateSystem("Equal Name", BodyPart.Chest, TrackingMode.Weighted)));
            await db.Exercises.AddRangeAsync(ExerciseDefinition.CreateSystem("Before", BodyPart.Chest, TrackingMode.Weighted), ExerciseDefinition.CreateSystem("Zulu", BodyPart.Chest, TrackingMode.Weighted));
            await db.SaveChangesAsync();
        }
        var ids = new List<Guid>();
        string? cursor = null;
        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?pageSize=2" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
            var page = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<JsonDocument>();
            ids.AddRange(page!.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
        } while (cursor is not null);

        Assert.Equal(9, ids.Count);
        Assert.Equal(9, ids.Distinct().Count());
    }

    [Fact]
    public async Task List_excludes_archived_and_other_users_custom_exercises()
    {
        var account = await AuthenticateAsync("catalog-archive-owner@example.com");
        var system = ExerciseDefinition.CreateSystem("Visible system", BodyPart.Chest, TrackingMode.Weighted);
        var archivedSystem = ExerciseDefinition.CreateSystem("Archived system", BodyPart.Chest, TrackingMode.Weighted);
        var mine = ExerciseDefinition.CreateCustom(account.UserId, "Visible mine", BodyPart.Chest, TrackingMode.Weighted);
        var archivedMine = ExerciseDefinition.CreateCustom(account.UserId, "Archived mine", BodyPart.Chest, TrackingMode.Weighted); archivedMine.Archive();
        var other = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "Other user", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddRangeAsync(system, archivedSystem, mine, archivedMine, other);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE exercise_definitions SET \"IsArchived\" = TRUE WHERE \"Id\" = {archivedSystem.Id}");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?bodyPart=Chest"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var page = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<JsonDocument>();
        var names = page!.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();

        Assert.Contains("Visible system", names); Assert.Contains("Visible mine", names);
        Assert.DoesNotContain("Archived system", names); Assert.DoesNotContain("Archived mine", names); Assert.DoesNotContain("Other user", names);
    }

    [Fact]
    public async Task List_serializes_mode_correct_performance_shapes_and_null_for_no_performance()
    {
        var account = await AuthenticateAsync("catalog-mode-shapes@example.com");
        var weighted = ExerciseDefinition.CreateSystem("Weighted shape", BodyPart.Chest, TrackingMode.Weighted);
        var bodyweight = ExerciseDefinition.CreateSystem("Bodyweight shape", BodyPart.Core, TrackingMode.Bodyweight);
        var assisted = ExerciseDefinition.CreateSystem("Assisted shape", BodyPart.Back, TrackingMode.Assisted);
        var none = ExerciseDefinition.CreateSystem("No performance", BodyPart.Arms, TrackingMode.Weighted);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Exercises.AddRangeAsync(weighted, bodyweight, assisted, none);
            await db.ExercisePerformances.AddRangeAsync(
                ExercisePerformance.Create(account.UserId, weighted.Id, TrackingMode.Weighted, DateTimeOffset.UtcNow, new ExercisePerformanceSet(70m, null, 8), new ExercisePerformanceSet(75m, null, 5)),
                ExercisePerformance.Create(account.UserId, bodyweight.Id, TrackingMode.Bodyweight, DateTimeOffset.UtcNow, new ExercisePerformanceSet(null, null, 12), new ExercisePerformanceSet(null, null, 15)),
                ExercisePerformance.Create(account.UserId, assisted.Id, TrackingMode.Assisted, DateTimeOffset.UtcNow, new ExercisePerformanceSet(null, 25m, 10), new ExercisePerformanceSet(null, 20m, 12)));
            await db.SaveChangesAsync();
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?pageSize=50"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var page = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<JsonDocument>();
        var items = page!.RootElement.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal(70m, items["Weighted shape"].GetProperty("lastBestSet").GetProperty("weightKg").GetDecimal()); Assert.Equal(JsonValueKind.Null, items["Weighted shape"].GetProperty("lastBestSet").GetProperty("assistedKg").ValueKind);
        Assert.Equal(JsonValueKind.Null, items["Bodyweight shape"].GetProperty("lastBestSet").GetProperty("weightKg").ValueKind); Assert.Equal(JsonValueKind.Null, items["Bodyweight shape"].GetProperty("lastBestSet").GetProperty("assistedKg").ValueKind); Assert.Equal(12, items["Bodyweight shape"].GetProperty("lastBestSet").GetProperty("reps").GetInt32());
        Assert.Equal(JsonValueKind.Null, items["Bodyweight shape"].GetProperty("allTimeBest").GetProperty("weightKg").ValueKind); Assert.Equal(JsonValueKind.Null, items["Bodyweight shape"].GetProperty("allTimeBest").GetProperty("assistedKg").ValueKind); Assert.Equal(15, items["Bodyweight shape"].GetProperty("allTimeBest").GetProperty("reps").GetInt32());
        Assert.Equal(JsonValueKind.Null, items["Assisted shape"].GetProperty("lastBestSet").GetProperty("weightKg").ValueKind); Assert.Equal(25m, items["Assisted shape"].GetProperty("lastBestSet").GetProperty("assistedKg").GetDecimal()); Assert.Equal(JsonValueKind.Null, items["Assisted shape"].GetProperty("allTimeBest").GetProperty("weightKg").ValueKind); Assert.Equal(20m, items["Assisted shape"].GetProperty("allTimeBest").GetProperty("assistedKg").GetDecimal()); Assert.Equal(12, items["Assisted shape"].GetProperty("allTimeBest").GetProperty("reps").GetInt32());
        Assert.Equal(JsonValueKind.Null, items["No performance"].GetProperty("lastPerformedAt").ValueKind); Assert.Equal(JsonValueKind.Null, items["No performance"].GetProperty("lastBestSet").ValueKind); Assert.Equal(JsonValueKind.Null, items["No performance"].GetProperty("allTimeBest").ValueKind);
    }

    [Fact]
    public async Task List_never_serializes_image_object_keys_or_thumbnail_urls()
    {
        var account = await AuthenticateAsync("catalog-image-nonleak@example.com");
        var systemDraft = ExerciseDefinition.CreateSystem("Draft image", BodyPart.Chest, TrackingMode.Weighted);
        var systemPublished = ExerciseDefinition.CreateSystem("Published image", BodyPart.Chest, TrackingMode.Weighted);
        var custom = ExerciseDefinition.CreateCustom(account.UserId, "Private image", BodyPart.Chest, TrackingMode.Weighted);
        var draft = ExerciseImage.CreateSystem(systemDraft, "master-draft-secret", "thumb-draft-secret", 1, "generated");
        var published = ExerciseImage.CreateSystem(systemPublished, "master-published-secret", "thumb-published-secret", 1, "generated");
        published.Review(Guid.NewGuid(), "rights-approved", true, true, true, DateTimeOffset.UtcNow); published.Publish(DateTimeOffset.UtcNow.AddMinutes(1));
        var privateImage = ExerciseImage.CreateCustomUpload(custom, account.UserId, "master-private-secret", "thumb-private-secret", 1, "camera");
        await using (var scope = _factory!.Services.CreateAsyncScope())
        { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Exercises.AddRangeAsync(systemDraft, systemPublished, custom); await db.ExerciseImages.AddRangeAsync(draft, published, privateImage); await db.SaveChangesAsync(); }
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?bodyPart=Chest"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        var response = await _client.SendAsync(request); var raw = await response.Content.ReadAsStringAsync(); using var page = JsonDocument.Parse(raw);
        var items = page.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(JsonValueKind.Null, Assert.Single(items, item => item.GetProperty("name").GetString() == "Draft image").GetProperty("thumbnailUrl").ValueKind);
        Assert.StartsWith("/api/v1/media/exercise-images/", Assert.Single(items, item => item.GetProperty("name").GetString() == "Published image").GetProperty("thumbnailUrl").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("/api/v1/media/exercise-images/", Assert.Single(items, item => item.GetProperty("name").GetString() == "Private image").GetProperty("thumbnailUrl").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("master-draft-secret", raw); Assert.DoesNotContain("thumb-draft-secret", raw); Assert.DoesNotContain("master-published-secret", raw); Assert.DoesNotContain("thumb-published-secret", raw); Assert.DoesNotContain("master-private-secret", raw); Assert.DoesNotContain("thumb-private-secret", raw);
    }

    [Fact]
    public async Task Create_custom_exercise_returns_created_and_is_visible_only_to_its_owner()
    {
        var owner = await AuthenticateAsync("custom-owner@example.com");
        var other = await AuthenticateAsync("custom-other@example.com");
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exercises/custom")
        {
            Content = JsonContent.Create(new { name = "  My Press  ", bodyPart = 1, trackingMode = 1, ownerId = other.UserId, operationId = Guid.NewGuid() })
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);

        var created = await _client.SendAsync(create);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonDocument>();
        var id = createdBody!.RootElement.GetProperty("id").GetGuid();

        using var ownerList = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?search=My%20Press");
        ownerList.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        using var otherList = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?search=My%20Press");
        otherList.Headers.Authorization = new AuthenticationHeaderValue("Bearer", other.Token);
        var ownerPage = await (await _client.SendAsync(ownerList)).Content.ReadFromJsonAsync<JsonDocument>();
        var otherPage = await (await _client.SendAsync(otherList)).Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/v1/exercises/custom/{id:D}", created.Headers.Location!.OriginalString);
        Assert.Contains(ownerPage!.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id && item.GetProperty("name").GetString() == "My Press");
        Assert.DoesNotContain(otherPage!.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Custom_requests_validate_canonical_fields_and_are_authorization_first_for_malformed_bodies()
    {
        using var unauthenticatedMalformed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exercises/custom")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        };
        var unauthorized = await _client.SendAsync(unauthenticatedMalformed);

        var account = await AuthenticateAsync("custom-validation@example.com");
        using var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exercises/custom")
        {
            Content = JsonContent.Create(new { name = "", bodyPart = 99, trackingMode = 99 })
        };
        invalid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        invalid.Headers.AcceptLanguage.ParseAdd("th-TH");
        var invalidResponse = await _client.SendAsync(invalid);
        var problem = await invalidResponse.Content.ReadFromJsonAsync<TrackZ.Contracts.Errors.ApiProblemDetails>();

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("application/problem+json", invalidResponse.Content.Headers.ContentType!.MediaType);
        Assert.Equal(10009, (int)problem!.ErrorCode);
        Assert.Equal("ข้อมูลคำขอไม่ถูกต้อง", problem.Message);
        Assert.Equal(["bodyPart", "name", "operationId", "trackingMode"], problem.FieldErrors!.Keys.OrderBy(key => key));
        Assert.All(problem.FieldErrors.Values, value => Assert.Single(value));
    }

    [Fact]
    public async Task Custom_routes_hide_foreign_and_archived_exercises_and_keep_archived_rows_persisted()
    {
        var owner = await AuthenticateAsync("custom-route-owner@example.com");
        var other = await AuthenticateAsync("custom-route-other@example.com");
        var exercise = ExerciseDefinition.CreateCustom(owner.UserId, "My Press", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }

        using var foreignUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/exercises/custom/{exercise.Id:D}")
        {
            Content = JsonContent.Create(new { name = "Nope", bodyPart = 2 })
        };
        foreignUpdate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", other.Token);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/exercises/custom/{exercise.Id:D}");
        delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        var foreign = await _client.SendAsync(foreignUpdate);
        var archived = await _client.SendAsync(delete);
        using var repeatDelete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/exercises/custom/{exercise.Id:D}");
        repeatDelete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        var repeated = await _client.SendAsync(repeatDelete);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var persisted = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().Exercises.FindAsync(exercise.Id);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsArchived);
    }

    [Fact]
    public async Task Custom_tracking_mode_is_immutable_after_performance_history_but_other_fields_remain_updatable()
    {
        var owner = await AuthenticateAsync("custom-history@example.com");
        var exercise = ExerciseDefinition.CreateCustom(owner.UserId, "History Press", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.ExercisePerformances.AddAsync(ExercisePerformance.Create(
                owner.UserId, exercise.Id, TrackingMode.Weighted, DateTimeOffset.UtcNow,
                new ExercisePerformanceSet(60m, null, 8), new ExercisePerformanceSet(70m, null, 5)));
            await db.SaveChangesAsync();
        }

        using var rejected = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/exercises/custom/{exercise.Id:D}")
        {
            Content = JsonContent.Create(new { name = "Rejected", bodyPart = 2, trackingMode = 2 })
        };
        rejected.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        var rejection = await _client.SendAsync(rejected);
        var rejectionProblem = await rejection.Content.ReadFromJsonAsync<TrackZ.Contracts.Errors.ApiProblemDetails>();

        using var allowed = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/exercises/custom/{exercise.Id:D}")
        {
            Content = JsonContent.Create(new { name = "  Renamed After History  ", bodyPart = 2 })
        };
        allowed.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        var success = await _client.SendAsync(allowed);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var persisted = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().Exercises.FindAsync(exercise.Id);
        Assert.Equal(HttpStatusCode.BadRequest, rejection.StatusCode);
        Assert.Equal(10009, (int)rejectionProblem!.ErrorCode);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.Equal("Renamed After History", persisted!.Name);
        Assert.Equal(BodyPart.Back, persisted.BodyPart);
        Assert.Equal(TrackingMode.Weighted, persisted.TrackingMode);
        Assert.True(persisted.HasSetHistory);
    }

    [Fact]
    public async Task Custom_tracking_mode_changes_before_any_history_exists()
    {
        var owner = await AuthenticateAsync("custom-prehistory-mode@example.com");
        var exercise = ExerciseDefinition.CreateCustom(owner.UserId, "Mode Press", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }

        using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/exercises/custom/{exercise.Id:D}")
        {
            Content = JsonContent.Create(new { name = "Mode Press", bodyPart = 1, trackingMode = 2 })
        };
        update.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);

        var response = await _client.SendAsync(update);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var persisted = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().Exercises.FindAsync(exercise.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(TrackingMode.Bodyweight, persisted!.TrackingMode);
    }

    [Fact]
    public async Task Concurrent_custom_creates_return_one_created_and_one_localized_duplicate_problem()
    {
        var owner = await AuthenticateAsync("custom-concurrency@example.com");
        var first = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exercises/custom")
        {
            Content = JsonContent.Create(new { name = "Concurrent Press", bodyPart = 1, trackingMode = 1, operationId = Guid.NewGuid() })
        };
        first.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        first.Headers.AcceptLanguage.ParseAdd("th-TH");
        var second = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exercises/custom")
        {
            Content = JsonContent.Create(new { name = "concurrent press", bodyPart = 1, trackingMode = 1, operationId = Guid.NewGuid() })
        };
        second.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        second.Headers.AcceptLanguage.ParseAdd("th-TH");

        var responses = await Task.WhenAll(_client.SendAsync(first), _client.SendAsync(second));
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var problem = await conflict.Content.ReadFromJsonAsync<TrackZ.Contracts.Errors.ApiProblemDetails>();

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(20002, (int)problem!.ErrorCode);
        Assert.Equal("มีท่าออกกำลังกายแบบกำหนดเองที่ใช้งานอยู่ชื่อนี้แล้ว", problem.Message);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Custom_create_is_idempotent_and_accepts_only_published_library_artwork()
    {
        var owner = await AuthenticateAsync($"custom-library-{Guid.NewGuid():N}@example.com");
        var system = ExerciseDefinition.CreateSystem("Published Art", BodyPart.Chest, TrackingMode.Weighted);
        var published = ExerciseImage.CreateSystem(system, "system/published/master.png", "system/published/thumb.png", 1, "source");
        published.Review(Guid.NewGuid(), "rights", true, true, true, DateTimeOffset.UtcNow);
        published.Publish(DateTimeOffset.UtcNow.AddSeconds(1));
        var draft = ExerciseImage.CreateSystem(system, "system/draft/master.png", "system/draft/thumb.png", 2, "draft");
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(system);
            await db.ExerciseImages.AddRangeAsync(published, draft);
            await db.SaveChangesAsync();
        }
        var operationId = Guid.NewGuid();

        async Task<HttpResponseMessage> CreateAsync(Guid libraryImageId, Guid requestOperationId, string name) => await _client.SendAsync(new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/exercises/custom")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", owner.Token) },
            Content = JsonContent.Create(new
            {
                name,
                bodyPart = 1,
                trackingMode = 1,
                operationId = requestOperationId,
                libraryImageId
            })
        });

        var created = await CreateAsync(published.Id, operationId, "Library Custom Press");
        var replay = await CreateAsync(published.Id, operationId, "Library Custom Press");
        var firstId = (await created.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetGuid();
        var replayId = (await replay.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetGuid();
        var rejectedDraft = await CreateAsync(draft.Id, Guid.NewGuid(), "Draft Custom Press");

        await using (var uploadScope = _factory.Services.CreateAsyncScope())
        {
            var db = uploadScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var custom = await db.Exercises.SingleAsync(exercise => exercise.Id == firstId);
            await db.ExerciseImages.AddAsync(ExerciseImage.CreateCustomUpload(
                custom, owner.UserId,
                $"private/{owner.UserId:D}/master.jpg",
                $"private/{owner.UserId:D}/thumbnail.jpg", 1, "upload"));
            await db.SaveChangesAsync();
        }

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises?bodyPart=Chest");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        var catalog = await (await _client.SendAsync(list)).Content.ReadFromJsonAsync<JsonDocument>();

        await using var verify = _factory.Services.CreateAsyncScope();
        var rows = await verify.ServiceProvider.GetRequiredService<AppDbContext>().Exercises
            .Where(exercise => exercise.OwnerId == owner.UserId && exercise.ClientOperationId == operationId)
            .ToListAsync();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(firstId, replayId);
        Assert.Equal(published.Id, Assert.Single(rows).LibraryImageId);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedDraft.StatusCode);
        var catalogItems = catalog!.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(catalogItems, item => item.GetProperty("id").GetGuid() == system.Id
            && item.GetProperty("libraryImageId").GetGuid() == published.Id);
        Assert.Contains(catalogItems, item => item.GetProperty("id").GetGuid() == firstId
            && item.GetProperty("thumbnailUrl").GetString()!.Contains(published.Id.ToString("D"), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("$.NAME", "name")]
    [InlineData("$.BoDyPaRt", "bodyPart")]
    [InlineData("$.TRACKINGmode", "trackingMode")]
    [InlineData("$.LibraryIMAGEid", "libraryImageId")]
    [InlineData("$.uploadedIMAGEkey", "uploadedImageKey")]
    [InlineData("$.name.value", "body")]
    [InlineData("$.unknown", "body")]
    [InlineData("$.uploadedImageKey[0]", "body")]
    [InlineData("$['name']", "body")]
    public void Custom_json_paths_canonicalize_only_known_root_properties(string path, string expectedField)
    {
        var mapper = typeof(TrackZ.Api.Endpoints.ExerciseEndpoints).GetMethod(
            "MapCustomJsonPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var actual = (string)mapper.Invoke(null, [path])!;

        Assert.Equal(expectedField, actual);
    }

    [Theory]
    [InlineData("{\"NAME\":{},\"bodyPart\":1,\"trackingMode\":1}", "name")]
    [InlineData("{\"name\":\"Press\",\"BODYPART\":{},\"trackingMode\":1}", "bodyPart")]
    [InlineData("{\"name\":\"Press\",\"bodyPart\":1,\"TRACKINGMODE\":{}}", "trackingMode")]
    [InlineData("{\"name\":\"Press\",\"bodyPart\":1,\"trackingMode\":1,\"LIBRARYIMAGEID\":{}}", "libraryImageId")]
    [InlineData("{\"name\":\"Press\",\"bodyPart\":1,\"trackingMode\":1,\"UPLOADEDIMAGEKEY\":{}}", "uploadedImageKey")]
    public async Task Case_variant_malformed_custom_properties_return_canonical_thai_validation_fields(string body, string field)
    {
        var account = await AuthenticateAsync($"custom-case-{Guid.NewGuid():N}@example.com");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exercises/custom")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        request.Headers.AcceptLanguage.ParseAdd("th-TH");

        var response = await _client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<TrackZ.Contracts.Errors.ApiProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10009, (int)problem!.ErrorCode);
        Assert.Equal([field], problem.FieldErrors!.Keys);
        Assert.Equal($"ข้อมูล {field} ไม่ถูกต้อง", Assert.Single(problem.FieldErrors[field]));
    }

    private static string SignedCursor(int version, string orderingName, Guid orderingId)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { Version = version, OrderingName = orderingName, OrderingId = orderingId });
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("trackz.catalog.cursor.v1:test-signing-key-that-is-at-least-thirty-two-bytes-long"));
        var signature = HMACSHA256.HashData(key, payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CatalogPath => Path.Combine(RepositoryRoot, "assets", "exercises", "catalog.json");

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the TrackZ repository root.");
        }
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
            }))
            .ConfigureServices(services =>
            {
                services.RemoveAll<IObjectStorage>();
                services.AddSingleton<FakeObjectStorage>();
                services.AddSingleton<IObjectStorage>(provider => provider.GetRequiredService<FakeObjectStorage>());
                services.ReplaceStagingLifecycleWithNoOpForTests();
            });
    }

    private sealed class FakeObjectStorage : IObjectStorage
    {
        private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _objects = new(StringComparer.Ordinal);
        public int Count => _objects.Count;

        public Task<ObjectStorageObject?> GetAsync(string ownerPrefix, string key, CancellationToken cancellationToken) =>
            Task.FromResult(_objects.TryGetValue(key, out var value)
                ? new ObjectStorageObject(value.Bytes.Length, value.ContentType, new MemoryStream(value.Bytes, writable: false))
                : null);

        public async Task PutAsync(string ownerPrefix, string key, Stream content, string contentType, CancellationToken cancellationToken)
        {
            using var bytes = new MemoryStream();
            await content.CopyToAsync(bytes, cancellationToken);
            _objects[key] = (bytes.ToArray(), contentType);
        }

        public Task DeleteAsync(string ownerPrefix, string key, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Catalog list acceptance does not delete storage objects.");
    }
}
