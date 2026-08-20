using System.Globalization;
using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Exercises;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises.Models;

namespace TrackZ.Mobile.Features.Exercises.Data;

public enum PendingCustomOperationKind
{
    Create = 0,
    Update = 1
}

public enum PendingCustomSyncPhase
{
    PendingDetails = 0,
    DetailsSaved = 1,
    UploadReserved = 2,
    ContentUploaded = 3,
    PendingDetailsUploadReserved = 4,
    PendingDetailsContentUploaded = 5
}

public enum PendingCustomSyncStatus
{
    Pending = 0,
    UserActionRequired = 1
}

public sealed record CachedLibraryImage(Guid ImageId, string Name, string? ThumbnailUri);

public sealed record PendingCustomExercise(
    Guid OperationId,
    Guid LocalExerciseId,
    Guid? ServerExerciseId,
    string Name,
    BodyPart BodyPart,
    TrackingMode TrackingMode,
    Guid? LibraryImageId,
    string? LocalImagePath,
    string? LocalImageContentType,
    DateTimeOffset CreatedAt,
    string? LocalPreviewPath = null,
    PendingCustomOperationKind OperationKind = PendingCustomOperationKind.Create,
    PendingCustomSyncPhase Phase = PendingCustomSyncPhase.PendingDetails,
    Guid? UploadId = null,
    string? UploadUri = null,
    PendingCustomSyncStatus SyncStatus = PendingCustomSyncStatus.Pending,
    BusinessErrorCode? FailureCode = null,
    string? FailureMessage = null,
    DateTimeOffset? UploadExpiresAt = null);

public sealed class ExerciseCache
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _initialized;

    public ExerciseCache(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public async Task<IReadOnlyList<CachedExercise>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, BodyPart, TrackingMode, ThumbnailUri, LastPerformedAt,
                   LastBestWeightKg, LastBestAssistedKg, LastBestReps,
                   AllTimeBestWeightKg, AllTimeBestAssistedKg, AllTimeBestReps,
                   IsCustom, IsPendingSync, LastSyncedAt, LibraryImageId, RemoteThumbnailRoute
            FROM cached_exercises
            ORDER BY Name COLLATE NOCASE, Id;
            """;
        var result = new List<CachedExercise>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadExercise(reader));
        return result;
    }

    public async Task ReplaceAllAsync(
        IReadOnlyList<ExerciseSummaryDto> exercises,
        DateTimeOffset lastSyncedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exercises);
        Validate(exercises);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var localThumbnails = await ReadLocalThumbnailsAsync(connection, transaction, cancellationToken);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM cached_exercises WHERE IsPendingSync = 0;";
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var exercise in exercises)
        {
            localThumbnails.TryGetValue(exercise.Id, out var localThumbnail);
            await UpsertAsync(
                connection,
                transaction,
                CachedExercise.FromDto(exercise, lastSyncedAt, localThumbnail),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task QueueAsync(PendingCustomExercise pending, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var cached = new CachedExercise
        {
            Id = pending.LocalExerciseId,
            Name = pending.Name,
            BodyPart = pending.BodyPart,
            TrackingMode = pending.TrackingMode,
            ThumbnailUri = pending.LocalPreviewPath ?? pending.LocalImagePath,
            LibraryImageId = pending.LibraryImageId,
            IsCustom = true,
            IsPendingSync = true,
            LastSyncedAt = pending.CreatedAt
        };
        await UpsertAsync(connection, transaction, cached, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO pending_custom_exercises
                (OperationId, LocalExerciseId, ServerExerciseId, Name, BodyPart, TrackingMode,
                 LibraryImageId, LocalImagePath, LocalImageContentType, CreatedAt, LocalPreviewPath,
                 OperationKind, Phase, UploadId, UploadUri, SyncStatus, FailureCode, FailureMessage,
                 UploadExpiresAt)
            VALUES
                ($operationId, $localExerciseId, $serverExerciseId, $name, $bodyPart, $trackingMode,
                 $libraryImageId, $localImagePath, $localImageContentType, $createdAt, $localPreviewPath,
                 $operationKind, $phase, $uploadId, $uploadUri, $syncStatus, $failureCode, $failureMessage,
                 $uploadExpiresAt)
            ON CONFLICT(OperationId) DO UPDATE SET
                ServerExerciseId = excluded.ServerExerciseId,
                Name = excluded.Name,
                BodyPart = excluded.BodyPart,
                TrackingMode = excluded.TrackingMode,
                LibraryImageId = excluded.LibraryImageId,
                LocalImagePath = excluded.LocalImagePath,
                LocalImageContentType = excluded.LocalImageContentType,
                LocalPreviewPath = excluded.LocalPreviewPath,
                OperationKind = excluded.OperationKind,
                Phase = excluded.Phase,
                UploadId = excluded.UploadId,
                UploadUri = excluded.UploadUri,
                SyncStatus = excluded.SyncStatus,
                FailureCode = excluded.FailureCode,
                FailureMessage = excluded.FailureMessage,
                UploadExpiresAt = excluded.UploadExpiresAt;
            """;
        Add(command, "$operationId", pending.OperationId.ToString("D"));
        Add(command, "$localExerciseId", pending.LocalExerciseId.ToString("D"));
        Add(command, "$serverExerciseId", pending.ServerExerciseId?.ToString("D"));
        Add(command, "$name", pending.Name);
        Add(command, "$bodyPart", (int)pending.BodyPart);
        Add(command, "$trackingMode", (int)pending.TrackingMode);
        Add(command, "$libraryImageId", pending.LibraryImageId?.ToString("D"));
        Add(command, "$localImagePath", pending.LocalImagePath);
        Add(command, "$localImageContentType", pending.LocalImageContentType);
        Add(command, "$createdAt", Format(pending.CreatedAt));
        Add(command, "$localPreviewPath", pending.LocalPreviewPath);
        Add(command, "$operationKind", (int)pending.OperationKind);
        Add(command, "$phase", (int)pending.Phase);
        Add(command, "$uploadId", pending.UploadId?.ToString("D"));
        Add(command, "$uploadUri", pending.UploadUri);
        Add(command, "$syncStatus", (int)pending.SyncStatus);
        Add(command, "$failureCode", pending.FailureCode is null ? null : (int)pending.FailureCode.Value);
        Add(command, "$failureMessage", pending.FailureMessage);
        Add(command, "$uploadExpiresAt", pending.UploadExpiresAt is null ? null : Format(pending.UploadExpiresAt.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PendingCustomExercise>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        GetByStatusAsync(PendingCustomSyncStatus.Pending, cancellationToken);

    public Task<IReadOnlyList<PendingCustomExercise>> GetFailedAsync(CancellationToken cancellationToken = default) =>
        GetByStatusAsync(PendingCustomSyncStatus.UserActionRequired, cancellationToken);

    private async Task<IReadOnlyList<PendingCustomExercise>> GetByStatusAsync(
        PendingCustomSyncStatus status,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OperationId, LocalExerciseId, ServerExerciseId, Name, BodyPart, TrackingMode,
                   LibraryImageId, LocalImagePath, LocalImageContentType, CreatedAt, LocalPreviewPath,
                   OperationKind, Phase, UploadId, UploadUri, SyncStatus, FailureCode, FailureMessage,
                   UploadExpiresAt
            FROM pending_custom_exercises
            WHERE SyncStatus = $syncStatus
            ORDER BY CreatedAt, OperationId;
            """;
        Add(command, "$syncStatus", (int)status);
        var result = new List<PendingCustomExercise>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PendingCustomExercise(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                (BodyPart)reader.GetInt32(4),
                (TrackingMode)reader.GetInt32(5),
                reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                Parse(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                (PendingCustomOperationKind)reader.GetInt32(11),
                (PendingCustomSyncPhase)reader.GetInt32(12),
                reader.IsDBNull(13) ? null : Guid.Parse(reader.GetString(13)),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                (PendingCustomSyncStatus)reader.GetInt32(15),
                reader.IsDBNull(16) ? null : (BusinessErrorCode)reader.GetInt32(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : Parse(reader.GetString(18))));
        }
        return result;
    }

    public async Task<PendingCustomExercise?> FindPendingByLocalIdAsync(
        Guid localExerciseId,
        CancellationToken cancellationToken = default)
    {
        var pending = await GetPendingAsync(cancellationToken);
        var failed = await GetFailedAsync(cancellationToken);
        return pending.Concat(failed).SingleOrDefault(item => item.LocalExerciseId == localExerciseId);
    }

    public async Task<PendingCustomExercise?> FindOutboxByOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var pending = await GetPendingAsync(cancellationToken);
        var failed = await GetFailedAsync(cancellationToken);
        return pending.Concat(failed).SingleOrDefault(item => item.OperationId == operationId);
    }

    public async Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pending_custom_exercises WHERE SyncStatus = 0;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task<CachedExercise?> FindExerciseAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        var rows = await GetAllAsync(cancellationToken);
        return rows.SingleOrDefault(exercise => exercise.Id == exerciseId);
    }

    public async Task RollbackPendingAsync(
        Guid operationId,
        Guid localExerciseId,
        CachedExercise? previousExercise,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var pending = connection.CreateCommand())
        {
            pending.Transaction = transaction;
            pending.CommandText = "DELETE FROM pending_custom_exercises WHERE OperationId = $operationId;";
            Add(pending, "$operationId", operationId.ToString("D"));
            await pending.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var cached = connection.CreateCommand())
        {
            cached.Transaction = transaction;
            cached.CommandText = "DELETE FROM cached_exercises WHERE Id = $localExerciseId AND IsPendingSync = 1;";
            Add(cached, "$localExerciseId", localExerciseId.ToString("D"));
            await cached.ExecuteNonQueryAsync(cancellationToken);
        }
        if (previousExercise is not null)
            await UpsertAsync(connection, transaction, previousExercise, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkPendingFailedAsync(
        Guid operationId,
        BusinessErrorCode code,
        string message,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending_custom_exercises
            SET SyncStatus = 1, FailureCode = $failureCode, FailureMessage = $failureMessage
            WHERE OperationId = $operationId;
            """;
        Add(command, "$failureCode", (int)code);
        Add(command, "$failureMessage", message);
        Add(command, "$operationId", operationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CachedLibraryImage>> GetLibraryImagesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ImageId, Name, ThumbnailUri FROM cached_library_images ORDER BY Name COLLATE NOCASE, ImageId;";
        var result = new List<CachedLibraryImage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CachedLibraryImage(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return result;
    }

    public async Task ReplaceLibraryImagesAsync(
        IReadOnlyList<CachedLibraryImage> images,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM cached_library_images;";
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var image in images)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO cached_library_images (ImageId, Name, ThumbnailUri) VALUES ($id, $name, $thumbnail);";
            Add(insert, "$id", image.ImageId.ToString("D"));
            Add(insert, "$name", image.Name);
            Add(insert, "$thumbnail", image.ThumbnailUri);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetLibraryThumbnailAsync(
        Guid imageId,
        string localThumbnailUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localThumbnailUri);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE cached_library_images SET ThumbnailUri = $thumbnail WHERE ImageId = $id;";
        Add(command, "$thumbnail", localThumbnailUri);
        Add(command, "$id", imageId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetPendingServerIdAsync(
        Guid operationId,
        Guid serverExerciseId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending_custom_exercises
            SET ServerExerciseId = $serverExerciseId
            WHERE OperationId = $operationId;
            """;
        Add(command, "$serverExerciseId", serverExerciseId.ToString("D"));
        Add(command, "$operationId", operationId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The pending custom exercise no longer exists.");
    }

    public async Task CompletePendingAsync(
        Guid operationId,
        Guid localExerciseId,
        CachedExercise serverExercise,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var deleteLocal = connection.CreateCommand())
        {
            deleteLocal.Transaction = transaction;
            deleteLocal.CommandText = "DELETE FROM cached_exercises WHERE Id = $id;";
            Add(deleteLocal, "$id", localExerciseId.ToString("D"));
            await deleteLocal.ExecuteNonQueryAsync(cancellationToken);
        }
        await UpsertAsync(connection, transaction, serverExercise, cancellationToken);
        await using (var deletePending = connection.CreateCommand())
        {
            deletePending.Transaction = transaction;
            deletePending.CommandText = "DELETE FROM pending_custom_exercises WHERE OperationId = $id;";
            Add(deletePending, "$id", operationId.ToString("D"));
            await deletePending.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpsertServerExerciseAsync(CachedExercise exercise, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await UpsertAsync(connection, transaction, exercise, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetServerThumbnailAsync(
        Guid exerciseId,
        string localThumbnailUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localThumbnailUri);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE cached_exercises
            SET ThumbnailUri = $thumbnailUri
            WHERE Id = $id AND IsPendingSync = 0;
            """;
        Add(command, "$thumbnailUri", localThumbnailUri);
        Add(command, "$id", exerciseId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var table in new[] { "pending_custom_exercises", "cached_exercises", "cached_library_images" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS cached_exercises (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Name TEXT NOT NULL,
                    BodyPart INTEGER NOT NULL,
                    TrackingMode INTEGER NOT NULL,
                    ThumbnailUri TEXT NULL,
                    RemoteThumbnailRoute TEXT NULL,
                    LastPerformedAt TEXT NULL,
                    LastBestWeightKg TEXT NULL,
                    LastBestAssistedKg TEXT NULL,
                    LastBestReps INTEGER NULL,
                    AllTimeBestWeightKg TEXT NULL,
                    AllTimeBestAssistedKg TEXT NULL,
                    AllTimeBestReps INTEGER NULL,
                    IsCustom INTEGER NOT NULL,
                    IsPendingSync INTEGER NOT NULL,
                    LastSyncedAt TEXT NOT NULL,
                    LibraryImageId TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS pending_custom_exercises (
                    OperationId TEXT PRIMARY KEY NOT NULL,
                    LocalExerciseId TEXT NOT NULL,
                    ServerExerciseId TEXT NULL,
                    Name TEXT NOT NULL,
                    BodyPart INTEGER NOT NULL,
                    TrackingMode INTEGER NOT NULL,
                    LibraryImageId TEXT NULL,
                    LocalImagePath TEXT NULL,
                    LocalImageContentType TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    LocalPreviewPath TEXT NULL,
                    OperationKind INTEGER NOT NULL DEFAULT 0,
                    Phase INTEGER NOT NULL DEFAULT 0,
                    UploadId TEXT NULL,
                    UploadUri TEXT NULL,
                    SyncStatus INTEGER NOT NULL DEFAULT 0,
                    FailureCode INTEGER NULL,
                    FailureMessage TEXT NULL,
                    UploadExpiresAt TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS cached_library_images (
                    ImageId TEXT PRIMARY KEY NOT NULL,
                    Name TEXT NOT NULL,
                    ThumbnailUri TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await EnsureColumnAsync(connection, "cached_exercises", "LibraryImageId", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "cached_exercises", "RemoteThumbnailRoute", "TEXT NULL", cancellationToken);
            await using (var normalizeLegacyThumbnail = connection.CreateCommand())
            {
                normalizeLegacyThumbnail.CommandText = """
                    UPDATE cached_exercises
                    SET RemoteThumbnailRoute = ThumbnailUri, ThumbnailUri = NULL
                    WHERE IsPendingSync = 0
                      AND RemoteThumbnailRoute IS NULL
                      AND ThumbnailUri LIKE '/api/v1/media/exercise-images/%/thumbnail';
                    """;
                await normalizeLegacyThumbnail.ExecuteNonQueryAsync(cancellationToken);
            }
            await EnsureColumnAsync(connection, "pending_custom_exercises", "LocalPreviewPath", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "pending_custom_exercises", "OperationKind", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, "pending_custom_exercises", "Phase", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, "pending_custom_exercises", "UploadId", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "pending_custom_exercises", "UploadUri", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "pending_custom_exercises", "SyncStatus", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, "pending_custom_exercises", "FailureCode", "INTEGER NULL", cancellationToken);
            await EnsureColumnAsync(connection, "pending_custom_exercises", "FailureMessage", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "pending_custom_exercises", "UploadExpiresAt", "TEXT NULL", cancellationToken);
            await using (var normalizeLegacy = connection.CreateCommand())
            {
                normalizeLegacy.CommandText = """
                    UPDATE pending_custom_exercises
                    SET OperationKind = 1, Phase = 0
                    WHERE ServerExerciseId IS NOT NULL AND OperationKind = 0 AND Phase = 0;
                    """;
                await normalizeLegacy.ExecuteNonQueryAsync(cancellationToken);
            }
            _initialized = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string name,
        string declaration,
        CancellationToken cancellationToken)
    {
        await using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), name, StringComparison.OrdinalIgnoreCase)) return;
        }
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {name} {declaration};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task UpsertAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        CachedExercise exercise,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO cached_exercises
                (Id, Name, BodyPart, TrackingMode, ThumbnailUri, RemoteThumbnailRoute, LastPerformedAt,
                 LastBestWeightKg, LastBestAssistedKg, LastBestReps,
                 AllTimeBestWeightKg, AllTimeBestAssistedKg, AllTimeBestReps,
                 IsCustom, IsPendingSync, LastSyncedAt, LibraryImageId)
            VALUES
                ($id, $name, $bodyPart, $trackingMode, $thumbnailUri, $remoteThumbnailRoute, $lastPerformedAt,
                 $lastWeight, $lastAssisted, $lastReps,
                 $bestWeight, $bestAssisted, $bestReps,
                 $isCustom, $isPendingSync, $lastSyncedAt, $libraryImageId)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                BodyPart = excluded.BodyPart,
                TrackingMode = excluded.TrackingMode,
                ThumbnailUri = excluded.ThumbnailUri,
                RemoteThumbnailRoute = excluded.RemoteThumbnailRoute,
                LastPerformedAt = excluded.LastPerformedAt,
                LastBestWeightKg = excluded.LastBestWeightKg,
                LastBestAssistedKg = excluded.LastBestAssistedKg,
                LastBestReps = excluded.LastBestReps,
                AllTimeBestWeightKg = excluded.AllTimeBestWeightKg,
                AllTimeBestAssistedKg = excluded.AllTimeBestAssistedKg,
                AllTimeBestReps = excluded.AllTimeBestReps,
                IsCustom = excluded.IsCustom,
                IsPendingSync = excluded.IsPendingSync,
                LastSyncedAt = excluded.LastSyncedAt,
                LibraryImageId = excluded.LibraryImageId
            WHERE excluded.IsPendingSync = 1 OR cached_exercises.IsPendingSync = 0;
            """;
        Add(command, "$id", exercise.Id.ToString("D"));
        Add(command, "$name", exercise.Name);
        Add(command, "$bodyPart", (int)exercise.BodyPart);
        Add(command, "$trackingMode", (int)exercise.TrackingMode);
        Add(command, "$thumbnailUri", exercise.ThumbnailUri);
        Add(command, "$remoteThumbnailRoute", exercise.RemoteThumbnailRoute);
        Add(command, "$lastPerformedAt", exercise.LastPerformedAt is null ? null : Format(exercise.LastPerformedAt.Value));
        Add(command, "$lastWeight", Decimal(exercise.LastBestSet?.WeightKg));
        Add(command, "$lastAssisted", Decimal(exercise.LastBestSet?.AssistedKg));
        Add(command, "$lastReps", exercise.LastBestSet?.Reps);
        Add(command, "$bestWeight", Decimal(exercise.AllTimeBest?.WeightKg));
        Add(command, "$bestAssisted", Decimal(exercise.AllTimeBest?.AssistedKg));
        Add(command, "$bestReps", exercise.AllTimeBest?.Reps);
        Add(command, "$isCustom", exercise.IsCustom ? 1 : 0);
        Add(command, "$isPendingSync", exercise.IsPendingSync ? 1 : 0);
        Add(command, "$lastSyncedAt", Format(exercise.LastSyncedAt));
        Add(command, "$libraryImageId", exercise.LibraryImageId?.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CachedExercise ReadExercise(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Name = reader.GetString(1),
        BodyPart = (BodyPart)reader.GetInt32(2),
        TrackingMode = (TrackingMode)reader.GetInt32(3),
        ThumbnailUri = reader.IsDBNull(4) ? null : reader.GetString(4),
        RemoteThumbnailRoute = reader.IsDBNull(16) ? null : reader.GetString(16),
        LastPerformedAt = reader.IsDBNull(5) ? null : Parse(reader.GetString(5)),
        LastBestSet = Set(reader, 6, 7, 8),
        AllTimeBest = Set(reader, 9, 10, 11),
        IsCustom = reader.GetInt32(12) != 0,
        IsPendingSync = reader.GetInt32(13) != 0,
        LastSyncedAt = Parse(reader.GetString(14)),
        LibraryImageId = reader.IsDBNull(15) ? null : Guid.Parse(reader.GetString(15))
    };

    private static async Task<Dictionary<Guid, string>> ReadLocalThumbnailsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, ThumbnailUri FROM cached_exercises WHERE IsPendingSync = 0 AND ThumbnailUri IS NOT NULL;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var path = reader.GetString(1);
            if (IsLocalThumbnail(path)) result[Guid.Parse(reader.GetString(0))] = path;
        }
        return result;
    }

    private static bool IsLocalThumbnail(string path) =>
        !Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsFile;

    private static PerformanceSetDto? Set(SqliteDataReader reader, int weightIndex, int assistedIndex, int repsIndex) =>
        reader.IsDBNull(repsIndex)
            ? null
            : new PerformanceSetDto(
                reader.IsDBNull(weightIndex) ? null : decimal.Parse(reader.GetString(weightIndex), CultureInfo.InvariantCulture),
                reader.IsDBNull(assistedIndex) ? null : decimal.Parse(reader.GetString(assistedIndex), CultureInfo.InvariantCulture),
                reader.GetInt32(repsIndex));

    private static void Validate(IEnumerable<ExerciseSummaryDto> exercises)
    {
        foreach (var exercise in exercises)
        {
            if (exercise.Id == Guid.Empty) throw new ArgumentException("Exercise IDs cannot be empty.", nameof(exercises));
            if (string.IsNullOrWhiteSpace(exercise.Name)) throw new ArgumentException("Exercise names cannot be empty.", nameof(exercises));
            if (!Enum.IsDefined(exercise.BodyPart)) throw new ArgumentException("Body part is invalid.", nameof(exercises));
            if (!Enum.IsDefined(exercise.TrackingMode)) throw new ArgumentException("Tracking mode is invalid.", nameof(exercises));
        }
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string? Decimal(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
