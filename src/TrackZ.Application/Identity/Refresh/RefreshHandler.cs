using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Identity.Common;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Identity;

namespace TrackZ.Application.Identity.Refresh;

public sealed class RefreshHandler(IAppDbContext db, ITokenService tokenService)
    : IRequestHandler<RefreshCommand, AuthTokenPair>
{
    public async Task<AuthTokenPair> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || string.IsNullOrWhiteSpace(request.DeviceName))
        {
            throw InvalidRefreshToken();
        }

        // Raw refresh tokens are intentionally never used after this one-way hash calculation.
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var currentToken = await db.FindRefreshTokenForUpdateAsync(tokenHash, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (currentToken is null || currentToken.RevokedAt is not null || currentToken.ExpiresAt <= now)
        {
            throw InvalidRefreshToken();
        }

        var replacement = tokenService.CreateTokenPair(currentToken.UserId, currentToken.SessionId);
        currentToken.Revoke(now);
        await db.RefreshTokens.AddAsync(RefreshToken.Create(
            currentToken.UserId,
            tokenService.HashRefreshToken(replacement.RefreshToken),
            currentToken.SessionId,
            tokenService.GetRefreshTokenExpiration(),
            now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replacement;
    }

    private static BusinessException InvalidRefreshToken() => new(
        BusinessErrorCode.RefreshTokenInvalid,
        "The refresh token is invalid or expired.",
        401);
}
