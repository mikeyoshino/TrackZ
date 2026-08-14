using MediatR;
using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Contracts.Errors;

namespace TrackZ.Application.Identity.Logout;

public sealed class LogoutHandler(IAppDbContext db) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var tokens = await db.FindActiveSessionTokensForUpdateAsync(request.UserId, request.SessionId, cancellationToken);

        // A session outside the authenticated user's ownership is indistinguishable from a missing session.
        if (tokens.Count == 0)
        {
            throw new BusinessException(
                BusinessErrorCode.RefreshTokenInvalid,
                "The requested session was not found.",
                404);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var token in tokens)
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
