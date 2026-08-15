using Microsoft.Extensions.Options;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Infrastructure.Identity;
using TrackZ.Infrastructure.Sync;

namespace TrackZ.Infrastructure.Tests.Sync;

public sealed class HmacSyncCursorCodecTests
{
    [Fact]
    public void Decode_rejects_a_noncanonical_signature_alias()
    {
        var codec = CreateCodec();
        var ownerId = Guid.NewGuid();
        var cursor = codec.Encode(ownerId, 42);
        var signature = cursor[(cursor.IndexOf('.') + 1)..];
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var lastValue = alphabet.IndexOf(signature[^1]);
        var aliasValue = (lastValue & ~3) | ((lastValue + 1) & 3);
        var alias = cursor[..^1] + alphabet[aliasValue];

        Assert.NotEqual(cursor, alias);
        Assert.Throws<BusinessException>(() => codec.Decode(alias, ownerId));
    }

    private static HmacSyncCursorCodec CreateCodec() => new(Options.Create(new JwtOptions
    {
        Issuer = "trackz-api",
        Audience = "trackz-mobile",
        SigningKey = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 14
    }));
}
