using System.Globalization;
using Microsoft.Data.Sqlite;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class ActiveWorkoutCoordinatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"trackz-workout-{Guid.NewGuid():N}.db");
    private readonly Guid _exerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Save_set_persists_exact_workout_graph_and_typed_outbox_before_returning()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)
        ]);

        var saved = await fixture.Coordinator.SaveSetAsync(
            _exerciseId,
            new LocalSet(70.125m, null, 10));

        var recreated = CreateFixture();
        var restored = await recreated.Coordinator.RestoreActiveAsync();
        var set = Assert.Single(Assert.Single(restored!.Exercises).Sets);
        Assert.Equal(saved.Id, set.Id);
        Assert.Equal(70.125m, set.WeightKg);
        Assert.Equal(10, set.Reps);
        Assert.Equal(TimeSpan.Zero, set.CompletedAt.Offset);

        var pending = await recreated.Outbox.PendingAsync();
        Assert.Equal(2, pending.Count);
        var operation = pending[1];
        Assert.Equal(saved.OperationId, operation.OperationId);
        Assert.Equal(OutboxOperationType.SaveSet, operation.Type);
        var payload = operation.DeserializePayload<SaveSetOutboxPayload>();
        Assert.Equal(saved.Id, payload.SetId);
        Assert.Equal("70.125", payload.WeightKg);
        Assert.Null(payload.AssistedKg);
        Assert.Equal(10, payload.Reps);
    }

    [Theory]
    [InlineData(TrackingMode.Weighted, "82.375", null)]
    [InlineData(TrackingMode.Bodyweight, null, null)]
    [InlineData(TrackingMode.Assisted, null, "27.625")]
    public async Task Restore_preserves_every_tracking_mode_and_exact_decimal_text(
        TrackingMode mode,
        string? weight,
        string? assisted)
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, mode)]);
        await fixture.Coordinator.SaveSetAsync(
            _exerciseId,
            new LocalSet(
                weight is null ? null : decimal.Parse(weight, CultureInfo.InvariantCulture),
                assisted is null ? null : decimal.Parse(assisted, CultureInfo.InvariantCulture),
                12));

        var restored = await CreateFixture().Coordinator.RestoreActiveAsync();
        var exercise = Assert.Single(restored!.Exercises);
        var set = Assert.Single(exercise.Sets);
        Assert.Equal(mode, exercise.TrackingMode);
        Assert.Equal(weight, set.WeightKg?.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(assisted, set.AssistedKg?.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_workout_graph_and_operation_together()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        await CreateOutboxFailureTriggerAsync();

        await Assert.ThrowsAsync<SqliteException>(() => fixture.Coordinator.SaveSetAsync(
            _exerciseId,
            new LocalSet(70m, null, 10)));

        var recreated = CreateFixture();
        var restored = await recreated.Coordinator.RestoreActiveAsync();
        Assert.Empty(Assert.Single(restored!.Exercises).Sets);
        Assert.Single(await recreated.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Replaying_same_operation_and_payload_is_idempotent_but_changed_contract_fails_closed()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        var workout = (await fixture.Repository.GetActiveAsync(default))!;
        var operation = Assert.Single(await fixture.Outbox.PendingAsync());

        await fixture.Repository.SaveWorkoutAndEnqueueAsync(workout, operation, default);
        Assert.Single(await fixture.Outbox.PendingAsync());

        var changed = operation with { Payload = operation.Payload + " " };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(workout, changed, default));
        Assert.Single(await fixture.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Different_snapshot_at_same_local_version_fails_closed_and_rolls_back_new_operation()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        var original = (await fixture.Repository.GetActiveAsync(default))!;
        var changed = original with { StartedAt = original.StartedAt.AddMinutes(5) };
        var operation = OutboxOperation.Create(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            changed.Id,
            OutboxOperationType.StartWorkout,
            new StartWorkoutOutboxPayload(changed.Id, changed.StartedAt, []),
            changed.BaseVersion,
            changed.StartedAt);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(changed, operation, default));

        Assert.Equal(original.StartedAt, (await fixture.Repository.GetActiveAsync(default))!.StartedAt);
        Assert.Single(await fixture.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Concurrent_saves_are_serialized_and_restore_contiguous_set_order()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);

        var first = new LocalSet(60m, null, 12);
        var second = new LocalSet(65m, null, 8);
        await Task.WhenAll(
            fixture.Coordinator.SaveSetAsync(_exerciseId, first),
            fixture.Coordinator.SaveSetAsync(_exerciseId, second));

        var restored = await CreateFixture().Coordinator.RestoreActiveAsync();
        var sets = Assert.Single(restored!.Exercises).Sets;
        Assert.Equal([0, 1], sets.Select(set => set.Order));
        Assert.Equal([first.Id, second.Id], sets.Select(set => set.Id));
    }

    [Fact]
    public async Task Database_rejects_a_second_active_workout()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);

        var second = ActiveWorkout(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var operation = OutboxOperation.Create(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            second.Id,
            OutboxOperationType.StartWorkout,
            new StartWorkoutOutboxPayload(second.Id, second.StartedAt, []),
            second.BaseVersion,
            Now);

        await Assert.ThrowsAsync<SqliteException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(second, operation, default));
        Assert.Single(await fixture.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Corrupt_decimal_row_fails_closed_instead_of_returning_partial_workout()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(75m, null, 5));
        await ExecuteRawAsync("UPDATE LocalSet SET WeightKg = 'not-a-decimal';");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateFixture().Coordinator.RestoreActiveAsync());
    }

    [Fact]
    public async Task Session_reset_clears_workout_and_outbox_and_stale_queued_write_cannot_repopulate_them()
    {
        var database = new TrackZLocalDatabase(_databasePath);
        var realRepository = new LocalWorkoutRepository(database);
        var blocking = new BlockingWorkoutRepository(realRepository);
        var boundary = new AccountSessionBoundary();
        var coordinator = new ActiveWorkoutCoordinator(blocking, boundary, new FixedClock(Now));

        var stale = coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        await blocking.Entered.Task;
        var reset = boundary.ResetAsync(coordinator.ClearPrivateDataAsync);
        blocking.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stale);
        await reset;
        Assert.Null(await new LocalWorkoutRepository(database).GetActiveAsync(default));
        Assert.Empty(await new OutboxRepository(database).PendingAsync());
    }

    [Fact]
    public async Task Restore_started_before_reset_never_returns_the_retired_session_workout()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        var blocking = new BlockingWorkoutRepository(fixture.Repository) { BlockReads = true };
        var coordinator = new ActiveWorkoutCoordinator(blocking, fixture.Boundary, new FixedClock(Now));

        var restore = coordinator.RestoreActiveAsync();
        await blocking.Entered.Task;
        var reset = fixture.Boundary.ResetAsync(coordinator.ClearPrivateDataAsync);
        blocking.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore);
        await reset;
        Assert.Null(await fixture.Repository.GetActiveAsync(default));
    }

    [Fact]
    public async Task Schema_initialization_is_idempotent_versioned_and_enforces_foreign_keys_after_restart()
    {
        var first = new TrackZLocalDatabase(_databasePath);
        await first.InitializeAsync();
        var second = new TrackZLocalDatabase(_databasePath);
        await second.InitializeAsync();

        await using var connection = await OpenRawAsync();
        await using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(TrackZLocalDatabase.CurrentSchemaVersion, Convert.ToInt32(await version.ExecuteScalarAsync()));

        await using var invalidChild = connection.CreateCommand();
        invalidChild.CommandText = """
            INSERT INTO LocalWorkoutExercise
                (Id, WorkoutId, ExerciseDefinitionId, TrackingMode, SortOrder, DeletedAt, Version, BaseVersion)
            VALUES
                ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                 'cccccccc-cccc-cccc-cccc-cccccccccccc', 1, 0, NULL, 1, 0);
            """;
        await Assert.ThrowsAsync<SqliteException>(() => invalidChild.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Pending_outbox_is_ordered_by_created_at_then_operation_id_and_retains_payload()
    {
        var fixture = CreateFixture();
        var workout = ActiveWorkout(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var laterId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var earlierId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var later = OutboxOperation.Create(
            laterId, workout.Id, OutboxOperationType.StartWorkout,
            new StartWorkoutOutboxPayload(workout.Id, workout.StartedAt, []), 0, Now);
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(workout, later, default);
        var earlier = later with { OperationId = earlierId };
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(workout, earlier, default);

        var pending = await fixture.Outbox.PendingAsync();
        Assert.Equal([earlierId, laterId], pending.Select(item => item.OperationId));
        Assert.All(pending, item => Assert.False(string.IsNullOrWhiteSpace(item.Payload)));
    }

    private Fixture CreateFixture()
    {
        var database = new TrackZLocalDatabase(_databasePath);
        var repository = new LocalWorkoutRepository(database);
        var boundary = new AccountSessionBoundary();
        return new Fixture(
            database,
            repository,
            new OutboxRepository(database),
            boundary,
            new ActiveWorkoutCoordinator(repository, boundary, new FixedClock(Now)));
    }

    private static LocalWorkout ActiveWorkout(Guid workoutId, Guid workoutExerciseId) => new(
        workoutId,
        LocalWorkoutStatus.Active,
        Now,
        null,
        null,
        1,
        0,
        [new LocalWorkoutExercise(
            workoutExerciseId,
            workoutId,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TrackingMode.Weighted,
            0,
            null,
            1,
            0,
            [])]);

    private async Task CreateOutboxFailureTriggerAsync() => await ExecuteRawAsync("""
        CREATE TRIGGER FailSaveSetOutbox
        BEFORE INSERT ON OutboxOperation
        WHEN NEW.OperationType = 2
        BEGIN
            SELECT RAISE(ABORT, 'injected outbox failure');
        END;
        """);

    private async Task ExecuteRawAsync(string sql)
    {
        await using var connection = await OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenRawAsync()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await foreignKeys.ExecuteNonQueryAsync();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed record Fixture(
        TrackZLocalDatabase Database,
        LocalWorkoutRepository Repository,
        OutboxRepository Outbox,
        AccountSessionBoundary Boundary,
        ActiveWorkoutCoordinator Coordinator);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class BlockingWorkoutRepository(ILocalWorkoutRepository inner) : ILocalWorkoutRepository
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockReads { get; init; }

        public async Task SaveWorkoutAndEnqueueAsync(
            LocalWorkout workout,
            OutboxOperation operation,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            await inner.SaveWorkoutAndEnqueueAsync(workout, operation, cancellationToken);
        }

        public async Task<LocalWorkout?> GetActiveAsync(CancellationToken cancellationToken)
        {
            if (BlockReads)
            {
                Entered.TrySetResult();
                await Release.Task;
            }
            return await inner.GetActiveAsync(cancellationToken);
        }

        public Task ClearPrivateDataAsync(CancellationToken cancellationToken) =>
            inner.ClearPrivateDataAsync(cancellationToken);
    }
}
