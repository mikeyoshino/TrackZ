using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;
using TrackZ.Infrastructure.Identity;

namespace TrackZ.Infrastructure.Exercises;

public sealed class HmacExerciseCursorCodec(IOptions<JwtOptions> options) : IExerciseCursorCodec
{
    private const int Version = 1;
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes($"trackz.catalog.cursor.v1:{options.Value.SigningKey}"));

    public CatalogCursor Decode(string cursor)
    {
        try
        {
            var segments = cursor.Split('.', StringSplitOptions.None);
            if (segments.Length != 2) throw new FormatException();

            var payload = Base64UrlDecode(segments[0]);
            var suppliedSignature = Base64UrlDecode(segments[1]);
            var expectedSignature = HMACSHA256.HashData(_key, payload);
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature)) throw new CryptographicException();

            var model = JsonSerializer.Deserialize<CursorPayload>(payload) ?? throw new FormatException();
            if (model.Version != Version || string.IsNullOrWhiteSpace(model.OrderingName) || model.OrderingId == Guid.Empty) throw new FormatException();
            return new CatalogCursor(model.Version, model.OrderingName, model.OrderingId);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            throw InvalidCursor();
        }
    }

    public string Encode(CatalogCursor cursor)
    {
        if (cursor.Version != Version || string.IsNullOrWhiteSpace(cursor.OrderingName) || cursor.OrderingId == Guid.Empty)
        {
            throw InvalidCursor();
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(cursor.Version, cursor.OrderingName, cursor.OrderingId));
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{Base64UrlEncode(payload)}.{Base64UrlEncode(signature)}";
    }

    private static BusinessException InvalidCursor() => new(BusinessErrorCode.InvalidRequest, "The cursor is invalid.", StatusCodes.Status400BadRequest);

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new FormatException();
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        var decoded = Convert.FromBase64String(
            base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '='));
        if (!string.Equals(value, Base64UrlEncode(decoded), StringComparison.Ordinal))
        {
            throw new FormatException();
        }

        return decoded;
    }

    private sealed record CursorPayload(int Version, string OrderingName, Guid OrderingId);
}
