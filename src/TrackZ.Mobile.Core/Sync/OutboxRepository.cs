using System.Globalization;
using Microsoft.Data.Sqlite;
using TrackZ.Mobile.Data;

namespace TrackZ.Mobile.Sync;

public sealed class OutboxRepository(TrackZLocalDatabase database)
{
    public Task<IReadOnlyList<OutboxOperation>> PendingAsync(
        CancellationToken cancellationToken = default) =>
        database.ReadAsync(ReadPendingAsync, cancellationToken);

    private static async Task<IReadOnlyList<OutboxOperation>> ReadPendingAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<OutboxOperation>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                   CreatedAt, State, DeletedAt, Version
            FROM OutboxOperation
            WHERE State = 1 AND DeletedAt IS NULL
            ORDER BY CreatedAt, OperationId;
            """;
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
                    Positive(reader.GetInt64(8))));
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
