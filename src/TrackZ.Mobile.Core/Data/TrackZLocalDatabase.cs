using Microsoft.Data.Sqlite;

namespace TrackZ.Mobile.Data;

public sealed class TrackZLocalDatabase
{
    public const int CurrentSchemaVersion = 10;
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
            if (version is not (0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9))
            {
                throw new InvalidDataException(
                    $"Workout database schema {version} is not supported; expected {CurrentSchemaVersion}.");
            }

            var syncStateUpgrade = version is 1 or 2 or 3 or 4
                ? await BuildSyncStateUpgradeAsync(
                    connection,
                    addEffortColumn: version is 2 or 3 or 4,
                    cancellationToken)
                : string.Empty;
            var guidanceUpgrade = version == 5
                ? BuildGuidanceUpgrade()
                : string.Empty;
            // Some legacy databases already have this column (for example after a
            // partial older upgrade). Do not let a duplicate ALTER stop the batch.
            var needsPlateCount = version is 0 or 1 || !await HasPlateCountAsync(connection, cancellationToken);
            var coachingUpgrade = await BuildSetCoachingUpgradeAsync(connection, cancellationToken);
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var schema = connection.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = (version is 2 or 3 or 4
                ? syncStateUpgrade
                : version == 5
                ? guidanceUpgrade
                : version is 6 or 7
                ? string.Empty
                : version == 1
                ? $$"""
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
                        Effort INTEGER NULL CHECK (Effort IS NULL OR Effort IN (1, 2, 3)),
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
                         Effort, CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion)
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
                           legacy.AssistedKg, legacy.Reps, NULL, legacy.CompletedAt, legacy.UpdatedAt,
                           legacy.DeletedAt, legacy.Version, legacy.BaseVersion
                    FROM LocalSetV1 AS legacy;

                    DROP TABLE LocalSetV1;
                    CREATE UNIQUE INDEX UX_LocalSet_ActiveOrder
                        ON LocalSet(WorkoutExerciseId, SortOrder)
                        WHERE DeletedAt IS NULL;
                    {{syncStateUpgrade}}
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
                    Effort INTEGER NULL CHECK (Effort IS NULL OR Effort IN (1, 2, 3)),
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
                    OperationType INTEGER NOT NULL CHECK (OperationType BETWEEN 1 AND 11),
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
                    FailureCode INTEGER NULL,
                    FOREIGN KEY (EntityId) REFERENCES LocalWorkout(Id) ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS IX_OutboxOperation_Pending
                    ON OutboxOperation(State, CreatedAt, OperationId)
                    WHERE State = 1 AND DeletedAt IS NULL;

                CREATE TABLE IF NOT EXISTS HistoryUndo (
                    OperationId TEXT PRIMARY KEY NOT NULL,
                    SnapshotJson TEXT NOT NULL CHECK (json_valid(SnapshotJson)),
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (OperationId) REFERENCES OutboxOperation(OperationId) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS SyncCursor (
                    Scope TEXT PRIMARY KEY NOT NULL,
                    Cursor TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 0)
                );

                PRAGMA user_version = 7;
                """) + (needsPlateCount ? BuildPlateCountUpgrade() : string.Empty);
            await schema.ExecuteNonQueryAsync(cancellationToken);
            schema.CommandText = coachingUpgrade + """
                CREATE TABLE IF NOT EXISTS CoachJournal (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Payload TEXT NOT NULL CHECK (json_valid(Payload))
                );
                CREATE TABLE IF NOT EXISTS TrainingSchedule (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Payload TEXT NOT NULL CHECK (json_valid(Payload))
                );
                PRAGMA user_version = 10;
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
                         "TrainingSchedule", "CoachJournal", "HistoryUndo", "OutboxOperation", "LocalSet", "LocalWorkoutExercise", "LocalWorkout", "SyncCursor"
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

    private static async Task<bool> HasPlateCountAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('LocalSet') WHERE name = 'PlateCount'";
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) > 0;
    }

    private static async Task<string> BuildSetCoachingUpgradeAsync(
        SqliteConnection connection, CancellationToken token)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(LocalSet);";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) columns.Add(reader.GetString(1));
        var statements = new List<string>();
        if (!columns.Contains("EffortScore")) statements.Add(
            "ALTER TABLE LocalSet ADD COLUMN EffortScore INTEGER NULL CHECK (EffortScore IS NULL OR EffortScore BETWEEN 0 AND 100);");
        if (!columns.Contains("IsWarmup")) statements.Add(
            "ALTER TABLE LocalSet ADD COLUMN IsWarmup INTEGER NULL CHECK (IsWarmup IS NULL OR IsWarmup IN (0, 1));");
        if (!columns.Contains("HasPain")) statements.Add(
            "ALTER TABLE LocalSet ADD COLUMN HasPain INTEGER NULL CHECK (HasPain IS NULL OR HasPain IN (0, 1));");
        return string.Join(Environment.NewLine, statements) + Environment.NewLine;
    }

    private static async Task<string> BuildSyncStateUpgradeAsync(
        SqliteConnection connection,
        bool addEffortColumn,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(OutboxOperation);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(1));
        }

        var statements = new List<string>();
        if (addEffortColumn)
        {
            statements.Add("""
                ALTER TABLE LocalSet
                    ADD COLUMN Effort INTEGER NULL
                    CHECK (Effort IS NULL OR Effort IN (1, 2, 3));
                """);
        }
        Add("ServerVersion", "INTEGER NULL CHECK (ServerVersion IS NULL OR ServerVersion >= 0)");
        Add("RetryCount", "INTEGER NOT NULL DEFAULT 0 CHECK (RetryCount >= 0)");
        Add("NextAttemptAt", "TEXT NULL");
        Add("ServerPayload", "TEXT NULL CHECK (ServerPayload IS NULL OR json_valid(ServerPayload))");
        Add("ReplacesOperationId", "TEXT NULL");
        Add("SendStartedAt", "TEXT NULL");
        Add("NeutralizedAt", "TEXT NULL");
        Add("FailureCode", "INTEGER NULL");
        statements.Add("""
            CREATE TABLE IF NOT EXISTS HistoryUndo (
                OperationId TEXT PRIMARY KEY NOT NULL,
                SnapshotJson TEXT NOT NULL CHECK (json_valid(SnapshotJson)),
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (OperationId) REFERENCES OutboxOperation(OperationId) ON DELETE CASCADE
            );
            """);
        statements.Add("DROP INDEX IF EXISTS IX_OutboxOperation_Pending;");
        statements.Add("ALTER TABLE HistoryUndo RENAME TO HistoryUndoV4;");
        statements.Add("ALTER TABLE OutboxOperation RENAME TO OutboxOperationV4;");
        statements.Add("""
            CREATE TABLE OutboxOperation (
                OperationId TEXT PRIMARY KEY NOT NULL,
                EntityId TEXT NOT NULL,
                OperationType INTEGER NOT NULL CHECK (OperationType BETWEEN 1 AND 11),
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
                FailureCode INTEGER NULL,
                FOREIGN KEY (EntityId) REFERENCES LocalWorkout(Id) ON DELETE RESTRICT
            );
            INSERT INTO OutboxOperation
                (OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
                 State, DeletedAt, Version, ServerVersion, RetryCount, NextAttemptAt,
                 ServerPayload, ReplacesOperationId, SendStartedAt, NeutralizedAt, FailureCode)
            SELECT OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
                   State, DeletedAt, Version, ServerVersion, RetryCount, NextAttemptAt,
                   ServerPayload, ReplacesOperationId, SendStartedAt, NeutralizedAt, FailureCode
            FROM OutboxOperationV4;
            CREATE INDEX IX_OutboxOperation_Pending
                ON OutboxOperation(State, CreatedAt, OperationId)
                WHERE State = 1 AND DeletedAt IS NULL;
            CREATE TABLE HistoryUndo (
                OperationId TEXT PRIMARY KEY NOT NULL,
                SnapshotJson TEXT NOT NULL CHECK (json_valid(SnapshotJson)),
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (OperationId) REFERENCES OutboxOperation(OperationId) ON DELETE CASCADE
            );
            INSERT INTO HistoryUndo (OperationId, SnapshotJson, CreatedAt)
            SELECT OperationId, SnapshotJson, CreatedAt FROM HistoryUndoV4;
            DROP TABLE HistoryUndoV4;
            DROP TABLE OutboxOperationV4;
            PRAGMA user_version = 7;
            """);
        return string.Join(Environment.NewLine, statements);

        void Add(string name, string definition)
        {
            if (!columns.Contains(name))
                statements.Add($"ALTER TABLE OutboxOperation ADD COLUMN {name} {definition};");
        }
    }

    private static string BuildGuidanceUpgrade() => """
        ALTER TABLE LocalSet
            ADD COLUMN Effort INTEGER NULL
            CHECK (Effort IS NULL OR Effort IN (1, 2, 3));
        DROP INDEX IF EXISTS IX_OutboxOperation_Pending;
        ALTER TABLE HistoryUndo RENAME TO HistoryUndoV5;
        ALTER TABLE OutboxOperation RENAME TO OutboxOperationV5;
        CREATE TABLE OutboxOperation (
            OperationId TEXT PRIMARY KEY NOT NULL,
            EntityId TEXT NOT NULL,
            OperationType INTEGER NOT NULL CHECK (OperationType BETWEEN 1 AND 11),
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
            FailureCode INTEGER NULL,
            FOREIGN KEY (EntityId) REFERENCES LocalWorkout(Id) ON DELETE RESTRICT
        );
        INSERT INTO OutboxOperation
            (OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
             State, DeletedAt, Version, ServerVersion, RetryCount, NextAttemptAt,
             ServerPayload, ReplacesOperationId, SendStartedAt, NeutralizedAt, FailureCode)
        SELECT OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
               State, DeletedAt, Version, ServerVersion, RetryCount, NextAttemptAt,
               ServerPayload, ReplacesOperationId, SendStartedAt, NeutralizedAt, FailureCode
        FROM OutboxOperationV5;
        CREATE INDEX IX_OutboxOperation_Pending
            ON OutboxOperation(State, CreatedAt, OperationId)
            WHERE State = 1 AND DeletedAt IS NULL;
        CREATE TABLE HistoryUndo (
            OperationId TEXT PRIMARY KEY NOT NULL,
            SnapshotJson TEXT NOT NULL CHECK (json_valid(SnapshotJson)),
            CreatedAt TEXT NOT NULL,
            FOREIGN KEY (OperationId) REFERENCES OutboxOperation(OperationId) ON DELETE CASCADE
        );
        INSERT INTO HistoryUndo (OperationId, SnapshotJson, CreatedAt)
        SELECT OperationId, SnapshotJson, CreatedAt FROM HistoryUndoV5;
        DROP TABLE HistoryUndoV5;
        DROP TABLE OutboxOperationV5;
        PRAGMA user_version = 7;
        """;

    private static string BuildPlateCountUpgrade() => """
        ALTER TABLE LocalSet
            ADD COLUMN PlateCount INTEGER NULL
            CHECK (PlateCount IS NULL OR PlateCount BETWEEN 1 AND 999);
        PRAGMA user_version = 7;
        """;
}
