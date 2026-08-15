using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
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
        var handler = new BearerTokenHandler(new StubAccessTokenProvider()) { InnerHandler = terminal };
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
    public async Task Thumbnail_cache_rejects_absolute_urls_without_leaking_bearer_request()
    {
        var handler = new NeverCalledHandler();
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        try
        {
            var cache = new AuthenticatedExerciseThumbnailCache(
                new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") }, directory);

            await Assert.ThrowsAsync<InvalidDataException>(() => cache.CacheAsync("https://evil.example/image.jpg"));
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
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

    private static ExerciseSummaryDto Summary(Guid id, string name, BodyPart bodyPart) => new(
        id, name, bodyPart, TrackingMode.Weighted, null, null, null, null, false);

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
}
