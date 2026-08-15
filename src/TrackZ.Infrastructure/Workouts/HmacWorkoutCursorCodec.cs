using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Workouts;
using TrackZ.Contracts.Errors;
using TrackZ.Infrastructure.Identity;

namespace TrackZ.Infrastructure.Workouts;

public sealed class HmacWorkoutCursorCodec(IOptions<JwtOptions> options) : IWorkoutCursorCodec
{
    private const int Version = 1;
    private readonly byte[] _key = SHA256.HashData(
        Encoding.UTF8.GetBytes($"trackz.workout.cursor.v1:{options.Value.SigningKey}"));

    public WorkoutCursor Decode(string cursor)
    {
        try
        {
            var segments = cursor.Split('.', StringSplitOptions.None);
            if (segments.Length != 2) throw new FormatException();
            var payload = DecodeBase64Url(segments[0]);
            var suppliedSignature = DecodeBase64Url(segments[1]);
            var expectedSignature = HMACSHA256.HashData(_key, payload);
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                throw new CryptographicException();
            }

            var model = JsonSerializer.Deserialize<Payload>(payload) ?? throw new FormatException();
            if (model.Version != Version
                || model.WorkoutId == Guid.Empty
                || model.CompletedAt == default
                || model.CompletedAt.Offset != TimeSpan.Zero)
            {
                throw new FormatException();
            }

            return new WorkoutCursor(model.Version, model.CompletedAt, model.WorkoutId);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            throw InvalidCursor();
        }
    }

    public string Encode(WorkoutCursor cursor)
    {
        if (cursor.Version != Version
            || cursor.WorkoutId == Guid.Empty
            || cursor.CompletedAt == default
            || cursor.CompletedAt.Offset != TimeSpan.Zero)
        {
            throw InvalidCursor();
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new Payload(cursor.Version, cursor.CompletedAt, cursor.WorkoutId));
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{EncodeBase64Url(payload)}.{EncodeBase64Url(signature)}";
    }

    private static BusinessException InvalidCursor() => new(
        BusinessErrorCode.InvalidRequest,
        "The cursor is invalid.",
        400);

    private static string EncodeBase64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new FormatException();
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '='));
    }

    private sealed record Payload(int Version, DateTimeOffset CompletedAt, Guid WorkoutId);
}
