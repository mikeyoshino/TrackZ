using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Identity.Common;

namespace TrackZ.Infrastructure.Identity;

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AuthTokenPair CreateTokenPair(Guid userId, Guid sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var accessToken = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
                new Claim("sid", sessionId.ToString("D"))
            ],
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        return new AuthTokenPair(
            new JwtSecurityTokenHandler().WriteToken(accessToken),
            Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64)),
            expiresAt);
    }

    public string HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }

    public DateTimeOffset GetRefreshTokenExpiration() => DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays);
}
