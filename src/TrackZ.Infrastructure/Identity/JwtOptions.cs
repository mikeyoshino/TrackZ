namespace TrackZ.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 14;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Issuer)
        && !string.IsNullOrWhiteSpace(Audience)
        && SigningKey.Length >= 32
        && AccessTokenMinutes is > 0 and <= 60
        && RefreshTokenDays is > 0 and <= 90;
}
