using System.Globalization;
using Microsoft.Data.Sqlite;
using TrackZ.Mobile.Data;

namespace TrackZ.Mobile.Sync;

public sealed class OutboxRepository(TrackZLocalDatabase database)
{
    public Task<IReadOnlyList<OutboxOperation>> PendingAsync(
        CancellationToken cancellationToken = default) =>
        database.ReadAsync(ReadPendingAsync, cancellationToken);

    public Task<IReadOnlyList<OutboxOperation>> ConflictedAsync(
        CancellationToken cancellationToken = default) =>
        database.ReadAsync(ReadConflictedAsync, cancellationToken);

    private static Task<IReadOnlyList<OutboxOperation>> ReadConflictedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        ReadByStateAsync(connection, OutboxOperationState.Conflicted, cancellationToken);

    private static async Task<IReadOnlyList<OutboxOperation>> ReadPendingAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        await ReadByStateAsync(connection, OutboxOperationState.Pending, cancellationToken);

    private static async Task<IReadOnlyList<OutboxOperation>> ReadByStateAsync(
        SqliteConnection connection,
        OutboxOperationState state,
        CancellationToken cancellationToken)
    {
        var result = new List<OutboxOperation>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                   CreatedAt, State, DeletedAt, Version, ServerVersion, RetryCount,
                   NextAttemptAt, ServerPayload, ReplacesOperationId
            FROM OutboxOperation
            WHERE State = $state AND DeletedAt IS NULL
            ORDER BY CreatedAt, OperationId;
            """;
        command.Parameters.AddWithValue("$state", (int)state);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        try
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new OutboxOperation(
                    ParseGuid(reader.GetString(0)),
                    ParseGuid(reader.GetString(1)),
                    ParseEnum<OutboxOperationType>(reader.GetInt32(2)),
                    reader.GetString(3),
                    NonNegative(reader.GetInt64(4)),
                    ParseTimestamp(reader.GetString(5)),
                    ParseEnum<OutboxOperationState>(reader.GetInt32(6)),
                    reader.IsDBNull(7) ? null : ParseTimestamp(reader.GetString(7)),
                    Positive(reader.GetInt64(8)),
                    reader.IsDBNull(9) ? null : NonNegative(reader.GetInt64(9)),
                    reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : ParseTimestamp(reader.GetString(11)),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : ParseGuid(reader.GetString(13))));
            }
            return result;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException("An outbox row is corrupt.", exception);
        }
    }

    private static Guid ParseGuid(string text) =>
        Guid.TryParseExact(text, "D", out var value) && value != Guid.Empty
            ? value
            : throw new InvalidDataException("An outbox identifier is invalid.");

    private static T ParseEnum<T>(int value) where T : struct, Enum =>
        Enum.IsDefined(typeof(T), value)
            ? (T)Enum.ToObject(typeof(T), value)
            : throw new InvalidDataException("An outbox enum is invalid.");

    private static long NonNegative(long value) =>
        value >= 0 ? value : throw new InvalidDataException("An outbox version is negative.");

    private static long Positive(long value) =>
        value > 0 ? value : throw new InvalidDataException("An outbox version is not positive.");

    private static DateTimeOffset ParseTimestamp(string text)
    {
        if (!DateTimeOffset.TryParseExact(
                text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            || value.Offset != TimeSpan.Zero)
            throw new InvalidDataException("An outbox timestamp is not canonical UTC.");
        return value;
    }
}
