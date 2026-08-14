using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Identity;

namespace TrackZ.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    Task<RefreshToken?> FindRefreshTokenForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<List<RefreshToken>> FindActiveSessionTokensForUpdateAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IAppDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
