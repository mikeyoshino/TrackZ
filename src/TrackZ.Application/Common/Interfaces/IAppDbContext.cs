using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Identity;

namespace TrackZ.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
