using TrackZ.Domain.Identity;

namespace TrackZ.Application.Common.Interfaces;

public interface IAppDbContext
{
    Task<User?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<bool> TryAddUserAsync(User user, CancellationToken cancellationToken = default);

    Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);

    Task<RefreshToken?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<RefreshToken?> FindRefreshTokenForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefreshToken>> FindActiveSessionTokensForUpdateAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IAppDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task AcquireSessionLockAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
