using System.Globalization;
using Microsoft.Data.Sqlite;
using TrackZ.Domain.Exercises;
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

    Task ClearPrivateDataAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalWorkoutRepository(TrackZLocalDatabase database) : ILocalWorkoutRepository
{
    public async Task SaveWorkoutAndEnqueueAsync(
        LocalWorkout workout,
        OutboxOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workout);
        ArgumentNullException.ThrowIfNull(operation);
        ValidateGraph(workout);
        ValidateOperation(workout, operation);

        _ = await database.WriteAsync(async (connection, transaction, token) =>
        {
            await UpsertWorkoutAsync(connection, transaction, workout, token);
            foreach (var exercise in workout.Exercises)
            {
                await UpsertExerciseAsync(connection, transaction, exercise, token);
                foreach (var set in exercise.Sets)
                {
                    await UpsertSetAsync(connection, transaction, set, token);
                }
            }
            await InsertOrVerifyOperationAsync(connection, transaction, operation, token);
            return true;
        }, cancellationToken);
    }

    public Task<LocalWorkout?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        database.ReadAsync(ReadActiveAsync, cancellationToken);

    public Task ClearPrivateDataAsync(CancellationToken cancellationToken = default) =>
        database.ClearPrivateDataAsync(cancellationToken);

    private static async Task<LocalWorkout?> ReadActiveAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            LocalWorkout? workout = null;
            await using (var command = connection.CreateCommand())
            {
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
            if (workout is null) return null;

            var exercises = new List<LocalWorkoutExercise>();
            await using (var command = connection.CreateCommand())
            {
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
                command.CommandText = """
                    SELECT Id, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                           CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion
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
                        GuidValue(reader, 1),
                        NonNegativeInt32(reader, 2),
                        DecimalValue(reader, 3),
                        DecimalValue(reader, 4),
                        PositiveInt32(reader, 5),
                        Timestamp(reader, 6)!.Value,
                        Timestamp(reader, 7),
                        Timestamp(reader, 8),
                        NonNegativeInt64(reader, 9),
                        NonNegativeInt64(reader, 10),
                        Guid.Empty));
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
            WHERE excluded.Version > LocalWorkoutExercise.Version;
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
                (Id, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                 CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion)
            VALUES
                ($id, $exerciseId, $order, $weight, $assisted, $reps,
                 $completedAt, $updatedAt, $deletedAt, $version, $baseVersion)
            ON CONFLICT(Id) DO UPDATE SET
                WorkoutExerciseId = excluded.WorkoutExerciseId,
                SortOrder = excluded.SortOrder,
                WeightKg = excluded.WeightKg,
                AssistedKg = excluded.AssistedKg,
                Reps = excluded.Reps,
                CompletedAt = excluded.CompletedAt,
                UpdatedAt = excluded.UpdatedAt,
                DeletedAt = excluded.DeletedAt,
                Version = excluded.Version,
                BaseVersion = excluded.BaseVersion
            WHERE excluded.Version > LocalSet.Version;
            """;
        Add(command, "$id", Id(set.Id));
        Add(command, "$exerciseId", Id(set.WorkoutExerciseId));
        Add(command, "$order", set.Order);
        Add(command, "$weight", DecimalText(set.WeightKg));
        Add(command, "$assisted", DecimalText(set.AssistedKg));
        Add(command, "$reps", set.Reps);
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
            SELECT WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                   CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion
            FROM LocalSet WHERE Id = $id;
            """;
        Add(command, "$id", Id(set.Id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            && reader.GetString(0) == Id(set.WorkoutExerciseId)
            && reader.GetInt32(1) == set.Order
            && NullableString(reader, 2) == DecimalText(set.WeightKg)
            && NullableString(reader, 3) == DecimalText(set.AssistedKg)
            && reader.GetInt32(4) == set.Reps
            && reader.GetString(5) == Timestamp(set.CompletedAt)
            && NullableString(reader, 6) == Timestamp(set.UpdatedAt)
            && NullableString(reader, 7) == Timestamp(set.DeletedAt)
            && reader.GetInt64(8) == set.Version
            && reader.GetInt64(9) == set.BaseVersion;
    }

    private static async Task InsertOrVerifyOperationAsync(
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
                     CreatedAt, State, DeletedAt, Version)
                VALUES
                    ($id, $entityId, $type, $payload, $baseVersion,
                     $createdAt, $state, $deletedAt, $version)
                ON CONFLICT(OperationId) DO NOTHING;
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
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1) return;
        }

        await using var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = """
            SELECT EntityId, OperationType, Payload, BaseVersion, CreatedAt, State, DeletedAt, Version
            FROM OutboxOperation
            WHERE OperationId = $id;
            """;
        Add(existing, "$id", Id(operation.OperationId));
        await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || reader.GetString(0) != Id(operation.EntityId)
            || reader.GetInt32(1) != (int)operation.Type
            || reader.GetString(2) != operation.Payload
            || reader.GetInt64(3) != operation.BaseVersion
            || reader.GetString(4) != Timestamp(operation.CreatedAt)
            || reader.GetInt32(5) != (int)operation.State
            || NullableString(reader, 6) != Timestamp(operation.DeletedAt)
            || reader.GetInt64(7) != operation.Version)
        {
            throw new InvalidDataException(
                "The operation identifier is already bound to a different outbox contract.");
        }
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
                if (set.Id == Guid.Empty || set.WorkoutExerciseId != exercise.Id)
                    throw new ArgumentException("Set identity is invalid.", nameof(workout));
                ValidateVersion(set.Version, set.BaseVersion, nameof(workout));
                RequireTimestamp(set.CompletedAt, nameof(set.CompletedAt));
                OptionalTimestamp(set.UpdatedAt, nameof(set.UpdatedAt));
                OptionalTimestamp(set.DeletedAt, nameof(set.DeletedAt));
                if (set.Reps is < 1 or > 999 || !ValidMeasurement(exercise.TrackingMode, set))
                    throw new ArgumentException("Set measurement is invalid for its tracking mode.", nameof(workout));
            }
        }
    }

    private static bool ValidMeasurement(TrackingMode mode, LocalSet set) => mode switch
    {
        TrackingMode.Weighted => ValidKilograms(set.WeightKg) && set.AssistedKg is null,
        TrackingMode.Bodyweight => set.WeightKg is null && set.AssistedKg is null,
        TrackingMode.Assisted => set.WeightKg is null && ValidKilograms(set.AssistedKg),
        _ => false
    };

    private static bool ValidKilograms(decimal? value) => value is { } kilograms
        && kilograms is >= 0.001m and <= 99999.999m
        && ((decimal.GetBits(kilograms)[3] >> 16) & 0xff) <= 3;

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
}
