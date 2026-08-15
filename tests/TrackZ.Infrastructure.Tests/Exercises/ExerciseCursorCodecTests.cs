using Microsoft.Extensions.Options;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;
using TrackZ.Infrastructure.Exercises;
using TrackZ.Infrastructure.Identity;

namespace TrackZ.Infrastructure.Tests.Exercises;

public sealed class ExerciseCursorCodecTests
{
    [Fact]
    public void Cursor_rejects_noncanonical_signature_alias_without_changing_valid_round_trip()
    {
        var codec = CreateCodec();
        var cursorValue = new CatalogCursor(1, "Bench Press", Guid.NewGuid());
        var cursor = codec.Encode(cursorValue);
        var alias = NonCanonicalSignatureAlias(cursor);

        Assert.NotEqual(cursor, alias);
        Assert.Equal(DecodeSegments(cursor), DecodeSegments(alias));
        Assert.Equal(cursorValue, codec.Decode(cursor));
        var error = Assert.Throws<BusinessException>(() => codec.Decode(alias));
        Assert.Equal(BusinessErrorCode.InvalidRequest, error.Code);
        Assert.Equal(400, error.StatusCode);
    }

    private static HmacExerciseCursorCodec CreateCodec() => new(Options.Create(new JwtOptions
    {
        Issuer = "trackz-api",
        Audience = "trackz-mobile",
        SigningKey = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 14
    }));

    private static string NonCanonicalSignatureAlias(string cursor)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var lastValue = alphabet.IndexOf(cursor[^1]);
        var aliasValue = (lastValue & ~3) | ((lastValue + 1) & 3);
        return cursor[..^1] + alphabet[aliasValue];
    }

    private static byte[][] DecodeSegments(string cursor) => cursor
        .Split('.')
        .Select(value => Convert.FromBase64String(
            value.Replace('-', '+').Replace('_', '/')
                .PadRight(value.Length + (4 - value.Length % 4) % 4, '=')))
        .ToArray();
}
