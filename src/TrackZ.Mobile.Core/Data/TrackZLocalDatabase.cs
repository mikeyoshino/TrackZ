using Microsoft.Data.Sqlite;

namespace TrackZ.Mobile.Data;

public sealed class TrackZLocalDatabase
{
    public const int CurrentSchemaVersion = 2;
    private const int BusyTimeoutMilliseconds = 5_000;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private volatile bool _initialized;

    public TrackZLocalDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var connection = await OpenConfiguredAsync(cancellationToken);
            await using (var journal = connection.CreateCommand())
            {
                journal.CommandText = "PRAGMA journal_mode = WAL;";
                _ = await journal.ExecuteScalarAsync(cancellationToken);
            }

            var version = await ReadSchemaVersionAsync(connection, cancellationToken);
            if (version == CurrentSchemaVersion)
            {
                _initialized = true;
                return;
            }
            if (version is not (0 or 1))
            {
                throw new InvalidDataException(
                    $"Workout database schema {version} is not supported; expected {CurrentSchemaVersion}.");
            }

            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var schema = connection.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = version == 1
                ? """
                    DROP INDEX IF EXISTS UX_LocalSet_ActiveOrder;
                    ALTER TABLE LocalSet RENAME TO LocalSetV1;

                    CREATE TABLE LocalSet (
                        Id TEXT PRIMARY KEY NOT NULL,
                        OperationId TEXT NOT NULL UNIQUE,
                        WorkoutExerciseId TEXT NOT NULL,
                        SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
                        WeightKg TEXT NULL CHECK (WeightKg IS NULL OR typeof(WeightKg) = 'text'),
                        AssistedKg TEXT NULL CHECK (AssistedKg IS NULL OR typeof(AssistedKg) = 'text'),
                        Reps INTEGER NOT NULL CHECK (Reps BETWEEN 1 AND 999),
                        CompletedAt TEXT NOT NULL,
                        UpdatedAt TEXT NULL,
                        DeletedAt TEXT NULL,
                        Version INTEGER NOT NULL CHECK (Version >= 0),
                        BaseVersion INTEGER NOT NULL CHECK (BaseVersion >= 0 AND BaseVersion <= Version),
                        FOREIGN KEY (WorkoutExerciseId) REFERENCES LocalWorkoutExercise(Id) ON DELETE CASCADE,
                        CHECK (NOT (WeightKg IS NOT NULL AND AssistedKg IS NOT NULL))
                    );

                    INSERT INTO LocalSet
                        (Id, OperationId, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                         CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion)
                    SELECT legacy.Id,
                           COALESCE(
                               (SELECT operation.OperationId
                                FROM OutboxOperation AS operation
                                WHERE operation.OperationType = 2
                                  AND CASE
                                          WHEN json_valid(operation.Payload)
                                          THEN json_extract(operation.Payload, '$.setId')
                                      END = legacy.Id
                                ORDER BY operation.CreatedAt, operation.OperationId
                                LIMIT 1),
                               legacy.Id),
                           legacy.WorkoutExerciseId, legacy.SortOrder, legacy.WeightKg,
                           legacy.AssistedKg, legacy.Reps, legacy.CompletedAt, legacy.UpdatedAt,
                           legacy.DeletedAt, legacy.Version, legacy.BaseVersion
                    FROM LocalSetV1 AS legacy;

                    DROP TABLE LocalSetV1;
                    CREATE UNIQUE INDEX UX_LocalSet_ActiveOrder
                        ON LocalSet(WorkoutExerciseId, SortOrder)
                        WHERE DeletedAt IS NULL;
                    PRAGMA user_version = 2;
                    """
                : """
                CREATE TABLE IF NOT EXISTS LocalWorkout (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Status INTEGER NOT NULL CHECK (Status IN (1, 2, 3)),
                    StartedAt TEXT NOT NULL,
                    CompletedAt TEXT NULL,
                    DeletedAt TEXT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 0),
                    BaseVersion INTEGER NOT NULL CHECK (BaseVersion >= 0 AND BaseVersion <= Version),
                    CHECK ((Status = 3 AND CompletedAt IS NOT NULL) OR (Status IN (1, 2) AND CompletedAt IS NULL))
                );

                CREATE UNIQUE INDEX IF NOT EXISTS UX_LocalWorkout_OneActive
                    ON LocalWorkout(Status)
                    WHERE Status = 2 AND DeletedAt IS NULL;

                CREATE TABLE IF NOT EXISTS LocalWorkoutExercise (
                    Id TEXT PRIMARY KEY NOT NULL,
                    WorkoutId TEXT NOT NULL,
                    ExerciseDefinitionId TEXT NOT NULL,
                    TrackingMode INTEGER NOT NULL CHECK (TrackingMode IN (1, 2, 3)),
                    SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
                    DeletedAt TEXT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 0),
                    BaseVersion INTEGER NOT NULL CHECK (BaseVersion >= 0 AND BaseVersion <= Version),
                    FOREIGN KEY (WorkoutId) REFERENCES LocalWorkout(Id) ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS UX_LocalWorkoutExercise_ActiveOrder
                    ON LocalWorkoutExercise(WorkoutId, SortOrder)
                    WHERE DeletedAt IS NULL;
                CREATE UNIQUE INDEX IF NOT EXISTS UX_LocalWorkoutExercise_ActiveDefinition
                    ON LocalWorkoutExercise(WorkoutId, ExerciseDefinitionId)
                    WHERE DeletedAt IS NULL;

                CREATE TABLE IF NOT EXISTS LocalSet (
                    Id TEXT PRIMARY KEY NOT NULL,
                    OperationId TEXT NOT NULL UNIQUE,
                    WorkoutExerciseId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
                    WeightKg TEXT NULL CHECK (WeightKg IS NULL OR typeof(WeightKg) = 'text'),
                    AssistedKg TEXT NULL CHECK (AssistedKg IS NULL OR typeof(AssistedKg) = 'text'),
                    Reps INTEGER NOT NULL CHECK (Reps BETWEEN 1 AND 999),
                    CompletedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL,
                    DeletedAt TEXT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 0),
                    BaseVersion INTEGER NOT NULL CHECK (BaseVersion >= 0 AND BaseVersion <= Version),
                    FOREIGN KEY (WorkoutExerciseId) REFERENCES LocalWorkoutExercise(Id) ON DELETE CASCADE,
                    CHECK (NOT (WeightKg IS NOT NULL AND AssistedKg IS NOT NULL))
                );

                CREATE UNIQUE INDEX IF NOT EXISTS UX_LocalSet_ActiveOrder
                    ON LocalSet(WorkoutExerciseId, SortOrder)
                    WHERE DeletedAt IS NULL;

                CREATE TABLE IF NOT EXISTS OutboxOperation (
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

                CREATE INDEX IF NOT EXISTS IX_OutboxOperation_Pending
                    ON OutboxOperation(State, CreatedAt, OperationId)
                    WHERE State = 1 AND DeletedAt IS NULL;

                CREATE TABLE IF NOT EXISTS SyncCursor (
                    Scope TEXT PRIMARY KEY NOT NULL,
                    Cursor TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 0)
                );

                PRAGMA user_version = 2;
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    public async Task ClearPrivateDataAsync(CancellationToken cancellationToken = default)
    {
        await WriteAsync(async (connection, transaction, token) =>
        {
            foreach (var table in new[]
                     {
                         "OutboxOperation", "LocalSet", "LocalWorkoutExercise", "LocalWorkout", "SyncCursor"
                     })
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"DELETE FROM {table};";
                await command.ExecuteNonQueryAsync(token);
            }
            return true;
        }, cancellationToken);
    }

    internal async Task<TResult> ReadAsync<TResult>(
        Func<SqliteConnection, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConfiguredAsync(cancellationToken);
        return await read(connection, cancellationToken);
    }

    internal async Task<TResult> ReadTransactionAsync<TResult>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConfiguredAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var result = await read(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    internal async Task<TResult> WriteAsync<TResult>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<TResult>> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        await InitializeAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConfiguredAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction(deferred: false);
            var result = await write(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConfiguredAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var pragmas = connection.CreateCommand();
            pragmas.CommandText = $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {BusyTimeoutMilliseconds};";
            await pragmas.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
