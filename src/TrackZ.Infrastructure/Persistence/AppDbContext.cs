using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Exercises.Custom;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Identity;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;

namespace TrackZ.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext, IExerciseCatalogReadStore, ICustomExerciseStore
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ExerciseDefinition> Exercises => Set<ExerciseDefinition>();

    public DbSet<ExerciseImage> ExerciseImages => Set<ExerciseImage>();

    public DbSet<ExercisePerformance> ExercisePerformances => Set<ExercisePerformance>();

    public Task<User?> FindUserByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default) =>
        Users.SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);

    public async Task<bool> TryAddUserAsync(User user, CancellationToken cancellationToken = default)
    {
        await Users.AddAsync(user, cancellationToken);

        try
        {
            await SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsNormalizedEmailUniqueConstraintViolation(exception))
        {
            Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    public async Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default) =>
        await RefreshTokens.AddAsync(refreshToken, cancellationToken);

    public async Task<bool> TryCreateCustomAsync(ExerciseDefinition exercise, CancellationToken cancellationToken)
    {
        await Exercises.AddAsync(exercise, cancellationToken);
        return await TrySaveCustomAsync(cancellationToken, exercise);
    }

    public async Task<ExerciseDefinition?> FindActiveCustomOwnedAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken)
    {
        var exercise = await Exercises.SingleOrDefaultAsync(item =>
            item.Id == exerciseId && item.OwnerId == ownerId && !item.IsArchived, cancellationToken);

        if (exercise is not null && await ExercisePerformances.AnyAsync(item => item.ExerciseDefinitionId == exerciseId, cancellationToken))
        {
            exercise.RecordSetHistory();
        }

        return exercise;
    }

    public Task<bool> TrySaveCustomAsync(CancellationToken cancellationToken) => TrySaveCustomAsync(cancellationToken, null);

    public Task<RefreshToken?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        RefreshTokens.AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public Task<RefreshToken?> FindRefreshTokenForUpdateAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        RefreshTokens.FromSqlInterpolated($"SELECT * FROM refresh_tokens WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> FindActiveSessionTokensForUpdateAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        await RefreshTokens.FromSqlInterpolated($"SELECT * FROM refresh_tokens WHERE \"UserId\" = {userId} AND \"SessionId\" = {sessionId} AND \"RevokedAt\" IS NULL FOR UPDATE")
            .ToListAsync(cancellationToken);

    public async Task<IAppDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        new AppDbTransaction(await Database.BeginTransactionAsync(cancellationToken));

    public Task AcquireSessionLockAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BitConverter.ToInt64(sessionId.ToByteArray(), 0)})", cancellationToken);

    public async Task<IReadOnlyList<CatalogExerciseReadItem>> ListAsync(
        Guid userId,
        BodyPart? bodyPart,
        string? normalizedSearch,
        CatalogCursor? after,
        int take,
        CancellationToken cancellationToken)
    {
        var query =
            from exercise in Exercises.AsNoTracking()
            join performance in ExercisePerformances.AsNoTracking().Where(item => item.UserId == userId)
                on exercise.Id equals performance.ExerciseDefinitionId into performanceRows
            from performance in performanceRows.DefaultIfEmpty()
            where !exercise.IsArchived
                && (exercise.OwnerId == null || exercise.OwnerId == userId)
                && (!bodyPart.HasValue || exercise.BodyPart == bodyPart.Value)
                && (normalizedSearch == null || exercise.NormalizedName.Contains(normalizedSearch))
                && (after == null
                    || string.Compare(exercise.Name, after.OrderingName) > 0
                    || (exercise.Name == after.OrderingName && exercise.Id.CompareTo(after.OrderingId) > 0))
            orderby exercise.Name, exercise.Id
            select new CatalogExerciseReadItem(
                new ExerciseSummaryDto(
                    exercise.Id,
                    exercise.Name,
                    exercise.BodyPart,
                    exercise.TrackingMode,
                    // Object keys are private implementation details. Task 4 will resolve a reviewed
                    // system image to a public endpoint or a signed private rendition URL.
                    null,
                    performance == null ? null : performance.LastPerformedAt,
                    performance == null || performance.TrackingMode != exercise.TrackingMode || performance.LastBestReps == null
                        ? null
                        : new PerformanceSetDto(
                            exercise.TrackingMode == TrackingMode.Weighted ? performance.LastBestWeightKg : null,
                            exercise.TrackingMode == TrackingMode.Assisted ? performance.LastBestAssistedKg : null,
                            performance.LastBestReps.Value),
                    performance == null || performance.TrackingMode != exercise.TrackingMode || performance.AllTimeBestReps == null
                        ? null
                        : new PerformanceSetDto(
                            exercise.TrackingMode == TrackingMode.Weighted ? performance.AllTimeBestWeightKg : null,
                            exercise.TrackingMode == TrackingMode.Assisted ? performance.AllTimeBestAssistedKg : null,
                            performance.AllTimeBestReps.Value),
                    exercise.OwnerId != null),
                exercise.Name,
                exercise.Id);

        return await query.Take(take).ToListAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    private static bool IsNormalizedEmailUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_users_NormalizedEmail"
        };

    private async Task<bool> TrySaveCustomAsync(CancellationToken cancellationToken, ExerciseDefinition? addedExercise)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsActiveCustomNameUniqueConstraintViolation(exception))
        {
            if (addedExercise is not null)
            {
                Entry(addedExercise).State = EntityState.Detached;
            }

            return false;
        }
    }

    private static bool IsActiveCustomNameUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_exercise_definitions_OwnerId_NormalizedName"
        };

    private sealed class AppDbTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        : IAppDbTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
