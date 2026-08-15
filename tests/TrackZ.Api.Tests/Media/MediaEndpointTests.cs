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
using TrackZ.Application.Media;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Exercises;
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

    [Fact]
    public async Task Media_authorization_authenticates_before_image_or_rendition_disclosure()
    {
        var response = await _client.GetAsync(
            $"/api/v1/media/exercise-images/{Guid.NewGuid():D}/not-a-rendition");

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

        var authorization = await SendAuthorizedAsync(account.Token, HttpMethod.Get, thumbnail!, content: null);
        var accessJson = await authorization.Content.ReadAsStringAsync();
        using var accessDocument = JsonDocument.Parse(accessJson);
        var signedUrl = accessDocument.RootElement.GetProperty("url").GetString();
        var expiresAt = accessDocument.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(HttpStatusCode.OK, authorization.StatusCode);
        Assert.StartsWith("https://media.trackz.test/media/v1/exercise-images/", signedUrl, StringComparison.Ordinal);
        Assert.InRange(expiresAt, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.DoesNotContain("private/", accessJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(account.UserId.ToString("D"), accessJson, StringComparison.OrdinalIgnoreCase);

        using var signedRequest = new HttpRequestMessage(HttpMethod.Get, signedUrl);
        var read = await _client.SendAsync(signedRequest);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("image/jpeg", read.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", Assert.Single(read.Headers.GetValues("Cache-Control")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal([9, 8, 7], await read.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Accepted_content_put_can_be_replayed_after_a_lost_no_content_response()
    {
        var account = await AuthenticateAsync($"media-put-replay-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, "Replay press", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }

        var reservation = await SendAuthorizedAsync(
            account.Token,
            HttpMethod.Post,
            "/api/v1/media/exercise-images/uploads",
            JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L }));
        using var reservationJson = JsonDocument.Parse(await reservation.Content.ReadAsStringAsync());
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;

        using var firstBytes = new ByteArrayContent([1, 2, 3, 4]);
        firstBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var first = await SendAuthorizedAsync(account.Token, HttpMethod.Put, contentRoute, firstBytes);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // A lost acknowledgement can outlive the original reservation window. Once the exact
        // upload is durably accepted, expiry must not turn its replay into a false failure.
        await using (var expireScope = _factory.Services.CreateAsyncScope())
        {
            var database = expireScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.ImageUploadTickets.ExecuteUpdateAsync(setters => setters
                .SetProperty(ticket => ticket.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        using var replayBytes = new ByteArrayContent([1, 2, 3, 4]);
        replayBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var replay = await SendAuthorizedAsync(account.Token, HttpMethod.Put, contentRoute, replayBytes);

        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var ticket = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ImageUploadTickets.SingleAsync();
        Assert.Equal(ImageUploadState.Uploaded, ticket.State);
    }

    [Fact]
    public async Task Ambiguous_mark_uploaded_commit_is_reconciled_without_deleting_the_accepted_object()
    {
        await using var factory = new MediaApiFactory(_container!.GetConnectionString(), injectAmbiguousMarkFailure: true);
        using var client = factory.CreateClient();
        var registration = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = $"media-ambiguous-{Guid.NewGuid():N}@example.com", password = "ValidPassword!42" });
        var registered = await registration.Content.ReadFromJsonAsync<RegistrationResponse>();
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = registered!.Email, password = "ValidPassword!42", deviceName = "media-tests" });
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        var exercise = ExerciseDefinition.CreateCustom(
            registered.UserId, "Ambiguous press", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }

        using var reserveRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/media/exercise-images/uploads")
        {
            Content = JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L })
        };
        reserveRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var reservation = await client.SendAsync(reserveRequest);
        using var reservationJson = JsonDocument.Parse(await reservation.Content.ReadAsStringAsync());
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var putRequest = new HttpRequestMessage(HttpMethod.Put, contentRoute) { Content = bytes };
        putRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var put = await client.SendAsync(putRequest);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        using var completeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/media/exercise-images/uploads/{uploadId:D}/complete");
        completeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var complete = await client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
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

        var publishedAuthorization = await SendAuthorizedAsync(reader.Token, HttpMethod.Get,
            $"/api/v1/media/exercise-images/{published.Id:D}/thumbnail", null);
        var draftRead = await SendAuthorizedAsync(reader.Token, HttpMethod.Get,
            $"/api/v1/media/exercise-images/{draft.Id:D}/thumbnail", null);
        var privateRead = await SendAuthorizedAsync(reader.Token, HttpMethod.Get,
            $"/api/v1/media/exercise-images/{privateImage.Id:D}/thumbnail", null);

        Assert.Equal(HttpStatusCode.OK, publishedAuthorization.StatusCode);
        using var access = JsonDocument.Parse(await publishedAuthorization.Content.ReadAsStringAsync());
        var publishedRead = await _client.GetAsync(access.RootElement.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, publishedRead.StatusCode);
        Assert.Equal([7, 8, 9], await publishedRead.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, draftRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, privateRead.StatusCode);
    }

    [Fact]
    public async Task Signed_media_rejects_tampering_and_expiry_then_allows_authenticated_reauthorization()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.Zero));
        await using var factory = new MediaApiFactory(_container!.GetConnectionString(), timeProvider: clock);
        using var client = factory.CreateClient();
        var account = await AuthenticateAsync(client, $"signed-expiry-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(account.UserId, "Signed Press", BodyPart.Chest, TrackingMode.Weighted);
        var image = ExerciseImage.CreateCustomUpload(
            exercise,
            account.UserId,
            $"private/{account.UserId:D}/{exercise.Id:D}/master.jpg",
            $"private/{account.UserId:D}/{exercise.Id:D}/thumbnail.jpg",
            1,
            "upload");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.ExerciseImages.AddAsync(image);
            await db.SaveChangesAsync();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            await storage.PutAsync(
                $"private/{account.UserId:D}/",
                image.ThumbnailObjectKey,
                new MemoryStream([3, 2, 1]),
                "image/jpeg",
                default);
        }

        var authorization = await SendAuthorizedAsync(
            client,
            account.Token,
            HttpMethod.Get,
            $"/api/v1/media/exercise-images/{image.Id:D}/thumbnail",
            null);
        var access = await authorization.Content.ReadFromJsonAsync<SignedMediaAccessDto>();
        Assert.NotNull(access);

        var replacement = access!.Url[^1] == 'A' ? 'B' : 'A';
        var tampered = access.Url[..^1] + replacement;
        var tamperedResponse = await client.GetAsync(tampered);
        Assert.Equal(HttpStatusCode.NotFound, tamperedResponse.StatusCode);

        clock.Advance(TimeSpan.FromSeconds(61));
        var expired = await client.GetAsync(access.Url);
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);

        var refreshedAuthorization = await SendAuthorizedAsync(
            client,
            account.Token,
            HttpMethod.Get,
            $"/api/v1/media/exercise-images/{image.Id:D}/thumbnail",
            null);
        var refreshed = await refreshedAuthorization.Content.ReadFromJsonAsync<SignedMediaAccessDto>();
        Assert.True(refreshed!.ExpiresAt > access.ExpiresAt);
        var downloaded = await client.GetAsync(refreshed.Url);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal([3, 2, 1], await downloaded.Content.ReadAsByteArrayAsync());
    }

    private async Task<(Guid UserId, string Token)> AuthenticateAsync(string email)
        => await AuthenticateAsync(_client, email);

    private static async Task<(Guid UserId, string Token)> AuthenticateAsync(HttpClient client, string email)
    {
        var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "ValidPassword!42" });
        var registered = await registration.Content.ReadFromJsonAsync<RegistrationResponse>();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "ValidPassword!42", deviceName = "media-tests" });
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        return (registered!.UserId, token!.AccessToken);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(string token, HttpMethod method, string uri, HttpContent? content, string? language = null)
        => await SendAuthorizedAsync(_client, token, method, uri, content, language);

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(HttpClient client, string token, HttpMethod method, string uri, HttpContent? content, string? language = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (language is not null) request.Headers.AcceptLanguage.ParseAdd(language);
        return await client.SendAsync(request);
    }

    private sealed record RegistrationResponse(Guid UserId, string Email);
    private sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed class MediaApiFactory(
        string connectionString,
        bool injectAmbiguousMarkFailure = false,
        TimeProvider? timeProvider = null) : WebApplicationFactory<Program>
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
                if (timeProvider is not null)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(timeProvider);
                }
                services.AddSingleton<IObjectStorage, FakeObjectStorage>();
                services.AddSingleton<IImageProcessor, FakeImageProcessor>();
                if (injectAmbiguousMarkFailure)
                {
                    services.RemoveAll<IExerciseImageUploadStore>();
                    services.AddScoped<IExerciseImageUploadStore>(provider =>
                        new AmbiguousMarkUploadStore(provider.GetRequiredService<AppDbContext>()));
                }
            });
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow = _utcNow.Add(elapsed);
    }

    private sealed class AmbiguousMarkUploadStore(AppDbContext inner) : IExerciseImageUploadStore
    {
        private bool _failed;

        public Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken) =>
            inner.FindOwnedActiveExerciseAsync(exerciseId, ownerId, cancellationToken);
        public Task AddTicketAsync(ImageUploadTicket ticket, CancellationToken cancellationToken) => inner.AddTicketAsync(ticket, cancellationToken);
        public Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken) => inner.FindOwnedTicketAsync(ticketId, ownerId, cancellationToken);
        public Task<ExerciseImage?> FindImageAsync(Guid imageId, CancellationToken cancellationToken) => inner.FindImageAsync(imageId, cancellationToken);
        public Task<ExerciseImage?> FindOwnedImageAsync(Guid imageId, Guid ownerId, CancellationToken cancellationToken) => inner.FindOwnedImageAsync(imageId, ownerId, cancellationToken);
        public Task<ExerciseImage?> FindReadableImageAsync(Guid imageId, Guid ownerId, CancellationToken cancellationToken) => inner.FindReadableImageAsync(imageId, ownerId, cancellationToken);
        public Task<ExerciseImage?> FindSignedReadableImageAsync(Guid imageId, CancellationToken cancellationToken) => inner.FindSignedReadableImageAsync(imageId, cancellationToken);
        public Task<StagingUploadTransition> TryMarkUploadedAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken) => inner.TryMarkUploadedAsync(ticketId, ownerId, cancellationToken);
        public Task<UploadClaim> TryClaimUploadAsync(Guid ticketId, Guid ownerId, TimeSpan lease, CancellationToken cancellationToken) => inner.TryClaimUploadAsync(ticketId, ownerId, lease, cancellationToken);

        public async Task<StagingUploadTransition> TryMarkUploadedAsync(
            Guid ticketId,
            Guid ownerId,
            Guid uploadLeaseId,
            CancellationToken cancellationToken)
        {
            var transition = await inner.TryMarkUploadedAsync(ticketId, ownerId, uploadLeaseId, cancellationToken);
            if (!_failed && transition == StagingUploadTransition.Uploaded)
            {
                _failed = true;
                throw new IOException("Simulated lost PostgreSQL commit acknowledgement.");
            }
            return transition;
        }

        public Task<ExerciseImage> CommitCompletionAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, string masterKey, string thumbnailKey, CancellationToken cancellationToken) => inner.CommitCompletionAsync(ticketId, ownerId, processingLeaseId, masterKey, thumbnailKey, cancellationToken);
        public Task<bool> IsUploadedAttemptDurableAsync(Guid ticketId, Guid ownerId, string stagingObjectKey, string contentType, long length, CancellationToken cancellationToken) => inner.IsUploadedAttemptDurableAsync(ticketId, ownerId, stagingObjectKey, contentType, length, cancellationToken);
        public Task<ExerciseImage?> FindCompletedByAttemptAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, string masterKey, string thumbnailKey, CancellationToken cancellationToken) => inner.FindCompletedByAttemptAsync(ticketId, ownerId, processingLeaseId, masterKey, thumbnailKey, cancellationToken);
        public Task<IReadOnlyList<ImageUploadCleanupCandidate>> ListCleanupCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken) => inner.ListCleanupCandidatesAsync(now, cancellationToken);
        public Task<ImageUploadCleanupCandidate?> TryClaimCleanupAsync(ImageUploadCleanupCandidate candidate, DateTimeOffset now, CancellationToken cancellationToken) => inner.TryClaimCleanupAsync(candidate, now, cancellationToken);
        public Task<bool> CompleteCleanupClaimAsync(Guid ticketId, Guid cleanupClaimId, CancellationToken cancellationToken) => inner.CompleteCleanupClaimAsync(ticketId, cleanupClaimId, cancellationToken);
        public Task ReleaseCleanupClaimAsync(Guid ticketId, Guid cleanupClaimId, CancellationToken cancellationToken) => inner.ReleaseCleanupClaimAsync(ticketId, cleanupClaimId, cancellationToken);
        public Task MarkCleanupCompleteAsync(Guid ticketId, string? stagingKey, Guid? processingLeaseId, CancellationToken cancellationToken) => inner.MarkCleanupCompleteAsync(ticketId, stagingKey, processingLeaseId, cancellationToken);
        public Task<bool> TryFailClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken) => inner.TryFailClaimAsync(ticketId, ownerId, processingLeaseId, cancellationToken);
        public Task<bool> TryReleaseClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken, bool retainAttemptForCleanup = false) => inner.TryReleaseClaimAsync(ticketId, ownerId, processingLeaseId, cancellationToken, retainAttemptForCleanup);
        public Task SaveAsync(CancellationToken cancellationToken) => inner.SaveAsync(cancellationToken);
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
