using TrackZ.Application.Identity.Common;

namespace TrackZ.Application.Common.Interfaces;

public interface ITokenService
{
    AuthTokenPair CreateTokenPair(Guid userId, Guid sessionId);

    string HashRefreshToken(string refreshToken);

    DateTimeOffset GetRefreshTokenExpiration();
}
