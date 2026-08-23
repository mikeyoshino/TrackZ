using System.Globalization;
using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Shared;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class WorkoutViewModelTests
{
    private sealed class MemoryPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    [Fact]
    public async Task Restored_active_workout_reports_only_sets_actually_logged()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(fixture.SecondId, TrackingMode.Bodyweight)
        ]);
        await fixture.Coordinator.SaveSetAsync(fixture.FirstId, new LocalSet(50, null, 8));
        await fixture.Coordinator.SaveSetAsync(fixture.FirstId, new LocalSet(55, null, 6));
        var viewModel = fixture.CreateViewModel();

        await viewModel.RestoreAsync();

        Assert.Equal([2, 0], viewModel.Exercises.Select(item => item.LoggedSetCount));
        Assert.Equal(["2 sets", "0 sets"], viewModel.Exercises.Select(item => item.LoggedSetText));
        Assert.Equal("2 exercises · 2 sets logged", viewModel.WorkoutContextText);
        Assert.Equal(
            ["Press, 2 sets. Open set logger.",
             "Pull-up, 0 sets. Open set logger."],
            viewModel.Exercises.Select(item => item.AccessibilitySummary));
        Assert.All(viewModel.Exercises, item =>
        {
            Assert.DoesNotContain(" of ", item.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LAST", item.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Weight", item.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Open set logger", item.AccessibilitySummary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Set_count_and_open_action_copy_are_localized_in_english_and_thai()
    {
        var english = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("en-US"));
        var thai = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

        Assert.Equal("1 set", string.Format(english.SetCountSingularFormat, 1));
        Assert.Equal("2 sets", string.Format(english.SetCountPluralFormat, 2));
        Assert.Equal("1 เซ็ต", string.Format(thai.SetCountSingularFormat, 1));
        Assert.Equal("2 เซ็ต", string.Format(thai.SetCountPluralFormat, 2));
        Assert.Equal("Press, 2 sets. Open set logger.", string.Format(
            english.OpenSetLoggerAccessibilityFormat, "Press", "2 sets"));
        Assert.Equal("Press, 2 เซ็ต เปิดหน้าบันทึกเซ็ต", string.Format(
            thai.OpenSetLoggerAccessibilityFormat, "Press", "2 เซ็ต"));
        Assert.Equal("2 sets logged", string.Format(english.SetsLoggedFormat, 2));
    }

    [Fact]
    public async Task Restored_active_workout_revalidates_a_stale_local_artwork_path_against_the_remote_route()
    {
        const string route = "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail";
        const string staleThumbnail = "/deleted-cache/press.png";
        const string refreshedThumbnail = "/bounded-cache/press-refreshed.png";
        await using var fixture = await Fixture.CreateAsync(thumbnailRoute: route);
        await fixture.Cache.SetServerThumbnailAsync(fixture.FirstId, staleThumbnail);
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted)
        ]);
        var thumbnails = new RecordingThumbnailCache(refreshedThumbnail);
        var viewModel = fixture.CreateViewModel(thumbnailCache: thumbnails);

        await viewModel.RestoreAsync();

        Assert.Equal([route], thumbnails.RequestedRoutes);
        Assert.Equal(refreshedThumbnail, viewModel.Exercises.Single().ThumbnailUri);
    }

    [Fact]
    public async Task Active_reorder_and_remove_remain_durable_after_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(fixture.SecondId, TrackingMode.Bodyweight),
            new WorkoutExerciseSelection(fixture.ThirdId, TrackingMode.Assisted)
        ]);
        var viewModel = fixture.CreateViewModel();
        await viewModel.RestoreAsync();
        var second = viewModel.Exercises.Single(item => item.ExerciseDefinitionId == fixture.SecondId);
        var third = viewModel.Exercises.Single(item => item.ExerciseDefinitionId == fixture.ThirdId);

        await viewModel.MoveUpCommand.ExecuteAsync(third);
        third = viewModel.Exercises.Single(item => item.ExerciseDefinitionId == fixture.ThirdId);
        await viewModel.MoveUpCommand.ExecuteAsync(third);
        await viewModel.RemoveExerciseCommand.ExecuteAsync(second);
        var restored = fixture.CreateViewModel();
        await restored.RestoreAsync();

        Assert.Equal(
            [fixture.ThirdId, fixture.FirstId],
            restored.Exercises.Select(item => item.ExerciseDefinitionId));
    }

    [Fact]
    public async Task Restored_last_performance_follows_the_shared_weight_unit_preference()
    {
        await using var fixture = await Fixture.CreateAsync(lastWeightKg: 70.125m);
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted)
        ]);
        var preferences = new WeightUnitPreference(new MemoryPreferenceStore());
        var viewModel = fixture.CreateViewModel(preferences);

        await viewModel.RestoreAsync();
        Assert.Contains("70.125", viewModel.Exercises.Single().LastText, StringComparison.Ordinal);
        Assert.Contains("kg", viewModel.Exercises.Single().LastText, StringComparison.Ordinal);

        preferences.Set(WeightDisplayUnit.Pounds);

        Assert.Contains("154.60", viewModel.Exercises.Single().LastText, StringComparison.Ordinal);
        Assert.Contains("lb", viewModel.Exercises.Single().LastText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restored_active_workout_materializes_and_persists_missing_api_artwork()
    {
        const string route = "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail";
        const string localThumbnail = "/bounded-cache/press.png";
        await using var fixture = await Fixture.CreateAsync(thumbnailRoute: route);
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted)
        ]);
        var thumbnails = new RecordingThumbnailCache(localThumbnail);
        var viewModel = fixture.CreateViewModel(thumbnailCache: thumbnails);

        await viewModel.RestoreAsync();

        Assert.Equal([route], thumbnails.RequestedRoutes);
        Assert.Equal(localThumbnail, viewModel.Exercises.Single().ThumbnailUri);
        Assert.Equal(localThumbnail, (await fixture.Cache.GetAllAsync())
            .Single(item => item.Id == fixture.FirstId).ThumbnailUri);
    }

    [Fact]
    public async Task Newly_selected_workout_exercise_materializes_missing_api_artwork()
    {
        const string route = "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail";
        const string localThumbnail = "/bounded-cache/press.png";
        await using var fixture = await Fixture.CreateAsync(thumbnailRoute: route);
        var thumbnails = new RecordingThumbnailCache(localThumbnail);
        var viewModel = fixture.CreateViewModel(thumbnailCache: thumbnails);

        await viewModel.AddExercisesAsync([fixture.FirstId]);

        Assert.Equal([route], thumbnails.RequestedRoutes);
        Assert.Equal(localThumbnail, viewModel.Exercises.Single().ThumbnailUri);
    }

    [Fact]
    public async Task Successful_restore_clears_a_stale_mutation_error_before_presenting_the_queue()
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewModel = fixture.CreateViewModel();
        await viewModel.AddExercisesAsync([fixture.FirstId]);
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.SecondId, TrackingMode.Bodyweight)
        ]);

        await viewModel.StartWorkoutCommand.ExecuteAsync();
        Assert.Equal(WorkoutResources.English.SaveFailed, viewModel.ErrorMessage);
        Assert.Equal(TrackZNoticeSeverity.Error, viewModel.NoticeSeverity);
        Assert.Equal(WorkoutResources.English.SaveFailed, viewModel.NoticeMessage);

        await viewModel.RestoreAsync();

        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasNotice);
        Assert.Equal(fixture.SecondId, Assert.Single(viewModel.Exercises).ExerciseDefinitionId);
    }

    [Fact]
    public async Task Finish_notifies_navigation_only_after_the_completed_workout_is_durable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var started = await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted)
        ]);
        await fixture.Coordinator.SaveSetAsync(fixture.FirstId, new LocalSet(50, null, 8));
        var viewModel = fixture.CreateViewModel();
        await viewModel.RestoreAsync();
        Guid? notified = null;
        viewModel.WorkoutFinished += (_, workoutId) => notified = workoutId;

        await viewModel.FinishWorkoutCommand.ExecuteAsync();

        Assert.Equal(started.Id, notified);
        Assert.Equal(started.Id, viewModel.CompletedWorkoutId);
        Assert.Null(await fixture.Coordinator.RestoreActiveAsync());
    }

    [Fact]
    public async Task Finish_with_an_unlogged_exercise_keeps_the_workout_active_and_presents_a_warning()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(fixture.SecondId, TrackingMode.Bodyweight)
        ]);
        await fixture.Coordinator.SaveSetAsync(fixture.FirstId, new LocalSet(50, null, 8));
        var viewModel = fixture.CreateViewModel();
        await viewModel.RestoreAsync();

        await viewModel.FinishWorkoutCommand.ExecuteAsync();

        Assert.True(viewModel.HasStarted);
        Assert.Null(viewModel.CompletedWorkoutId);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(TrackZNoticeSeverity.Warning, viewModel.NoticeSeverity);
        Assert.Equal(
            "Log at least 1 set for every exercise before finishing · 1 remaining",
            viewModel.NoticeMessage);
        Assert.NotNull(await fixture.Coordinator.RestoreActiveAsync());
    }

    [Fact]
    public async Task Warning_can_be_dismissed_and_is_presented_again_if_finish_is_retried()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted)
        ]);
        var viewModel = fixture.CreateViewModel();
        await viewModel.RestoreAsync();
        await viewModel.FinishWorkoutCommand.ExecuteAsync();

        viewModel.DismissNoticeCommand.Execute(null);

        Assert.False(viewModel.HasNotice);
        Assert.Null(viewModel.NoticeMessage);

        await viewModel.FinishWorkoutCommand.ExecuteAsync();

        Assert.True(viewModel.HasNotice);
        Assert.Equal(TrackZNoticeSeverity.Warning, viewModel.NoticeSeverity);
    }

    [Fact]
    public void Finish_warning_copy_is_localized_in_English_and_Thai()
    {
        var english = WorkoutResources.English.FinishNeedsSetsFormat;
        var thai = WorkoutResources.ForCulture(
            System.Globalization.CultureInfo.GetCultureInfo("th-TH")).FinishNeedsSetsFormat;

        Assert.Equal(
            "Log at least 1 set for every exercise before finishing · {0} remaining",
            english);
        Assert.Equal(
            "บันทึกอย่างน้อย 1 เซ็ตให้ครบทุกท่าก่อนจบการฝึก · เหลือ {0} ท่า",
            thai);
        Assert.Equal("Dismiss", WorkoutResources.English.DismissNotice);
        Assert.Equal(
            "ปิดข้อความ",
            WorkoutResources.ForCulture(
                System.Globalization.CultureInfo.GetCultureInfo("th-TH")).DismissNotice);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly AccountSessionBoundary _boundary;
        private readonly ExerciseCache _cache;

        private Fixture(
            string root,
            AccountSessionBoundary boundary,
            ExerciseCache cache,
            ActiveWorkoutCoordinator coordinator,
            Guid firstId,
            Guid secondId,
            Guid thirdId)
        {
            _root = root;
            _boundary = boundary;
            _cache = cache;
            Coordinator = coordinator;
            FirstId = firstId;
            SecondId = secondId;
            ThirdId = thirdId;
        }

        public ActiveWorkoutCoordinator Coordinator { get; }
        public ExerciseCache Cache => _cache;
        public Guid FirstId { get; }
        public Guid SecondId { get; }
        public Guid ThirdId { get; }

        public static async Task<Fixture> CreateAsync(
            decimal? lastWeightKg = null,
            string? thumbnailRoute = null)
        {
            var root = Path.Combine(Path.GetTempPath(), $"trackz-workout-vm-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();
            var cache = new ExerciseCache(Path.Combine(root, "exercises.db"));
            await cache.ReplaceAllAsync([
                Summary(first, "Press", TrackingMode.Weighted, lastWeightKg, thumbnailRoute),
                Summary(second, "Pull-up", TrackingMode.Bodyweight),
                Summary(third, "Assisted Dip", TrackingMode.Assisted)
            ], DateTimeOffset.UtcNow);
            var boundary = new AccountSessionBoundary();
            var repository = new LocalWorkoutRepository(
                new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            return new Fixture(
                root,
                boundary,
                cache,
                new ActiveWorkoutCoordinator(repository, boundary, new Clock()),
                first,
                second,
                third);
        }

        public WorkoutViewModel CreateViewModel(
            IWeightUnitPreference? unitPreference = null,
            IExerciseThumbnailCache? thumbnailCache = null) =>
            new(Coordinator, _cache, _boundary, WorkoutResources.English,
                unitPreference: unitPreference,
                thumbnailCache: thumbnailCache);

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private static ExerciseSummaryDto Summary(
            Guid id,
            string name,
            TrackingMode mode,
            decimal? lastWeightKg = null,
            string? thumbnailRoute = null) =>
            new(
                id,
                name,
                BodyPart.Chest,
                mode,
                thumbnailRoute,
                null,
                lastWeightKg is null ? null : new PerformanceSetDto(lastWeightKg, null, 8),
                null,
                false);

        private sealed class Clock : IClock
        {
            private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
            public DateTimeOffset UtcNow => new(Interlocked.Increment(ref _ticks), TimeSpan.Zero);
        }
    }

    private sealed class RecordingThumbnailCache(string localThumbnail) : IExerciseThumbnailCache
    {
        public List<string?> RequestedRoutes { get; } = [];

        public Task<string?> CacheAsync(
            string? thumbnailUri,
            CancellationToken cancellationToken = default)
        {
            RequestedRoutes.Add(thumbnailUri);
            return Task.FromResult<string?>(localThumbnail);
        }
    }
}
