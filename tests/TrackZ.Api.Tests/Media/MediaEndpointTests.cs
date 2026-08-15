using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
using TrackZ.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TrackZ.Api.Tests.Media;

public sealed class MediaEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private MediaApiFactory? _factory;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("trackz_media_api_tests").WithUsername("trackz").WithPassword("trackz_media_api_tests_only").Build();
        try { await _container.StartAsync(); }
        catch (DockerUnavailableException exception)
        {
            await _container.DisposeAsync();
            throw SkipException.ForSkip($"Docker is unavailable; media API integration tests require Docker. {exception.Message}");
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Issuer", "trackz-api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "trackz-mobile");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-signing-key-that-is-at-least-thirty-two-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", "14");
        _factory = new MediaApiFactory(_container.GetConnectionString());
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
    public async Task Upload_request_authenticates_before_rejecting_malformed_json()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/media/exercise-images/uploads")
        { Content = new StringContent("{", Encoding.UTF8, "application/json") };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("application/json", "{\"ExerciseId\":\"not-a-guid\",\"contentType\":\"image/png\",\"length\":4}", "exerciseId")]
    [InlineData("application/problem+json; charset=UTF-8", "{\"exerciseId\":{\"nested\":true}}", "exerciseId")]
    [InlineData("application/json; charset=utf-8; charset=utf-8", "{}", "body")]
    [InlineData("text/json", "{}", "body")]
    public async Task Upload_request_canonicalizes_or_rejects_body_with_localized_problem_details(string contentType, string json, string field)
    {
        var account = await AuthenticateAsync($"media-invalid-{Guid.NewGuid():N}@example.com");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/media/exercise-images/uploads")
        { Content = new StringContent(json, Encoding.UTF8) };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        request.Headers.AcceptLanguage.ParseAdd("th-TH");

        var response = await _client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(BusinessErrorCode.InvalidRequest, problem!.ErrorCode);
        Assert.Equal("ข้อมูลคำขอไม่ถูกต้อง", problem.Message);
        Assert.Equal([field], problem.FieldErrors!.Keys);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task Request_put_complete_and_opaque_thumbnail_read_never_expose_storage_internals()
    {
        var account = await AuthenticateAsync("media-happy@example.com");
        var exercise = ExerciseDefinition.CreateCustom(account.UserId, "Media exercise", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }

        var upload = await SendAuthorizedAsync(account.Token, HttpMethod.Post, "/api/v1/media/exercise-images/uploads", JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L }));
        var uploadJson = await upload.Content.ReadAsStringAsync();
        using var uploadDocument = JsonDocument.Parse(uploadJson);
        var uploadUri = uploadDocument.RootElement.GetProperty("uploadUri").GetString();
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.DoesNotContain("private/", uploadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bucket", uploadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access", uploadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("minio", uploadJson, StringComparison.OrdinalIgnoreCase);

        using var uploadBytes = new ByteArrayContent([1, 2, 3, 4]);
        uploadBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var put = await SendAuthorizedAsync(account.Token, HttpMethod.Put, uploadUri!, uploadBytes);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var uploadId = uploadDocument.RootElement.GetProperty("uploadId").GetGuid();
        var complete = await SendAuthorizedAsync(account.Token, HttpMethod.Post, $"/api/v1/media/exercise-images/uploads/{uploadId:D}/complete", content: null);
        var completeJson = await complete.Content.ReadAsStringAsync();
        using var completeDocument = JsonDocument.Parse(completeJson);
        var thumbnail = completeDocument.RootElement.GetProperty("thumbnailUrl").GetString();
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.StartsWith("/api/v1/media/exercise-images/", thumbnail, StringComparison.Ordinal);
        Assert.DoesNotContain("private/", completeJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trackz-test-private", completeJson, StringComparison.OrdinalIgnoreCase);

        var read = await SendAuthorizedAsync(account.Token, HttpMethod.Get, thumbnail!, content: null);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("image/jpeg", read.Content.Headers.ContentType?.MediaType);
        Assert.Equal([9, 8, 7], await read.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Missing_media_routes_are_indistinguishable_and_localized()
    {
        var owner = await AuthenticateAsync("media-owner@example.com");
        var foreign = await AuthenticateAsync("media-foreign@example.com");
        var image = Guid.NewGuid();
        var unknown = await SendAuthorizedAsync(owner.Token, HttpMethod.Get, $"/api/v1/media/exercise-images/{image:D}/thumbnail", null, "th-TH");
        var foreignRead = await SendAuthorizedAsync(foreign.Token, HttpMethod.Get, $"/api/v1/media/exercise-images/{image:D}/thumbnail", null, "th-TH");

        var unknownProblem = await unknown.Content.ReadFromJsonAsync<ApiProblemDetails>();
        var foreignProblem = await foreignRead.Content.ReadFromJsonAsync<ApiProblemDetails>();
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal(BusinessErrorCode.ExerciseNotFound, unknownProblem!.ErrorCode);
        Assert.Equal("ไม่พบรายการท่าออกกำลังกาย", unknownProblem.Message);
        Assert.Equal(unknownProblem.Message, foreignProblem!.Message);
        Assert.Null(unknownProblem.FieldErrors);
    }

    [Fact]
    public async Task Published_system_thumbnail_is_readable_but_draft_and_foreign_private_images_are_hidden()
    {
        var reader = await AuthenticateAsync($"media-library-reader-{Guid.NewGuid():N}@example.com");
        var foreign = await AuthenticateAsync($"media-library-owner-{Guid.NewGuid():N}@example.com");
        var system = ExerciseDefinition.CreateSystem("Library", BodyPart.Chest, TrackingMode.Weighted);
        var published = ExerciseImage.CreateSystem(system, "system/library/master.png", "system/library/thumb.png", 1, "source");
        published.Review(Guid.NewGuid(), "rights", true, true, true, DateTimeOffset.UtcNow);
        published.Publish(DateTimeOffset.UtcNow.AddSeconds(1));
        var draft = ExerciseImage.CreateSystem(system, "system/draft/master.png", "system/draft/thumb.png", 2, "draft");
        var custom = ExerciseDefinition.CreateCustom(foreign.UserId, "Foreign", BodyPart.Chest, TrackingMode.Weighted);
        var privateImage = ExerciseImage.CreateCustomUpload(
            custom, foreign.UserId,
            $"private/{foreign.UserId:D}/master.jpg",
            $"private/{foreign.UserId:D}/thumb.jpg", 1, "upload");
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddRangeAsync(system, custom);
            await db.ExerciseImages.AddRangeAsync(published, draft, privateImage);
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            await using var bytes = new MemoryStream([7, 8, 9]);
            await storage.PutAsync("system/", published.ThumbnailObjectKey, bytes, "image/png", CancellationToken.None);
            await db.SaveChangesAsync();
        }

        var publishedRead = await SendAuthorizedAsync(reader.Token, HttpMethod.Get,
            $"/api/v1/media/exercise-images/{published.Id:D}/thumbnail", null);
        var draftRead = await SendAuthorizedAsync(reader.Token, HttpMethod.Get,
            $"/api/v1/media/exercise-images/{draft.Id:D}/thumbnail", null);
        var privateRead = await SendAuthorizedAsync(reader.Token, HttpMethod.Get,
            $"/api/v1/media/exercise-images/{privateImage.Id:D}/thumbnail", null);

        Assert.Equal(HttpStatusCode.OK, publishedRead.StatusCode);
        Assert.Equal([7, 8, 9], await publishedRead.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, draftRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, privateRead.StatusCode);
    }

    private async Task<(Guid UserId, string Token)> AuthenticateAsync(string email)
    {
        var registration = await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "ValidPassword!42" });
        var registered = await registration.Content.ReadFromJsonAsync<RegistrationResponse>();
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "ValidPassword!42", deviceName = "media-tests" });
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        return (registered!.UserId, token!.AccessToken);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(string token, HttpMethod method, string uri, HttpContent? content, string? language = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (language is not null) request.Headers.AcceptLanguage.ParseAdd(language);
        return await _client.SendAsync(request);
    }

    private sealed record RegistrationResponse(Guid UserId, string Email);
    private sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed class MediaApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing")
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TrackZ"] = connectionString,
                ["Jwt:Issuer"] = "trackz-api", ["Jwt:Audience"] = "trackz-mobile",
                ["Jwt:SigningKey"] = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
                ["Jwt:AccessTokenMinutes"] = "15", ["Jwt:RefreshTokenDays"] = "14"
            }))
            .ConfigureServices(services =>
            {
                services.RemoveAll<IObjectStorage>();
                services.RemoveAll<IImageProcessor>();
                services.AddSingleton<IObjectStorage, FakeObjectStorage>();
                services.AddSingleton<IImageProcessor, FakeImageProcessor>();
            });
    }

    private sealed class FakeObjectStorage : IObjectStorage
    {
        private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _objects = new(StringComparer.Ordinal);
        public async Task PutAsync(string prefix, string key, Stream content, string contentType, CancellationToken cancellationToken)
        { using var bytes = new MemoryStream(); await content.CopyToAsync(bytes, cancellationToken); _objects[key] = (bytes.ToArray(), contentType); }
        public Task DeleteAsync(string prefix, string key, CancellationToken cancellationToken) { _objects.Remove(key); return Task.CompletedTask; }
        public Task<ObjectStorageObject?> GetAsync(string prefix, string key, CancellationToken cancellationToken) =>
            Task.FromResult(_objects.TryGetValue(key, out var value) ? new ObjectStorageObject(value.Bytes.Length, value.ContentType, new MemoryStream(value.Bytes, writable: false)) : null);
    }

    private sealed class FakeImageProcessor : IImageProcessor
    {
        public Task<ProcessedExerciseImage> ProcessExerciseImageAsync(Stream source, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessedExerciseImage([4, 3, 2, 1], [9, 8, 7], "image/jpeg", "image/png"));
    }
}
