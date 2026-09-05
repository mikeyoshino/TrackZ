using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Data;

public interface ILocalWorkoutRepository
{
    Task SaveWorkoutAndEnqueueAsync(
        LocalWorkout workout,
        OutboxOperation operation,
        CancellationToken cancellationToken = default);

    Task<LocalWorkout?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalWorkout>> GetHistoryAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("History reads are not supported by this repository.");

    Task<LocalWorkout?> GetHistoryWorkoutAsync(
        Guid workoutId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("History reads are not supported by this repository.");

    Task<bool> IsExerciseHistorySessionInvalidatedAsync(
        Guid workoutId,
        Guid exerciseDefinitionId,
        CancellationToken cancellationToken = default);

    Task SaveHistoryMutationAndEnqueueAsync(
        LocalWorkout previous,
        LocalWorkout workout,
        OutboxOperation operation,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("History mutations are not supported by this repository.");

    Task<LocalWorkout> UndoHistoryMutationAsync(
        Guid operationId,
        DateTimeOffset neutralizedAt,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("History undo is not supported by this repository.");

    Task<OutboxOperation?> GetOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLatestOperationCreatedAtAsync(
        CancellationToken cancellationToken = default);

    Task ClearPrivateDataAsync(CancellationToken cancellationToken = default);
}

internal interface ILocalWorkoutReadCheckpoint
{
    Task AfterHeaderReadAsync(CancellationToken cancellationToken);
}

public sealed class LocalWorkoutRepository : ILocalWorkoutRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TrackZLocalDatabase _database;
    private readonly ILocalWorkoutReadCheckpoint? _readCheckpoint;

    public LocalWorkoutRepository(TrackZLocalDatabase database)
        : this(database, null)
    {
    }

    internal LocalWorkoutRepository(
        TrackZLocalDatabase database,
        ILocalWorkoutReadCheckpoint? readCheckpoint)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _readCheckpoint = readCheckpoint;
    }

    public async Task SaveWorkoutAndEnqueueAsync(
        LocalWorkout workout,
        OutboxOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workout);
        ArgumentNullException.ThrowIfNull(operation);
        ValidateGraph(workout);
        ValidateOperation(workout, operation);

        _ = await _database.WriteAsync(async (connection, transaction, token) =>
        {
            if (await ExistingOperationMatchesOrThrowAsync(
                    connection, transaction, operation, token)) return true;
            await EnsureMutationCanFollowReplacementAsync(
                connection, transaction, operation, token);
            await ValidateCoverageAndStageOrdersAsync(connection, transaction, workout, token);
            await UpsertWorkoutAsync(connection, transaction, workout, token);
            foreach (var exercise in workout.Exercises)
            {
                await UpsertExerciseAsync(connection, transaction, exercise, token);
                foreach (var set in exercise.Sets)
                {
                    await UpsertSetAsync(connection, transaction, set, token);
                }
            }
            await InsertOperationAsync(connection, transaction, operation, token);
            return true;
        }, cancellationToken);
    }

    public Task<LocalWorkout?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        _database.ReadTransactionAsync(ReadActiveAsync, cancellationToken);

    public Task<IReadOnlyList<LocalWorkout>> GetHistoryAsync(
        CancellationToken cancellationToken = default) =>
        _database.ReadTransactionAsync(ReadHistoryAsync, cancellationToken);

    public async Task<LocalWorkout?> GetHistoryWorkoutAsync(
        Guid workoutId,
        CancellationToken cancellationToken = default)
    {
        if (workoutId == Guid.Empty)
            throw new ArgumentException("Workout ID cannot be empty.", nameof(workoutId));
        return (await GetHistoryAsync(cancellationToken)).SingleOrDefault(item => item.Id == workoutId);
    }

    public Task<bool> IsExerciseHistorySessionInvalidatedAsync(
        Guid workoutId,
        Guid exerciseDefinitionId,
        CancellationToken cancellationToken = default)
    {
        if (workoutId == Guid.Empty)
            throw new ArgumentException("Workout ID cannot be empty.", nameof(workoutId));
        if (exerciseDefinitionId == Guid.Empty)
            throw new ArgumentException("Exercise definition ID cannot be empty.", nameof(exerciseDefinitionId));
        return _database.ReadAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT workout.Status, workout.DeletedAt,
                       EXISTS (
                           SELECT 1
                           FROM LocalWorkoutExercise AS exercise
                           INNER JOIN LocalSet AS workoutSet
                               ON workoutSet.WorkoutExerciseId = exercise.Id
                              AND workoutSet.DeletedAt IS NULL
                           WHERE exercise.WorkoutId = workout.Id
                             AND exercise.ExerciseDefinitionId = $exerciseDefinitionId
                             AND exercise.DeletedAt IS NULL
                       )
                FROM LocalWorkout AS workout
                WHERE workout.Id = $workoutId;
                """;
            Add(command, "$workoutId", Id(workoutId));
            Add(command, "$exerciseDefinitionId", Id(exerciseDefinitionId));
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return false;
            return reader.GetInt32(0) != (int)LocalWorkoutStatus.Completed
                || !reader.IsDBNull(1)
                || reader.GetInt32(2) == 0;
        }, cancellationToken);
    }

    public async Task SaveHistoryMutationAndEnqueueAsync(
        LocalWorkout previous,
        LocalWorkout workout,
        OutboxOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(workout);
        ArgumentNullException.ThrowIfNull(operation);
        ValidateGraph(previous);
        ValidateGraph(workout);
        ValidateOperation(workout, operation);
        var isCompletion = operation.Type == OutboxOperationType.CompleteWorkout
            && previous.Status == LocalWorkoutStatus.Active
            && workout.Status == LocalWorkoutStatus.Completed;
        var isHistoricalMutation = operation.Type is (
                OutboxOperationType.EditSet
                or OutboxOperationType.DeleteSet
                or OutboxOperationType.DeleteWorkout
                or OutboxOperationType.DeleteWorkoutExercise)
            && previous.Status == LocalWorkoutStatus.Completed
            && workout.Status == LocalWorkoutStatus.Completed;
        if (previous.Id != workout.Id
            || (!isCompletion && !isHistoricalMutation)
            || workout.Version != previous.Version + 1
            || operation.BaseVersion != previous.Version)
            throw new ArgumentException("The history mutation contract is invalid.", nameof(operation));

        var snapshotJson = JsonSerializer.Serialize(ToUndoSnapshot(previous), JsonOptions);
        _ = await _database.WriteAsync(async (connection, transaction, token) =>
        {
            if (await ExistingOperationMatchesOrThrowAsync(
                    connection, transaction, operation, token)) return true;
            await EnsureMutationCanFollowReplacementAsync(connection, transaction, operation, token);
            await ValidateCoverageAndStageOrdersAsync(connection, transaction, workout, token);
            await UpsertWorkoutAsync(connection, transaction, workout, token);
            foreach (var exercise in workout.Exercises)
            {
                await UpsertExerciseAsync(connection, transaction, exercise, token);
                foreach (var set in exercise.Sets)
                    await UpsertSetAsync(connection, transaction, set, token);
            }
            await InsertOperationAsync(connection, transaction, operation, token);
            await using var undo = connection.CreateCommand();
            undo.Transaction = transaction;
            undo.CommandText = """
                INSERT INTO HistoryUndo (OperationId, SnapshotJson, CreatedAt)
                VALUES ($operationId, $snapshot, $createdAt);
                """;
            Add(undo, "$operationId", Id(operation.OperationId));
            Add(undo, "$snapshot", snapshotJson);
            Add(undo, "$createdAt", Timestamp(operation.CreatedAt));
            await undo.ExecuteNonQueryAsync(token);
            return true;
        }, cancellationToken);
    }

    public Task<LocalWorkout> UndoHistoryMutationAsync(
        Guid operationId,
        DateTimeOffset neutralizedAt,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
        RequireTimestamp(neutralizedAt, nameof(neutralizedAt));
        return _database.WriteAsync(async (connection, transaction, token) =>
        {
            var operation = await ReadOperationAsync(connection, transaction, operationId, token);
            if (operation.Type is not (
                    OutboxOperationType.CompleteWorkout
                    or OutboxOperationType.EditSet
                    or OutboxOperationType.DeleteSet
                    or OutboxOperationType.DeleteWorkout
                    or OutboxOperationType.DeleteWorkoutExercise)
                || operation.NeutralizedAt is not null)
                throw new InvalidOperationException("The operation cannot be undone.");
            var safeState = operation.State is OutboxOperationState.Rejected
                or OutboxOperationState.Conflicted
                || operation.State == OutboxOperationState.Pending
                && operation.SendStartedAt is null;
            if (!safeState)
                throw new InvalidOperationException(
                    "The operation may already have reached the server and cannot be undone yet.");

            await using (var successors = connection.CreateCommand())
            {
                successors.Transaction = transaction;
                successors.CommandText = """
                    SELECT EXISTS (
                        SELECT 1 FROM OutboxOperation
                        WHERE EntityId = $entityId AND NeutralizedAt IS NULL
                          AND (CreatedAt > $createdAt
                               OR (CreatedAt = $createdAt AND OperationId > $operationId))
                    );
                    """;
                Add(successors, "$entityId", Id(operation.EntityId));
                Add(successors, "$createdAt", Timestamp(operation.CreatedAt));
                Add(successors, "$operationId", Id(operation.OperationId));
                if (Convert.ToInt64(await successors.ExecuteScalarAsync(token)) != 0)
                    throw new InvalidOperationException(
                        "Undo later workout changes before undoing this operation.");
            }

            string snapshotJson;
            await using (var snapshot = connection.CreateCommand())
            {
                snapshot.Transaction = transaction;
                snapshot.CommandText = "SELECT SnapshotJson FROM HistoryUndo WHERE OperationId = $id;";
                Add(snapshot, "$id", Id(operationId));
                snapshotJson = (string?)await snapshot.ExecuteScalarAsync(token)
                    ?? throw new InvalidDataException("The durable undo snapshot is missing.");
            }
            var durableSnapshot = JsonSerializer.Deserialize<HistoryUndoWorkout>(snapshotJson, JsonOptions)
                ?? throw new InvalidDataException("The durable undo snapshot is malformed.");
            var restored = FromUndoSnapshot(durableSnapshot);
            ValidateGraph(restored);
            if (restored.Id != operation.EntityId)
                throw new InvalidDataException("The undo snapshot belongs to another workout.");

            await RestoreSnapshotAsync(connection, transaction, restored, token);
            await NeutralizeOperationAsync(
                connection, transaction, operationId, neutralizedAt, token);
            var ancestorId = operation.ReplacesOperationId;
            var visited = new HashSet<Guid> { operation.OperationId };
            while (ancestorId is { } id)
            {
                if (!visited.Add(id))
                    throw new InvalidDataException("The replacement chain is cyclic.");
                var ancestor = await ReadOperationAsync(connection, transaction, id, token);
                if (ancestor.NeutralizedAt is null)
                {
                    if (ancestor.State is not (
                            OutboxOperationState.Conflicted or OutboxOperationState.Rejected))
                        throw new InvalidOperationException(
                            "A replaced operation may have reached the server and cannot be undone safely.");
                    await NeutralizeOperationAsync(
                        connection, transaction, ancestor.OperationId, neutralizedAt, token);
                }
                ancestorId = ancestor.ReplacesOperationId;
            }
            await using (var removeUndo = connection.CreateCommand())
            {
                removeUndo.Transaction = transaction;
                removeUndo.CommandText = "DELETE FROM HistoryUndo WHERE OperationId = $id;";
                Add(removeUndo, "$id", Id(operationId));
                if (await removeUndo.ExecuteNonQueryAsync(token) != 1)
                    throw new InvalidDataException("The undo snapshot changed concurrently.");
            }
            return restored;
        }, cancellationToken);
    }

    private static async Task NeutralizeOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        DateTimeOffset neutralizedAt,
        CancellationToken cancellationToken)
    {
        await using var neutralize = connection.CreateCommand();
        neutralize.Transaction = transaction;
        neutralize.CommandText = """
            UPDATE OutboxOperation
            SET State = $state, DeletedAt = $neutralizedAt,
                NeutralizedAt = $neutralizedAt, SendStartedAt = NULL,
                NextAttemptAt = NULL, Version = Version + 1
            WHERE OperationId = $id AND NeutralizedAt IS NULL;
            """;
        Add(neutralize, "$state", (int)OutboxOperationState.Rejected);
        Add(neutralize, "$neutralizedAt", Timestamp(neutralizedAt));
        Add(neutralize, "$id", Id(operationId));
        if (await neutralize.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidDataException("The undo operation changed concurrently.");
    }

    public Task<OutboxOperation?> GetOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
        return _database.ReadAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                       CreatedAt, State, DeletedAt, Version, ServerVersion, RetryCount,
                       NextAttemptAt, ServerPayload, ReplacesOperationId, SendStartedAt,
                       NeutralizedAt, FailureCode
                FROM OutboxOperation WHERE OperationId = $id;
                """;
            Add(command, "$id", Id(operationId));
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            return ReadOperation(reader);
        }, cancellationToken);
    }

    public Task<DateTimeOffset?> GetLatestOperationCreatedAtAsync(
        CancellationToken cancellationToken = default) =>
        _database.ReadAsync<DateTimeOffset?>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(CreatedAt) FROM OutboxOperation;";
            var value = await command.ExecuteScalarAsync(token);
            return value is string text ? ParseTimestamp(text) : null;
        }, cancellationToken);

    public Task ClearPrivateDataAsync(CancellationToken cancellationToken = default) =>
        _database.ClearPrivateDataAsync(cancellationToken);

    private async Task<LocalWorkout?> ReadActiveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            LocalWorkout? workout = null;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT Id, Status, StartedAt, CompletedAt, DeletedAt, Version, BaseVersion
                    FROM LocalWorkout
                    WHERE Status = 2 AND DeletedAt IS NULL
                    ORDER BY Id;
                    """;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    workout = new LocalWorkout(
                        GuidValue(reader, 0),
                        EnumValue<LocalWorkoutStatus>(reader, 1),
                        Timestamp(reader, 2)!.Value,
                        Timestamp(reader, 3),
                        Timestamp(reader, 4),
                        NonNegativeInt64(reader, 5),
                        NonNegativeInt64(reader, 6),
                        []);
                    if (await reader.ReadAsync(cancellationToken))
                        throw new InvalidDataException("More than one active workout was stored.");
                }
            }
            if (_readCheckpoint is not null)
                await _readCheckpoint.AfterHeaderReadAsync(cancellationToken);
            if (workout is null) return null;

            var exercises = new List<LocalWorkoutExercise>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT Id, WorkoutId, ExerciseDefinitionId, TrackingMode, SortOrder,
                           DeletedAt, Version, BaseVersion
                    FROM LocalWorkoutExercise
                    WHERE WorkoutId = $workoutId
                    ORDER BY SortOrder, Id;
                    """;
                Add(command, "$workoutId", Id(workout.Id));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    exercises.Add(new LocalWorkoutExercise(
                        GuidValue(reader, 0),
                        GuidValue(reader, 1),
                        GuidValue(reader, 2),
                        EnumValue<TrackingMode>(reader, 3),
                        NonNegativeInt32(reader, 4),
                        Timestamp(reader, 5),
                        NonNegativeInt64(reader, 6),
                        NonNegativeInt64(reader, 7),
                        []));
                }
            }

            var hydrated = new List<LocalWorkoutExercise>(exercises.Count);
            foreach (var exercise in exercises)
            {
                var sets = new List<LocalSet>();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT Id, OperationId, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                           Effort, CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion, PlateCount
                    FROM LocalSet
                    WHERE WorkoutExerciseId = $exerciseId
                    ORDER BY SortOrder, Id;
                    """;
                Add(command, "$exerciseId", Id(exercise.Id));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    sets.Add(new LocalSet(
                        GuidValue(reader, 0),
                        GuidValue(reader, 2),
                        NonNegativeInt32(reader, 3),
                        DecimalValue(reader, 4),
                        DecimalValue(reader, 5),
                        PositiveInt32(reader, 6),
                        Timestamp(reader, 8)!.Value,
                        Timestamp(reader, 9),
                        Timestamp(reader, 10),
                        NonNegativeInt64(reader, 11),
                        NonNegativeInt64(reader, 12),
                        GuidValue(reader, 1),
                        reader.IsDBNull(7)
                            ? null
                            : EnumValue<SetEffortRating>(reader, 7),
                        reader.IsDBNull(13) ? null : reader.GetInt32(13)));
                }
                hydrated.Add(exercise with { Sets = sets });
            }

            var result = workout with { Exercises = hydrated };
            ValidateGraph(result);
            return result;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException("The active workout database row is corrupt.", exception);
        }
    }

    private async Task<IReadOnlyList<LocalWorkout>> ReadHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var workouts = new List<LocalWorkout>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT Id, Status, StartedAt, CompletedAt, DeletedAt, Version, BaseVersion
                FROM LocalWorkout
                WHERE Status = 3 AND (
                    DeletedAt IS NULL OR EXISTS (
                        SELECT 1
                        FROM OutboxOperation AS operation
                        INNER JOIN HistoryUndo AS undo
                            ON undo.OperationId = operation.OperationId
                        WHERE operation.EntityId = LocalWorkout.Id
                    )
                )
                ORDER BY CompletedAt DESC, Id DESC;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                workouts.Add(new LocalWorkout(
                    GuidValue(reader, 0),
                    EnumValue<LocalWorkoutStatus>(reader, 1),
                    Timestamp(reader, 2)!.Value,
                    Timestamp(reader, 3),
                    Timestamp(reader, 4),
                    NonNegativeInt64(reader, 5),
                    NonNegativeInt64(reader, 6),
                    []));
            }
        }

        var hydrated = new List<LocalWorkout>(workouts.Count);
        foreach (var workout in workouts)
        {
            var exercises = new List<LocalWorkoutExercise>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT Id, WorkoutId, ExerciseDefinitionId, TrackingMode, SortOrder,
                           DeletedAt, Version, BaseVersion
                    FROM LocalWorkoutExercise
                    WHERE WorkoutId = $workoutId
                    ORDER BY SortOrder, Id;
                    """;
                Add(command, "$workoutId", Id(workout.Id));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    exercises.Add(new LocalWorkoutExercise(
                        GuidValue(reader, 0),
                        GuidValue(reader, 1),
                        GuidValue(reader, 2),
                        EnumValue<TrackingMode>(reader, 3),
                        NonNegativeInt32(reader, 4),
                        Timestamp(reader, 5),
                        NonNegativeInt64(reader, 6),
                        NonNegativeInt64(reader, 7),
                        []));
                }
            }

            var hydratedExercises = new List<LocalWorkoutExercise>(exercises.Count);
            foreach (var exercise in exercises)
            {
                var sets = new List<LocalSet>();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT Id, OperationId, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                           Effort, CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion, PlateCount
                    FROM LocalSet
                    WHERE WorkoutExerciseId = $exerciseId AND (
                        DeletedAt IS NULL OR EXISTS (
                            SELECT 1
                            FROM OutboxOperation AS operation
                            INNER JOIN HistoryUndo AS undo
                                ON undo.OperationId = operation.OperationId
                            WHERE operation.EntityId = $workoutId
                              AND operation.OperationType = 5
                              AND operation.NeutralizedAt IS NULL
                              AND json_extract(operation.Payload, '$.setId') = LocalSet.Id
                        )
                    )
                    ORDER BY SortOrder, Id;
                    """;
                Add(command, "$exerciseId", Id(exercise.Id));
                Add(command, "$workoutId", Id(workout.Id));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    sets.Add(new LocalSet(
                        GuidValue(reader, 0),
                        GuidValue(reader, 2),
                        NonNegativeInt32(reader, 3),
                        DecimalValue(reader, 4),
                        DecimalValue(reader, 5),
                        PositiveInt32(reader, 6),
                        Timestamp(reader, 8)!.Value,
                        Timestamp(reader, 9),
                        Timestamp(reader, 10),
                        NonNegativeInt64(reader, 11),
                        NonNegativeInt64(reader, 12),
                        GuidValue(reader, 1),
                        reader.IsDBNull(7)
                            ? null
                            : EnumValue<SetEffortRating>(reader, 7),
                        reader.IsDBNull(13) ? null : reader.GetInt32(13)));
                }
                hydratedExercises.Add(exercise with { Sets = sets });
            }

            var result = workout with { Exercises = hydratedExercises };
            ValidateGraph(result);
            hydrated.Add(result);
        }
        return hydrated;
    }

    private static async Task UpsertWorkoutAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalWorkout workout,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO LocalWorkout
                (Id, Status, StartedAt, CompletedAt, DeletedAt, Version, BaseVersion)
            VALUES
                ($id, $status, $startedAt, $completedAt, $deletedAt, $version, $baseVersion)
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status,
                StartedAt = excluded.StartedAt,
                CompletedAt = excluded.CompletedAt,
                DeletedAt = excluded.DeletedAt,
                Version = excluded.Version,
                BaseVersion = excluded.BaseVersion
            WHERE excluded.Version > LocalWorkout.Version;
            """;
        Add(command, "$id", Id(workout.Id));
        Add(command, "$status", (int)workout.Status);
        Add(command, "$startedAt", Timestamp(workout.StartedAt));
        Add(command, "$completedAt", Timestamp(workout.CompletedAt));
        Add(command, "$deletedAt", Timestamp(workout.DeletedAt));
        Add(command, "$version", workout.Version);
        Add(command, "$baseVersion", workout.BaseVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0
            && !await WorkoutMatchesAsync(connection, transaction, workout, cancellationToken))
            throw new InvalidDataException("A stale or divergent workout snapshot cannot replace local data.");
    }

    private static async Task UpsertExerciseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalWorkoutExercise exercise,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO LocalWorkoutExercise
                (Id, WorkoutId, ExerciseDefinitionId, TrackingMode, SortOrder, DeletedAt, Version, BaseVersion)
            VALUES
                ($id, $workoutId, $exerciseId, $trackingMode, $order, $deletedAt, $version, $baseVersion)
            ON CONFLICT(Id) DO UPDATE SET
                WorkoutId = excluded.WorkoutId,
                ExerciseDefinitionId = excluded.ExerciseDefinitionId,
                TrackingMode = excluded.TrackingMode,
                SortOrder = excluded.SortOrder,
                DeletedAt = excluded.DeletedAt,
                Version = excluded.Version,
                BaseVersion = excluded.BaseVersion
            WHERE excluded.Version >= LocalWorkoutExercise.Version;
            """;
        Add(command, "$id", Id(exercise.Id));
        Add(command, "$workoutId", Id(exercise.WorkoutId));
        Add(command, "$exerciseId", Id(exercise.ExerciseDefinitionId));
        Add(command, "$trackingMode", (int)exercise.TrackingMode);
        Add(command, "$order", exercise.Order);
        Add(command, "$deletedAt", Timestamp(exercise.DeletedAt));
        Add(command, "$version", exercise.Version);
        Add(command, "$baseVersion", exercise.BaseVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0
            && !await ExerciseMatchesAsync(connection, transaction, exercise, cancellationToken))
            throw new InvalidDataException("A stale or divergent workout exercise cannot replace local data.");
    }

    private static async Task UpsertSetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalSet set,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO LocalSet
                (Id, OperationId, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                 Effort, CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion, PlateCount)
            VALUES
                ($id, $operationId, $exerciseId, $order, $weight, $assisted, $reps,
                 $effort, $completedAt, $updatedAt, $deletedAt, $version, $baseVersion, $plateCount)
            ON CONFLICT(Id) DO UPDATE SET
                OperationId = excluded.OperationId,
                WorkoutExerciseId = excluded.WorkoutExerciseId,
                SortOrder = excluded.SortOrder,
                WeightKg = excluded.WeightKg,
                AssistedKg = excluded.AssistedKg,
                PlateCount = excluded.PlateCount,
                Reps = excluded.Reps,
                Effort = excluded.Effort,
                CompletedAt = excluded.CompletedAt,
                UpdatedAt = excluded.UpdatedAt,
                DeletedAt = excluded.DeletedAt,
                Version = excluded.Version,
                BaseVersion = excluded.BaseVersion
            WHERE excluded.Version >= LocalSet.Version;
            """;
        Add(command, "$id", Id(set.Id));
        Add(command, "$operationId", Id(set.OperationId));
        Add(command, "$exerciseId", Id(set.WorkoutExerciseId));
        Add(command, "$order", set.Order);
        Add(command, "$weight", DecimalText(set.WeightKg));
        Add(command, "$assisted", DecimalText(set.AssistedKg));
        Add(command, "$plateCount", set.PlateCount);
        Add(command, "$reps", set.Reps);
        Add(command, "$effort", set.Effort is null ? null : (int)set.Effort.Value);
        Add(command, "$completedAt", Timestamp(set.CompletedAt));
        Add(command, "$updatedAt", Timestamp(set.UpdatedAt));
        Add(command, "$deletedAt", Timestamp(set.DeletedAt));
        Add(command, "$version", set.Version);
        Add(command, "$baseVersion", set.BaseVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0
            && !await SetMatchesAsync(connection, transaction, set, cancellationToken))
            throw new InvalidDataException("A stale or divergent set cannot replace local data.");
    }

    private static async Task<bool> WorkoutMatchesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalWorkout workout,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Status, StartedAt, CompletedAt, DeletedAt, Version, BaseVersion
            FROM LocalWorkout WHERE Id = $id;
            """;
        Add(command, "$id", Id(workout.Id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            && reader.GetInt32(0) == (int)workout.Status
            && reader.GetString(1) == Timestamp(workout.StartedAt)
            && NullableString(reader, 2) == Timestamp(workout.CompletedAt)
            && NullableString(reader, 3) == Timestamp(workout.DeletedAt)
            && reader.GetInt64(4) == workout.Version
            && reader.GetInt64(5) == workout.BaseVersion;
    }

    private static async Task<bool> ExerciseMatchesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalWorkoutExercise exercise,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT WorkoutId, ExerciseDefinitionId, TrackingMode, SortOrder, DeletedAt, Version, BaseVersion
            FROM LocalWorkoutExercise WHERE Id = $id;
            """;
        Add(command, "$id", Id(exercise.Id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            && reader.GetString(0) == Id(exercise.WorkoutId)
            && reader.GetString(1) == Id(exercise.ExerciseDefinitionId)
            && reader.GetInt32(2) == (int)exercise.TrackingMode
            && reader.GetInt32(3) == exercise.Order
            && NullableString(reader, 4) == Timestamp(exercise.DeletedAt)
            && reader.GetInt64(5) == exercise.Version
            && reader.GetInt64(6) == exercise.BaseVersion;
    }

    private static async Task<bool> SetMatchesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalSet set,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT OperationId, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                   Effort, CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion, PlateCount
            FROM LocalSet WHERE Id = $id;
            """;
        Add(command, "$id", Id(set.Id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            && reader.GetString(0) == Id(set.OperationId)
            && reader.GetString(1) == Id(set.WorkoutExerciseId)
            && reader.GetInt32(2) == set.Order
            && NullableString(reader, 3) == DecimalText(set.WeightKg)
            && NullableString(reader, 4) == DecimalText(set.AssistedKg)
            && reader.GetInt32(5) == set.Reps
            && (reader.IsDBNull(6)
                ? set.Effort is null
                : EnumValue<SetEffortRating>(reader, 6) == set.Effort)
            && reader.GetString(7) == Timestamp(set.CompletedAt)
            && NullableString(reader, 8) == Timestamp(set.UpdatedAt)
            && NullableString(reader, 9) == Timestamp(set.DeletedAt)
            && reader.GetInt64(10) == set.Version
            && reader.GetInt64(11) == set.BaseVersion
            && (reader.IsDBNull(12) ? set.PlateCount is null : reader.GetInt32(12) == set.PlateCount);
    }

    private static async Task ValidateCoverageAndStageOrdersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalWorkout workout,
        CancellationToken cancellationToken)
    {
        const int temporaryOrderOffset = 1_000_000_000;
        if (workout.Exercises.Count >= temporaryOrderOffset
            || workout.Exercises.SelectMany(item => item.Sets).Any(set => set.Order >= temporaryOrderOffset))
            throw new InvalidDataException("The workout graph is too large to stage orders safely.");

        var incomingExercises = workout.Exercises.ToDictionary(item => item.Id);
        var existingExerciseIds = new HashSet<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT Id, WorkoutId, ExerciseDefinitionId, TrackingMode, SortOrder,
                       DeletedAt, Version, BaseVersion
                FROM LocalWorkoutExercise WHERE WorkoutId = $workoutId;
                """;
            Add(command, "$workoutId", Id(workout.Id));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = GuidValue(reader, 0);
                existingExerciseIds.Add(id);
                if (!incomingExercises.TryGetValue(id, out var incoming))
                    throw new InvalidDataException("A full workout graph cannot omit a persisted exercise row.");
                if (GuidValue(reader, 1) != incoming.WorkoutId
                    || GuidValue(reader, 2) != incoming.ExerciseDefinitionId
                    || EnumValue<TrackingMode>(reader, 3) != incoming.TrackingMode)
                    throw new InvalidDataException("A persisted workout exercise identity cannot be changed.");
                var existingVersion = NonNegativeInt64(reader, 6);
                if (incoming.Version < existingVersion
                    || incoming.Version == existingVersion
                    && (incoming.Order != NonNegativeInt32(reader, 4)
                        || Timestamp(incoming.DeletedAt) != NullableString(reader, 5)
                        || incoming.BaseVersion != NonNegativeInt64(reader, 7)))
                    throw new InvalidDataException("A workout exercise change requires a newer local version.");
            }
        }

        var incomingSets = workout.Exercises.SelectMany(item => item.Sets).ToDictionary(item => item.Id);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT localSet.Id, localSet.OperationId, localSet.WorkoutExerciseId,
                       localSet.SortOrder, localSet.WeightKg, localSet.AssistedKg, localSet.Reps,
                       localSet.Effort, localSet.CompletedAt, localSet.UpdatedAt, localSet.DeletedAt,
                       localSet.Version, localSet.BaseVersion
                FROM LocalSet AS localSet
                INNER JOIN LocalWorkoutExercise AS exercise
                    ON exercise.Id = localSet.WorkoutExerciseId
                WHERE exercise.WorkoutId = $workoutId;
                """;
            Add(command, "$workoutId", Id(workout.Id));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = GuidValue(reader, 0);
                if (!incomingSets.TryGetValue(id, out var incoming))
                    throw new InvalidDataException("A full workout graph cannot omit a persisted set row.");
                if (GuidValue(reader, 1) != incoming.OperationId
                    || GuidValue(reader, 2) != incoming.WorkoutExerciseId)
                    throw new InvalidDataException("A persisted set identity cannot be changed.");
                var existingVersion = NonNegativeInt64(reader, 11);
                if (incoming.Version < existingVersion
                    || incoming.Version == existingVersion
                    && (incoming.Order != NonNegativeInt32(reader, 3)
                        || DecimalText(incoming.WeightKg) != NullableString(reader, 4)
                        || DecimalText(incoming.AssistedKg) != NullableString(reader, 5)
                        || incoming.Reps != PositiveInt32(reader, 6)
                        || (reader.IsDBNull(7)
                            ? incoming.Effort is not null
                            : EnumValue<SetEffortRating>(reader, 7) != incoming.Effort)
                        || Timestamp(incoming.CompletedAt) != reader.GetString(8)
                        || Timestamp(incoming.UpdatedAt) != NullableString(reader, 9)
                        || Timestamp(incoming.DeletedAt) != NullableString(reader, 10)
                        || incoming.BaseVersion != NonNegativeInt64(reader, 12)))
                    throw new InvalidDataException("A set change requires a newer local version.");
            }
        }

        if (existingExerciseIds.Count == 0) return;
        await using (var stageExercises = connection.CreateCommand())
        {
            stageExercises.Transaction = transaction;
            stageExercises.CommandText = """
                UPDATE LocalWorkoutExercise
                SET SortOrder = SortOrder + $offset
                WHERE WorkoutId = $workoutId AND DeletedAt IS NULL;
                """;
            Add(stageExercises, "$offset", temporaryOrderOffset);
            Add(stageExercises, "$workoutId", Id(workout.Id));
            await stageExercises.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var stageSets = connection.CreateCommand();
        stageSets.Transaction = transaction;
        stageSets.CommandText = """
            UPDATE LocalSet
            SET SortOrder = SortOrder + $offset
            WHERE DeletedAt IS NULL
              AND WorkoutExerciseId IN (
                  SELECT Id FROM LocalWorkoutExercise WHERE WorkoutId = $workoutId
              );
            """;
        Add(stageSets, "$offset", temporaryOrderOffset);
        Add(stageSets, "$workoutId", Id(workout.Id));
        await stageSets.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OutboxOperation operation,
        CancellationToken cancellationToken)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO OutboxOperation
                    (OperationId, EntityId, OperationType, Payload, BaseVersion,
                     CreatedAt, State, DeletedAt, Version, FailureCode)
                VALUES
                    ($id, $entityId, $type, $payload, $baseVersion,
                     $createdAt, $state, $deletedAt, $version, $failureCode)
                """;
            Add(insert, "$id", Id(operation.OperationId));
            Add(insert, "$entityId", Id(operation.EntityId));
            Add(insert, "$type", (int)operation.Type);
            Add(insert, "$payload", operation.Payload);
            Add(insert, "$baseVersion", operation.BaseVersion);
            Add(insert, "$createdAt", Timestamp(operation.CreatedAt));
            Add(insert, "$state", (int)operation.State);
            Add(insert, "$deletedAt", Timestamp(operation.DeletedAt));
            Add(insert, "$version", operation.Version);
            Add(insert, "$failureCode", operation.FailureCode is null
                ? null
                : (int)operation.FailureCode.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<OutboxOperation> ReadOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                   CreatedAt, State, DeletedAt, Version, ServerVersion, RetryCount,
                   NextAttemptAt, ServerPayload, ReplacesOperationId, SendStartedAt,
                   NeutralizedAt, FailureCode
            FROM OutboxOperation WHERE OperationId = $id;
            """;
        Add(command, "$id", Id(operationId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The history operation does not exist.");
        return ReadOperation(reader);
    }

    private static async Task RestoreSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalWorkout workout,
        CancellationToken cancellationToken)
    {
        await using (var stage = connection.CreateCommand())
        {
            stage.Transaction = transaction;
            stage.CommandText = """
                UPDATE LocalWorkoutExercise SET SortOrder = SortOrder + 1000000000
                WHERE WorkoutId = $workoutId AND DeletedAt IS NULL;
                UPDATE LocalSet SET SortOrder = SortOrder + 1000000000
                WHERE DeletedAt IS NULL AND WorkoutExerciseId IN
                    (SELECT Id FROM LocalWorkoutExercise WHERE WorkoutId = $workoutId);
                """;
            Add(stage, "$workoutId", Id(workout.Id));
            await stage.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var root = connection.CreateCommand())
        {
            root.Transaction = transaction;
            root.CommandText = """
                UPDATE LocalWorkout
                SET Status = $status, StartedAt = $startedAt, CompletedAt = $completedAt,
                    DeletedAt = $deletedAt, Version = $version, BaseVersion = $baseVersion
                WHERE Id = $id;
                """;
            Add(root, "$id", Id(workout.Id));
            Add(root, "$status", (int)workout.Status);
            Add(root, "$startedAt", Timestamp(workout.StartedAt));
            Add(root, "$completedAt", Timestamp(workout.CompletedAt));
            Add(root, "$deletedAt", Timestamp(workout.DeletedAt));
            Add(root, "$version", workout.Version);
            Add(root, "$baseVersion", workout.BaseVersion);
            if (await root.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("The undo workout is missing.");
        }
        foreach (var exercise in workout.Exercises)
        {
            await using (var updateExercise = connection.CreateCommand())
            {
                updateExercise.Transaction = transaction;
                updateExercise.CommandText = """
                    UPDATE LocalWorkoutExercise
                    SET SortOrder = $order, DeletedAt = $deletedAt, Version = $version,
                        BaseVersion = $baseVersion
                    WHERE Id = $id AND WorkoutId = $workoutId
                      AND ExerciseDefinitionId = $definitionId AND TrackingMode = $mode;
                    """;
                Add(updateExercise, "$id", Id(exercise.Id));
                Add(updateExercise, "$workoutId", Id(workout.Id));
                Add(updateExercise, "$definitionId", Id(exercise.ExerciseDefinitionId));
                Add(updateExercise, "$mode", (int)exercise.TrackingMode);
                Add(updateExercise, "$order", exercise.Order);
                Add(updateExercise, "$deletedAt", Timestamp(exercise.DeletedAt));
                Add(updateExercise, "$version", exercise.Version);
                Add(updateExercise, "$baseVersion", exercise.BaseVersion);
                if (await updateExercise.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("The undo exercise identity changed.");
            }
            foreach (var set in exercise.Sets)
            {
                await using var updateSet = connection.CreateCommand();
                updateSet.Transaction = transaction;
                updateSet.CommandText = """
                    UPDATE LocalSet
                    SET SortOrder = $order, WeightKg = $weight, AssistedKg = $assisted,
                        Reps = $reps, Effort = $effort, CompletedAt = $completedAt, UpdatedAt = $updatedAt,
                        DeletedAt = $deletedAt, Version = $version, BaseVersion = $baseVersion
                    WHERE Id = $id AND OperationId = $operationId
                      AND WorkoutExerciseId = $exerciseId;
                    """;
                Add(updateSet, "$id", Id(set.Id));
                Add(updateSet, "$operationId", Id(set.OperationId));
                Add(updateSet, "$exerciseId", Id(exercise.Id));
                Add(updateSet, "$order", set.Order);
                Add(updateSet, "$weight", DecimalText(set.WeightKg));
                Add(updateSet, "$assisted", DecimalText(set.AssistedKg));
                Add(updateSet, "$reps", set.Reps);
                Add(updateSet, "$effort", set.Effort is null ? null : (int)set.Effort.Value);
                Add(updateSet, "$completedAt", Timestamp(set.CompletedAt));
                Add(updateSet, "$updatedAt", Timestamp(set.UpdatedAt));
                Add(updateSet, "$deletedAt", Timestamp(set.DeletedAt));
                Add(updateSet, "$version", set.Version);
                Add(updateSet, "$baseVersion", set.BaseVersion);
                if (await updateSet.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("The undo set identity changed.");
            }
        }
    }

    private static async Task<bool> ExistingOperationMatchesOrThrowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OutboxOperation operation,
        CancellationToken cancellationToken)
    {
        await using var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = """
            SELECT EntityId, OperationType, Payload, BaseVersion, CreatedAt
            FROM OutboxOperation
            WHERE OperationId = $id;
            """;
        Add(existing, "$id", Id(operation.OperationId));
        await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return false;
        if (reader.GetString(0) != Id(operation.EntityId)
            || reader.GetInt32(1) != (int)operation.Type
            || reader.GetString(2) != operation.Payload
            || reader.GetInt64(3) != operation.BaseVersion
            || reader.GetString(4) != Timestamp(operation.CreatedAt))
        {
            throw new InvalidDataException(
                "The operation identifier is already bound to a different outbox contract.");
        }
        return true;
    }

    private static async Task EnsureMutationCanFollowReplacementAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OutboxOperation operation,
        CancellationToken cancellationToken)
    {
        await using (var conflict = connection.CreateCommand())
        {
            conflict.Transaction = transaction;
            conflict.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM OutboxOperation
                    WHERE State = 4 AND DeletedAt IS NULL AND EntityId = $entityId
                );
                """;
            Add(conflict, "$entityId", Id(operation.EntityId));
            if (Convert.ToInt64(await conflict.ExecuteScalarAsync(cancellationToken)) != 0)
                throw new InvalidOperationException(
                    "Resolve the sync conflict before recording another workout change.");
        }

        DateTimeOffset? latestReplacementAt = null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT replacement.CreatedAt, replacement.State, replacement.DeletedAt,
                   replacement.ServerVersion, workout.BaseVersion
            FROM OutboxOperation AS replacement
            INNER JOIN LocalWorkout AS workout ON workout.Id = replacement.EntityId
            WHERE replacement.ReplacesOperationId IS NOT NULL
              AND replacement.EntityId = $entityId;
            """;
        Add(command, "$entityId", Id(operation.EntityId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var replacementAt = ParseTimestamp(reader.GetString(0));
            if (latestReplacementAt is null || replacementAt > latestReplacementAt)
                latestReplacementAt = replacementAt;
            var state = EnumValue<OutboxOperationState>(reader, 1);
            var isLive = reader.IsDBNull(2)
                && state is OutboxOperationState.Pending or OutboxOperationState.Conflicted;
            var isAppliedBeforeAuthority = state == OutboxOperationState.Applied
                && !reader.IsDBNull(3)
                && reader.GetInt64(4) < reader.GetInt64(3);
            if (isLive || isAppliedBeforeAuthority)
                throw new InvalidOperationException(
                    "Synchronize the conflict replacement before recording another workout change.");
        }

        if (latestReplacementAt is { } causalBarrier
            && operation.CreatedAt <= causalBarrier)
            throw new InvalidOperationException(
                "A workout change must follow the conflict replacement causally.");
    }

    private static void ValidateGraph(LocalWorkout workout)
    {
        if (workout.Id == Guid.Empty) throw new ArgumentException("Workout ID cannot be empty.", nameof(workout));
        if (!Enum.IsDefined(workout.Status)) throw new ArgumentOutOfRangeException(nameof(workout));
        ValidateVersion(workout.Version, workout.BaseVersion, nameof(workout));
        RequireTimestamp(workout.StartedAt, nameof(workout.StartedAt));
        OptionalTimestamp(workout.CompletedAt, nameof(workout.CompletedAt));
        OptionalTimestamp(workout.DeletedAt, nameof(workout.DeletedAt));
        if ((workout.Status == LocalWorkoutStatus.Completed) != (workout.CompletedAt is not null))
            throw new ArgumentException("Workout completion state is inconsistent.", nameof(workout));
        if (workout.Exercises is null) throw new ArgumentException("Workout exercises are required.", nameof(workout));
        if (workout.Exercises.Select(item => item.Id).Distinct().Count() != workout.Exercises.Count)
            throw new ArgumentException("Workout exercise IDs must be unique.", nameof(workout));
        ValidateContiguous(workout.Exercises.Where(item => item.DeletedAt is null).Select(item => item.Order), "exercise");

        foreach (var exercise in workout.Exercises)
        {
            if (exercise.Id == Guid.Empty || exercise.WorkoutId != workout.Id
                || exercise.ExerciseDefinitionId == Guid.Empty)
                throw new ArgumentException("Workout exercise identity is invalid.", nameof(workout));
            if (!Enum.IsDefined(exercise.TrackingMode)) throw new ArgumentOutOfRangeException(nameof(workout));
            ValidateVersion(exercise.Version, exercise.BaseVersion, nameof(workout));
            OptionalTimestamp(exercise.DeletedAt, nameof(exercise.DeletedAt));
            if (exercise.Sets is null) throw new ArgumentException("Exercise sets are required.", nameof(workout));
            if (exercise.Sets.Select(item => item.Id).Distinct().Count() != exercise.Sets.Count)
                throw new ArgumentException("Set IDs must be unique.", nameof(workout));
            ValidateContiguous(exercise.Sets.Where(item => item.DeletedAt is null).Select(item => item.Order), "set");

            foreach (var set in exercise.Sets)
            {
                if (set.Id == Guid.Empty || set.OperationId == Guid.Empty
                    || set.WorkoutExerciseId != exercise.Id)
                    throw new ArgumentException("Set identity is invalid.", nameof(workout));
                ValidateVersion(set.Version, set.BaseVersion, nameof(workout));
                RequireTimestamp(set.CompletedAt, nameof(set.CompletedAt));
                OptionalTimestamp(set.UpdatedAt, nameof(set.UpdatedAt));
                OptionalTimestamp(set.DeletedAt, nameof(set.DeletedAt));
                if (set.Effort is { } effort && !Enum.IsDefined(effort))
                    throw new ArgumentOutOfRangeException(nameof(workout));
                if (set.Reps is < 1 or > 999 || !ValidMeasurement(exercise.TrackingMode, set))
                    throw new ArgumentException("Set measurement is invalid for its tracking mode.", nameof(workout));
            }
        }
    }

    private static bool ValidMeasurement(TrackingMode mode, LocalSet set) => mode switch
    {
        TrackingMode.Weighted => set.AssistedKg is null
            && ((ValidKilograms(set.WeightKg) && set.PlateCount is null)
                || (set.WeightKg is null && ValidPlateCount(set.PlateCount))),
        TrackingMode.Bodyweight => set.WeightKg is null && set.AssistedKg is null && set.PlateCount is null,
        TrackingMode.Assisted => set.WeightKg is null
            && ((ValidKilograms(set.AssistedKg) && set.PlateCount is null)
                || (set.AssistedKg is null && ValidPlateCount(set.PlateCount))),
        _ => false
    };

    private static bool ValidKilograms(decimal? value) => value is { } kilograms
        && kilograms is >= 0.001m and <= 99999.999m
        && ((decimal.GetBits(kilograms)[3] >> 16) & 0xff) <= 3;

    private static bool ValidPlateCount(int? value) => value is >= 1 and <= 999;

    private static void ValidateOperation(LocalWorkout workout, OutboxOperation operation)
    {
        if (operation.OperationId == Guid.Empty || operation.EntityId != workout.Id)
            throw new ArgumentException("Outbox operation identity is invalid.", nameof(operation));
        if (!Enum.IsDefined(operation.Type) || !Enum.IsDefined(operation.State)
            || operation.BaseVersion < 0 || operation.Version < 1
            || string.IsNullOrWhiteSpace(operation.Payload))
            throw new ArgumentException("Outbox operation contract is invalid.", nameof(operation));
        RequireTimestamp(operation.CreatedAt, nameof(operation.CreatedAt));
        OptionalTimestamp(operation.DeletedAt, nameof(operation.DeletedAt));
        OptionalTimestamp(operation.SendStartedAt, nameof(operation.SendStartedAt));
        OptionalTimestamp(operation.NeutralizedAt, nameof(operation.NeutralizedAt));
    }

    private static void ValidateVersion(long version, long baseVersion, string parameter)
    {
        if (version < 0 || baseVersion < 0 || baseVersion > version)
            throw new ArgumentException("Local and base versions are inconsistent.", parameter);
    }

    private static void ValidateContiguous(IEnumerable<int> values, string label)
    {
        var orders = values.Order().ToArray();
        if (!orders.SequenceEqual(Enumerable.Range(0, orders.Length)))
            throw new ArgumentException($"Active {label} order must be unique and contiguous.");
    }

    private static void RequireTimestamp(DateTimeOffset timestamp, string parameter)
    {
        if (timestamp == default || timestamp.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameter);
    }

    private static void OptionalTimestamp(DateTimeOffset? timestamp, string parameter)
    {
        if (timestamp is { } value) RequireTimestamp(value, parameter);
    }

    private static Guid GuidValue(SqliteDataReader reader, int ordinal)
    {
        if (!Guid.TryParseExact(reader.GetString(ordinal), "D", out var value) || value == Guid.Empty)
            throw new InvalidDataException("A stored identifier is invalid.");
        return value;
    }

    private static OutboxOperation ReadOperation(SqliteDataReader reader) => new(
        GuidValue(reader, 0),
        GuidValue(reader, 1),
        EnumValue<OutboxOperationType>(reader, 2),
        reader.GetString(3),
        NonNegativeInt64(reader, 4),
        Timestamp(reader, 5)!.Value,
        EnumValue<OutboxOperationState>(reader, 6),
        Timestamp(reader, 7),
        NonNegativeInt64(reader, 8),
        reader.IsDBNull(9) ? null : NonNegativeInt64(reader, 9),
        NonNegativeInt32(reader, 10),
        Timestamp(reader, 11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        reader.IsDBNull(13) ? null : GuidValue(reader, 13),
        Timestamp(reader, 14),
        Timestamp(reader, 15),
        reader.IsDBNull(16) ? null : EnumValue<TrackZ.Contracts.Errors.BusinessErrorCode>(reader, 16));

    private static T EnumValue<T>(SqliteDataReader reader, int ordinal) where T : struct, Enum
    {
        var value = reader.GetInt32(ordinal);
        if (!Enum.IsDefined(typeof(T), value)) throw new InvalidDataException("A stored enum value is invalid.");
        return (T)Enum.ToObject(typeof(T), value);
    }

    private static int NonNegativeInt32(SqliteDataReader reader, int ordinal)
    {
        var value = reader.GetInt32(ordinal);
        if (value < 0) throw new InvalidDataException("A stored integer is negative.");
        return value;
    }

    private static int PositiveInt32(SqliteDataReader reader, int ordinal)
    {
        var value = reader.GetInt32(ordinal);
        if (value <= 0) throw new InvalidDataException("A stored integer is not positive.");
        return value;
    }

    private static long NonNegativeInt64(SqliteDataReader reader, int ordinal)
    {
        var value = reader.GetInt64(ordinal);
        if (value < 0) throw new InvalidDataException("A stored integer is negative.");
        return value;
    }

    private static decimal? DecimalValue(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        if (!decimal.TryParse(
                reader.GetString(ordinal),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var value))
            throw new InvalidDataException("A stored decimal is invalid.");
        return value;
    }

    private static DateTimeOffset? Timestamp(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseTimestamp(reader.GetString(ordinal));

    private static DateTimeOffset ParseTimestamp(string text)
    {
        if (!DateTimeOffset.TryParseExact(
                text,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var value)
            || value.Offset != TimeSpan.Zero)
            throw new InvalidDataException("A stored timestamp is not canonical UTC.");
        return value;
    }

    private static string Id(Guid value) => value.ToString("D");
    private static string? DecimalText(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture);
    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string? Timestamp(DateTimeOffset? value) =>
        value is null ? null : Timestamp(value.Value);
    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static HistoryUndoWorkout ToUndoSnapshot(LocalWorkout workout) => new(
        workout.Id,
        workout.Status,
        workout.StartedAt,
        workout.CompletedAt,
        workout.DeletedAt,
        workout.Version,
        workout.BaseVersion,
        workout.Exercises.Select(exercise => new HistoryUndoExercise(
            exercise.Id,
            exercise.WorkoutId,
            exercise.ExerciseDefinitionId,
            exercise.TrackingMode,
            exercise.Order,
            exercise.DeletedAt,
            exercise.Version,
            exercise.BaseVersion,
            exercise.Sets.Select(set => new HistoryUndoSet(
                set.Id,
                set.WorkoutExerciseId,
                set.Order,
                set.WeightKg,
                set.AssistedKg,
                set.Reps,
                set.CompletedAt,
                set.UpdatedAt,
                set.DeletedAt,
                set.Version,
                set.BaseVersion,
                set.OperationId,
                set.Effort,
                set.PlateCount)).ToArray())).ToArray());

    private static LocalWorkout FromUndoSnapshot(HistoryUndoWorkout workout) => new(
        workout.Id,
        workout.Status,
        workout.StartedAt,
        workout.CompletedAt,
        workout.DeletedAt,
        workout.Version,
        workout.BaseVersion,
        workout.Exercises.Select(exercise => new LocalWorkoutExercise(
            exercise.Id,
            exercise.WorkoutId,
            exercise.ExerciseDefinitionId,
            exercise.TrackingMode,
            exercise.Order,
            exercise.DeletedAt,
            exercise.Version,
            exercise.BaseVersion,
            exercise.Sets.Select(set => new LocalSet(
                set.Id,
                set.WorkoutExerciseId,
                set.Order,
                set.WeightKg,
                set.AssistedKg,
                set.Reps,
                set.CompletedAt,
                set.UpdatedAt,
                set.DeletedAt,
                set.Version,
                set.BaseVersion,
                set.OperationId,
                set.Effort,
                set.PlateCount)).ToArray())).ToArray());

    private sealed record HistoryUndoWorkout(
        Guid Id,
        LocalWorkoutStatus Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? DeletedAt,
        long Version,
        long BaseVersion,
        IReadOnlyList<HistoryUndoExercise> Exercises);

    private sealed record HistoryUndoExercise(
        Guid Id,
        Guid WorkoutId,
        Guid ExerciseDefinitionId,
        TrackingMode TrackingMode,
        int Order,
        DateTimeOffset? DeletedAt,
        long Version,
        long BaseVersion,
        IReadOnlyList<HistoryUndoSet> Sets);

    private sealed record HistoryUndoSet(
        Guid Id,
        Guid WorkoutExerciseId,
        int Order,
        decimal? WeightKg,
        decimal? AssistedKg,
        int Reps,
        DateTimeOffset CompletedAt,
        DateTimeOffset? UpdatedAt,
        DateTimeOffset? DeletedAt,
        long Version,
        long BaseVersion,
        Guid OperationId,
        SetEffortRating? Effort = null,
        int? PlateCount = null);
}
