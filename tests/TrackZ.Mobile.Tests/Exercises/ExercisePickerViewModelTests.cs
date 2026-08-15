using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;
using System.Net;
using System.Text;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class ExercisePickerViewModelTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"trackz-picker-{Guid.NewGuid():N}.db");
    private ExerciseCache _cache = null!;

    public Task InitializeAsync()
    {
        _cache = new ExerciseCache(_databasePath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Offline_picker_displays_cached_last_and_pr_and_allows_multi_select()
    {
        var press = ChestPressWithPerformance();
        await _cache.ReplaceAllAsync([press], new DateTimeOffset(2026, 8, 15, 1, 0, 0, TimeSpan.Zero));
        var sut = new ExercisePickerViewModel(_cache, new StubCatalogApi(), new StubConnectivity(false), new FixedClock());

        await sut.LoadAsync(BodyPart.Chest);
        sut.ToggleSelectionCommand.Execute(sut.Exercises[0]);

        Assert.Equal(70m, sut.Exercises[0].LastBestSet!.WeightKg);
        Assert.Equal(75m, sut.Exercises[0].AllTimeBest!.WeightKg);
        Assert.Equal("70 kg × 8", sut.Exercises[0].LastDisplay);
        Assert.Equal("75 kg × 5", sut.Exercises[0].PersonalRecordDisplay);
        Assert.Single(sut.SelectedExerciseIds);
    }

    [Fact]
    public async Task Load_shows_cache_before_online_refresh_then_replaces_it_transactionally()
    {
        var cached = ChestPressWithPerformance();
        var refreshed = Summary(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Cable Fly", BodyPart.Chest);
        await _cache.ReplaceAllAsync([cached], new DateTimeOffset(2026, 8, 14, 1, 0, 0, TimeSpan.Zero));
        var api = new GatedCatalogApi();
        var sut = new ExercisePickerViewModel(_cache, api, new StubConnectivity(true), new FixedClock());

        await sut.LoadAsync(BodyPart.Chest);

        Assert.Equal("Bench Press", Assert.Single(sut.Exercises).Name);
        api.Complete([refreshed]);
        await sut.RefreshCompletion;

        Assert.Equal("Cable Fly", Assert.Single(sut.Exercises).Name);
        var persisted = await new ExerciseCache(_databasePath).GetAllAsync();
        Assert.Equal("Cable Fly", Assert.Single(persisted).Name);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 2, 3, 4, TimeSpan.Zero), persisted[0].LastSyncedAt);
    }

    [Fact]
    public async Task Search_and_body_part_filter_preserve_selection_by_exercise_id()
    {
        var chest = ChestPressWithPerformance();
        var back = Summary(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Lat Pulldown", BodyPart.Back);
        await _cache.ReplaceAllAsync([chest, back], DateTimeOffset.UtcNow);
        var sut = new ExercisePickerViewModel(_cache, new StubCatalogApi(), new StubConnectivity(false), new FixedClock());

        await sut.LoadAsync(null);
        sut.ToggleSelectionCommand.Execute(chest.Id);
        sut.SearchText = " lat ";

        Assert.Equal("Lat Pulldown", Assert.Single(sut.Exercises).Name);
        sut.SearchText = string.Empty;
        sut.SelectedBodyPart = BodyPart.Chest;
        Assert.True(Assert.Single(sut.Exercises).IsSelected);
        Assert.Equal(chest.Id, Assert.Single(sut.SelectedExerciseIds));
    }

    [Fact]
    public async Task Failed_replacement_leaves_the_previous_catalog_intact()
    {
        var cached = ChestPressWithPerformance();
        await _cache.ReplaceAllAsync([cached], DateTimeOffset.UtcNow);
        var invalid = Summary(Guid.NewGuid(), " ", BodyPart.Chest);

        await Assert.ThrowsAsync<ArgumentException>(() => _cache.ReplaceAllAsync([invalid], DateTimeOffset.UtcNow));

        Assert.Equal(cached.Id, Assert.Single(await _cache.GetAllAsync()).Id);
    }

    [Fact]
    public async Task Catalog_http_client_follows_opaque_cursors_until_all_items_are_loaded()
    {
        var firstId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var secondId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var handler = new QueueHttpHandler(
            Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{firstId}}","name":"First","bodyPart":1,"trackingMode":1,"thumbnailUrl":null,"lastPerformedAt":null,"lastBestSet":null,"allTimeBest":null,"isCustom":false}],"nextCursor":"opaque.cursor"}"""),
            Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{secondId}}","name":"Second","bodyPart":2,"trackingMode":1,"thumbnailUrl":null,"lastPerformedAt":null,"lastBestSet":null,"allTimeBest":null,"isCustom":false}],"nextCursor":null}"""));
        var client = new TrackZExerciseApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") });

        var result = await client.GetAllAsync();

        Assert.Equal([firstId, secondId], result.Select(item => item.Id));
        Assert.Equal("/api/v1/exercises?pageSize=50", handler.Requests[0].PathAndQuery);
        Assert.Equal("/api/v1/exercises?pageSize=50&cursor=opaque.cursor", handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task Background_network_failure_keeps_cache_and_exposes_stable_error_without_faulting_load()
    {
        var cached = ChestPressWithPerformance();
        await _cache.ReplaceAllAsync([cached], DateTimeOffset.UtcNow);
        var sut = new ExercisePickerViewModel(
            _cache,
            new FailingCatalogApi(),
            new StubConnectivity(true),
            new FixedClock());

        await sut.LoadAsync();
        await sut.RefreshCompletion;

        Assert.Equal(cached.Id, Assert.Single(sut.Exercises).Id);
        Assert.Equal(TrackZ.Contracts.Errors.BusinessErrorCode.InternalServerError, sut.LastErrorCode);
    }

    [Fact]
    public async Task Refresh_does_not_overwrite_pending_local_thumbnail_or_label()
    {
        var id = Guid.Parse("77777777-7777-7777-7777-777777777777");
        await _cache.QueueAsync(new PendingCustomExercise(
            Guid.NewGuid(), id, id, "Pending Press", BodyPart.Chest, TrackingMode.Weighted,
            null, "/local/original.png", "image/png", DateTimeOffset.UtcNow));

        await _cache.ReplaceAllAsync(
            [Summary(id, "Pending Press", BodyPart.Chest)],
            new DateTimeOffset(2026, 8, 15, 4, 0, 0, TimeSpan.Zero));

        var cached = Assert.Single(await _cache.GetAllAsync());
        Assert.True(cached.IsPendingSync);
        Assert.Equal("/local/original.png", cached.ThumbnailUri);
        Assert.Equal("Pending Sync", cached.SyncLabel);
    }

    [Fact]
    public async Task Api_http_handler_adds_the_current_bearer_token()
    {
        var terminal = new AuthorizationRecordingHandler();
        var handler = new BearerTokenHandler(new StubAccessTokenProvider(), new Uri("https://trackz.test")) { InnerHandler = terminal };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") };

        await client.GetAsync("/api/v1/exercises");

        Assert.Equal("Bearer", terminal.Authorization?.Scheme);
        Assert.Equal("access-token", terminal.Authorization?.Parameter);
    }

    [Fact]
    public async Task Online_refresh_caches_protected_thumbnail_to_a_local_path()
    {
        var remote = new ExerciseSummaryDto(
            Guid.NewGuid(), "Private Press", BodyPart.Chest, TrackingMode.Weighted,
            "/api/v1/media/exercise-images/private/thumbnail", null, null, null, true);
        var sut = new ExercisePickerViewModel(
            _cache,
            new ImmediateCatalogApi([remote]),
            new StubConnectivity(true),
            new FixedClock(),
            thumbnailCache: new StubThumbnailCache());

        await sut.LoadAsync();
        await sut.RefreshCompletion;

        Assert.Equal("/local/private-thumbnail.jpg", Assert.Single(sut.Exercises).ThumbnailUri);
        Assert.Equal("/local/private-thumbnail.jpg", Assert.Single(await _cache.GetAllAsync()).ThumbnailUri);
    }

    [Fact]
    public async Task Failed_thumbnail_does_not_discard_refreshed_metadata()
    {
        var refreshed = Summary(
            Guid.NewGuid(), "New Metadata", BodyPart.Back,
            "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail");
        var sut = new ExercisePickerViewModel(
            _cache,
            new ImmediateCatalogApi([refreshed]),
            new StubConnectivity(true),
            new FixedClock(),
            thumbnailCache: new FailingThumbnailCache());

        await sut.LoadAsync();
        await sut.RefreshCompletion;

        var cached = Assert.Single(await _cache.GetAllAsync());
        Assert.Equal("New Metadata", cached.Name);
        Assert.Null(cached.ThumbnailUri);
        Assert.Null(sut.LastErrorCode);
    }

    [Fact]
    public async Task Thumbnail_fill_uses_bounded_parallelism()
    {
        var exercises = Enumerable.Range(1, 12)
            .Select(index => Summary(
                Guid.NewGuid(), $"Exercise {index}", BodyPart.Chest,
                $"/api/v1/media/exercise-images/{Guid.NewGuid():D}/thumbnail"))
            .ToArray();
        var thumbnails = new ConcurrencyTrackingThumbnailCache();
        var sut = new ExercisePickerViewModel(
            _cache,
            new ImmediateCatalogApi(exercises),
            new StubConnectivity(true),
            new FixedClock(),
            thumbnailCache: thumbnails);

        await sut.LoadAsync();
        await sut.RefreshCompletion;

        Assert.InRange(thumbnails.MaximumConcurrency, 2, 4);
        Assert.Equal(12, (await _cache.GetAllAsync()).Count(item => item.ThumbnailUri == "/local/image.jpg"));
    }

    [Theory]
    [InlineData("//evil.example/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("/%2f%2fevil.example/x")]
    [InlineData("/\\evil.example/x")]
    [InlineData("https://trackz.test/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("http://trackz.test/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("https://trackz.test:444/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("https://evil.example/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("/api/v1/media/exercise-images/not-a-guid/thumbnail")]
    [InlineData("/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/master")]
    public async Task Thumbnail_cache_rejects_noncanonical_routes_without_sending_request(string route)
    {
        var handler = new NeverCalledHandler();
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        try
        {
            var cache = new AuthenticatedExerciseThumbnailCache(
                new HttpClient(handler) { BaseAddress = new Uri("https://api.trackz.test") },
                new HttpClient(new NeverCalledHandler()),
                new Uri("https://media.trackz.test"),
                directory,
                new FixedClock(),
                new AccountSessionBoundary());

            await Assert.ThrowsAsync<InvalidDataException>(() => cache.CacheAsync(route));
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Thumbnail_cache_gets_authorization_then_downloads_without_forwarding_the_api_bearer()
    {
        var clock = new FixedClock();
        var signedUrl = SignedThumbnailUrl(clock.UtcNow.AddMinutes(1));
        var authorization = new AuthorizationResponseHandler(signedUrl, clock.UtcNow.AddMinutes(1));
        var media = new ImageResponseHandler();
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        try
        {
            var apiClient = new HttpClient(authorization) { BaseAddress = new Uri("https://api.trackz.test") };
            apiClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "private-api-token");
            var cache = new AuthenticatedExerciseThumbnailCache(
                apiClient,
                new HttpClient(media),
                new Uri("https://media.trackz.test"),
                directory,
                clock,
                new AccountSessionBoundary());

            var local = await cache.CacheAsync(
                "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail");

            Assert.Equal(1, authorization.CallCount);
            Assert.Equal("Bearer private-api-token", authorization.Authorization);
            Assert.Equal(1, media.CallCount);
            Assert.Null(media.Authorization);
            Assert.EndsWith(".jpg", local, StringComparison.Ordinal);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(local!));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("//evil.example/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://evil.example/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test.evil.example/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test@evil.example/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test:444/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test/media/v1/exercise-images/88888888-8888-8888-8888-888888888888/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test/media/v1/%2e%2e/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&next=https://evil.example")]
    public async Task Thumbnail_cache_rejects_off_origin_or_confused_signed_urls_without_contacting_them(string signedUrl)
    {
        var clock = new FixedClock();
        var media = new NeverCalledHandler();
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        try
        {
            var cache = new AuthenticatedExerciseThumbnailCache(
                new HttpClient(new AuthorizationResponseHandler(signedUrl, DateTimeOffset.FromUnixTimeSeconds(1786759444)))
                { BaseAddress = new Uri("https://api.trackz.test") },
                new HttpClient(media),
                new Uri("https://media.trackz.test"),
                directory,
                clock,
                new AccountSessionBoundary());

            await Assert.ThrowsAsync<InvalidDataException>(() => cache.CacheAsync(
                "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail"));

            Assert.Equal(0, media.CallCount);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Expired_signed_download_reauthorizes_once_then_caches_the_bytes()
    {
        var clock = new FixedClock();
        var expiry = clock.UtcNow.AddMinutes(1);
        var signedUrl = SignedThumbnailUrl(expiry);
        var authorizations = new QueueHttpHandler(
            SignedAccessResponse(signedUrl, expiry),
            SignedAccessResponse(signedUrl, expiry));
        var media = new QueueHttpHandler(
            new HttpResponseMessage(HttpStatusCode.Gone),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = ImageResponseHandler.ImageContent("image/png") });
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        try
        {
            var cache = new AuthenticatedExerciseThumbnailCache(
                new HttpClient(authorizations) { BaseAddress = new Uri("https://api.trackz.test") },
                new HttpClient(media),
                new Uri("https://media.trackz.test"),
                directory,
                clock,
                new AccountSessionBoundary());

            var local = await cache.CacheAsync(
                "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail");

            Assert.Equal(2, authorizations.Requests.Count);
            Assert.Equal(2, media.Requests.Count);
            Assert.EndsWith(".png", local, StringComparison.Ordinal);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(local!));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string SignedThumbnailUrl(DateTimeOffset expiresAt) =>
        $"https://media.trackz.test/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail" +
        $"?expires={expiresAt.ToUnixTimeSeconds()}&signature={new string('a', 43)}";

    private static HttpResponseMessage SignedAccessResponse(string url, DateTimeOffset expiresAt) =>
        Json(HttpStatusCode.OK, $$"""{"url":"{{url}}","expiresAt":"{{expiresAt:O}}"}""");

    [Fact]
    public async Task Thumbnail_response_from_reset_generation_is_never_promoted_to_account_cache()
    {
        var handler = new DelayedImageResponseHandler();
        var clock = new FixedClock();
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        var boundary = new AccountSessionBoundary();
        try
        {
            var cache = new AuthenticatedExerciseThumbnailCache(
                new HttpClient(new AuthorizationResponseHandler(
                    SignedThumbnailUrl(clock.UtcNow.AddMinutes(1)),
                    clock.UtcNow.AddMinutes(1))) { BaseAddress = new Uri("https://api.trackz.test") },
                new HttpClient(handler),
                new Uri("https://media.trackz.test"),
                directory,
                clock,
                boundary);
            var caching = cache.CacheAsync(
                "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail");
            await handler.Entered;

            await boundary.ResetAsync(cache.ClearAsync);
            handler.Release();
            var local = await caching;

            Assert.Null(local);
            Assert.Empty(Directory.Exists(directory) ? Directory.EnumerateFiles(directory) : []);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Bearer_handler_never_sends_token_to_off_origin_absolute_request()
    {
        var terminal = new AuthorizationRecordingHandler();
        var handler = new BearerTokenHandler(new StubAccessTokenProvider(), new Uri("https://trackz.test")) { InnerHandler = terminal };
        using var client = new HttpClient(handler);

        await client.GetAsync("https://evil.example/x");

        Assert.Null(terminal.Authorization);
    }

    private static ExerciseSummaryDto ChestPressWithPerformance() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "Bench Press",
        BodyPart.Chest,
        TrackingMode.Weighted,
        "/images/bench-thumbnail",
        new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero),
        new PerformanceSetDto(70m, null, 8),
        new PerformanceSetDto(75m, null, 5),
        false);

    private static ExerciseSummaryDto Summary(Guid id, string name, BodyPart bodyPart, string? thumbnailUrl = null) => new(
        id, name, bodyPart, TrackingMode.Weighted, thumbnailUrl, null, null, null, false);

    private sealed class StubConnectivity(bool isOnline) : IConnectivityService
    {
        public bool IsOnline { get; } = isOnline;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class StubCatalogApi : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExerciseSummaryDto>>([]);
    }

    private sealed class GatedCatalogApi : IExerciseCatalogApi
    {
        private readonly TaskCompletionSource<IReadOnlyList<ExerciseSummaryDto>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            _completion.Task.WaitAsync(cancellationToken);

        public void Complete(IReadOnlyList<ExerciseSummaryDto> exercises) => _completion.SetResult(exercises);
    }

    private sealed class FailingCatalogApi : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("offline");
    }

    private sealed class ImmediateCatalogApi(IReadOnlyList<ExerciseSummaryDto> exercises) : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(exercises);
    }

    private sealed class StubThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(thumbnailUri is null ? null : "/local/private-thumbnail.jpg");
    }

    private sealed class FailingThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            throw new IOException("thumbnail unavailable");
    }

    private sealed class ConcurrencyTrackingThumbnailCache : IExerciseThumbnailCache
    {
        private int _current;
        private int _maximum;
        public int MaximumConcurrency => _maximum;

        public async Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _current);
            InterlockedExtensions.Max(ref _maximum, current);
            try
            {
                await Task.Delay(20, cancellationToken);
                return "/local/image.jpg";
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            int observed;
            do
            {
                observed = Volatile.Read(ref location);
                if (observed >= value) return;
            }
            while (Interlocked.CompareExchange(ref location, value, observed) != observed);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 15, 2, 3, 4, TimeSpan.Zero);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class StubAccessTokenProvider : IAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("access-token");
    }

    private sealed class AuthorizationRecordingHandler : HttpMessageHandler
    {
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ImageResponseHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ImageContent()
            });
        }

        public static ByteArrayContent ImageContent(string contentType = "image/jpeg")
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return content;
        }
    }

    private sealed class AuthorizationResponseHandler(string url, DateTimeOffset expiresAt) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(Json(
                HttpStatusCode.OK,
                $$"""{"url":"{{url}}","expiresAt":"{{expiresAt:O}}"}"""));
        }
    }

    private sealed class DelayedImageResponseHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ImageResponseHandler.ImageContent()
            };
        }
    }
}
