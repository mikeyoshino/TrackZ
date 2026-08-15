using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Exercises;
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class SetLoggerViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 2, 0, 0, TimeSpan.Zero);
    private static readonly Guid ExerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"trackz-logger-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Match_last_uses_corresponding_next_set_in_original_order()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var history = Previous(TrackingMode.Weighted,
            Set(0, 70m, null, 10), Set(1, 70m, null, 9), Set(2, 67.5m, null, 10));
        var sut = fixture.CreateLogger(history);
        await sut.LoadAsync(ExerciseId, "Bench Press");

        sut.MatchLastCommand.Execute(null);
        Assert.Equal(70m, sut.WeightKg);
        Assert.Equal(10, sut.Reps);
        await sut.CompleteSetCommand.ExecuteAsync();
        sut.MatchLastCommand.Execute(null);

        Assert.Equal(70m, sut.WeightKg);
        Assert.Equal(9, sut.Reps);
        Assert.Equal([10, 9, 10], sut.LastSets.Select(item => item.Reps));
    }

    [Theory]
    [InlineData(TrackingMode.Weighted, 82.375, null, 8)]
    [InlineData(TrackingMode.Bodyweight, null, null, 14)]
    [InlineData(TrackingMode.Assisted, null, 27.625, 11)]
    public async Task Match_last_preserves_the_measurement_shape_for_each_tracking_mode(
        TrackingMode mode,
        double? weight,
        double? assisted,
        int reps)
    {
        var fixture = await CreateFixtureAsync(mode);
        var sut = fixture.CreateLogger(Previous(
            mode, Set(0, Decimal(weight), Decimal(assisted), reps)));
        await sut.LoadAsync(ExerciseId, "Exercise");

        sut.MatchLastCommand.Execute(null);

        Assert.Equal(Decimal(weight), sut.WeightKg);
        Assert.Equal(Decimal(assisted), sut.AssistedKg);
        Assert.Equal(reps, sut.Reps);
    }

    [Fact]
    public async Task Pound_display_conversion_never_replaces_canonical_kilograms()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var sut = fixture.CreateLogger(Previous(TrackingMode.Weighted, Set(0, 100m, null, 5)));
        await sut.LoadAsync(ExerciseId, "Bench Press");
        sut.MatchLastCommand.Execute(null);

        sut.DisplayUnit = WeightDisplayUnit.Pounds;

        Assert.Equal(220.46m, sut.DisplayWeight);
        Assert.Equal(100m, sut.WeightKg);
        await sut.CompleteSetCommand.ExecuteAsync();
        var saved = Assert.Single(Assert.Single((await fixture.Repository.GetActiveAsync())!.Exercises).Sets);
        Assert.Equal(100m, saved.WeightKg);
    }

    [Fact]
    public async Task Celebration_starts_only_after_durable_save_and_visible_state_update()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        SetLoggerViewModel? sut = null;
        fixture.Feedback.OnSaved = async () =>
        {
            var restored = await fixture.Repository.GetActiveAsync();
            Assert.Single(Assert.Single(restored!.Exercises).Sets);
            Assert.Single(sut!.TodaySets);
        };
        sut = fixture.CreateLogger(Previous(TrackingMode.Weighted, Set(0, 70m, null, 10)));
        await sut.LoadAsync(ExerciseId, "Bench Press");
        sut.WeightKg = 72.5m;
        sut.Reps = 8;

        await sut.CompleteSetCommand.ExecuteAsync();

        Assert.Equal(1, fixture.Feedback.CallCount);
        Assert.Equal(72.5m, Assert.Single(sut.TodaySets).WeightKg);
    }

    [Fact]
    public async Task Rapid_taps_create_one_durable_set_and_one_feedback()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        fixture.Feedback.Block = true;
        var sut = fixture.CreateLogger(null);
        await sut.LoadAsync(ExerciseId, "Bench Press");
        sut.WeightKg = 60m;
        sut.Reps = 12;

        var first = sut.CompleteSetCommand.ExecuteAsync();
        await fixture.Feedback.Entered.Task;
        var second = sut.CompleteSetCommand.ExecuteAsync();
        fixture.Feedback.Release.TrySetResult();
        await Task.WhenAll(first, second);

        var restored = await fixture.Repository.GetActiveAsync();
        Assert.Single(Assert.Single(restored!.Exercises).Sets);
        Assert.Equal(1, fixture.Feedback.CallCount);
    }

    [Fact]
    public async Task Network_sync_starts_after_feedback_and_never_delays_local_completion()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var sync = new BlockingSyncRunner(() => Assert.Equal(1, fixture.Feedback.CallCount));
        var sut = fixture.CreateLogger(null, sync: sync);
        await sut.LoadAsync(ExerciseId, "Bench Press");
        sut.WeightKg = 60m;
        sut.Reps = 12;

        await sut.CompleteSetCommand.ExecuteAsync();
        await sync.Entered.Task;

        Assert.Equal(WorkoutSyncState.Syncing, sut.SyncState);
        Assert.False(sut.IsBusy);
        sync.Release.TrySetResult();
        await sut.SyncCompletion;
    }

    [Fact]
    public async Task Durable_save_failure_keeps_today_empty_and_never_celebrates()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        await CreateSaveFailureTriggerAsync();
        var sut = fixture.CreateLogger(null);
        await sut.LoadAsync(ExerciseId, "Bench Press");
        sut.WeightKg = 70m;
        sut.Reps = 10;

        await sut.CompleteSetCommand.ExecuteAsync();

        Assert.Empty(sut.TodaySets);
        Assert.Equal(0, fixture.Feedback.CallCount);
        Assert.NotNull(sut.ErrorMessage);
        Assert.Empty(Assert.Single((await fixture.Repository.GetActiveAsync())!.Exercises).Sets);
    }

    [Fact]
    public async Task Feedback_failure_does_not_turn_a_durable_save_into_an_apparent_failure()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        fixture.Feedback.Throw = true;
        var sut = fixture.CreateLogger(null);
        await sut.LoadAsync(ExerciseId, "Bench Press");
        sut.WeightKg = 70m;
        sut.Reps = 10;

        await sut.CompleteSetCommand.ExecuteAsync();

        var persisted = Assert.Single(Assert.Single((await fixture.Repository.GetActiveAsync())!.Exercises).Sets);
        Assert.Equal(persisted.Id, Assert.Single(sut.TodaySets).Id);
        Assert.Equal(1, fixture.Feedback.CallCount);
        Assert.Null(sut.ErrorMessage);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task Offline_load_uses_cached_exact_previous_sets_without_an_error_toast()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Bodyweight);
        var history = Previous(TrackingMode.Bodyweight, Set(0, null, null, 12), Set(1, null, null, 9));
        var sut = fixture.CreateLogger(history, online: false);

        await sut.LoadAsync(ExerciseId, "Pull-up");

        Assert.Equal([12, 9], sut.LastSets.Select(item => item.Reps));
        Assert.Equal(WorkoutSyncState.Offline, sut.SyncState);
        Assert.Null(sut.ErrorMessage);
    }

    [Fact]
    public async Task Malformed_history_failure_is_exposed_as_safe_localized_state_instead_of_escaping()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var sut = new SetLoggerViewModel(
            fixture.Coordinator,
            new FailingHistory(),
            fixture.Feedback,
            fixture.Boundary,
            new StubConnectivity(true),
            new OutboxRepository(fixture.Database),
            WorkoutResources.English);

        await sut.LoadAsync(ExerciseId, "Bench Press");

        Assert.Equal("Could not load workout details", sut.ErrorMessage);
        Assert.False(sut.IsBusy);
        Assert.Equal(0, fixture.Feedback.CallCount);
    }

    [Fact]
    public async Task Restore_displays_today_sets_in_persisted_order_and_targets_the_next_previous_index()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Assisted);
        await fixture.Coordinator.SaveSetAsync(ExerciseId, new LocalSet(null, 30m, 10));
        await fixture.Coordinator.SaveSetAsync(ExerciseId, new LocalSet(null, 25m, 8));
        var sut = fixture.CreateLogger(Previous(TrackingMode.Assisted,
            Set(0, null, 32.5m, 10), Set(1, null, 27.5m, 9), Set(2, null, 25m, 7)));

        await sut.LoadAsync(ExerciseId, "Assisted Pull-up");
        sut.MatchLastCommand.Execute(null);

        Assert.Equal([30m, 25m], sut.TodaySets.Select(item => item.AssistedKg));
        Assert.Equal(25m, sut.AssistedKg);
        Assert.Equal(7, sut.Reps);
    }

    [Fact]
    public async Task Account_reset_during_feedback_clears_private_view_state_without_a_second_save()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        fixture.Feedback.Block = true;
        var sut = fixture.CreateLogger(null);
        await sut.LoadAsync(ExerciseId, "Bench Press");
        sut.WeightKg = 55m;
        sut.Reps = 10;

        var save = sut.CompleteSetCommand.ExecuteAsync();
        await fixture.Feedback.Entered.Task;
        var reset = fixture.Boundary.ResetAsync(fixture.Coordinator.ClearPrivateDataAsync);
        fixture.Feedback.Release.TrySetResult();
        await Task.WhenAll(save, reset);

        Assert.Empty(sut.TodaySets);
        Assert.Empty(sut.LastSets);
        Assert.Null(sut.ErrorMessage);
        Assert.Null(await fixture.Repository.GetActiveAsync());
        Assert.Equal(1, fixture.Feedback.CallCount);
    }

    [Fact]
    public async Task Account_reset_before_durable_commit_cancels_save_without_feedback()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var blocking = new BlockingWorkoutRepository(fixture.Repository);
        var coordinator = new ActiveWorkoutCoordinator(blocking, fixture.Boundary, new FixedClock());
        var sut = new SetLoggerViewModel(
            coordinator,
            new StubHistory(null),
            fixture.Feedback,
            fixture.Boundary,
            new StubConnectivity(true),
            new OutboxRepository(fixture.Database),
            WorkoutResources.English);
        await sut.LoadAsync(ExerciseId, "Bench Press");
        sut.WeightKg = 55m;
        sut.Reps = 10;

        var save = sut.CompleteSetCommand.ExecuteAsync();
        await blocking.Entered.Task;
        var reset = fixture.Boundary.ResetAsync(coordinator.ClearPrivateDataAsync);
        blocking.Release.TrySetResult();
        await Task.WhenAll(save, reset);

        Assert.Equal(0, fixture.Feedback.CallCount);
        Assert.Empty(sut.TodaySets);
        Assert.Null(await fixture.Repository.GetActiveAsync());
    }

    [Theory]
    [InlineData(TrackingMode.Weighted, "0", null, 10)]
    [InlineData(TrackingMode.Weighted, "1.0001", null, 10)]
    [InlineData(TrackingMode.Assisted, null, "100000", 10)]
    [InlineData(TrackingMode.Bodyweight, null, null, 0)]
    [InlineData(TrackingMode.Bodyweight, null, null, 1000)]
    public async Task Domain_bounds_scale_and_mode_disable_invalid_local_completion(
        TrackingMode mode,
        string? weight,
        string? assisted,
        int reps)
    {
        var fixture = await CreateFixtureAsync(mode);
        var sut = fixture.CreateLogger(null);
        await sut.LoadAsync(ExerciseId, "Exercise");
        sut.WeightKg = weight is null ? null : decimal.Parse(weight, CultureInfo.InvariantCulture);
        sut.AssistedKg = assisted is null ? null : decimal.Parse(assisted, CultureInfo.InvariantCulture);
        sut.Reps = reps;

        Assert.False(sut.CompleteSetCommand.CanExecute(null));
    }

    [Fact]
    public async Task Thai_validation_and_status_copy_comes_from_localized_resources()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var sut = fixture.CreateLogger(null, online: false, culture: CultureInfo.GetCultureInfo("th-TH"));
        await sut.LoadAsync(ExerciseId, "Bench Press");

        Assert.Equal("ออฟไลน์ · บันทึกไว้ในเครื่อง", sut.SyncStatusText);
        Assert.Equal("กรอกน้ำหนักและจำนวนครั้งที่ถูกต้อง", sut.ValidationMessage);
    }

    [Fact]
    public async Task Draft_reorder_preserves_stable_selection_order_in_the_single_start_operation()
    {
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var third = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var fixture = CreateFixture();
        var cachePath = Path.Combine(Path.GetTempPath(), $"trackz-draft-{Guid.NewGuid():N}.db");
        try
        {
            var cache = new ExerciseCache(cachePath);
            await cache.ReplaceAllAsync([
                Summary(ExerciseId, "Bench Press", TrackingMode.Weighted),
                Summary(second, "Pull-up", TrackingMode.Bodyweight),
                Summary(third, "Assisted Dip", TrackingMode.Assisted)
            ], Now);
            var sut = new WorkoutViewModel(fixture.Coordinator, cache, fixture.Boundary, WorkoutResources.English);

            await sut.AddExercisesAsync([second, ExerciseId, third]);
            sut.MoveUpCommand.Execute(third);
            sut.RemoveExerciseCommand.Execute(ExerciseId);
            await sut.StartWorkoutCommand.ExecuteAsync();

            var active = await fixture.Repository.GetActiveAsync();
            Assert.Equal([second, third], active!.Exercises.OrderBy(item => item.Order).Select(item => item.ExerciseDefinitionId));
            var operation = Assert.Single(await new OutboxRepository(fixture.Database).PendingAsync());
            Assert.Equal(OutboxOperationType.StartWorkout, operation.Type);
            Assert.Equal([second, third], operation.DeserializePayload<StartWorkoutOutboxPayload>()
                .Exercises.OrderBy(item => item.Order).Select(item => item.ExerciseDefinitionId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }
    }

    [Fact]
    public async Task Exact_previous_session_http_response_is_cached_and_available_offline_in_set_order()
    {
        var firstId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var secondId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var handler = new QueueHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""
                {"items":[{"workoutId":"cccccccc-cccc-cccc-cccc-cccccccccccc","completedAt":"2026-08-14T02:00:00+00:00","trackingMode":1,"weightedVolumeKg":1327.5,"sets":[{"id":"{{firstId}}","order":0,"weightKg":70.125,"assistedKg":null,"reps":10,"completedAt":"2026-08-14T02:10:00+00:00","updatedAt":null},{"id":"{{secondId}}","order":1,"weightKg":69.375,"assistedKg":null,"reps":9,"completedAt":"2026-08-14T02:12:00+00:00","updatedAt":null}]}],"nextCursor":"unused"}
                """, Encoding.UTF8, "application/json")
        });
        var api = new TrackZExerciseApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") });
        var historyPath = Path.Combine(Path.GetTempPath(), $"trackz-history-{Guid.NewGuid():N}.db");
        try
        {
            var cache = new ExerciseHistoryCache(historyPath);
            var online = new CachedExerciseHistorySource(cache, api, new AccountSessionBoundary());

            var refreshed = await online.GetMostRecentAsync(ExerciseId, refreshIfOnline: true);
            var offline = await new CachedExerciseHistorySource(
                new ExerciseHistoryCache(historyPath), new ThrowingHistoryApi(), new AccountSessionBoundary())
                .GetMostRecentAsync(ExerciseId, refreshIfOnline: false);

            Assert.Equal("/api/v1/exercises/11111111-1111-1111-1111-111111111111/history?pageSize=1", handler.Path);
            Assert.Equal([firstId, secondId], refreshed!.Sets.Select(item => item.Id));
            Assert.Equal([70.125m, 69.375m], offline!.Sets.Select(item => item.WeightKg));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(historyPath)) File.Delete(historyPath);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private async Task<Fixture> CreateFixtureAsync(TrackingMode mode)
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(ExerciseId, mode)]);
        return fixture;
    }

    private Fixture CreateFixture()
    {
        var database = new TrackZLocalDatabase(_databasePath);
        var repository = new LocalWorkoutRepository(database);
        var boundary = new AccountSessionBoundary();
        return new Fixture(
            database,
            repository,
            boundary,
            new ActiveWorkoutCoordinator(repository, boundary, new FixedClock()),
            new RecordingFeedback());
    }

    private async Task CreateSaveFailureTriggerAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER FailLoggerSave
            BEFORE INSERT ON OutboxOperation
            WHEN NEW.OperationType = 2
            BEGIN SELECT RAISE(ABORT, 'logger save failed'); END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static ExerciseHistorySessionDto Previous(TrackingMode mode, params WorkoutSetDto[] sets) =>
        new(Guid.NewGuid(), Now.AddDays(-2), mode, 0m, sets);

    private static WorkoutSetDto Set(int order, decimal? weight, decimal? assisted, int reps) =>
        new(Guid.NewGuid(), order, weight, assisted, reps, Now.AddDays(-2).AddMinutes(order), null);

    private static ExerciseSummaryDto Summary(Guid id, string name, TrackingMode mode) =>
        new(id, name, BodyPart.Chest, mode, null, null, null, null, false);

    private static decimal? Decimal(double? value) => value is null ? null : Convert.ToDecimal(value.Value);

    private sealed record Fixture(
        TrackZLocalDatabase Database,
        LocalWorkoutRepository Repository,
        AccountSessionBoundary Boundary,
        ActiveWorkoutCoordinator Coordinator,
        RecordingFeedback Feedback)
    {
        public SetLoggerViewModel CreateLogger(
            ExerciseHistorySessionDto? previous,
            bool online = true,
            CultureInfo? culture = null,
            IWorkoutSyncRunner? sync = null) => new(
                Coordinator,
                new StubHistory(previous),
                Feedback,
                Boundary,
                new StubConnectivity(online),
                new OutboxRepository(Database),
                WorkoutResources.ForCulture(culture ?? CultureInfo.GetCultureInfo("en-US")),
                syncRunner: sync);
    }

    private sealed class StubHistory(ExerciseHistorySessionDto? previous) : IExerciseHistorySource
    {
        public Task<ExerciseHistorySessionDto?> GetMostRecentAsync(
            Guid exerciseId,
            bool refreshIfOnline,
            CancellationToken cancellationToken = default) => Task.FromResult(previous);
    }

    private sealed class FailingHistory : IExerciseHistorySource
    {
        public Task<ExerciseHistorySessionDto?> GetMostRecentAsync(
            Guid exerciseId,
            bool refreshIfOnline,
            CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("malformed history");
    }

    private sealed class RecordingFeedback : ISetSavedFeedback
    {
        public int CallCount { get; private set; }
        public bool Block { get; set; }
        public bool Throw { get; set; }
        public Func<Task>? OnSaved { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SetSavedAsync(LocalSet savedSet, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Throw) throw new InvalidOperationException("Feedback unavailable");
            if (OnSaved is not null) await OnSaved();
            Entered.TrySetResult();
            if (Block) await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class StubConnectivity(bool online) : IConnectivityService
    {
        public bool IsOnline { get; } = online;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class BlockingWorkoutRepository(ILocalWorkoutRepository inner) : ILocalWorkoutRepository
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SaveWorkoutAndEnqueueAsync(
            LocalWorkout workout,
            OutboxOperation operation,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            await inner.SaveWorkoutAndEnqueueAsync(workout, operation, cancellationToken);
        }

        public Task<LocalWorkout?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            inner.GetActiveAsync(cancellationToken);
        public Task<OutboxOperation?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default) =>
            inner.GetOperationAsync(operationId, cancellationToken);
        public Task<DateTimeOffset?> GetLatestOperationCreatedAtAsync(CancellationToken cancellationToken = default) =>
            inner.GetLatestOperationCreatedAtAsync(cancellationToken);
        public Task ClearPrivateDataAsync(CancellationToken cancellationToken = default) =>
            inner.ClearPrivateDataAsync(cancellationToken);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class BlockingSyncRunner(Action onEntered) : IWorkoutSyncRunner
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SyncRunStatus> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            onEntered();
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return SyncRunStatus.Completed;
        }
    }

    private sealed class QueueHttpHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.PathAndQuery;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHistoryApi : IExerciseHistoryApi
    {
        public Task<ExerciseHistorySessionDto?> GetMostRecentAsync(
            Guid exerciseId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Offline API must not be called.");
    }
}
