using System.Net;
using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Sync;

public sealed class SyncCoordinatorTests
{
    [Fact]
    public async Task Completed_sync_run_notifies_status_observers()
    {
        await using var context = await SyncContext.CreateAsync();
        var statusChanged = typeof(SyncCoordinator).GetEvent("StatusChanged");
        Assert.NotNull(statusChanged);
        var notificationCount = 0;
        EventHandler handler = (_, _) => notificationCount++;
        statusChanged.AddEventHandler(context.Coordinator, handler);

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public async Task Sync_state_schema_survives_database_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackz-sync-{Guid.NewGuid():N}.db");
        try
        {
            await new TrackZLocalDatabase(path).InitializeAsync();
            await new TrackZLocalDatabase(path).InitializeAsync();

            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(TrackZLocalDatabase.CurrentSchemaVersion,
                Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }

    [Fact]
    public async Task Genuine_v2_outbox_rows_upgrade_with_retry_and_conflict_defaults_intact()
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        var expected = Assert.Single(await context.Outbox.PendingAsync());
        await using (var connection = new SqliteConnection($"Data Source={context.Path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP INDEX IX_OutboxOperation_Pending;
                ALTER TABLE OutboxOperation RENAME TO OutboxOperationV3;
                CREATE TABLE OutboxOperation (
                    OperationId TEXT PRIMARY KEY NOT NULL,
                    EntityId TEXT NOT NULL,
                    OperationType INTEGER NOT NULL CHECK (OperationType BETWEEN 1 AND 7),
                    Payload TEXT NOT NULL CHECK (length(Payload) > 0),
                    BaseVersion INTEGER NOT NULL CHECK (BaseVersion >= 0),
                    CreatedAt TEXT NOT NULL,
                    State INTEGER NOT NULL CHECK (State IN (1, 2, 3, 4)),
                    DeletedAt TEXT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 1),
                    FOREIGN KEY (EntityId) REFERENCES LocalWorkout(Id) ON DELETE RESTRICT
                );
                INSERT INTO OutboxOperation
                    (OperationId, EntityId, OperationType, Payload, BaseVersion,
                     CreatedAt, State, DeletedAt, Version)
                SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                       CreatedAt, State, DeletedAt, Version
                FROM OutboxOperationV3;
                DROP TABLE OutboxOperationV3;
                CREATE INDEX IX_OutboxOperation_Pending
                    ON OutboxOperation(State, CreatedAt, OperationId)
                    WHERE State = 1 AND DeletedAt IS NULL;
                ALTER TABLE LocalSet DROP COLUMN Effort;
                PRAGMA user_version = 2;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var restarted = new TrackZLocalDatabase(context.Path);
        await restarted.InitializeAsync();

        var restored = Assert.Single(await new OutboxRepository(restarted).PendingAsync());
        Assert.Equal(expected.OperationId, restored.OperationId);
        Assert.Equal(expected.Payload, restored.Payload);
        Assert.Equal(0, restored.RetryCount);
        Assert.Null(restored.ServerVersion);
        Assert.Null(restored.NextAttemptAt);
        Assert.Null(restored.ServerPayload);
        Assert.Null(restored.ReplacesOperationId);
    }

    [Fact]
    public async Task Genuine_v3_rows_upgrade_with_undo_safety_columns_and_table_intact()
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        var expected = Assert.Single(await context.Outbox.PendingAsync());
        await using (var connection = new SqliteConnection($"Data Source={context.Path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE HistoryUndo;
                DROP INDEX IX_OutboxOperation_Pending;
                ALTER TABLE OutboxOperation RENAME TO OutboxOperationV4;
                CREATE TABLE OutboxOperation (
                    OperationId TEXT PRIMARY KEY NOT NULL,
                    EntityId TEXT NOT NULL,
                    OperationType INTEGER NOT NULL CHECK (OperationType BETWEEN 1 AND 7),
                    Payload TEXT NOT NULL CHECK (length(Payload) > 0),
                    BaseVersion INTEGER NOT NULL CHECK (BaseVersion >= 0),
                    CreatedAt TEXT NOT NULL,
                    State INTEGER NOT NULL CHECK (State IN (1, 2, 3, 4)),
                    DeletedAt TEXT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 1),
                    ServerVersion INTEGER NULL CHECK (ServerVersion IS NULL OR ServerVersion >= 0),
                    RetryCount INTEGER NOT NULL DEFAULT 0 CHECK (RetryCount >= 0),
                    NextAttemptAt TEXT NULL,
                    ServerPayload TEXT NULL CHECK (ServerPayload IS NULL OR json_valid(ServerPayload)),
                    ReplacesOperationId TEXT NULL,
                    FOREIGN KEY (EntityId) REFERENCES LocalWorkout(Id) ON DELETE RESTRICT
                );
                INSERT INTO OutboxOperation
                    (OperationId, EntityId, OperationType, Payload, BaseVersion,
                     CreatedAt, State, DeletedAt, Version, ServerVersion, RetryCount,
                     NextAttemptAt, ServerPayload, ReplacesOperationId)
                SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                       CreatedAt, State, DeletedAt, Version, ServerVersion, RetryCount,
                       NextAttemptAt, ServerPayload, ReplacesOperationId
                FROM OutboxOperationV4;
                DROP TABLE OutboxOperationV4;
                CREATE INDEX IX_OutboxOperation_Pending
                    ON OutboxOperation(State, CreatedAt, OperationId)
                    WHERE State = 1 AND DeletedAt IS NULL;
                ALTER TABLE LocalSet DROP COLUMN Effort;
                PRAGMA user_version = 3;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var restarted = new TrackZLocalDatabase(context.Path);
        await restarted.InitializeAsync();

        var restored = Assert.Single(await new OutboxRepository(restarted).PendingAsync());
        Assert.Equal(expected.OperationId, restored.OperationId);
        Assert.Null(restored.SendStartedAt);
        Assert.Null(restored.NeutralizedAt);
        await using var verification = new SqliteConnection($"Data Source={context.Path};Pooling=False");
        await verification.OpenAsync();
        await using var schema = verification.CreateCommand();
        schema.CommandText = """
            SELECT user_version FROM pragma_user_version;
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'HistoryUndo';
            """;
        await using var reader = await schema.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(TrackZLocalDatabase.CurrentSchemaVersion, reader.GetInt64(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
    }

    [Fact]
    public async Task Genuine_v4_rows_upgrade_to_typed_history_exercise_operations_without_data_loss()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackz-sync-v4-{Guid.NewGuid():N}.db");
        var workoutId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var create = connection.CreateCommand();
                create.CommandText = """
                    CREATE TABLE LocalWorkout (Id TEXT PRIMARY KEY NOT NULL);
                    CREATE TABLE LocalSet (Id TEXT PRIMARY KEY NOT NULL);
                    CREATE TABLE OutboxOperation (
                        OperationId TEXT PRIMARY KEY NOT NULL,
                        EntityId TEXT NOT NULL,
                        OperationType INTEGER NOT NULL CHECK (OperationType BETWEEN 1 AND 7),
                        Payload TEXT NOT NULL CHECK (length(Payload) > 0),
                        BaseVersion INTEGER NOT NULL CHECK (BaseVersion >= 0),
                        CreatedAt TEXT NOT NULL,
                        State INTEGER NOT NULL CHECK (State IN (1, 2, 3, 4)),
                        DeletedAt TEXT NULL,
                        Version INTEGER NOT NULL CHECK (Version >= 1),
                        ServerVersion INTEGER NULL CHECK (ServerVersion IS NULL OR ServerVersion >= 0),
                        RetryCount INTEGER NOT NULL DEFAULT 0 CHECK (RetryCount >= 0),
                        NextAttemptAt TEXT NULL,
                        ServerPayload TEXT NULL CHECK (ServerPayload IS NULL OR json_valid(ServerPayload)),
                        ReplacesOperationId TEXT NULL,
                        SendStartedAt TEXT NULL,
                        NeutralizedAt TEXT NULL,
                        FOREIGN KEY (EntityId) REFERENCES LocalWorkout(Id) ON DELETE RESTRICT
                    );
                    CREATE INDEX IX_OutboxOperation_Pending
                        ON OutboxOperation(State, CreatedAt, OperationId)
                        WHERE State = 1 AND DeletedAt IS NULL;
                    CREATE TABLE HistoryUndo (
                        OperationId TEXT PRIMARY KEY NOT NULL,
                        SnapshotJson TEXT NOT NULL CHECK (json_valid(SnapshotJson)),
                        CreatedAt TEXT NOT NULL,
                        FOREIGN KEY (OperationId) REFERENCES OutboxOperation(OperationId) ON DELETE CASCADE
                    );
                    INSERT INTO LocalWorkout (Id) VALUES ($workoutId);
                    INSERT INTO OutboxOperation
                        (OperationId, EntityId, OperationType, Payload, BaseVersion,
                         CreatedAt, State, Version)
                    VALUES ($operationId, $workoutId, 1, '{}', 0, $createdAt, 1, 1);
                    INSERT INTO HistoryUndo (OperationId, SnapshotJson, CreatedAt)
                    VALUES ($operationId, '{}', $createdAt);
                    PRAGMA user_version = 4;
                    """;
                create.Parameters.AddWithValue("$workoutId", workoutId.ToString("D"));
                create.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
                create.Parameters.AddWithValue("$createdAt", "2026-08-16T10:00:00.0000000+00:00");
                await create.ExecuteNonQueryAsync();
            }

            await new TrackZLocalDatabase(path).InitializeAsync();

            await using var verification = new SqliteConnection($"Data Source={path};Pooling=False");
            await verification.OpenAsync();
            await using var command = verification.CreateCommand();
            command.CommandText = """
                SELECT user_version FROM pragma_user_version;
                SELECT COUNT(*) FROM OutboxOperation WHERE OperationId = $operationId;
                SELECT COUNT(*) FROM HistoryUndo WHERE OperationId = $operationId;
                """;
            command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(TrackZLocalDatabase.CurrentSchemaVersion, reader.GetInt64(0));
            Assert.True(await reader.NextResultAsync());
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.True(await reader.NextResultAsync());
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt64(0));
            await reader.DisposeAsync();

            await using var insert = verification.CreateCommand();
            insert.CommandText = """
                INSERT INTO OutboxOperation
                    (OperationId, EntityId, OperationType, Payload, BaseVersion,
                     CreatedAt, State, Version)
                VALUES ($operationId, $workoutId, 10, '{}', 1, $createdAt, 1, 1);
                """;
            insert.Parameters.AddWithValue("$operationId", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$workoutId", workoutId.ToString("D"));
            insert.Parameters.AddWithValue("$createdAt", "2026-08-16T10:01:00.0000000+00:00");
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }

    [Fact]
    public async Task Conflict_keeps_exact_local_payload_and_server_authority_until_keep_server()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, new LocalSet(70m, null, 8));
        var pending = await context.Outbox.PendingAsync();
        var original = pending[1];
        var server = ServerGraph(local, version: 4);
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(original.OperationId, SyncOperationStatus.Conflict, 4, BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(server, sequence: 1)
        ], "cursor-1", false));

        await context.Coordinator.RunOnceAsync();

        var conflict = Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Equal(original.Payload, conflict.Payload);
        Assert.Equal(4, conflict.ServerVersion);
        Assert.NotNull(conflict.ServerPayload);
        _ = await new ConflictResolution(context.Coordinator)
            .ApplyLocalAgainstVersionAsync(conflict.OperationId, 4);
        await new ConflictResolution(context.Coordinator).KeepServerAsync(conflict.OperationId);
        Assert.Empty(await context.Outbox.ConflictedAsync());
        Assert.Empty(await context.Outbox.PendingAsync());
        var authoritative = (await context.Workouts.GetActiveAsync())!;
        Assert.Equal(4, authoritative.Version);
        Assert.Empty(authoritative.Exercises[0].Sets);
    }

    [Fact]
    public async Task Confirmed_active_discard_conflict_is_automatically_rebased_and_retried()
    {
        await using var context = await SyncContext.CreateAsync();
        var started = await context.StartAsync();
        await new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock).DiscardAsync();
        var pending = await context.Outbox.PendingAsync();
        var delete = pending[1];
        var authority = ServerGraph(started, 2);
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(delete.OperationId, SyncOperationStatus.Conflict, 2,
                BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(authority, 1)
        ], "cursor-discard-conflict", false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        var original = Assert.Single(await context.Outbox.ConflictedAsync());
        var replacement = Assert.Single(await context.Outbox.PendingAsync());
        Assert.Equal(OutboxOperationType.DeleteWorkout, replacement.Type);
        Assert.Equal(original.OperationId, replacement.ReplacesOperationId);
        Assert.Equal(authority.Version, replacement.BaseVersion);
        Assert.NotNull(replacement.NextAttemptAt);
        Assert.Null(await context.Workouts.GetActiveAsync());

        context.Clock.UtcNow = replacement.NextAttemptAt!.Value;
        var rebasedPayload = replacement.DeserializePayload<DeleteWorkoutOutboxPayload>();
        var deletedAuthority = authority with
        {
            DeletedAt = rebasedPayload.DeletedAt,
            Version = 3
        };
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(replacement.OperationId, SyncOperationStatus.Applied, 3, null)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(deletedAuthority, 2)
        ], "cursor-discard-deleted", false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        Assert.Empty(await context.Outbox.PendingAsync());
        Assert.Empty(await context.Outbox.ConflictedAsync());
        Assert.Empty(await context.Outbox.UnresolvedAsync());
        Assert.Null(await context.Workouts.GetActiveAsync());
    }

    [Fact]
    public async Task Ordinary_history_delete_conflict_is_not_automatically_rebased()
    {
        await using var context = await SyncContext.CreateAsync();
        var authority = CompletedServerGraph(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(authority, 1)
        ], "cursor-history-seed", false));
        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        var mutation = await new WorkoutHistoryCoordinator(
            context.Workouts, context.Boundary, context.Clock)
            .DeleteWorkoutAsync(authority.Id);
        var delete = Assert.Single(await context.Outbox.PendingAsync());
        Assert.Equal(mutation.OperationId, delete.OperationId);
        Assert.Equal(OutboxOperationType.DeleteWorkout, delete.Type);

        var newerAuthority = authority with { Version = 5 };
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(delete.OperationId, SyncOperationStatus.Conflict, 5,
                BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(newerAuthority, 2)
        ], "cursor-history-conflict", false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        var conflict = Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Equal(delete.OperationId, conflict.OperationId);
        Assert.Equal(5, conflict.ServerVersion);
        Assert.NotNull(conflict.ServerPayload);
        Assert.Empty(await context.Outbox.PendingAsync());
        Assert.Equal(0, await CountReplacementsAsync(context.Path, delete.OperationId));
    }

    [Fact]
    public async Task Confirmed_active_discard_when_server_is_already_deleted_archives_without_replacement()
    {
        await using var context = await SyncContext.CreateAsync();
        var started = await context.StartAsync();
        await new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock).DiscardAsync();
        var pending = await context.Outbox.PendingAsync();
        var delete = pending.Single(operation =>
            operation.Type == OutboxOperationType.DeleteWorkout);
        var deletedAt = delete.DeserializePayload<DeleteWorkoutOutboxPayload>().DeletedAt;
        var deletedAuthority = ServerGraph(started, 2) with { DeletedAt = deletedAt };
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(delete.OperationId, SyncOperationStatus.Conflict, 2,
                BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(deletedAuthority, 1)
        ], "cursor-discard-already-deleted", false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        var archived = Assert.IsType<OutboxOperation>(
            await context.Workouts.GetOperationAsync(delete.OperationId));
        Assert.Equal(OutboxOperationState.Applied, archived.State);
        Assert.Equal(2, archived.ServerVersion);
        Assert.NotNull(archived.DeletedAt);
        Assert.Empty(await context.Outbox.PendingAsync());
        Assert.Empty(await context.Outbox.ConflictedAsync());
        Assert.Empty(await context.Outbox.UnresolvedAsync());
        Assert.Equal(0, await CountReplacementsAsync(context.Path, delete.OperationId));
        Assert.Null(await context.Workouts.GetActiveAsync());
    }

    [Fact]
    public async Task Start_conflict_apply_local_merges_the_local_selection_without_erasing_server_exercises()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        var original = Assert.Single(await context.Outbox.PendingAsync());
        var server = EmptyServerGraph(local.Id, 1) with
        {
            StartedAt = local.StartedAt.AddMinutes(-1)
        };
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(original.OperationId, SyncOperationStatus.Conflict, 1,
                BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(server, 1)
        ], "cursor-start-conflict", false));
        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        var conflict = Assert.Single(await context.Outbox.ConflictedAsync());
        var replacement = await new ConflictResolution(context.Coordinator)
            .ApplyLocalAgainstVersionAsync(conflict.OperationId, 1);
        var payload = replacement.DeserializePayload<StartWorkoutOutboxPayload>();
        Assert.Equal(server.StartedAt, payload.StartedAt);
        var localExercise = Assert.Single(payload.Exercises);
        var remoteExercise = Assert.Single(server.Exercises);
        var merged = server with
        {
            Version = 2,
            Exercises = [
                new SyncWorkoutExerciseDto(
                    localExercise.WorkoutExerciseId,
                    localExercise.ExerciseDefinitionId,
                    localExercise.TrackingMode,
                    0,
                    null,
                    1,
                    []),
                remoteExercise with { Order = 1 }
            ]
        };
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(replacement.OperationId, SyncOperationStatus.Applied, 2, null)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(merged, 2)
        ], "cursor-start-merged", false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        Assert.Empty(await context.Outbox.PendingAsync());
        Assert.Empty(await context.Outbox.ConflictedAsync());
        var synchronized = (await context.Workouts.GetActiveAsync())!;
        Assert.Equal(2, synchronized.Exercises.Count);
        Assert.Equal(
            [localExercise.ExerciseDefinitionId, remoteExercise.ExerciseDefinitionId],
            synchronized.Exercises.OrderBy(item => item.Order)
                .Select(item => item.ExerciseDefinitionId));
    }

    [Fact]
    public async Task Rebase_uses_new_stable_id_and_causal_time_without_removing_original_until_ack()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, new LocalSet(70m, null, 8));
        var pending = await context.Outbox.PendingAsync();
        var original = pending[1];
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(original.OperationId, SyncOperationStatus.Conflict, 3, BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(ServerGraphWithRemoteSet(local, 3), 1)
        ], "cursor-1", false));
        await context.Coordinator.RunOnceAsync();

        var replacement = await new ConflictResolution(context.Coordinator)
            .ApplyLocalAgainstVersionAsync(original.OperationId, 3);
        var originalIntent = original.DeserializePayload<SaveSetOutboxPayload>();
        var rebasedIntent = replacement.DeserializePayload<SaveSetOutboxPayload>();

        Assert.NotEqual(original.OperationId, replacement.OperationId);
        Assert.Equal(3, replacement.BaseVersion);
        Assert.True(replacement.CreatedAt > original.CreatedAt);
        Assert.Equal(original.OperationId, replacement.ReplacesOperationId);
        Assert.Equal(originalIntent with { Order = 1 }, rebasedIntent);
        Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Single(await context.Outbox.PendingAsync());

        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(replacement.OperationId, SyncOperationStatus.Applied, 4, null)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([], "cursor-1", false));
        await context.Coordinator.RunOnceAsync();
        Assert.Empty(await context.Outbox.ConflictedAsync());
        Assert.Empty(await context.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Equal_authority_pull_keeps_retryable_rebase_replacement_pending_and_runnable()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, new LocalSet(70m, null, 8));
        var pending = await context.Outbox.PendingAsync();
        var original = pending[1];
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(original.OperationId, SyncOperationStatus.Conflict, 3,
                BusinessErrorCode.VersionConflict)
        ]));
        var authority = ServerGraphWithRemoteSet(local, 3);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(authority, 1)
        ], "cursor-1", false));
        await context.Coordinator.RunOnceAsync();
        var replacement = await new ConflictResolution(context.Coordinator)
            .ApplyLocalAgainstVersionAsync(original.OperationId, authority.Version);
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(replacement.OperationId, SyncOperationStatus.Retryable, null,
                BusinessErrorCode.InternalServerError)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(authority, 2)
        ], "cursor-2", false));

        await context.Coordinator.RunOnceAsync();

        var retry = Assert.Single(await context.Outbox.PendingAsync());
        Assert.Equal(replacement.OperationId, retry.OperationId);
        Assert.Equal(1, retry.RetryCount);
        Assert.NotNull(retry.NextAttemptAt);
        var retainedConflict = Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Equal(original.OperationId, retainedConflict.OperationId);

        context.Clock.UtcNow = retry.NextAttemptAt.Value;
        var acknowledged = AppendServerSet(authority, replacement, 4);
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(replacement.OperationId, SyncOperationStatus.Applied, acknowledged.Version, null)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(acknowledged, 3)
        ], "cursor-3", false));

        await context.Coordinator.RunOnceAsync();

        Assert.Equal(replacement.OperationId,
            context.Api.PushRequests[^1].Operations.Single().OperationId);
        Assert.Empty(await context.Outbox.PendingAsync());
        Assert.Empty(await context.Outbox.ConflictedAsync());
    }

    [Fact]
    public async Task Replacement_blocks_new_save_until_acknowledgement_pull_then_unblocks_causal_restart()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        var originalSet = new LocalSet(70m, null, 8) with
        {
            OperationId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")
        };
        await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, originalSet);
        var pending = await context.Outbox.PendingAsync();
        var original = pending[1];
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(original.OperationId, SyncOperationStatus.Conflict, 3,
                BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(ServerGraphWithRemoteSet(local, 3), 1)
        ], "cursor-1", false));
        await context.Coordinator.RunOnceAsync();
        context.Clock.UtcNow = local.StartedAt.AddDays(-1);
        var replacement = await new ConflictResolution(context.Coordinator)
            .ApplyLocalAgainstVersionAsync(original.OperationId, 3);
        var blockedSet = new LocalSet(75m, null, 7) with
        {
            OperationId = Guid.Parse("00000000-0000-0000-0000-000000000001")
        };
        var graphBeforeBlockedSave = (await context.Workouts.GetActiveAsync())!;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
                .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, blockedSet));

        AssertWorkoutIntentEqual(
            graphBeforeBlockedSave, (await context.Workouts.GetActiveAsync())!);
        var retainedConflict = Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Equal(original.OperationId, retainedConflict.OperationId);
        Assert.Equal(original.Payload, retainedConflict.Payload);
        Assert.Equal(3, retainedConflict.ServerVersion);
        Assert.NotNull(retainedConflict.ServerPayload);
        Assert.Equal(replacement, Assert.Single(await context.Outbox.PendingAsync()));

        var acknowledgementApi = new FakeSyncApi
        {
            PushResponse = new SyncPushResponse([
                new(replacement.OperationId, SyncOperationStatus.Applied, 4, null)
            ])
        };
        var pullEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePull = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authorityResponse = new SyncPullResponse([
            Change(ServerGraphAfterRebase(local, original, 4), 2)
        ], "cursor-2", false);
        acknowledgementApi.PullOverride = async (_, cancellationToken) =>
        {
            pullEntered.TrySetResult();
            await releasePull.Task.WaitAsync(cancellationToken);
            return authorityResponse;
        };
        var restartedDatabase = new TrackZLocalDatabase(context.Path);
        var restartedCoordinator = new SyncCoordinator(
            restartedDatabase, acknowledgementApi, context.Boundary, context.Clock);

        var acknowledgement = restartedCoordinator.RunOnceAsync();
        await pullEntered.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ActiveWorkoutCoordinator(
                    new LocalWorkoutRepository(restartedDatabase),
                    context.Boundary,
                    context.Clock)
                .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, blockedSet));
        AssertWorkoutIntentEqual(
            graphBeforeBlockedSave,
            (await new LocalWorkoutRepository(restartedDatabase).GetActiveAsync())!);
        releasePull.TrySetResult();
        await acknowledgement;

        Assert.Equal(
            [replacement.OperationId],
            acknowledgementApi.PushRequests.SelectMany(request => request.Operations)
                .Select(operation => operation.OperationId));
        var restartedWorkouts = new LocalWorkoutRepository(restartedDatabase);
        var saved = await new ActiveWorkoutCoordinator(
                restartedWorkouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, blockedSet);
        var successor = Assert.Single(await new OutboxRepository(restartedDatabase).PendingAsync());
        Assert.Equal(blockedSet.OperationId, successor.OperationId);
        Assert.Equal(4, successor.BaseVersion);
        Assert.True(successor.CreatedAt > replacement.CreatedAt);
        Assert.Equal(saved.Id, successor.DeserializePayload<SaveSetOutboxPayload>().SetId);

        var finalApi = new FakeSyncApi
        {
            PushResponse = new SyncPushResponse([
                new(successor.OperationId, SyncOperationStatus.Applied, 5, null)
            ])
        };
        var graphAfterSave = (await restartedWorkouts.GetActiveAsync())!;
        finalApi.PullResponses.Enqueue(new SyncPullResponse([
            Change(ServerGraph(graphAfterSave, 5), 3)
        ], "cursor-3", false));
        await new SyncCoordinator(
            new TrackZLocalDatabase(context.Path), finalApi, context.Boundary, context.Clock)
            .RunOnceAsync();

        Assert.Equal(
            [successor.OperationId],
            finalApi.PushRequests.SelectMany(request => request.Operations)
                .Select(operation => operation.OperationId));
        Assert.Empty(await context.Outbox.PendingAsync());
        Assert.Empty(await context.Outbox.ConflictedAsync());
    }

    [Fact]
    public async Task Rejected_replacement_does_not_discard_original_conflicted_intent()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, new LocalSet(70m, null, 8));
        var pending = await context.Outbox.PendingAsync();
        var original = pending[1];
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(original.OperationId, SyncOperationStatus.Conflict, 3, BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(ServerGraphWithRemoteSet(local, 3), 1)
        ], "cursor-1", false));
        await context.Coordinator.RunOnceAsync();
        var replacement = await new ConflictResolution(context.Coordinator)
            .ApplyLocalAgainstVersionAsync(original.OperationId, 3);
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(replacement.OperationId, SyncOperationStatus.Rejected, null, BusinessErrorCode.InvalidRequest)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([], "cursor-1", false));

        await context.Coordinator.RunOnceAsync();

        Assert.Equal(original.OperationId, Assert.Single(await context.Outbox.ConflictedAsync()).OperationId);
        Assert.Empty(await context.Outbox.PendingAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unresolved_conflict_blocks_new_save_even_after_replacement_rejection(
        bool rejectReplacement)
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, new LocalSet(70m, null, 8));
        var pending = await context.Outbox.PendingAsync();
        var original = pending[1];
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(original.OperationId, SyncOperationStatus.Conflict, 3,
                BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(ServerGraphWithRemoteSet(local, 3), 1)
        ], "cursor-1", false));
        await context.Coordinator.RunOnceAsync();
        if (rejectReplacement)
        {
            var replacement = await new ConflictResolution(context.Coordinator)
                .ApplyLocalAgainstVersionAsync(original.OperationId, 3);
            context.Api.PushResponse = new SyncPushResponse([
                new(replacement.OperationId, SyncOperationStatus.Rejected, null,
                    BusinessErrorCode.InvalidRequest)
            ]);
            context.Api.PullResponses.Enqueue(new SyncPullResponse([], "cursor-1", false));
            await context.Coordinator.RunOnceAsync();
        }
        context.Clock.UtcNow = context.Clock.UtcNow.AddDays(-1);
        var graphBeforeBlockedSave = (await context.Workouts.GetActiveAsync())!;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
                .SaveSetAsync(
                    local.Exercises[0].ExerciseDefinitionId,
                    new LocalSet(75m, null, 7)));

        AssertWorkoutIntentEqual(
            graphBeforeBlockedSave, (await context.Workouts.GetActiveAsync())!);
        var retained = Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Equal(original.OperationId, retained.OperationId);
        Assert.Equal(original.Payload, retained.Payload);
        Assert.Empty(await context.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Apply_local_resolves_a_causal_conflict_chain_in_order_without_discarding_later_intent()
    {
        await using var context = await SyncContext.CreateAsync();
        var (serverLocal, _, _) = await PrepareConflictWithSuccessorsAsync(context);
        var server = ServerGraph(serverLocal, 4);
        var pushed = new List<Guid>();

        for (var index = 0; index < 3; index++)
        {
            var conflicts = await context.Outbox.ConflictedAsync();
            var conflict = conflicts.OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.OperationId).First();
            Assert.Equal(3 - index, conflicts.Count);
            Assert.Equal(server.Version, conflict.ServerVersion);

            var replacement = await new ConflictResolution(context.Coordinator)
                .ApplyLocalAgainstVersionAsync(conflict.OperationId, server.Version);
            server = AppendServerSet(server, replacement, server.Version + 1);
            context.Api.PushResponses.Enqueue(new SyncPushResponse([
                new(replacement.OperationId, SyncOperationStatus.Applied, server.Version, null)
            ]));
            context.Api.PullResponses.Enqueue(new SyncPullResponse([
                Change(server, 2 + index)
            ], $"cursor-chain-{index}", false));

            Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());
            pushed.Add(replacement.OperationId);
        }

        Assert.Equal(pushed,
            context.Api.PushRequests.TakeLast(3).SelectMany(request => request.Operations)
                .Select(operation => operation.OperationId));
        Assert.Empty(await context.Outbox.ConflictedAsync());
        Assert.Empty(await context.Outbox.PendingAsync());
        var synchronized = (await context.Workouts.GetActiveAsync())!;
        Assert.Equal([70m, 71m, 72m], synchronized.Exercises[0].Sets
            .OrderBy(set => set.Order).Select(set => set.WeightKg));
    }

    [Fact]
    public async Task Keep_server_discards_only_the_selected_conflict_and_preserves_later_causal_intent()
    {
        await using var context = await SyncContext.CreateAsync();
        var (_, conflict, successors) = await PrepareConflictWithSuccessorsAsync(context);

        await new ConflictResolution(context.Coordinator).KeepServerAsync(conflict.OperationId);

        Assert.Equal(successors, await context.Outbox.ConflictedAsync());
        Assert.Empty(await context.Outbox.PendingAsync());
        var authoritative = (await context.Workouts.GetActiveAsync())!;
        Assert.Equal(4, authoritative.Version);
        Assert.Empty(authoritative.Exercises[0].Sets);
    }

    [Fact]
    public async Task Restart_runs_the_earliest_conflict_replacement_before_later_conflicted_successors()
    {
        await using var context = await SyncContext.CreateAsync();
        var (_, conflict, successors) = await PrepareConflictWithSuccessorsAsync(context);
        var replacement = conflict with
        {
            OperationId = Guid.NewGuid(),
            BaseVersion = 4,
            CreatedAt = successors[^1].CreatedAt.AddTicks(1),
            State = OutboxOperationState.Pending,
            ServerVersion = null,
            ServerPayload = null,
            ReplacesOperationId = conflict.OperationId
        };
        await InsertOperationAsync(context.Path, replacement);
        var restartedApi = new FakeSyncApi
        {
            PushResponse = new SyncPushResponse([
                new(replacement.OperationId, SyncOperationStatus.Applied, 5, null)
            ])
        };
        var restarted = new SyncCoordinator(
            new TrackZLocalDatabase(context.Path), restartedApi, context.Boundary, context.Clock);

        var status = await restarted.RunOnceAsync();

        Assert.Equal(SyncRunStatus.Completed, status);
        var pushed = Assert.Single(Assert.Single(restartedApi.PushRequests).Operations);
        Assert.Equal(replacement.OperationId, pushed.OperationId);
        Assert.Equal(successors, await context.Outbox.ConflictedAsync());
        Assert.Empty(await context.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Retryable_result_persists_bounded_backoff_across_restart()
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        var operation = Assert.Single(await context.Outbox.PendingAsync());
        context.Api.PushResponse = new SyncPushResponse([
            new(operation.OperationId, SyncOperationStatus.Retryable, null, BusinessErrorCode.InternalServerError)
        ]);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([], null, false));

        await context.Coordinator.RunOnceAsync();

        var retry = Assert.Single(await context.Outbox.PendingAsync());
        Assert.Equal(1, retry.RetryCount);
        Assert.Equal(context.Clock.UtcNow.AddSeconds(1), retry.NextAttemptAt);
        var restartedApi = new FakeSyncApi { PushResponse = new SyncPushResponse([]) };
        restartedApi.PullResponses.Enqueue(new SyncPullResponse([], null, false));
        var restarted = new SyncCoordinator(
            new TrackZLocalDatabase(context.Path), restartedApi, context.Boundary, context.Clock);
        await restarted.RunOnceAsync();
        Assert.Empty(restartedApi.PushRequests);
    }

    [Fact]
    public async Task Unknown_result_mapping_fails_closed_without_losing_outbox_rows()
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        context.Api.PushResponse = new SyncPushResponse([]);

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Coordinator.RunOnceAsync());

        Assert.Single(await context.Outbox.PendingAsync());
        Assert.Empty(context.Api.PullRequests);
    }

    public static TheoryData<SyncOperationStatus, long?, BusinessErrorCode?> MalformedPushResults => new()
    {
        { SyncOperationStatus.Applied, null, null },
        { SyncOperationStatus.Applied, 0, null },
        { SyncOperationStatus.Applied, 1, BusinessErrorCode.InvalidRequest },
        { SyncOperationStatus.Rejected, null, null },
        { SyncOperationStatus.Rejected, 1, BusinessErrorCode.InvalidRequest },
        { SyncOperationStatus.Rejected, null, BusinessErrorCode.InternalServerError },
        { SyncOperationStatus.Conflict, 1, BusinessErrorCode.InvalidRequest },
        { SyncOperationStatus.Conflict, 0, BusinessErrorCode.VersionConflict },
        { SyncOperationStatus.Retryable, null, null },
        { SyncOperationStatus.Retryable, 1, BusinessErrorCode.InternalServerError }
    };

    [Theory]
    [MemberData(nameof(MalformedPushResults))]
    public async Task Malformed_push_result_fields_fail_closed_without_mutating_sync_state(
        SyncOperationStatus status,
        long? serverVersion,
        BusinessErrorCode? errorCode)
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        var operation = Assert.Single(await context.Outbox.PendingAsync());
        context.Api.PushResponse = new SyncPushResponse([
            new(operation.OperationId, status, serverVersion, errorCode)
        ]);

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Coordinator.RunOnceAsync());

        Assert.Equal(operation, Assert.Single(await context.Outbox.PendingAsync()));
        Assert.Empty(await context.Outbox.ConflictedAsync());
        Assert.Empty(context.Api.PullRequests);
    }

    [Fact]
    public async Task Pull_graph_and_cursor_roll_back_together_when_later_change_is_malformed()
    {
        await using var context = await SyncContext.CreateAsync();
        var valid = EmptyServerGraph(Guid.NewGuid(), 1);
        var invalid = EmptyServerGraph(Guid.NewGuid(), 1) with
        {
            Exercises = [new SyncWorkoutExerciseDto(
                Guid.NewGuid(), Guid.NewGuid(), 99, 0, null, 1, [])]
        };
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(valid, 1), Change(invalid, 2)
        ], "cursor-after-two", false));

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Coordinator.RunOnceAsync());

        await using var connection = new SqliteConnection($"Data Source={context.Path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM LocalWorkout; SELECT COUNT(*) FROM SyncCursor;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
    }

    [Fact]
    public async Task Pull_authoritative_graph_round_trips_and_replaces_set_effort()
    {
        await using var context = await SyncContext.CreateAsync();
        var graph = CompletedServerGraph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        graph = graph with
        {
            Exercises = [graph.Exercises[0] with
            {
                Sets = [graph.Exercises[0].Sets[0] with { Effort = SetEffortRating.Easy }]
            }]
        };
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(graph, 1)
        ], "cursor-effort-1", false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());
        var inserted = Assert.Single(Assert.Single(
            (await context.Workouts.GetHistoryAsync()).Single().Exercises).Sets);
        Assert.Equal(SetEffortRating.Easy, inserted.Effort);

        var replacement = graph with
        {
            Version = 2,
            Exercises = [graph.Exercises[0] with
            {
                Version = 2,
                Sets = [graph.Exercises[0].Sets[0] with
                {
                    Version = 2,
                    Effort = SetEffortRating.TooHeavy
                }]
            }]
        };
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(replacement, 2)
        ], "cursor-effort-2", false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());
        var replaced = Assert.Single(Assert.Single(
            (await context.Workouts.GetHistoryAsync()).Single().Exercises).Sets);
        Assert.Equal(SetEffortRating.TooHeavy, replaced.Effort);
    }

    [Fact]
    public async Task Pull_rejects_exercise_id_collision_with_another_cached_workout()
    {
        await using var context = await SyncContext.CreateAsync();
        var cached = CompletedServerGraph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(cached, 1)
        ], "cursor-cached", false));
        await context.Coordinator.RunOnceAsync();
        var colliding = CompletedGraphWithDeletedExercise(
            Guid.NewGuid(), cached.Exercises[0].Id, Guid.NewGuid(), 1);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(colliding, 2)
        ], "cursor-collision", false));

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Coordinator.RunOnceAsync());

        await AssertOnlyCachedGraphRemainsAsync(context.Path, cached, "cursor-cached");
    }

    [Fact]
    public async Task Pull_rejects_set_id_collision_with_another_cached_exercise()
    {
        await using var context = await SyncContext.CreateAsync();
        var cached = CompletedServerGraph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(cached, 1)
        ], "cursor-cached", false));
        await context.Coordinator.RunOnceAsync();
        var colliding = CompletedGraphWithDeletedSet(
            Guid.NewGuid(), Guid.NewGuid(), cached.Exercises[0].Sets[0].Id, 1);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(colliding, 2)
        ], "cursor-collision", false));

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Coordinator.RunOnceAsync());

        await AssertOnlyCachedGraphRemainsAsync(context.Path, cached, "cursor-cached");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pull_collision_with_active_outbox_rolls_back_payload_and_cursor(bool collideSet)
    {
        await using var context = await SyncContext.CreateAsync();
        var cached = CompletedServerGraph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(cached, 1)
        ], "cursor-cached", false));
        await context.Coordinator.RunOnceAsync();
        var local = await context.StartAsync();
        var operation = Assert.Single(await context.Outbox.PendingAsync());
        var graph = ServerGraph(local, 4);
        graph = graph with
        {
            Exercises = collideSet
                ? [graph.Exercises[0] with
                {
                    Sets = [new SyncSetDto(
                        cached.Exercises[0].Sets[0].Id, 0, "70", null, 8,
                        local.StartedAt.AddMinutes(1), null, null, 1)]
                }]
                : [graph.Exercises[0] with { Id = cached.Exercises[0].Id }]
        };
        context.Api.PushResponse = new SyncPushResponse([
            new(operation.OperationId, SyncOperationStatus.Conflict, 4,
                BusinessErrorCode.VersionConflict)
        ]);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(graph, 2)
        ], "cursor-collision", false));

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Coordinator.RunOnceAsync());

        var conflict = Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Equal(operation.Payload, conflict.Payload);
        Assert.Null(conflict.ServerPayload);
        await AssertOnlyCachedGraphRemainsAsync(
            context.Path, cached, "cursor-cached", expectedWorkoutCount: 2);
    }

    [Fact]
    public async Task Pull_accepts_active_mutations_that_precede_later_workout_completion()
    {
        await using var context = await SyncContext.CreateAsync();
        var graph = CompletedGraphWithPreCompletionHistory(Guid.NewGuid(), 7);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(graph, 1)
        ], "cursor-history", false));

        var status = await context.Coordinator.RunOnceAsync();

        Assert.Equal(SyncRunStatus.Completed, status);
        await using var connection = new SqliteConnection($"Data Source={context.Path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CompletedAt FROM LocalWorkout WHERE Id = $workoutId;
            SELECT DeletedAt FROM LocalWorkoutExercise WHERE Id = $exerciseId;
            SELECT UpdatedAt, DeletedAt FROM LocalSet WHERE Id = $setId;
            SELECT Cursor FROM SyncCursor WHERE Scope = 'workouts';
            """;
        command.Parameters.AddWithValue("$workoutId", graph.Id.ToString("D"));
        command.Parameters.AddWithValue("$exerciseId", graph.Exercises[0].Id.ToString("D"));
        command.Parameters.AddWithValue("$setId", graph.Exercises[0].Sets[0].Id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(graph.CompletedAt!.Value.ToString("O"), reader.GetString(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(graph.Exercises[0].DeletedAt!.Value.ToString("O"), reader.GetString(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(graph.Exercises[0].Sets[0].UpdatedAt!.Value.ToString("O"), reader.GetString(0));
        Assert.Equal(graph.Exercises[0].Sets[0].DeletedAt!.Value.ToString("O"), reader.GetString(1));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal("cursor-history", reader.GetString(0));
    }

    [Fact]
    public async Task Invalid_persisted_cursor_rebootstraps_once_and_commits_graph_with_replacement_cursor()
    {
        await using var context = await SyncContext.CreateAsync();
        var graph = EmptyServerGraph(Guid.NewGuid(), 1);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([Change(graph, 1)], "cursor-stale", false));
        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());
        context.Api.PullOverride = (cursor, _) => cursor is not null
            ? throw new SyncApiException(
                SyncApiFailureKind.InvalidCursor,
                HttpStatusCode.BadRequest,
                BusinessErrorCode.InvalidRequest,
                "trace",
                "invalid cursor")
            : Task.FromResult(new SyncPullResponse(
                [Change(graph with { Version = 2 }, 1)], "cursor-fresh", false));

        var status = await context.Coordinator.RunOnceAsync();

        Assert.Equal(SyncRunStatus.Completed, status);
        Assert.Equal([null, "cursor-stale", null], context.Api.PullRequests);
        await using var connection = new SqliteConnection($"Data Source={context.Path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Version FROM LocalWorkout WHERE Id = $id;
            SELECT Cursor FROM SyncCursor WHERE Scope = 'workouts';
            """;
        command.Parameters.AddWithValue("$id", graph.Id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt64(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal("cursor-fresh", reader.GetString(0));
    }

    [Fact]
    public async Task Failed_cursor_rebootstrap_surfaces_permanent_failure_without_erasing_local_state()
    {
        await using var context = await SyncContext.CreateAsync();
        var graph = EmptyServerGraph(Guid.NewGuid(), 1);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([Change(graph, 1)], "cursor-stale", false));
        await context.Coordinator.RunOnceAsync();
        context.Api.PullOverride = (cursor, _) => throw new SyncApiException(
            cursor is null ? SyncApiFailureKind.Permanent : SyncApiFailureKind.InvalidCursor,
            HttpStatusCode.BadRequest,
            BusinessErrorCode.InvalidRequest,
            "trace",
            "invalid cursor");

        var status = await context.Coordinator.RunOnceAsync();

        Assert.Equal(SyncRunStatus.PermanentFailure, status);
        await using var connection = new SqliteConnection($"Data Source={context.Path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Version FROM LocalWorkout WHERE Id = $id;
            SELECT Cursor FROM SyncCursor WHERE Scope = 'workouts';
            """;
        command.Parameters.AddWithValue("$id", graph.Id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal("cursor-stale", reader.GetString(0));
    }

    [Fact]
    public async Task Empty_successful_cursor_rebootstrap_atomically_clears_the_invalid_persisted_cursor()
    {
        await using var context = await SyncContext.CreateAsync();
        var graph = EmptyServerGraph(Guid.NewGuid(), 1);
        context.Api.PullResponses.Enqueue(new SyncPullResponse(
            [Change(graph, 1)], "cursor-stale", false));
        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());
        context.Api.PullOverride = (cursor, _) => cursor is not null
            ? throw new SyncApiException(
                SyncApiFailureKind.InvalidCursor,
                HttpStatusCode.BadRequest,
                BusinessErrorCode.InvalidRequest,
                "trace",
                "invalid cursor")
            : Task.FromResult(new SyncPullResponse([], null, false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        Assert.Equal([null, "cursor-stale", null], context.Api.PullRequests);
        await using var connection = new SqliteConnection($"Data Source={context.Path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SyncCursor WHERE Scope = 'workouts';";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Permanent_http_push_rejection_archives_typed_history_failure_without_reconciling_forever()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        var active = new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock);
        await active.SaveSetAsync(
            local.Exercises[0].ExerciseDefinitionId,
            new LocalSet(70m, null, 8));
        var initial = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(initial[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(initial[1].OperationId, SyncOperationStatus.Applied, 2, null)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([], null, false));
        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await active.FinishAsync();
        context.Api.PushException = new SyncApiException(
            SyncApiFailureKind.Permanent,
            HttpStatusCode.BadRequest,
            BusinessErrorCode.InvalidSetValue,
            "trace-permanent",
            "invalid set");

        Assert.Equal(SyncRunStatus.PermanentFailure, await context.Coordinator.RunOnceAsync());

        Assert.Empty(await context.Outbox.PendingAsync());
        var rejected = Assert.Single(await context.Outbox.RejectedAsync(local.Id));
        Assert.Equal(OutboxOperationType.CompleteWorkout, rejected.Type);
        Assert.Equal(BusinessErrorCode.InvalidSetValue, rejected.FailureCode);
    }

    [Fact]
    public async Task Rejected_predecessor_returns_permanent_failure_and_leaves_successor_runnable()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(
                local.Exercises[0].ExerciseDefinitionId,
                new LocalSet(70m, null, 8));
        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(
                pending[0].OperationId,
                SyncOperationStatus.Rejected,
                null,
                BusinessErrorCode.InvalidRequest)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([], null, false));

        var status = await context.Coordinator.RunOnceAsync();

        Assert.Equal(SyncRunStatus.PermanentFailure, status);
        var rejected = Assert.Single(await context.Outbox.RejectedAsync(local.Id));
        Assert.Equal(pending[0].OperationId, rejected.OperationId);
        var successor = Assert.Single(await context.Outbox.PendingAsync());
        Assert.Equal(pending[1].OperationId, successor.OperationId);
        Assert.Null(successor.NextAttemptAt);
        Assert.Equal([null], context.Api.PullRequests);
    }

    [Fact]
    public async Task Pull_still_rejects_child_mutation_before_workout_start()
    {
        await using var context = await SyncContext.CreateAsync();
        var graph = CompletedServerGraph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        graph = graph with
        {
            Exercises = [graph.Exercises[0] with
            {
                Sets = [graph.Exercises[0].Sets[0] with
                {
                    CompletedAt = graph.StartedAt.AddTicks(-1)
                }]
            }]
        };
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(graph, 1)
        ], "cursor-invalid", false));

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Coordinator.RunOnceAsync());

        await using var connection = new SqliteConnection($"Data Source={context.Path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM LocalWorkout; SELECT COUNT(*) FROM SyncCursor;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
    }

    [Fact]
    public async Task Malformed_conflict_graph_does_not_commit_payload_or_cursor()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        var operation = Assert.Single(await context.Outbox.PendingAsync());
        context.Api.PushResponse = new SyncPushResponse([
            new(operation.OperationId, SyncOperationStatus.Conflict, 4, BusinessErrorCode.VersionConflict)
        ]);
        var malformed = ServerGraph(local, 4) with
        {
            Exercises = [ServerGraph(local, 4).Exercises[0] with { TrackingMode = 99 }]
        };
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(malformed, 1)
        ], "poisoned-cursor", false));

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Coordinator.RunOnceAsync());

        var conflict = Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Equal(4, conflict.ServerVersion);
        Assert.Null(conflict.ServerPayload);
        await using var connection = new SqliteConnection($"Data Source={context.Path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SyncCursor;";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Push_is_in_global_creation_order_and_partial_results_are_durable_before_pull()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        var set = await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, new LocalSet(70m, null, 8));
        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[1].OperationId, SyncOperationStatus.Conflict, 4, BusinessErrorCode.VersionConflict)
        ]));
        var graph = ServerGraph(local with
        {
            Exercises = [local.Exercises[0] with { Sets = [set] }]
        }, 4);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([Change(graph, 1)], "cursor-1", false));

        await context.Coordinator.RunOnceAsync();

        Assert.Equal(pending.Select(item => item.OperationId),
            context.Api.PushRequests.SelectMany(item => item.Operations).Select(item => item.OperationId));
        var conflict = Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Equal(pending[1].OperationId, conflict.OperationId);
        Assert.NotNull(conflict.ServerPayload);
        Assert.Empty(await context.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Network_absence_is_normal_and_retains_pending_data()
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        context.Api.PushException = new HttpRequestException("offline");

        var status = await context.Coordinator.RunOnceAsync();

        Assert.Equal(SyncRunStatus.Offline, status);
        Assert.Single(await context.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Authentication_failure_is_not_offline_and_retains_pending_data()
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        context.Api.PushException = new SyncApiException(
            SyncApiFailureKind.Authentication,
            HttpStatusCode.Unauthorized,
            BusinessErrorCode.InvalidCredentials,
            "trace-auth",
            "expired");

        var status = await context.Coordinator.RunOnceAsync();

        Assert.Equal(SyncRunStatus.AuthenticationRequired, status);
        Assert.Single(await context.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Retryable_http_failure_persists_bounded_schedule_instead_of_reporting_offline()
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        context.Api.PushException = new SyncApiException(
            SyncApiFailureKind.Retryable,
            HttpStatusCode.TooManyRequests,
            BusinessErrorCode.RateLimitExceeded,
            "trace-rate",
            "slow down");

        var status = await context.Coordinator.RunOnceAsync();

        Assert.Equal(SyncRunStatus.RetryScheduled, status);
        var pending = Assert.Single(await context.Outbox.PendingAsync());
        Assert.Equal(1, pending.RetryCount);
        Assert.Equal(context.Clock.UtcNow.AddSeconds(1), pending.NextAttemptAt);
    }

    [Theory]
    [InlineData(BusinessErrorCode.WorkoutNotFound)]
    [InlineData(BusinessErrorCode.WorkoutAlreadyCompleted)]
    [InlineData(BusinessErrorCode.InvalidSetValue)]
    public async Task Exact_permanent_business_codes_are_accepted_and_retained_for_presentation(
        BusinessErrorCode errorCode)
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        var operation = Assert.Single(await context.Outbox.PendingAsync());
        context.Api.PushResponse = new SyncPushResponse([
            new(operation.OperationId, SyncOperationStatus.Rejected, null, errorCode)
        ]);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([], null, false));

        Assert.Equal(SyncRunStatus.PermanentFailure, await context.Coordinator.RunOnceAsync());

        var rejected = Assert.Single(await context.Outbox.RejectedAsync(operation.EntityId));
        Assert.Equal(errorCode, rejected.FailureCode);
    }

    [Fact]
    public async Task Retryable_predecessor_blocks_causal_successor_across_runs()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, new LocalSet(70m, null, 8));
        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Retryable, null, BusinessErrorCode.InternalServerError)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([], null, false));

        await context.Coordinator.RunOnceAsync();
        context.Api.PullResponses.Enqueue(new SyncPullResponse([], null, false));
        await context.Coordinator.RunOnceAsync();

        Assert.Single(context.Api.PushRequests);
        var retained = await context.Outbox.PendingAsync();
        Assert.Equal(2, retained.Count);
        Assert.Equal(1, retained[0].RetryCount);
        Assert.Equal(0, retained[1].RetryCount);
        Assert.Equal(pending.Select(item => item.Payload), retained.Select(item => item.Payload));
    }

    [Fact]
    public async Task Pulling_applied_base_change_does_not_convert_retryable_successor_to_conflict()
    {
        await using var context = await SyncContext.CreateAsync();
        var local = await context.StartAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await new ActiveWorkoutCoordinator(context.Workouts, context.Boundary, context.Clock)
            .SaveSetAsync(local.Exercises[0].ExerciseDefinitionId, new LocalSet(70m, null, 8));
        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[1].OperationId, SyncOperationStatus.Retryable, null, BusinessErrorCode.InternalServerError)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(ServerGraph(local, 1), 1)
        ], "cursor-1", false));

        await context.Coordinator.RunOnceAsync();

        Assert.Empty(await context.Outbox.ConflictedAsync());
        var retry = Assert.Single(await context.Outbox.PendingAsync());
        Assert.Equal(pending[1].OperationId, retry.OperationId);
        Assert.Equal(1, retry.RetryCount);
        Assert.Equal(pending[1].Payload, retry.Payload);
    }

    [Fact]
    public async Task Account_reset_during_push_prevents_stale_results_mutating_new_generation()
    {
        await using var context = await SyncContext.CreateAsync();
        await context.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Api.PushOverride = async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return context.Api.PushResponse;
        };
        var operation = Assert.Single(await context.Outbox.PendingAsync());
        context.Api.PushResponse = new SyncPushResponse([
            new(operation.OperationId, SyncOperationStatus.Applied, 1, null)
        ]);

        var run = context.Coordinator.RunOnceAsync();
        await entered.Task;
        await context.Boundary.ResetAsync(token => context.Database.ClearPrivateDataAsync(token));
        release.SetResult();

        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        Assert.Empty(await context.Outbox.PendingAsync());
        Assert.Null(await context.Workouts.GetActiveAsync());
    }

    [Fact]
    public async Task Restart_keeps_save_before_effort_and_transient_failure_keeps_both_facts()
    {
        await using var context = await SyncContext.CreateAsync();
        var active = await context.StartAsync();
        var coordinator = new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock);
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        var saved = await coordinator.SaveSetAsync(
            active.Exercises[0].ExerciseDefinitionId,
            new LocalSet(70m, null, 12));
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
        await coordinator.RecordSetEffortAsync(
            active.Exercises[0].ExerciseDefinitionId,
            saved.Id,
            SetEffortRating.Productive,
            Guid.NewGuid());

        var restartedDatabase = new TrackZLocalDatabase(context.Path);
        await restartedDatabase.InitializeAsync();
        var pending = await new OutboxRepository(restartedDatabase).PendingAsync();
        Assert.Equal(
            [OutboxOperationType.StartWorkout, OutboxOperationType.SaveSet,
                OutboxOperationType.RecordSetEffort],
            pending.Select(operation => operation.Type));

        context.Api.PushException = new HttpRequestException("offline");
        Assert.Equal(SyncRunStatus.Offline, await context.Coordinator.RunOnceAsync());
        var restored = Assert.Single(Assert.Single(
            (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
        Assert.Equal(SetEffortRating.Productive, restored.Effort);
        Assert.Equal(3, (await context.Outbox.PendingAsync()).Count);
    }

    [Fact]
    public async Task Applied_push_and_pull_keep_effort_and_measurement_together()
    {
        await using var context = await SyncContext.CreateAsync();
        var active = await context.StartAsync();
        var coordinator = new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock);
        var saved = await coordinator.SaveSetAsync(
            active.Exercises[0].ExerciseDefinitionId,
            new LocalSet(70m, null, 12));
        await coordinator.RecordSetEffortAsync(
            active.Exercises[0].ExerciseDefinitionId,
            saved.Id,
            SetEffortRating.Productive,
            Guid.NewGuid());
        var rated = (await context.Workouts.GetActiveAsync())!;
        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[1].OperationId, SyncOperationStatus.Applied, 2, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[2].OperationId, SyncOperationStatus.Applied, 3, null)]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(ServerGraph(rated, 3), 1)
        ], "effort-cursor", false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());
        var restored = Assert.Single(Assert.Single(
            (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
        Assert.Equal(SetEffortRating.Productive, restored.Effort);
        Assert.Equal(70m, restored.WeightKg);
        Assert.Equal(12, restored.Reps);
    }

    [Fact]
    public async Task Permanent_effort_rejection_uses_server_effort_without_touching_measurement()
    {
        await using var context = await SyncContext.CreateAsync();
        var active = await context.StartAsync();
        var coordinator = new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock);
        var saved = await coordinator.SaveSetAsync(
            active.Exercises[0].ExerciseDefinitionId,
            new LocalSet(70m, null, 12));
        await coordinator.RecordSetEffortAsync(
            active.Exercises[0].ExerciseDefinitionId,
            saved.Id,
            SetEffortRating.Productive,
            Guid.NewGuid());
        var local = (await context.Workouts.GetActiveAsync())!;
        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[1].OperationId, SyncOperationStatus.Applied, 2, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[2].OperationId, SyncOperationStatus.Rejected, null,
                BusinessErrorCode.InvalidRequest)]));
        var authority = ServerGraph(local, 2);
        authority = authority with
        {
            Exercises = authority.Exercises.Select(exercise => exercise with
            {
                Version = 2,
                Sets = exercise.Sets.Select(set => set with
                {
                    Effort = null,
                    UpdatedAt = null,
                    Version = 1
                }).ToArray()
            }).ToArray()
        };
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(authority, 1)
        ], "rejected-effort-cursor", false));

        Assert.Equal(
            SyncRunStatus.PermanentFailure,
            await context.Coordinator.RunOnceAsync());
        var restored = Assert.Single(Assert.Single(
            (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
        Assert.Null(restored.Effort);
        Assert.Equal(70m, restored.WeightKg);
        Assert.Equal(12, restored.Reps);
    }

    [Fact]
    public async Task Save_set_conflict_at_effort_base_moves_effort_into_resolvable_conflict_chain()
    {
        await using var context = await SyncContext.CreateAsync();
        var active = await context.StartAsync();
        var coordinator = new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock);
        var saved = await coordinator.SaveSetAsync(
            active.Exercises[0].ExerciseDefinitionId,
            new LocalSet(70m, null, 12));
        await coordinator.RecordSetEffortAsync(
            active.Exercises[0].ExerciseDefinitionId,
            saved.Id,
            SetEffortRating.Productive,
            Guid.NewGuid());
        var pending = await context.Outbox.PendingAsync();
        var save = pending[1];
        var effort = pending[2];
        Assert.Equal(save.BaseVersion + 1, effort.BaseVersion);
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(save.OperationId, SyncOperationStatus.Conflict, effort.BaseVersion,
                BusinessErrorCode.VersionConflict)]));
        var authority = ServerGraphWithRemoteSet(active, effort.BaseVersion);
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(authority, 1)
        ], "save-effort-conflict-cursor", false));

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        var conflicts = await context.Outbox.ConflictedAsync();
        Assert.Equal(
            [OutboxOperationType.SaveSet, OutboxOperationType.RecordSetEffort],
            conflicts.Select(operation => operation.Type));
        Assert.Empty(await context.Outbox.PendingAsync());
        var replacement = await new ConflictResolution(context.Coordinator)
            .ApplyLocalAgainstVersionAsync(save.OperationId, authority.Version);
        Assert.Equal(save.OperationId, replacement.ReplacesOperationId);
        Assert.Equal(authority.Version, replacement.BaseVersion);
    }

    [Fact]
    public async Task Pull_transitioning_retryable_effort_to_conflict_applies_authority_before_cursor_advance()
    {
        await using var context = await SyncContext.CreateAsync();
        var active = await context.StartAsync();
        var coordinator = new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock);
        var saved = await coordinator.SaveSetAsync(
            active.Exercises[0].ExerciseDefinitionId,
            new LocalSet(70m, null, 12));
        await coordinator.RecordSetEffortAsync(
            active.Exercises[0].ExerciseDefinitionId,
            saved.Id,
            SetEffortRating.Productive,
            Guid.NewGuid());
        var local = (await context.Workouts.GetActiveAsync())!;
        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[1].OperationId, SyncOperationStatus.Applied, 2, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[2].OperationId, SyncOperationStatus.Retryable, null,
                BusinessErrorCode.InternalServerError)]));
        await context.Coordinator.RunOnceAsync();
        var retry = Assert.Single(await context.Outbox.PendingAsync());
        Assert.Equal(OutboxOperationType.RecordSetEffort, retry.Type);
        Assert.True(retry.NextAttemptAt > context.Clock.UtcNow);
        var authority = ServerGraph(local, 4);
        authority = authority with
        {
            Exercises = authority.Exercises.Select(exercise => exercise with
            {
                Sets = exercise.Sets.Select(set => set with
                {
                    Effort = SetEffortRating.Easy
                }).ToArray()
            }).ToArray()
        };
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(authority, 1)
        ], "pending-effort-conflict-cursor", false));
        var pushCount = context.Api.PushRequests.Count;

        Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());

        Assert.Equal(pushCount, context.Api.PushRequests.Count);
        var conflict = Assert.Single(await context.Outbox.ConflictedAsync());
        Assert.Equal(retry.OperationId, conflict.OperationId);
        Assert.NotNull(conflict.ServerPayload);
        Assert.Empty(await context.Outbox.PendingAsync());
        var restored = Assert.Single(Assert.Single(
            (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
        Assert.Equal(SetEffortRating.Easy, restored.Effort);
        await context.Coordinator.RunOnceAsync();
        Assert.Equal("pending-effort-conflict-cursor", context.Api.PullRequests[^1]);
    }

    [Fact]
    public async Task Effort_conflict_rebase_changes_only_base_version_and_recorded_time()
    {
        await using var context = await SyncContext.CreateAsync();
        var active = await context.StartAsync();
        var coordinator = new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock);
        var saved = await coordinator.SaveSetAsync(
            active.Exercises[0].ExerciseDefinitionId,
            new LocalSet(70m, null, 12));
        await coordinator.RecordSetEffortAsync(
            active.Exercises[0].ExerciseDefinitionId,
            saved.Id,
            SetEffortRating.Productive,
            Guid.NewGuid());
        var local = (await context.Workouts.GetActiveAsync())!;
        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[1].OperationId, SyncOperationStatus.Applied, 2, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[2].OperationId, SyncOperationStatus.Conflict, 4,
                BusinessErrorCode.VersionConflict)]));
        var authority = ServerGraph(local, 4);
        authority = authority with
        {
            Exercises = authority.Exercises.Select(exercise => exercise with
            {
                Sets = exercise.Sets.Select(set => set with
                {
                    Effort = SetEffortRating.Easy
                }).ToArray()
            }).ToArray()
        };
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(authority, 1)
        ], "effort-conflict-cursor", false));
        await context.Coordinator.RunOnceAsync();
        var conflict = Assert.Single(await context.Outbox.ConflictedAsync());
        var original = conflict.DeserializePayload<RecordSetEffortOutboxPayload>();
        var pulled = Assert.Single(Assert.Single(
            (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
        Assert.Equal(SetEffortRating.Easy, pulled.Effort);

        var replacement = await new ConflictResolution(context.Coordinator)
            .ApplyLocalAgainstVersionAsync(conflict.OperationId, 4);
        var rebased = replacement.DeserializePayload<RecordSetEffortOutboxPayload>();
        Assert.Equal(conflict.OperationId, replacement.ReplacesOperationId);
        Assert.Equal(4, replacement.BaseVersion);
        Assert.True(rebased.RecordedAt > original.RecordedAt);
        Assert.Equal(original.WorkoutId, rebased.WorkoutId);
        Assert.Equal(original.WorkoutExerciseId, rebased.WorkoutExerciseId);
        Assert.Equal(original.SetId, rebased.SetId);
        Assert.Equal(original.Effort, rebased.Effort);
        Assert.Equal((int)SetEffortRating.Productive, rebased.Effort);
        var authorityAfterRebase = Assert.Single(Assert.Single(
            (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
        Assert.Equal(SetEffortRating.Easy, authorityAfterRebase.Effort);
    }

    [Fact]
    public async Task Effort_conflict_cannot_rebase_against_a_completed_workout()
    {
        await using var context = await SyncContext.CreateAsync();
        var active = await context.StartAsync();
        var coordinator = new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock);
        var saved = await coordinator.SaveSetAsync(
            active.Exercises[0].ExerciseDefinitionId,
            new LocalSet(70m, null, 12));
        await coordinator.RecordSetEffortAsync(
            active.Exercises[0].ExerciseDefinitionId,
            saved.Id,
            SetEffortRating.Productive,
            Guid.NewGuid());
        var local = (await context.Workouts.GetActiveAsync())!;
        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[1].OperationId, SyncOperationStatus.Applied, 2, null)]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[2].OperationId, SyncOperationStatus.Conflict, 4,
                BusinessErrorCode.VersionConflict)]));
        var completedAuthority = ServerGraph(local, 4) with
        {
            Status = (int)WorkoutStatus.Completed,
            CompletedAt = context.Clock.UtcNow.AddMinutes(1)
        };
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(completedAuthority, 1)
        ], "completed-effort-conflict-cursor", false));
        await context.Coordinator.RunOnceAsync();
        var conflict = Assert.Single(await context.Outbox.ConflictedAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ConflictResolution(context.Coordinator)
                .ApplyLocalAgainstVersionAsync(conflict.OperationId, 4));
    }

    private static SyncChangeDto Change(SyncWorkoutDto graph, long sequence) => new(
        sequence, "Workout", graph.Id, graph.Version, graph.DeletedAt is not null,
        new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero), graph);

    private static SyncWorkoutDto ServerGraph(LocalWorkout local, long version) => new(
        local.Id, (int)local.Status, local.StartedAt, local.CompletedAt, local.DeletedAt, version,
        local.Exercises.Select(exercise => new SyncWorkoutExerciseDto(
            exercise.Id, exercise.ExerciseDefinitionId, (int)exercise.TrackingMode,
            exercise.Order, exercise.DeletedAt, exercise.Version,
            exercise.Sets.Select(set => new SyncSetDto(
                set.Id, set.Order, set.WeightKg?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                set.AssistedKg?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                set.Reps, set.CompletedAt, set.UpdatedAt, set.DeletedAt, set.Version,
                set.Effort)).ToArray())).ToArray());

    private static SyncWorkoutDto ServerGraphWithRemoteSet(LocalWorkout local, long version)
    {
        var graph = ServerGraph(local, version);
        var exercise = graph.Exercises[0];
        return graph with
        {
            Exercises = [exercise with
            {
                Version = exercise.Version + 1,
                Sets = [new SyncSetDto(
                    Guid.NewGuid(), 0, "60", null, 10,
                    local.StartedAt.AddSeconds(30), null, null, 1)]
            }]
        };
    }

    private static SyncWorkoutDto ServerGraphAfterRebase(
        LocalWorkout local,
        OutboxOperation original,
        long version)
    {
        var graph = ServerGraphWithRemoteSet(local, version);
        var exercise = graph.Exercises[0];
        var payload = original.DeserializePayload<SaveSetOutboxPayload>();
        return graph with
        {
            Exercises = [exercise with
            {
                Version = exercise.Version + 1,
                Sets = [.. exercise.Sets, new SyncSetDto(
                    payload.SetId, 1, payload.WeightKg, payload.AssistedKg, payload.Reps,
                    payload.CompletedAt, null, null, 1)]
            }]
        };
    }

    private static SyncWorkoutDto AppendServerSet(
        SyncWorkoutDto server,
        OutboxOperation replacement,
        long version)
    {
        var payload = replacement.DeserializePayload<SaveSetOutboxPayload>();
        var exercise = server.Exercises.Single(item => item.Id == payload.WorkoutExerciseId);
        return server with
        {
            Version = version,
            Exercises = server.Exercises.Select(item => item.Id == exercise.Id
                ? item with
                {
                    Version = item.Version + 1,
                    Sets = [.. item.Sets, new SyncSetDto(
                        payload.SetId,
                        item.Sets.Count(set => set.DeletedAt is null),
                        payload.WeightKg,
                        payload.AssistedKg,
                        payload.Reps,
                        payload.CompletedAt,
                        null,
                        null,
                        1)]
                }
                : item).ToArray()
        };
    }

    private static SyncWorkoutDto EmptyServerGraph(Guid id, long version) => new(
        id, 2,
        new DateTimeOffset(2026, 8, 16, 7, 0, 0, TimeSpan.Zero),
        null,
        null, version, [new SyncWorkoutExerciseDto(
            Guid.NewGuid(), Guid.NewGuid(), (int)TrackingMode.Bodyweight, 0, null, 1, [])]);

    private static SyncWorkoutDto CompletedServerGraph(
        Guid workoutId,
        Guid exerciseId,
        Guid setId,
        long version)
    {
        var startedAt = new DateTimeOffset(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);
        return new SyncWorkoutDto(
            workoutId, 3, startedAt, startedAt.AddMinutes(10), null, version,
            [new SyncWorkoutExerciseDto(
                exerciseId, Guid.NewGuid(), (int)TrackingMode.Weighted, 0, null, 1,
                [new SyncSetDto(
                    setId, 0, "70", null, 8, startedAt.AddMinutes(2), null, null, 1)])]);
    }

    private static SyncWorkoutDto CompletedGraphWithDeletedExercise(
        Guid workoutId,
        Guid collidingExerciseId,
        Guid activeSetId,
        long version)
    {
        var startedAt = new DateTimeOffset(2026, 8, 16, 8, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(10);
        return new SyncWorkoutDto(
            workoutId, 3, startedAt, completedAt, null, version,
            [
                new SyncWorkoutExerciseDto(
                    collidingExerciseId, Guid.NewGuid(), (int)TrackingMode.Bodyweight,
                    0, completedAt.AddMinutes(1), 2, []),
                new SyncWorkoutExerciseDto(
                    Guid.NewGuid(), Guid.NewGuid(), (int)TrackingMode.Weighted,
                    0, null, 1,
                    [new SyncSetDto(
                        activeSetId, 0, "75", null, 9,
                        startedAt.AddMinutes(3), null, null, 1)])
            ]);
    }

    private static SyncWorkoutDto CompletedGraphWithDeletedSet(
        Guid workoutId,
        Guid deletedExerciseId,
        Guid collidingSetId,
        long version)
    {
        var startedAt = new DateTimeOffset(2026, 8, 16, 8, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(10);
        var deletedAt = completedAt.AddMinutes(1);
        return new SyncWorkoutDto(
            workoutId, 3, startedAt, completedAt, null, version,
            [
                new SyncWorkoutExerciseDto(
                    deletedExerciseId, Guid.NewGuid(), (int)TrackingMode.Weighted,
                    0, deletedAt, 2,
                    [new SyncSetDto(
                        collidingSetId, 0, "80", null, 10,
                        startedAt.AddMinutes(3), null, deletedAt, 2)]),
                new SyncWorkoutExerciseDto(
                    Guid.NewGuid(), Guid.NewGuid(), (int)TrackingMode.Weighted,
                    0, null, 1,
                    [new SyncSetDto(
                        Guid.NewGuid(), 0, "75", null, 9,
                        startedAt.AddMinutes(4), null, null, 1)])
            ]);
    }

    private static SyncWorkoutDto CompletedGraphWithPreCompletionHistory(
        Guid workoutId,
        long version)
    {
        var startedAt = new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);
        return new SyncWorkoutDto(
            workoutId, 3, startedAt, startedAt.AddMinutes(10), null, version,
            [
                new SyncWorkoutExerciseDto(
                    Guid.NewGuid(), Guid.NewGuid(), (int)TrackingMode.Weighted,
                    0, startedAt.AddMinutes(8), 5,
                    [new SyncSetDto(
                        Guid.NewGuid(), 0, "72", null, 10,
                        startedAt.AddMinutes(1), startedAt.AddMinutes(2),
                        startedAt.AddMinutes(3), 3)]),
                new SyncWorkoutExerciseDto(
                    Guid.NewGuid(), Guid.NewGuid(), (int)TrackingMode.Weighted,
                    0, null, 2,
                    [new SyncSetDto(
                        Guid.NewGuid(), 0, "80", null, 8,
                        startedAt.AddMinutes(4), null, null, 1)])
            ]);
    }

    private static async Task AssertOnlyCachedGraphRemainsAsync(
        string path,
        SyncWorkoutDto cached,
        string expectedCursor,
        long expectedWorkoutCount = 1)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM LocalWorkout;
            SELECT WorkoutId, DeletedAt FROM LocalWorkoutExercise WHERE Id = $exerciseId;
            SELECT WorkoutExerciseId, WeightKg, DeletedAt FROM LocalSet WHERE Id = $setId;
            SELECT Cursor FROM SyncCursor WHERE Scope = 'workouts';
            """;
        command.Parameters.AddWithValue("$exerciseId", cached.Exercises[0].Id.ToString("D"));
        command.Parameters.AddWithValue("$setId", cached.Exercises[0].Sets[0].Id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedWorkoutCount, reader.GetInt64(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(cached.Id.ToString("D"), reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(cached.Exercises[0].Id.ToString("D"), reader.GetString(0));
        Assert.Equal("70", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedCursor, reader.GetString(0));
    }

    private static async Task<(
        LocalWorkout ServerGraph,
        OutboxOperation Conflict,
        IReadOnlyList<OutboxOperation> Successors)> PrepareConflictWithSuccessorsAsync(
        SyncContext context)
    {
        var serverGraph = await context.StartAsync();
        var active = new ActiveWorkoutCoordinator(
            context.Workouts, context.Boundary, context.Clock);
        for (var index = 0; index < 3; index++)
        {
            context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
            await active.SaveSetAsync(
                serverGraph.Exercises[0].ExerciseDefinitionId,
                new LocalSet(70m + index, null, 8 + index));
        }

        var pending = await context.Outbox.PendingAsync();
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)
        ]));
        context.Api.PushResponses.Enqueue(new SyncPushResponse([
            new(pending[1].OperationId, SyncOperationStatus.Conflict, 4,
                BusinessErrorCode.VersionConflict)
        ]));
        context.Api.PullResponses.Enqueue(new SyncPullResponse([
            Change(ServerGraph(serverGraph, 4), 1)
        ], "cursor-conflict", false));

        await context.Coordinator.RunOnceAsync();

        var conflicts = await context.Outbox.ConflictedAsync();
        Assert.Equal(3, conflicts.Count);
        return (serverGraph, conflicts[0], conflicts.Skip(1).ToArray());
    }

    private static async Task InsertOperationAsync(string path, OutboxOperation operation)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OutboxOperation
                (OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
                 State, DeletedAt, Version, ServerVersion, RetryCount, NextAttemptAt,
                 ServerPayload, ReplacesOperationId)
            VALUES ($id, $entityId, $type, $payload, $baseVersion, $createdAt,
                    $state, NULL, $version, NULL, 0, NULL, NULL, $replaces);
            """;
        command.Parameters.AddWithValue("$id", operation.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$entityId", operation.EntityId.ToString("D"));
        command.Parameters.AddWithValue("$type", (int)operation.Type);
        command.Parameters.AddWithValue("$payload", operation.Payload);
        command.Parameters.AddWithValue("$baseVersion", operation.BaseVersion);
        command.Parameters.AddWithValue("$createdAt", operation.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$state", (int)operation.State);
        command.Parameters.AddWithValue("$version", operation.Version);
        command.Parameters.AddWithValue("$replaces", operation.ReplacesOperationId!.Value.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountReplacementsAsync(string path, Guid operationId)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM OutboxOperation
            WHERE ReplacesOperationId = $operationId;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static void AssertWorkoutIntentEqual(LocalWorkout expected, LocalWorkout actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(
            expected.Exercises.SelectMany(exercise => exercise.Sets).Select(set => set.Id),
            actual.Exercises.SelectMany(exercise => exercise.Sets).Select(set => set.Id));
        Assert.Equal(
            expected.Exercises.SelectMany(exercise => exercise.Sets).Select(set => set.WeightKg),
            actual.Exercises.SelectMany(exercise => exercise.Sets).Select(set => set.WeightKg));
    }

    private sealed class SyncContext : IAsyncDisposable
    {
        private SyncContext(string path)
        {
            Path = path;
            Database = new TrackZLocalDatabase(path);
            Workouts = new LocalWorkoutRepository(Database);
            Outbox = new OutboxRepository(Database);
            Boundary = new AccountSessionBoundary();
            Clock = new MutableClock(new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero));
            Api = new FakeSyncApi();
            Coordinator = new SyncCoordinator(Database, Api, Boundary, Clock);
        }

        public string Path { get; }
        public TrackZLocalDatabase Database { get; }
        public LocalWorkoutRepository Workouts { get; }
        public OutboxRepository Outbox { get; }
        public AccountSessionBoundary Boundary { get; }
        public MutableClock Clock { get; }
        public FakeSyncApi Api { get; }
        public SyncCoordinator Coordinator { get; }

        public static async Task<SyncContext> CreateAsync()
        {
            var context = new SyncContext(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"trackz-sync-{Guid.NewGuid():N}.db"));
            await context.Database.InitializeAsync();
            return context;
        }

        public Task<LocalWorkout> StartAsync() => new ActiveWorkoutCoordinator(
            Workouts, Boundary, Clock).StartAsync([
                new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)
            ]);

        public ValueTask DisposeAsync()
        {
            File.Delete(Path);
            File.Delete(Path + "-wal");
            File.Delete(Path + "-shm");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSyncApi : ISyncApi
    {
        public SyncPushResponse PushResponse { get; set; } = new([]);
        public List<SyncPushRequest> PushRequests { get; } = [];
        public List<string?> PullRequests { get; } = [];
        public Queue<SyncPullResponse> PullResponses { get; } = [];
        public Queue<SyncPushResponse> PushResponses { get; } = [];
        public Exception? PushException { get; set; }
        public Func<SyncPushRequest, CancellationToken, Task<SyncPushResponse>>? PushOverride { get; set; }
        public Func<string?, CancellationToken, Task<SyncPullResponse>>? PullOverride { get; set; }

        public Task<SyncPushResponse> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default)
        {
            PushRequests.Add(request);
            if (PushException is not null) throw PushException;
            if (PushOverride is not null) return PushOverride(request, cancellationToken);
            return Task.FromResult(PushResponses.Count == 0
                ? PushResponse
                : PushResponses.Dequeue());
        }

        public Task<SyncPullResponse> PullAsync(
            string? cursor,
            CancellationToken cancellationToken = default)
        {
            PullRequests.Add(cursor);
            if (PullOverride is not null) return PullOverride(cursor, cancellationToken);
            return Task.FromResult(PullResponses.Count == 0
                ? new SyncPullResponse([], cursor, false)
                : PullResponses.Dequeue());
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
