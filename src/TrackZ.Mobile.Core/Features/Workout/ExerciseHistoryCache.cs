using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Workout;

public sealed class ExerciseHistoryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _initialized;

    public ExerciseHistoryCache(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public async Task<ExerciseHistorySessionDto?> GetMostRecentAsync(
        Guid exerciseId,
        CancellationToken cancellationToken = default)
    {
        ValidateExerciseId(exerciseId);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM PreviousExerciseSession WHERE ExerciseDefinitionId = $id;";
        command.Parameters.AddWithValue("$id", exerciseId.ToString("D"));
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (payload is null) return null;
        try
        {
            var value = JsonSerializer.Deserialize<ExerciseHistorySessionDto>(payload, JsonOptions)
                ?? throw new InvalidDataException("Cached exercise history is empty.");
            Validate(value);
            return value;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Cached exercise history is malformed.", exception);
        }
    }

    public async Task ReplaceAsync(
        Guid exerciseId,
        ExerciseHistorySessionDto? session,
        CancellationToken cancellationToken = default)
    {
        ValidateExerciseId(exerciseId);
        if (session is not null) Validate(session);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (session is null)
        {
            command.CommandText = "DELETE FROM PreviousExerciseSession WHERE ExerciseDefinitionId = $id;";
        }
        else
        {
            command.CommandText = """
                INSERT INTO PreviousExerciseSession (ExerciseDefinitionId, Payload)
                VALUES ($id, $payload)
                ON CONFLICT(ExerciseDefinitionId) DO UPDATE SET Payload = excluded.Payload;
                """;
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session, JsonOptions));
        }
        command.Parameters.AddWithValue("$id", exerciseId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PreviousExerciseSession;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS PreviousExerciseSession (
                    ExerciseDefinitionId TEXT PRIMARY KEY NOT NULL,
                    Payload TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void Validate(ExerciseHistorySessionDto session)
    {
        if (session.WorkoutId == Guid.Empty
            || !Enum.IsDefined(session.TrackingMode)
            || session.CompletedAt.Offset != TimeSpan.Zero
            || session.Sets is null)
            throw new InvalidDataException("Exercise history metadata is invalid.");
        var ordered = session.Sets.OrderBy(item => item.Order).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var set = ordered[index];
            if (set.Id == Guid.Empty || set.Order != index || set.Reps is < 1 or > 999)
                throw new InvalidDataException("Exercise history set identity or order is invalid.");
            var validShape = session.TrackingMode switch
            {
                TrackingMode.Weighted => set.WeightKg is > 0m && set.AssistedKg is null,
                TrackingMode.Bodyweight => set.WeightKg is null && set.AssistedKg is null,
                TrackingMode.Assisted => set.WeightKg is null && set.AssistedKg is > 0m,
                _ => false
            };
            if (!validShape) throw new InvalidDataException("Exercise history set shape is invalid.");
        }
    }

    private static void ValidateExerciseId(Guid exerciseId)
    {
        if (exerciseId == Guid.Empty)
            throw new ArgumentException("Exercise ID is required.", nameof(exerciseId));
    }
}

public sealed class CachedExerciseHistorySource(
    ExerciseHistoryCache cache,
    IExerciseHistoryApi api,
    IAccountSessionBoundary boundary) : IExerciseHistorySource
{
    public async Task<ExerciseHistorySessionDto?> GetMostRecentAsync(
        Guid exerciseId,
        bool refreshIfOnline,
        CancellationToken cancellationToken = default)
    {
        var generation = boundary.Capture();
        var cached = await cache.GetMostRecentAsync(exerciseId, cancellationToken);
        if (!refreshIfOnline) return cached;
        try
        {
            using var lease = boundary.CreateCancellationLease(generation, cancellationToken);
            var refreshed = await api.GetMostRecentAsync(exerciseId, lease.Token);
            var committed = await boundary.TryCommitAsync(
                generation,
                token => cache.ReplaceAsync(exerciseId, refreshed, token),
                cancellationToken);
            return committed ? refreshed : null;
        }
        catch (OperationCanceledException) when (boundary.IsCancellationRequested(generation))
        {
            return null;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException
            || exception is MobileApiException { IsRetryable: true })
        {
            return cached;
        }
    }
}
