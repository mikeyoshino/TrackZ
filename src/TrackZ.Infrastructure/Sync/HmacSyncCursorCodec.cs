using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Sync.Pull;
using TrackZ.Contracts.Errors;
using TrackZ.Infrastructure.Identity;

namespace TrackZ.Infrastructure.Sync;

public sealed class HmacSyncCursorCodec(IOptions<JwtOptions> options) : ISyncCursorCodec
{
    private readonly byte[] _key = SHA256.HashData(
        Encoding.UTF8.GetBytes($"trackz.sync.cursor.v1:{options.Value.SigningKey}"));

    public long Decode(string cursor, Guid expectedOwnerId)
    {
        try
        {
            var segments = cursor.Split('.', StringSplitOptions.None);
            if (segments.Length != 2) throw new FormatException();
            var payload = Decode64(segments[0]);
            if (!CryptographicOperations.FixedTimeEquals(
                    Decode64(segments[1]), HMACSHA256.HashData(_key, payload)))
                throw new CryptographicException();
            var model = JsonSerializer.Deserialize<Payload>(payload) ?? throw new FormatException();
            if (model.Version != 1 || model.OwnerId != expectedOwnerId || model.Sequence < 1)
                throw new FormatException();
            return model.Sequence;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or CryptographicException)
        {
            throw new BusinessException(BusinessErrorCode.InvalidRequest, "The cursor is invalid.", 400);
        }
    }

    public string Encode(Guid ownerId, long sequence)
    {
        if (ownerId == Guid.Empty || sequence < 1) throw new ArgumentException("Invalid sync cursor state.");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(1, ownerId, sequence));
        return $"{Encode64(payload)}.{Encode64(HMACSHA256.HashData(_key, payload))}";
    }

    private static string Encode64(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode64(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new FormatException();
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    private sealed record Payload(int Version, Guid OwnerId, long Sequence);
}
