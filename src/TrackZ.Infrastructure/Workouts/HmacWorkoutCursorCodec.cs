using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Workouts;
using TrackZ.Contracts.Errors;
using TrackZ.Infrastructure.Identity;

namespace TrackZ.Infrastructure.Workouts;

public sealed class HmacWorkoutCursorCodec(
    IOptions<JwtOptions> jwtOptions,
    IOptions<WorkoutCursorOptions> cursorOptions,
    TimeProvider timeProvider) : IWorkoutCursorCodec
{
    private const int Version = 1;
    private readonly byte[] _key = SHA256.HashData(
        Encoding.UTF8.GetBytes($"trackz.workout.cursor.v1:{jwtOptions.Value.SigningKey}"));
    private readonly TimeSpan _lifetime = TimeSpan.FromMinutes(cursorOptions.Value.LifetimeMinutes);

    public WorkoutCursor Decode(string cursor, WorkoutCursorScope expectedScope)
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
            var now = timeProvider.GetUtcNow().ToUniversalTime();
            if (model.Version != Version
                || !Enum.IsDefined(model.Purpose)
                || model.OwnerId == Guid.Empty
                || model.WorkoutId == Guid.Empty
                || model.IssuedAt == default
                || model.ExpiresAt == default
                || model.CompletedAt == default
                || model.IssuedAt.Offset != TimeSpan.Zero
                || model.ExpiresAt.Offset != TimeSpan.Zero
                || model.CompletedAt.Offset != TimeSpan.Zero
                || model.ExpiresAt - model.IssuedAt != _lifetime
                || model.IssuedAt > now
                || model.ExpiresAt <= now
                || !IsValidScope(model.OwnerId, model.Purpose, model.ExerciseId)
                || model.OwnerId != expectedScope.OwnerId
                || model.Purpose != expectedScope.Purpose
                || model.ExerciseId != expectedScope.ExerciseId
                || !IsValidScope(expectedScope.OwnerId, expectedScope.Purpose, expectedScope.ExerciseId))
            {
                throw new FormatException();
            }

            return new WorkoutCursor(
                model.Version,
                model.Purpose,
                model.OwnerId,
                model.ExerciseId,
                model.IssuedAt,
                model.ExpiresAt,
                model.CompletedAt,
                model.WorkoutId);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            throw InvalidCursor();
        }
    }

    public string Encode(WorkoutCursorScope scope, DateTimeOffset completedAt, Guid workoutId)
    {
        if (!IsValidScope(scope.OwnerId, scope.Purpose, scope.ExerciseId)
            || workoutId == Guid.Empty
            || completedAt == default
            || completedAt.Offset != TimeSpan.Zero)
        {
            throw InvalidCursor();
        }

        var issuedAt = timeProvider.GetUtcNow().ToUniversalTime();
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new Payload(
                Version,
                scope.Purpose,
                scope.OwnerId,
                scope.ExerciseId,
                issuedAt,
                issuedAt.Add(_lifetime),
                completedAt,
                workoutId));
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{EncodeBase64Url(payload)}.{EncodeBase64Url(signature)}";
    }

    private static BusinessException InvalidCursor() => new(
        BusinessErrorCode.InvalidRequest,
        "The cursor is invalid.",
        400);

    private static bool IsValidScope(
        Guid ownerId,
        WorkoutCursorPurpose purpose,
        Guid? exerciseId) => ownerId != Guid.Empty
        && Enum.IsDefined(purpose)
        && (purpose == WorkoutCursorPurpose.WorkoutHistory && exerciseId is null
            || purpose == WorkoutCursorPurpose.ExerciseHistory && exerciseId is { } id && id != Guid.Empty);

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
        var decoded = Convert.FromBase64String(
            base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '='));
        if (!string.Equals(value, EncodeBase64Url(decoded), StringComparison.Ordinal))
        {
            throw new FormatException();
        }

        return decoded;
    }

    private sealed record Payload(
        int Version,
        WorkoutCursorPurpose Purpose,
        Guid OwnerId,
        Guid? ExerciseId,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset CompletedAt,
        Guid WorkoutId);
}
