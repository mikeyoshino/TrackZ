using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class ExerciseArtworkStateTests
{
    [Fact]
    public async Task Failed_remote_artwork_can_retry_only_that_item()
    {
        var attempts = 0;
        var item = new ExercisePickerItem(
            new CachedExercise
            {
                Id = Guid.NewGuid(),
                Name = "Shoulder Press",
                BodyPart = BodyPart.Shoulders,
                TrackingMode = TrackingMode.Weighted,
                RemoteThumbnailRoute = "/api/v1/media/exercise-images/1/thumbnail",
                LastSyncedAt = DateTimeOffset.UtcNow
            },
            WorkoutResources.English,
            retryArtwork: async _ =>
            {
                attempts++;
                await Task.Yield();
                return "/cache/shoulder-press.jpg";
            });
        item.SetArtworkFailed();

        Assert.Equal(ExerciseArtworkState.Failed, item.ArtworkState);
        await item.RetryArtworkCommand.ExecuteAsync();

        Assert.Equal(1, attempts);
        Assert.Equal(ExerciseArtworkState.Ready, item.ArtworkState);
        Assert.Equal("/cache/shoulder-press.jpg", item.ThumbnailUri);
        Assert.True(item.HasArtwork);
        Assert.False(item.ShowsArtworkPlaceholder);
    }

    [Fact]
    public async Task One_failed_thumbnail_does_not_hide_other_results_and_retries_only_it()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-artwork-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            const string failedRoute = "/api/v1/media/exercise-images/11111111-1111-1111-1111-111111111111/thumbnail";
            const string readyRoute = "/api/v1/media/exercise-images/22222222-2222-2222-2222-222222222222/thumbnail";
            var failedId = Guid.NewGuid();
            var readyId = Guid.NewGuid();
            var thumbnails = new SelectiveThumbnailCache(failedRoute);
            var viewModel = new ExercisePickerViewModel(
                new ExerciseCache(Path.Combine(root, "exercises.db")),
                new CatalogApi([
                    Summary(failedId, "Shoulder Press", failedRoute),
                    Summary(readyId, "Lateral Raise", readyRoute)
                ]),
                new OnlineConnectivity(),
                new FixedClock(),
                thumbnailCache: thumbnails);

            await viewModel.LoadAsync(BodyPart.Shoulders);
            await viewModel.RefreshCompletion;

            Assert.Equal(2, viewModel.Exercises.Count);
            Assert.Equal(ExerciseArtworkState.Failed,
                viewModel.Exercises.Single(item => item.Id == failedId).ArtworkState);
            Assert.Equal(ExerciseArtworkState.Ready,
                viewModel.Exercises.Single(item => item.Id == readyId).ArtworkState);
            viewModel.SearchText = "Shoulder";
            viewModel.SearchText = string.Empty;
            var failed = viewModel.Exercises.Single(item => item.Id == failedId);
            Assert.Equal(ExerciseArtworkState.Failed, failed.ArtworkState);

            await failed.RetryArtworkCommand.ExecuteAsync();

            Assert.Equal(ExerciseArtworkState.Ready, failed.ArtworkState);
            Assert.Equal(2, thumbnails.Attempts[failedRoute]);
            Assert.Equal(1, thumbnails.Attempts[readyRoute]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Catalog_refresh_persists_remote_route_and_preserves_local_thumbnail()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackz-route-{Guid.NewGuid():N}.db");
        try
        {
            const string route = "/api/v1/media/exercise-images/33333333-3333-3333-3333-333333333333/thumbnail";
            var id = Guid.NewGuid();
            var cache = new ExerciseCache(path);
            await cache.ReplaceAllAsync([Summary(id, "Press", route)], DateTimeOffset.UtcNow);
            await cache.SetServerThumbnailAsync(id, "/cache/press.jpg");

            await cache.ReplaceAllAsync([Summary(id, "Press renamed", route)], DateTimeOffset.UtcNow);

            var cached = Assert.Single(await cache.GetAllAsync());
            Assert.Equal(route, cached.RemoteThumbnailRoute);
            Assert.Equal("/cache/press.jpg", cached.ThumbnailUri);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Existing_cache_schema_is_upgraded_with_remote_route_column()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackz-route-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var create = connection.CreateCommand();
                create.CommandText = """
                    CREATE TABLE cached_exercises (
                        Id TEXT PRIMARY KEY NOT NULL, Name TEXT NOT NULL, BodyPart INTEGER NOT NULL,
                        TrackingMode INTEGER NOT NULL, ThumbnailUri TEXT NULL, LastPerformedAt TEXT NULL,
                        LastBestWeightKg TEXT NULL, LastBestAssistedKg TEXT NULL, LastBestReps INTEGER NULL,
                        AllTimeBestWeightKg TEXT NULL, AllTimeBestAssistedKg TEXT NULL, AllTimeBestReps INTEGER NULL,
                        IsCustom INTEGER NOT NULL, IsPendingSync INTEGER NOT NULL, LastSyncedAt TEXT NOT NULL,
                        LibraryImageId TEXT NULL);
                    INSERT INTO cached_exercises
                        (Id, Name, BodyPart, TrackingMode, ThumbnailUri, LastPerformedAt,
                         LastBestWeightKg, LastBestAssistedKg, LastBestReps,
                         AllTimeBestWeightKg, AllTimeBestAssistedKg, AllTimeBestReps,
                         IsCustom, IsPendingSync, LastSyncedAt, LibraryImageId)
                    VALUES
                        ('44444444-4444-4444-4444-444444444444', 'Legacy Press', 3, 1,
                         '/api/v1/media/exercise-images/55555555-5555-5555-5555-555555555555/thumbnail',
                         NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, 0,
                         '2026-08-20T12:00:00.0000000+00:00', NULL);
                    """;
                await create.ExecuteNonQueryAsync();
            }

            var upgraded = Assert.Single(await new ExerciseCache(path).GetAllAsync());

            Assert.Null(upgraded.ThumbnailUri);
            Assert.Equal(
                "/api/v1/media/exercise-images/55555555-5555-5555-5555-555555555555/thumbnail",
                upgraded.RemoteThumbnailRoute);

            await using var verify = new SqliteConnection($"Data Source={path};Pooling=False");
            await verify.OpenAsync();
            await using var command = verify.CreateCommand();
            command.CommandText = "PRAGMA table_info(cached_exercises);";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            Assert.Contains("RemoteThumbnailRoute", columns);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static ExerciseSummaryDto Summary(Guid id, string name, string route) =>
        new(id, name, BodyPart.Shoulders, TrackingMode.Weighted, route, null, null, null, false);

    private sealed class CatalogApi(IReadOnlyList<ExerciseSummaryDto> items) : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(items);
    }

    private sealed class OnlineConnectivity : IConnectivityService
    {
        public bool IsOnline => true;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class SelectiveThumbnailCache(string failFirstFor) : IExerciseThumbnailCache
    {
        public ConcurrentDictionary<string, int> Attempts { get; } = new();

        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default)
        {
            var route = thumbnailUri ?? throw new ArgumentNullException(nameof(thumbnailUri));
            var attempt = Attempts.AddOrUpdate(route, 1, (_, value) => value + 1);
            if (route == failFirstFor && attempt == 1) throw new IOException("fixture failure");
            return Task.FromResult<string?>($"/cache/{route.GetHashCode():x}.jpg");
        }
    }
}
