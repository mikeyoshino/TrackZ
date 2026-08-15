using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Media;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Infrastructure.Media;
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
    public async Task Completed_content_put_can_be_replayed_after_completion_deleted_staging()
    {
        var account = await AuthenticateAsync(
            $"media-completed-replay-{Guid.NewGuid():N}@example.com");
        var completed = await CreateCompletedUploadAsync(
            _factory!.Services, _client, account, "Completed replay");
        var storage = Assert.IsType<FakeObjectStorage>(
            _factory.Services.GetRequiredService<IObjectStorage>());
        Assert.Null(await storage.GetAsync(
            $"staging/{account.UserId:D}/", completed.StagingKey, default));
        var writesBeforeReplay = storage.PutCount;

        using var replayBytes = new ByteArrayContent([1, 2, 3, 4]);
        replayBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var replay = await SendAuthorizedAsync(
            account.Token, HttpMethod.Put, completed.ContentRoute, replayBytes);

        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal(writesBeforeReplay, storage.PutCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Completed_content_put_replay_still_requires_the_exact_request_contract(
        bool mismatchContentType)
    {
        var account = await AuthenticateAsync(
            $"media-completed-request-mismatch-{mismatchContentType}-{Guid.NewGuid():N}@example.com");
        var completed = await CreateCompletedUploadAsync(
            _factory!.Services, _client, account, $"Completed mismatch {mismatchContentType}");
        var storage = Assert.IsType<FakeObjectStorage>(
            _factory.Services.GetRequiredService<IObjectStorage>());
        var writesBeforeReplay = storage.PutCount;
        using var replayBytes = new ByteArrayContent(
            mismatchContentType ? [1, 2, 3, 4] : [1, 2, 3, 4, 5]);
        replayBytes.Headers.ContentType = new MediaTypeHeaderValue(
            mismatchContentType ? "image/jpeg" : "image/png");

        var replay = await SendAuthorizedAsync(
            account.Token, HttpMethod.Put, completed.ContentRoute, replayBytes);

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(writesBeforeReplay, storage.PutCount);
    }

    [Fact]
    public async Task Completed_content_put_replay_does_not_disclose_a_foreign_ticket()
    {
        var owner = await AuthenticateAsync(
            $"media-completed-owner-{Guid.NewGuid():N}@example.com");
        var foreign = await AuthenticateAsync(
            $"media-completed-foreign-{Guid.NewGuid():N}@example.com");
        var completed = await CreateCompletedUploadAsync(
            _factory!.Services, _client, owner, "Completed foreign replay");
        var storage = Assert.IsType<FakeObjectStorage>(
            _factory.Services.GetRequiredService<IObjectStorage>());
        var writesBeforeReplay = storage.PutCount;
        using var replayBytes = new ByteArrayContent([1, 2, 3, 4]);
        replayBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var replay = await SendAuthorizedAsync(
            foreign.Token, HttpMethod.Put, completed.ContentRoute, replayBytes);

        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
        Assert.Equal(writesBeforeReplay, storage.PutCount);
    }

    [Theory]
    [InlineData(ImageUploadState.Processing)]
    [InlineData(ImageUploadState.Completed)]
    public async Task Content_put_replay_remains_idempotent_after_an_accepted_upload_advances(
        ImageUploadState successorState)
    {
        var account = await AuthenticateAsync($"media-put-successor-{successorState}-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, $"Replay {successorState}", BodyPart.Chest, TrackingMode.Weighted);
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
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;

        using var firstBytes = new ByteArrayContent([1, 2, 3, 4]);
        firstBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var first = await SendAuthorizedAsync(account.Token, HttpMethod.Put, contentRoute, firstBytes);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        await AdvanceAcceptedTicketAsync(_factory.Services, uploadId, account.UserId, successorState);
        var storage = Assert.IsType<FakeObjectStorage>(
            _factory.Services.GetRequiredService<IObjectStorage>());
        Assert.Equal(1, storage.PutCount);

        using var replayBytes = new ByteArrayContent([1, 2, 3, 4]);
        replayBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var replay = await SendAuthorizedAsync(account.Token, HttpMethod.Put, contentRoute, replayBytes);

        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal(1, storage.PutCount);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var ticket = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ImageUploadTickets.SingleAsync(candidate => candidate.Id == uploadId);
        Assert.Equal(successorState, ticket.State);
        var accepted = await storage.GetAsync(
            $"staging/{account.UserId:D}/", ticket.StagingObjectKey, default);
        Assert.NotNull(accepted);
        await using (accepted!.Content)
        {
            Assert.Equal(4, accepted.Length);
            Assert.Equal("image/png", accepted.ContentType);
        }
    }

    [Theory]
    [InlineData(ImageUploadState.Processing, true)]
    [InlineData(ImageUploadState.Processing, false)]
    [InlineData(ImageUploadState.Completed, true)]
    [InlineData(ImageUploadState.Completed, false)]
    public async Task Content_put_replay_requires_the_request_to_match_the_accepted_contract(
        ImageUploadState successorState,
        bool mismatchContentType)
    {
        var account = await AuthenticateAsync(
            $"media-put-request-mismatch-{successorState}-{mismatchContentType}-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, $"Request mismatch {successorState} {mismatchContentType}", BodyPart.Chest, TrackingMode.Weighted);
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
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var firstBytes = new ByteArrayContent([1, 2, 3, 4]);
        firstBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await SendAuthorizedAsync(account.Token, HttpMethod.Put, contentRoute, firstBytes)).StatusCode);
        await AdvanceAcceptedTicketAsync(_factory.Services, uploadId, account.UserId, successorState);
        var storage = Assert.IsType<FakeObjectStorage>(
            _factory.Services.GetRequiredService<IObjectStorage>());
        var writesBeforeReplay = storage.PutCount;

        using var replayBytes = new ByteArrayContent(
            mismatchContentType ? [1, 2, 3, 4] : [1, 2, 3, 4, 5]);
        replayBytes.Headers.ContentType = new MediaTypeHeaderValue(
            mismatchContentType ? "image/jpeg" : "image/png");
        var replay = await SendAuthorizedAsync(
            account.Token, HttpMethod.Put, contentRoute, replayBytes);

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(writesBeforeReplay, storage.PutCount);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var ticket = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ImageUploadTickets.AsNoTracking().SingleAsync(candidate => candidate.Id == uploadId);
        var accepted = await storage.GetAsync(
            $"staging/{account.UserId:D}/", ticket.StagingObjectKey, default);
        Assert.NotNull(accepted);
        await using (accepted!.Content)
        {
            Assert.Equal(4, accepted.Length);
            Assert.Equal("image/png", accepted.ContentType);
        }
    }

    [Theory]
    [InlineData(ImageUploadState.Uploaded, "image/jpeg", 4)]
    [InlineData(ImageUploadState.Uploaded, "image/png", 3)]
    [InlineData(ImageUploadState.Processing, "image/jpeg", 4)]
    [InlineData(ImageUploadState.Processing, "image/png", 3)]
    public async Task Content_put_replay_rejects_a_stored_object_that_no_longer_matches_the_accepted_contract(
        ImageUploadState successorState,
        string storedContentType,
        int storedLength)
    {
        var account = await AuthenticateAsync($"media-put-mismatch-{successorState}-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, $"Mismatch {successorState}", BodyPart.Chest, TrackingMode.Weighted);
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
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await SendAuthorizedAsync(account.Token, HttpMethod.Put, contentRoute, bytes)).StatusCode);

        await AdvanceAcceptedTicketAsync(_factory.Services, uploadId, account.UserId, successorState);
        await using var inspectScope = _factory.Services.CreateAsyncScope();
        var ticket = await inspectScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ImageUploadTickets.AsNoTracking().SingleAsync(candidate => candidate.Id == uploadId);
        var storage = Assert.IsType<FakeObjectStorage>(
            _factory.Services.GetRequiredService<IObjectStorage>());
        await storage.PutAsync(
            $"staging/{account.UserId:D}/",
            ticket.StagingObjectKey,
            new MemoryStream(Enumerable.Range(1, storedLength).Select(value => (byte)value).ToArray()),
            storedContentType,
            default);
        var writesBeforeReplay = storage.PutCount;

        using var replayBytes = new ByteArrayContent([1, 2, 3, 4]);
        replayBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var replay = await SendAuthorizedAsync(account.Token, HttpMethod.Put, contentRoute, replayBytes);

        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
        Assert.Equal(writesBeforeReplay, storage.PutCount);
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

    [Theory]
    [InlineData(ImageUploadState.Processing)]
    [InlineData(ImageUploadState.Completed)]
    public async Task Ambiguous_mark_uploaded_commit_is_reconciled_after_the_ticket_advances(
        ImageUploadState successorState)
    {
        await using var factory = new MediaApiFactory(
            _container!.GetConnectionString(), ambiguousSuccessorState: successorState);
        using var client = factory.CreateClient();
        var account = await AuthenticateAsync(
            client, $"media-ambiguous-{successorState}-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, $"Ambiguous {successorState}", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }

        var reservation = await SendAuthorizedAsync(
            client,
            account.Token,
            HttpMethod.Post,
            "/api/v1/media/exercise-images/uploads",
            JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L }));
        using var reservationJson = JsonDocument.Parse(await reservation.Content.ReadAsStringAsync());
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var put = await SendAuthorizedAsync(
            client, account.Token, HttpMethod.Put, contentRoute, bytes);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var storage = Assert.IsType<FakeObjectStorage>(
            factory.Services.GetRequiredService<IObjectStorage>());
        Assert.Equal(1, storage.PutCount);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var ticket = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ImageUploadTickets.AsNoTracking().SingleAsync(candidate => candidate.Id == uploadId);
        Assert.Equal(successorState, ticket.State);
        var accepted = await storage.GetAsync(
            $"staging/{account.UserId:D}/", ticket.StagingObjectKey, default);
        Assert.NotNull(accepted);
        await using (accepted!.Content)
        {
            Assert.Equal(4, accepted.Length);
            Assert.Equal("image/png", accepted.ContentType);
        }
    }

    [Fact]
    public async Task Ambiguous_mark_uploaded_reconciliation_failure_retains_accepted_staging_bytes()
    {
        await using var factory = new MediaApiFactory(
            _container!.GetConnectionString(),
            reconciliationBehavior: ReconciliationBehavior.Throw);
        using var client = factory.CreateClient();
        var account = await AuthenticateAsync(
            client, $"media-reconciliation-unknown-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, "Unknown reconciliation", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }

        var reservation = await SendAuthorizedAsync(
            client,
            account.Token,
            HttpMethod.Post,
            "/api/v1/media/exercise-images/uploads",
            JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L }));
        using var reservationJson = JsonDocument.Parse(await reservation.Content.ReadAsStringAsync());
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var put = await SendAuthorizedAsync(
            client, account.Token, HttpMethod.Put, contentRoute, bytes);

        Assert.Equal(HttpStatusCode.InternalServerError, put.StatusCode);
        var storage = Assert.IsType<FakeObjectStorage>(
            factory.Services.GetRequiredService<IObjectStorage>());
        Assert.Equal(1, storage.PutCount);
        Assert.Empty(storage.DeletedKeys);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var ticket = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ImageUploadTickets.AsNoTracking().SingleAsync(candidate => candidate.Id == uploadId);
        Assert.Equal(ImageUploadState.Uploaded, ticket.State);
        var accepted = await storage.GetAsync(
            $"staging/{account.UserId:D}/", ticket.StagingObjectKey, default);
        Assert.NotNull(accepted);
        await accepted!.Content.DisposeAsync();
    }

    [Fact]
    public async Task Conclusive_non_commit_reconciliation_deletes_only_its_lease_key_and_preserves_the_original_exception()
    {
        await using var factory = new MediaApiFactory(
            _container!.GetConnectionString(),
            reconciliationBehavior: ReconciliationBehavior.ConclusiveFalse);
        using var client = factory.CreateClient();
        var account = await AuthenticateAsync(
            client, $"media-reconciliation-false-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, "Conclusive reconciliation", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }
        var storage = Assert.IsType<FakeObjectStorage>(
            factory.Services.GetRequiredService<IObjectStorage>());
        var unrelatedKey = $"staging/{account.UserId:D}/{Guid.NewGuid():D}/unrelated";
        await storage.PutAsync(
            $"staging/{account.UserId:D}/", unrelatedKey,
            new MemoryStream([9, 8, 7, 6]), "image/png", default);

        var reservation = await SendAuthorizedAsync(
            client,
            account.Token,
            HttpMethod.Post,
            "/api/v1/media/exercise-images/uploads",
            JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L }));
        using var reservationJson = JsonDocument.Parse(await reservation.Content.ReadAsStringAsync());
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var put = await SendAuthorizedAsync(
            client, account.Token, HttpMethod.Put, contentRoute, bytes);
        var problem = await put.Content.ReadFromJsonAsync<ApiProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.Equal(BusinessErrorCode.VersionConflict, problem!.ErrorCode);
        Assert.Equal("Simulated original transition failure.", problem.Message);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var ticket = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ImageUploadTickets.AsNoTracking().SingleAsync(candidate => candidate.Id == uploadId);
        Assert.Equal(ImageUploadState.Uploading, ticket.State);
        Assert.Equal([ticket.StagingObjectKey], storage.DeletedKeys);
        Assert.Null(await storage.GetAsync(
            $"staging/{account.UserId:D}/", ticket.StagingObjectKey, default));
        var unrelated = await storage.GetAsync(
            $"staging/{account.UserId:D}/", unrelatedKey, default);
        Assert.NotNull(unrelated);
        await unrelated!.Content.DisposeAsync();
    }

    [Fact]
    public async Task Ambiguous_commit_with_delayed_visibility_never_deletes_the_eventually_accepted_lease_key()
    {
        var delayedCommit = new DelayedCommitCoordinator();
        await using var factory = new MediaApiFactory(
            _container!.GetConnectionString(),
            reconciliationBehavior: ReconciliationBehavior.DelayedVisibility,
            delayedCommitCoordinator: delayedCommit);
        using var client = factory.CreateClient();
        var account = await AuthenticateAsync(
            client, $"media-delayed-commit-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, "Delayed commit visibility", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }

        var reservation = await SendAuthorizedAsync(
            client,
            account.Token,
            HttpMethod.Post,
            "/api/v1/media/exercise-images/uploads",
            JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L }));
        using var reservationJson = JsonDocument.Parse(await reservation.Content.ReadAsStringAsync());
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var put = await SendAuthorizedAsync(
            client, account.Token, HttpMethod.Put, contentRoute, bytes);
        var eventualTransition = await delayedCommit.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.InternalServerError, put.StatusCode);
        Assert.False(delayedCommit.FirstReconciliationResult);
        Assert.Equal(StagingUploadTransition.Uploaded, eventualTransition);
        var storage = Assert.IsType<FakeObjectStorage>(
            factory.Services.GetRequiredService<IObjectStorage>());
        Assert.Empty(storage.DeletedKeys);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var ticket = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ImageUploadTickets.AsNoTracking().SingleAsync(candidate => candidate.Id == uploadId);
        Assert.Equal(ImageUploadState.Uploaded, ticket.State);
        var accepted = await storage.GetAsync(
            $"staging/{account.UserId:D}/", ticket.StagingObjectKey, default);
        Assert.NotNull(accepted);
        await accepted!.Content.DisposeAsync();
    }

    [Fact]
    public async Task Content_put_buffers_the_body_before_claiming_and_replays_a_concurrent_acceptance()
    {
        var bodyGate = new ServerBodyGate();
        await using var factory = new MediaApiFactory(
            _container!.GetConnectionString(), bodyGate: bodyGate);
        using var client = factory.CreateClient();
        var account = await AuthenticateAsync(
            client, $"media-buffer-before-lease-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, "Buffered press", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }
        var reservation = await SendAuthorizedAsync(
            client,
            account.Token,
            HttpMethod.Post,
            "/api/v1/media/exercise-images/uploads",
            JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L }));
        using var reservationJson = JsonDocument.Parse(await reservation.Content.ReadAsStringAsync());
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var blockedBody = new ByteArrayContent([1, 2, 3, 4]);
        blockedBody.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var slowPut = SendAuthorizedAsync(
            client, account.Token, HttpMethod.Put, contentRoute, blockedBody);
        await bodyGate.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using (var pendingScope = factory.Services.CreateAsyncScope())
            {
                var pending = await pendingScope.ServiceProvider.GetRequiredService<AppDbContext>()
                    .ImageUploadTickets.AsNoTracking().SingleAsync(candidate => candidate.Id == uploadId);
                Assert.Equal(ImageUploadState.Pending, pending.State);
                Assert.Null(pending.UploadLeaseId);
            }

            using var winningBytes = new ByteArrayContent([1, 2, 3, 4]);
            winningBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            var winningPut = await SendAuthorizedAsync(
                client, account.Token, HttpMethod.Put, contentRoute, winningBytes);
            Assert.Equal(HttpStatusCode.NoContent, winningPut.StatusCode);
        }
        finally
        {
            bodyGate.ReleaseRead.TrySetResult();
        }

        var replay = await slowPut.WaitAsync(TimeSpan.FromSeconds(5));
        var storage = Assert.IsType<FakeObjectStorage>(
            factory.Services.GetRequiredService<IObjectStorage>());
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal(1, storage.PutCount);
    }

    [Fact]
    public async Task Late_writer_materializes_only_its_unique_lifecycle_managed_staging_key()
    {
        var storage = new LateWriterObjectStorage();
        await using var factory = new MediaApiFactory(
            _container!.GetConnectionString(), objectStorage: storage);
        using var client = factory.CreateClient();
        var account = await AuthenticateAsync(
            client, $"media-late-writer-{Guid.NewGuid():N}@example.com");
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, "Late writer press", BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }
        var reservation = await SendAuthorizedAsync(
            client,
            account.Token,
            HttpMethod.Post,
            "/api/v1/media/exercise-images/uploads",
            JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L }));
        using var reservationJson = JsonDocument.Parse(await reservation.Content.ReadAsStringAsync());
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var oldBytes = new ByteArrayContent([1, 2, 3, 4]);
        oldBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var oldPut = SendAuthorizedAsync(
            client, account.Token, HttpMethod.Put, contentRoute, oldBytes);
        await storage.FirstPutStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using (var expireScope = factory.Services.CreateAsyncScope())
            {
                var db = expireScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.ImageUploadTickets
                    .Where(candidate => candidate.Id == uploadId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        candidate => candidate.UploadLeaseExpiresAt,
                        DateTimeOffset.UtcNow.AddMinutes(-1)));
            }

            using (var reclaimBytes = new ByteArrayContent([1, 2, 3, 4]))
            {
                reclaimBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                var schedulesCleanup = await SendAuthorizedAsync(
                    client, account.Token, HttpMethod.Put, contentRoute, reclaimBytes);
                Assert.Equal(HttpStatusCode.NotFound, schedulesCleanup.StatusCode);
            }

            await using (var cleanupScope = factory.Services.CreateAsyncScope())
            {
                var store = cleanupScope.ServiceProvider.GetRequiredService<IExerciseImageUploadStore>();
                var candidate = Assert.Single(
                    await store.ListCleanupCandidatesAsync(DateTimeOffset.UtcNow, default),
                    value => value.TicketId == uploadId);
                var claim = await store.TryClaimCleanupAsync(candidate, DateTimeOffset.UtcNow, default);
                Assert.NotNull(claim);
                await storage.DeleteAsync($"staging/{account.UserId:D}/", claim.StagingKey!, default);
                Assert.True(await store.CompleteCleanupClaimAsync(uploadId, claim.CleanupClaimId!.Value, default));
            }

            using var newerBytes = new ByteArrayContent([1, 2, 3, 4]);
            newerBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            var newerPut = await SendAuthorizedAsync(
                client, account.Token, HttpMethod.Put, contentRoute, newerBytes);
            Assert.Equal(HttpStatusCode.NoContent, newerPut.StatusCode);
        }
        finally
        {
            storage.ReleaseFirstPut.TrySetResult();
        }

        var lateResult = await oldPut.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.NotFound, lateResult.StatusCode);
        Assert.Equal(2, storage.MaterializedKeys.Count);
        var oldKey = Assert.IsType<string>(storage.FirstPutKey);
        var newerKey = Assert.Single(storage.MaterializedKeys, key => key != oldKey);
        var managedOwnerPrefix = $"staging/{account.UserId:D}/";
        Assert.StartsWith(managedOwnerPrefix, oldKey, StringComparison.Ordinal);
        Assert.StartsWith(managedOwnerPrefix, newerKey, StringComparison.Ordinal);
        Assert.NotEqual(oldKey, newerKey);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var durable = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ImageUploadTickets.AsNoTracking().SingleAsync(candidate => candidate.Id == uploadId);
        Assert.Equal(newerKey, durable.StagingObjectKey);
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

    private static async Task<(Guid UploadId, string ContentRoute, string StagingKey)> CreateCompletedUploadAsync(
        IServiceProvider services,
        HttpClient client,
        (Guid UserId, string Token) account,
        string exerciseName)
    {
        var exercise = ExerciseDefinition.CreateCustom(
            account.UserId, exerciseName, BodyPart.Chest, TrackingMode.Weighted);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Exercises.AddAsync(exercise);
            await db.SaveChangesAsync();
        }

        var reservation = await SendAuthorizedAsync(
            client,
            account.Token,
            HttpMethod.Post,
            "/api/v1/media/exercise-images/uploads",
            JsonContent.Create(new { exerciseId = exercise.Id, contentType = "image/png", length = 4L }));
        Assert.Equal(HttpStatusCode.OK, reservation.StatusCode);
        using var reservationJson = JsonDocument.Parse(await reservation.Content.ReadAsStringAsync());
        var uploadId = reservationJson.RootElement.GetProperty("uploadId").GetGuid();
        var contentRoute = reservationJson.RootElement.GetProperty("uploadUri").GetString()!;
        using var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await SendAuthorizedAsync(
                client, account.Token, HttpMethod.Put, contentRoute, bytes)).StatusCode);

        string stagingKey;
        await using (var scope = services.CreateAsyncScope())
        {
            stagingKey = (await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .ImageUploadTickets.AsNoTracking()
                .SingleAsync(ticket => ticket.Id == uploadId)).StagingObjectKey;
        }
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAuthorizedAsync(
                client,
                account.Token,
                HttpMethod.Post,
                $"/api/v1/media/exercise-images/uploads/{uploadId:D}/complete",
                null)).StatusCode);
        return (uploadId, contentRoute, stagingKey);
    }

    private static async Task AdvanceAcceptedTicketAsync(
        IServiceProvider services,
        Guid uploadId,
        Guid ownerId,
        ImageUploadState successorState)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await AdvanceAcceptedTicketAsync(db, uploadId, ownerId, successorState);
    }

    private static async Task AdvanceAcceptedTicketAsync(
        AppDbContext db,
        Guid uploadId,
        Guid ownerId,
        ImageUploadState successorState)
    {
        var ticket = await db.ImageUploadTickets.SingleAsync(candidate => candidate.Id == uploadId);
        if (successorState == ImageUploadState.Uploaded)
            return;
        Assert.True(ticket.TryClaim(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
        if (successorState == ImageUploadState.Processing)
        {
            await db.SaveChangesAsync();
            return;
        }

        Assert.Equal(ImageUploadState.Completed, successorState);
        var exercise = await db.Exercises.SingleAsync(candidate => candidate.Id == ticket.ExerciseDefinitionId);
        var image = ExerciseImage.CreateCustomUpload(
            exercise,
            ownerId,
            $"private/{ownerId:D}/{uploadId:D}/replay/master.jpg",
            $"private/{ownerId:D}/{uploadId:D}/replay/thumbnail.jpg",
            1,
            "test");
        await db.ExerciseImages.AddAsync(image);
        ticket.Complete(image.Id, ticket.ProcessingLeaseId!.Value, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private sealed record RegistrationResponse(Guid UserId, string Email);
    private sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private enum ReconciliationBehavior
    {
        Delegate,
        Throw,
        ConclusiveFalse,
        DelayedVisibility
    }

    private sealed class DelayedCommitCoordinator
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<StagingUploadTransition>? _completion;

        public bool? FirstReconciliationResult { get; private set; }
        public Task<StagingUploadTransition> Completion =>
            _completion ?? throw new InvalidOperationException("Delayed commit was not started.");

        public void Start(Func<Task<StagingUploadTransition>> commit) =>
            _completion = CommitAfterReleaseAsync(commit);

        public async Task<bool> ObserveThenReleaseAsync(Func<Task<bool>> reconcile)
        {
            var result = await reconcile();
            FirstReconciliationResult = result;
            _release.TrySetResult();
            return result;
        }

        private async Task<StagingUploadTransition> CommitAfterReleaseAsync(
            Func<Task<StagingUploadTransition>> commit)
        {
            await _release.Task;
            return await commit();
        }
    }

    private sealed class MediaApiFactory(
        string connectionString,
        bool injectAmbiguousMarkFailure = false,
        ImageUploadState? ambiguousSuccessorState = null,
        ReconciliationBehavior reconciliationBehavior = ReconciliationBehavior.Delegate,
        TimeProvider? timeProvider = null,
        IObjectStorage? objectStorage = null,
        ServerBodyGate? bodyGate = null,
        DelayedCommitCoordinator? delayedCommitCoordinator = null) : WebApplicationFactory<Program>
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
                services.AddSingleton<IObjectStorage>(objectStorage ?? new FakeObjectStorage());
                services.ReplaceStagingLifecycleWithNoOpForTests();
                services.AddSingleton<IImageProcessor, FakeImageProcessor>();
                if (bodyGate is not null)
                    services.AddSingleton<IStartupFilter>(new ServerBodyGateStartupFilter(bodyGate));
                if (injectAmbiguousMarkFailure
                    || ambiguousSuccessorState is not null
                    || reconciliationBehavior != ReconciliationBehavior.Delegate)
                {
                    services.RemoveAll<IExerciseImageUploadStore>();
                    services.AddScoped<IExerciseImageUploadStore>(provider =>
                        new AmbiguousMarkUploadStore(
                            provider.GetRequiredService<AppDbContext>(),
                            ambiguousSuccessorState,
                            reconciliationBehavior,
                            connectionString,
                            delayedCommitCoordinator));
                }
            });
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow = _utcNow.Add(elapsed);
    }

    private sealed class AmbiguousMarkUploadStore(
        AppDbContext inner,
        ImageUploadState? successorState,
        ReconciliationBehavior reconciliationBehavior,
        string connectionString,
        DelayedCommitCoordinator? delayedCommitCoordinator) : IExerciseImageUploadStore
    {
        private bool _failed;

        public Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken) =>
            inner.FindOwnedActiveExerciseAsync(exerciseId, ownerId, cancellationToken);
        public Task AddTicketAsync(ImageUploadTicket ticket, CancellationToken cancellationToken) => inner.AddTicketAsync(ticket, cancellationToken);
        public Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken) => inner.FindOwnedTicketAsync(ticketId, ownerId, cancellationToken);
        public Task<ImageUploadTicket?> FindOwnedTicketSnapshotAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken) => inner.FindOwnedTicketSnapshotAsync(ticketId, ownerId, cancellationToken);
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
            if (!_failed && reconciliationBehavior == ReconciliationBehavior.ConclusiveFalse)
            {
                _failed = true;
                throw new BusinessException(
                    BusinessErrorCode.VersionConflict,
                    "Simulated original transition failure.",
                    409);
            }
            if (!_failed && reconciliationBehavior == ReconciliationBehavior.DelayedVisibility)
            {
                _failed = true;
                var delayed = delayedCommitCoordinator
                    ?? throw new InvalidOperationException("A delayed commit coordinator is required.");
                delayed.Start(async () =>
                {
                    await using var durable = new AppDbContext(
                        new DbContextOptionsBuilder<AppDbContext>()
                            .UseNpgsql(connectionString)
                            .Options);
                    return await durable.TryMarkUploadedAsync(
                        ticketId, ownerId, uploadLeaseId, CancellationToken.None);
                });
                throw new UploadTransitionCommitAmbiguousException(
                    new IOException("Simulated delayed PostgreSQL commit acknowledgement."));
            }

            var transition = await inner.TryMarkUploadedAsync(ticketId, ownerId, uploadLeaseId, cancellationToken);
            if (!_failed && transition == StagingUploadTransition.Uploaded)
            {
                _failed = true;
                if (successorState is not null)
                    await AdvanceAcceptedTicketAsync(inner, ticketId, ownerId, successorState.Value);
                throw new UploadTransitionCommitAmbiguousException(
                    new IOException("Simulated lost PostgreSQL commit acknowledgement."));
            }
            return transition;
        }

        public Task<ExerciseImage> CommitCompletionAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, string masterKey, string thumbnailKey, CancellationToken cancellationToken) => inner.CommitCompletionAsync(ticketId, ownerId, processingLeaseId, masterKey, thumbnailKey, cancellationToken);
        public Task<bool> IsAcceptedUploadAttemptDurableAsync(Guid ticketId, Guid ownerId, string stagingObjectKey, string contentType, long length, CancellationToken cancellationToken) =>
            reconciliationBehavior switch
            {
                ReconciliationBehavior.Throw => Task.FromException<bool>(
                    new IOException("Simulated reconciliation read failure.")),
                ReconciliationBehavior.ConclusiveFalse => Task.FromResult(false),
                ReconciliationBehavior.DelayedVisibility =>
                    (delayedCommitCoordinator
                        ?? throw new InvalidOperationException("A delayed commit coordinator is required."))
                    .ObserveThenReleaseAsync(() => inner.IsAcceptedUploadAttemptDurableAsync(
                        ticketId, ownerId, stagingObjectKey, contentType, length, cancellationToken)),
                _ => inner.IsAcceptedUploadAttemptDurableAsync(
                    ticketId, ownerId, stagingObjectKey, contentType, length, cancellationToken)
            };
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
        public int PutCount { get; private set; }
        public List<string> DeletedKeys { get; } = [];
        public async Task PutAsync(string prefix, string key, Stream content, string contentType, CancellationToken cancellationToken)
        { using var bytes = new MemoryStream(); await content.CopyToAsync(bytes, cancellationToken); _objects[key] = (bytes.ToArray(), contentType); PutCount++; }
        public Task DeleteAsync(string prefix, string key, CancellationToken cancellationToken) { _objects.Remove(key); DeletedKeys.Add(key); return Task.CompletedTask; }
        public Task<ObjectStorageObject?> GetAsync(string prefix, string key, CancellationToken cancellationToken) =>
            Task.FromResult(_objects.TryGetValue(key, out var value) ? new ObjectStorageObject(value.Bytes.Length, value.ContentType, new MemoryStream(value.Bytes, writable: false)) : null);
    }

    private sealed class LateWriterObjectStorage : IObjectStorage
    {
        private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _objects = new(StringComparer.Ordinal);
        private int _putCount;
        public TaskCompletionSource FirstPutStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstPut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> MaterializedKeys { get; } = [];
        public string? FirstPutKey { get; private set; }

        public async Task PutAsync(
            string prefix,
            string key,
            Stream content,
            string contentType,
            CancellationToken cancellationToken)
        {
            using var bytes = new MemoryStream();
            await content.CopyToAsync(bytes, cancellationToken);
            if (Interlocked.Increment(ref _putCount) == 1)
            {
                FirstPutKey = key;
                FirstPutStarted.TrySetResult();
                await ReleaseFirstPut.Task.WaitAsync(cancellationToken);
            }
            _objects[key] = (bytes.ToArray(), contentType);
            MaterializedKeys.Enqueue(key);
        }

        public Task DeleteAsync(string prefix, string key, CancellationToken cancellationToken)
        {
            _objects.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task<ObjectStorageObject?> GetAsync(string prefix, string key, CancellationToken cancellationToken) =>
            Task.FromResult(_objects.TryGetValue(key, out var value)
                ? new ObjectStorageObject(
                    value.Bytes.Length,
                    value.ContentType,
                    new MemoryStream(value.Bytes, writable: false))
                : null);
    }

    private sealed class ServerBodyGate
    {
        private int _wrapCount;
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream WrapFirst(Stream inner) =>
            Interlocked.Increment(ref _wrapCount) == 1
                ? new GatedReadStream(inner, ReadStarted, ReleaseRead)
                : inner;
    }

    private sealed class ServerBodyGateStartupFilter(ServerBodyGate gate) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Method == HttpMethods.Put
                    && context.Request.Path.Value?.EndsWith("/content", StringComparison.Ordinal) == true)
                    context.Request.Body = gate.WrapFirst(context.Request.Body);
                await continuation();
            });
            next(app);
        };
    }

    private sealed class GatedReadStream(
        Stream inner,
        TaskCompletionSource readStarted,
        TaskCompletionSource releaseRead) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            readStarted.TrySetResult();
            await releaseRead.Task.WaitAsync(cancellationToken);
            return await inner.ReadAsync(buffer, cancellationToken);
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeImageProcessor : IImageProcessor
    {
        public Task<ProcessedExerciseImage> ProcessExerciseImageAsync(Stream source, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessedExerciseImage([4, 3, 2, 1], [9, 8, 7], "image/jpeg", "image/png"));
    }
}
