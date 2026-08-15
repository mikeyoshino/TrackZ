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

        sut.UsePoundsCommand.Execute(null);

        Assert.Equal(220.46m, sut.DisplayWeight);
        Assert.Equal(100m, sut.WeightKg);
        await sut.CompleteSetCommand.ExecuteAsync();
        var saved = Assert.Single(Assert.Single((await fixture.Repository.GetActiveAsync())!.Exercises).Sets);
        Assert.Equal(100m, saved.WeightKg);
    }

    [Fact]
    public async Task Persisted_pound_preference_drives_entry_and_all_set_rows_while_storage_stays_kilograms()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        await fixture.Coordinator.SaveSetAsync(ExerciseId, new LocalSet(50m, null, 6));
        var store = new MemoryWorkoutPreferenceStore();
        var preference = new WeightUnitPreference(store);
        preference.Set(WeightDisplayUnit.Pounds);
        var sut = fixture.CreateLogger(
            Previous(TrackingMode.Weighted, Set(0, 50m, null, 6), Set(1, 100m, null, 5)),
            unitPreference: preference);

        await sut.LoadAsync(ExerciseId, "Bench Press");
        sut.MatchLastCommand.Execute(null);

        Assert.Equal(WeightDisplayUnit.Pounds, sut.DisplayUnit);
        Assert.Equal("220.46 lb × 5", sut.LastSets[1].MeasurementText);
        Assert.Equal("110.23 lb × 6", Assert.Single(sut.TodaySets).MeasurementText);
        Assert.Equal(220.46m, sut.DisplayWeight);
        sut.DisplayWeight = 176.37m;
        sut.Reps = 8;
        await sut.CompleteSetCommand.ExecuteAsync();
        var saved = (await fixture.Repository.GetActiveAsync())!.Exercises.Single().Sets.OrderBy(item => item.Order).Last();
        Assert.Equal(80m, saved.WeightKg);
        Assert.Null(saved.AssistedKg);
    }

    [Fact]
    public async Task Unit_selector_command_persists_across_logger_composition_and_uses_Thai_labels()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Assisted);
        var store = new MemoryWorkoutPreferenceStore();
        var first = fixture.CreateLogger(
            Previous(TrackingMode.Assisted, Set(0, null, 25.125m, 10)),
            culture: CultureInfo.GetCultureInfo("th-TH"),
            unitPreference: new WeightUnitPreference(store));
        await first.LoadAsync(ExerciseId, "Assisted Pull-up");

        first.UsePoundsCommand.Execute(null);
        var restoredPreference = new WeightUnitPreference(store);
        var restored = fixture.CreateLogger(
            Previous(TrackingMode.Assisted, Set(0, null, 25.125m, 10)),
            culture: CultureInfo.GetCultureInfo("th-TH"),
            unitPreference: restoredPreference);
        await restored.LoadAsync(ExerciseId, "Assisted Pull-up");

        Assert.Equal(WeightDisplayUnit.Pounds, restored.DisplayUnit);
        Assert.Equal("ปอนด์", restored.WeightUnitLabel);
        Assert.Equal("55.39 ปอนด์ · 10 ครั้ง", Assert.Single(restored.LastSets).MeasurementText);
        restored.UseKilogramsCommand.Execute(null);
        Assert.Equal(WeightDisplayUnit.Kilograms, new WeightUnitPreference(store).Current);
        Assert.Equal("25.125 กก. · 10 ครั้ง", Assert.Single(restored.LastSets).MeasurementText);
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
    public async Task Sync_status_refresh_failure_after_commit_keeps_the_set_successful_without_a_retry_write()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var status = new ControllableStatusSource(new OutboxRepository(fixture.Database));
        var sut = fixture.CreateLogger(null, status: status);
        await sut.LoadAsync(ExerciseId, "Bench Press");
        status.ThrowOnNextRead();
        sut.WeightKg = 70m;
        sut.Reps = 10;

        await sut.CompleteSetCommand.ExecuteAsync();

        Assert.Single(Assert.Single((await fixture.Repository.GetActiveAsync())!.Exercises).Sets);
        Assert.Single(sut.TodaySets);
        Assert.Equal(1, fixture.Feedback.CallCount);
        Assert.Null(sut.ErrorMessage);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task Account_reset_during_post_commit_status_refresh_never_runs_stale_feedback()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var status = new ControllableStatusSource(new OutboxRepository(fixture.Database));
        var sut = fixture.CreateLogger(null, status: status);
        await sut.LoadAsync(ExerciseId, "Bench Press");
        status.BlockNextRead();
        sut.WeightKg = 55m;
        sut.Reps = 10;

        var save = sut.CompleteSetCommand.ExecuteAsync();
        await status.Entered.Task;
        var reset = fixture.Boundary.ResetAsync(fixture.Coordinator.ClearPrivateDataAsync);
        status.Release.TrySetResult();
        await Task.WhenAll(save, reset);

        Assert.Equal(0, fixture.Feedback.CallCount);
        Assert.Empty(sut.TodaySets);
        Assert.Null(await fixture.Repository.GetActiveAsync());
        Assert.Null(sut.ErrorMessage);
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
    public async Task Permanently_rejected_current_workout_operation_survives_restart_in_a_distinct_status()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var workoutId = (await fixture.Repository.GetActiveAsync())!.Id;
        await ArchiveOperationsAsRejectedAsync(workoutId);
        var restartedDatabase = new TrackZLocalDatabase(_databasePath);
        var restartedRepository = new LocalWorkoutRepository(restartedDatabase);
        var restartedBoundary = new AccountSessionBoundary();
        var sut = new SetLoggerViewModel(
            new ActiveWorkoutCoordinator(restartedRepository, restartedBoundary, new FixedClock()),
            new StubHistory(null),
            fixture.Feedback,
            restartedBoundary,
            new StubConnectivity(true),
            new OutboxRepository(restartedDatabase),
            WorkoutResources.English);

        await sut.LoadAsync(ExerciseId, "Bench Press");

        Assert.Equal(WorkoutSyncState.PermanentFailure, sut.SyncState);
        Assert.Equal("Sync failed permanently", sut.SyncStatusText);
        Assert.NotEqual(WorkoutResources.English.Synced, sut.SyncStatusText);
    }

    [Fact]
    public async Task Rejected_operation_for_another_workout_is_not_projected_into_the_current_workout()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var unrelatedWorkoutId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        await InsertUnrelatedRejectedOperationAsync(unrelatedWorkoutId);
        var restartedDatabase = new TrackZLocalDatabase(_databasePath);
        var sut = new SetLoggerViewModel(
            fixture.Coordinator,
            new StubHistory(null),
            fixture.Feedback,
            fixture.Boundary,
            new StubConnectivity(true),
            new OutboxRepository(restartedDatabase),
            WorkoutResources.English);

        await sut.LoadAsync(ExerciseId, "Bench Press");

        Assert.Equal(WorkoutSyncState.Pending, sut.SyncState);
        Assert.Equal(WorkoutResources.English.Pending, sut.SyncStatusText);
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
    public async Task Stepper_accessibility_descriptions_are_localized_action_and_mode_specific()
    {
        var weightedFixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var weighted = weightedFixture.CreateLogger(null);
        await weighted.LoadAsync(ExerciseId, "Bench Press");

        Assert.Equal("Decrease weight", weighted.DecrementWeightDescription);
        Assert.Equal("Increase weight", weighted.IncrementWeightDescription);
        Assert.Equal("Decrease reps", weighted.DecrementRepsDescription);
        Assert.Equal("Increase reps", weighted.IncrementRepsDescription);
        Assert.NotEqual(weighted.DecrementWeightDescription, weighted.IncrementWeightDescription);

        await weightedFixture.Boundary.ResetAsync(weightedFixture.Coordinator.ClearPrivateDataAsync);
        await weightedFixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(ExerciseId, TrackingMode.Assisted)
        ]);
        var assisted = weightedFixture.CreateLogger(
            null,
            culture: CultureInfo.GetCultureInfo("th-TH"));
        await assisted.LoadAsync(ExerciseId, "Assisted Pull-up");

        Assert.Equal("ลดน้ำหนักช่วย", assisted.DecrementWeightDescription);
        Assert.Equal("เพิ่มน้ำหนักช่วย", assisted.IncrementWeightDescription);
        Assert.Equal("ลดจำนวนครั้ง", assisted.DecrementRepsDescription);
        Assert.Equal("เพิ่มจำนวนครั้ง", assisted.IncrementRepsDescription);
        Assert.NotEqual(assisted.DecrementWeightDescription, assisted.IncrementWeightDescription);
    }

    [Fact]
    public async Task Disposed_navigation_loggers_unsubscribe_and_commands_cannot_write_or_accumulate_refreshes()
    {
        var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
        var connectivity = new TrackingConnectivity();
        var status = new CountingStatusSource(new OutboxRepository(fixture.Database));
        var disposed = new List<SetLoggerViewModel>();
        for (var index = 0; index < 5; index++)
        {
            var logger = new SetLoggerViewModel(
                fixture.Coordinator,
                new StubHistory(null),
                fixture.Feedback,
                fixture.Boundary,
                connectivity,
                status,
                WorkoutResources.English);
            await logger.LoadAsync(ExerciseId, "Bench Press");
            logger.WeightKg = 60m;
            logger.Reps = 10;
            logger.Dispose();
            disposed.Add(logger);
        }

        var readsAfterNavigation = status.ReadCount;
        Assert.Equal(0, connectivity.SubscriberCount);
        connectivity.RaiseChanged();
        await Task.Delay(50);
        Assert.Equal(readsAfterNavigation, status.ReadCount);
        Assert.All(disposed, logger => Assert.False(logger.CompleteSetCommand.CanExecute(null)));
        await disposed[0].CompleteSetCommand.ExecuteAsync();
        Assert.Empty(Assert.Single((await fixture.Repository.GetActiveAsync())!.Exercises).Sets);

        await fixture.Boundary.ResetAsync(fixture.Coordinator.ClearPrivateDataAsync);
        Assert.All(disposed, logger => Assert.Equal("Bench Press", logger.ExerciseName));
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

    private async Task ArchiveOperationsAsRejectedAsync(Guid workoutId)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboxOperation
            SET State = 3, DeletedAt = '2026-08-16T02:30:00.0000000+00:00'
            WHERE EntityId = $workoutId;
            """;
        command.Parameters.AddWithValue("$workoutId", workoutId.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertUnrelatedRejectedOperationAsync(Guid workoutId)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var workout = connection.CreateCommand())
        {
            workout.Transaction = (SqliteTransaction)transaction;
            workout.CommandText = """
                INSERT INTO LocalWorkout
                    (Id, Status, StartedAt, CompletedAt, DeletedAt, Version, BaseVersion)
                VALUES ($id, 1, '2026-08-15T02:00:00.0000000+00:00', NULL, NULL, 1, 0);
                """;
            workout.Parameters.AddWithValue("$id", workoutId.ToString("D"));
            await workout.ExecuteNonQueryAsync();
        }
        await using (var operation = connection.CreateCommand())
        {
            operation.Transaction = (SqliteTransaction)transaction;
            operation.CommandText = """
                INSERT INTO OutboxOperation
                    (OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
                     State, DeletedAt, Version, RetryCount)
                VALUES ($operationId, $workoutId, 1, '{}', 0,
                        '2026-08-15T02:00:00.0000000+00:00', 3,
                        '2026-08-15T02:01:00.0000000+00:00', 2, 0);
                """;
            operation.Parameters.AddWithValue("$operationId", Guid.NewGuid().ToString("D"));
            operation.Parameters.AddWithValue("$workoutId", workoutId.ToString("D"));
            await operation.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
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
            IWorkoutSyncRunner? sync = null,
            IWorkoutOutboxStatusSource? status = null,
            IWeightUnitPreference? unitPreference = null) => new(
                Coordinator,
                new StubHistory(previous),
                Feedback,
                Boundary,
                new StubConnectivity(online),
                status ?? new OutboxRepository(Database),
                WorkoutResources.ForCulture(culture ?? CultureInfo.GetCultureInfo("en-US")),
                syncRunner: sync,
                unitPreference: unitPreference);
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

    private sealed class TrackingConnectivity : IConnectivityService
    {
        private EventHandler? _changed;
        public bool IsOnline => true;
        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;
        public event EventHandler? ConnectivityChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }
        public void RaiseChanged() => _changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class CountingStatusSource(IWorkoutOutboxStatusSource inner) : IWorkoutOutboxStatusSource
    {
        public int ReadCount { get; private set; }
        public Task<IReadOnlyList<OutboxOperation>> PendingAsync(Guid workoutId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return inner.PendingAsync(workoutId, cancellationToken);
        }
        public Task<IReadOnlyList<OutboxOperation>> ConflictedAsync(Guid workoutId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return inner.ConflictedAsync(workoutId, cancellationToken);
        }
        public Task<IReadOnlyList<OutboxOperation>> RejectedAsync(Guid workoutId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return inner.RejectedAsync(workoutId, cancellationToken);
        }
    }

    private sealed class MemoryWorkoutPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    private sealed class ControllableStatusSource(IWorkoutOutboxStatusSource inner) : IWorkoutOutboxStatusSource
    {
        private bool _block;
        private bool _throw;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void BlockNextRead() => _block = true;
        public void ThrowOnNextRead() => _throw = true;

        public async Task<IReadOnlyList<OutboxOperation>> PendingAsync(Guid workoutId, CancellationToken cancellationToken = default) =>
            await inner.PendingAsync(workoutId, cancellationToken);

        public async Task<IReadOnlyList<OutboxOperation>> ConflictedAsync(Guid workoutId, CancellationToken cancellationToken = default)
        {
            if (_throw)
            {
                _throw = false;
                throw new IOException("status refresh failed");
            }
            if (_block)
            {
                _block = false;
                Entered.TrySetResult();
                await Release.Task;
            }
            return await inner.ConflictedAsync(workoutId, cancellationToken);
        }

        public Task<IReadOnlyList<OutboxOperation>> RejectedAsync(
            Guid workoutId,
            CancellationToken cancellationToken = default) =>
            inner.RejectedAsync(workoutId, cancellationToken);
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
