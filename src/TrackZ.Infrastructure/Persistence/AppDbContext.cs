using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Exercises.Custom;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Identity;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;
using TrackZ.Application.Media;

namespace TrackZ.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext, IExerciseCatalogReadStore, ICustomExerciseStore, IExerciseImageUploadStore
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ExerciseDefinition> Exercises => Set<ExerciseDefinition>();

    public DbSet<ExerciseImage> ExerciseImages => Set<ExerciseImage>();

    public DbSet<ExercisePerformance> ExercisePerformances => Set<ExercisePerformance>();
    public DbSet<ImageUploadTicket> ImageUploadTickets => Set<ImageUploadTicket>();

    public Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken) => Exercises.SingleOrDefaultAsync(x => x.Id == exerciseId && x.OwnerId == ownerId && !x.IsArchived, cancellationToken);
    public Task AddTicketAsync(ImageUploadTicket ticket, CancellationToken cancellationToken) => ImageUploadTickets.AddAsync(ticket, cancellationToken).AsTask();
    public Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken) => ImageUploadTickets.SingleOrDefaultAsync(x => x.Id == ticketId && x.OwnerId == ownerId, cancellationToken);
    public Task<ExerciseImage?> FindImageAsync(Guid imageId, CancellationToken cancellationToken) => ExerciseImages.SingleOrDefaultAsync(x => x.Id == imageId, cancellationToken);
    public Task<ExerciseImage?> FindOwnedImageAsync(Guid imageId, Guid ownerId, CancellationToken cancellationToken) => ExerciseImages.SingleOrDefaultAsync(x => x.Id == imageId && x.OwnerId == ownerId && x.IsPrivate, cancellationToken);
    public async Task<StagingUploadTransition> TryMarkUploadedAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAsync(ticketId, ownerId, cancellationToken);
        if (ticket is null) return StagingUploadTransition.Rejected;
        if (ticket.State != ImageUploadState.Pending && ticket.State != ImageUploadState.Uploading)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return ticket.State == ImageUploadState.Uploaded ? StagingUploadTransition.RetainedByAnotherUpload : StagingUploadTransition.Rejected;
        }
        var now = DateTimeOffset.UtcNow;
        if (ticket.State == ImageUploadState.Pending)
            _ = ticket.TryClaimUpload(now, TimeSpan.FromMinutes(2), out _, out _);
        if (!ticket.TryMarkUploaded(ticket.UploadLeaseId ?? Guid.Empty, now))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return StagingUploadTransition.Rejected;
        }
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return StagingUploadTransition.Uploaded;
    }
    public async Task<UploadClaim> TryClaimUploadAsync(Guid ticketId, Guid ownerId, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAsync(ticketId, ownerId, cancellationToken) ?? throw new InvalidOperationException("Ticket is unavailable.");
        if (!ticket.TryClaimUpload(DateTimeOffset.UtcNow, lease, out var leaseId, out var key))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException("Upload is unavailable.");
        }
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new UploadClaim(leaseId, key, ticket.UploadLeaseExpiresAt!.Value);
    }
    public async Task<StagingUploadTransition> TryMarkUploadedAsync(Guid ticketId, Guid ownerId, Guid uploadLeaseId, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAsync(ticketId, ownerId, cancellationToken);
        if (ticket is null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return StagingUploadTransition.Rejected;
        }
        if (ticket.State != ImageUploadState.Uploading || ticket.UploadLeaseId != uploadLeaseId)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return ticket.State == ImageUploadState.Uploaded ? StagingUploadTransition.RetainedByAnotherUpload : StagingUploadTransition.Rejected;
        }
        if (!ticket.TryMarkUploaded(uploadLeaseId, DateTimeOffset.UtcNow))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return StagingUploadTransition.Rejected;
        }
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return StagingUploadTransition.Uploaded;
    }
    public async Task<ExerciseImage> CommitCompletionAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, string masterKey, string thumbnailKey, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAsync(ticketId, ownerId, cancellationToken) ?? throw new InvalidOperationException("Ticket is unavailable.");
        await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BitConverter.ToInt64(ticket.ExerciseDefinitionId.ToByteArray(), 0)})", cancellationToken);
        var exercise = await Exercises.SingleOrDefaultAsync(x => x.Id == ticket.ExerciseDefinitionId && x.OwnerId == ownerId && !x.IsArchived, cancellationToken)
            ?? throw new InvalidOperationException("Exercise is unavailable.");
        if (!ticket.IsClaimHeldBy(processingLeaseId, DateTimeOffset.UtcNow)) throw new InvalidOperationException("Ticket lease is unavailable.");
        var version = (await ExerciseImages.Where(x => x.ExerciseDefinitionId == exercise.Id).MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var image = ExerciseImage.CreateCustomUpload(exercise, ownerId, masterKey, thumbnailKey, version, "validated-upload");
        var commitStarted = false;
        try
        {
            await ExerciseImages.AddAsync(image, cancellationToken);
            ticket.Complete(image.Id, processingLeaseId, DateTimeOffset.UtcNow);
            await SaveChangesAsync(cancellationToken);
            commitStarted = true;
            await transaction.CommitAsync(cancellationToken);
            return image;
        }
        catch
        {
            if (!commitStarted) await transaction.RollbackAsync(CancellationToken.None);
            ChangeTracker.Clear();
            throw;
        }
    }
    public async Task<ExerciseImage?> FindCompletedByAttemptAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, string masterKey, string thumbnailKey, CancellationToken cancellationToken)
    {
        ChangeTracker.Clear();
        return await (from ticket in ImageUploadTickets.AsNoTracking()
                      join image in ExerciseImages.AsNoTracking() on ticket.ExerciseImageId equals image.Id
                      where ticket.Id == ticketId && ticket.OwnerId == ownerId && ticket.State == ImageUploadState.Completed
                         && image.OwnerId == ownerId && image.MasterObjectKey == masterKey && image.ThumbnailObjectKey == thumbnailKey
                      select image).SingleOrDefaultAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<ImageUploadCleanupCandidate>> ListCleanupCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        return await ImageUploadTickets.AsNoTracking()
            .Where(x => x.CleanupStagingObjectKey != null || x.CleanupProcessingLeaseId != null ||
                ((x.State == ImageUploadState.Pending || x.State == ImageUploadState.Uploading || x.State == ImageUploadState.Uploaded) && x.ExpiresAt <= now) ||
                (x.State == ImageUploadState.Processing && x.LeaseExpiresAt <= now))
            .Select(x => new ImageUploadCleanupCandidate(x.Id, x.OwnerId, x.ExerciseDefinitionId, x.State,
                x.CleanupStagingObjectKey ?? (x.ExpiresAt <= now ? x.StagingObjectKey : null),
                x.CleanupProcessingLeaseId ?? (x.State == ImageUploadState.Processing && x.LeaseExpiresAt <= now ? x.ProcessingLeaseId : null)))
            .ToListAsync(cancellationToken);
    }
    public async Task MarkCleanupCompleteAsync(Guid ticketId, string? stagingKey, Guid? processingLeaseId, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAsync(ticketId, Guid.Empty, cancellationToken);
        // Lock by id without owner filtering because this is an internal worker.
        if (ticket is null)
        {
            ChangeTracker.Clear();
            ticket = await ImageUploadTickets.FromSqlInterpolated($"SELECT * FROM image_upload_tickets WHERE \"Id\" = {ticketId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        }
        if (ticket is null) { await transaction.RollbackAsync(CancellationToken.None); return; }
        ticket.MarkCleanupComplete(stagingKey, processingLeaseId);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public Task<bool> TryFailClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken) =>
        TransitionClaimAsync(ticketId, ownerId, processingLeaseId, ticket => ticket.Fail(processingLeaseId), cancellationToken);
    public Task<bool> TryReleaseClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken) =>
        TransitionClaimAsync(ticketId, ownerId, processingLeaseId, ticket => ticket.ReleaseForRetry(processingLeaseId), cancellationToken);
    public Task SaveAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    private async Task<ImageUploadTicket?> LockTicketAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken)
    {
        // The request may already have read the ticket before another request committed a
        // transition. A fresh lock read is required; EF's identity map would otherwise reuse
        // the stale entity and defeat both the state check and the fencing token.
        ChangeTracker.Clear();
        return await ImageUploadTickets.FromSqlInterpolated($"SELECT * FROM image_upload_tickets WHERE \"Id\" = {ticketId} AND \"OwnerId\" = {ownerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> TransitionClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, Func<ImageUploadTicket, bool> transition, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAsync(ticketId, ownerId, cancellationToken);
        if (ticket is null || !transition(ticket))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return false;
        }
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

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
            join image in ExerciseImages.AsNoTracking().Where(image =>
                    image.OwnerId == userId
                    && image.IsPrivate
                    && image.Source == ExerciseImageSource.UserUpload
                    && image.ReviewState == null
                    && image.RightsReference == null
                    && image.ReviewedByUserId == null
                    && image.ReviewedAt == null
                    && image.PublishedAt == null
                    && !image.AnatomyApproved
                    && !image.MovementApproved
                    && !image.RightsApproved)
                on exercise.Id equals image.ExerciseDefinitionId into images
            where !exercise.IsArchived
                && (exercise.OwnerId == null || exercise.OwnerId == userId)
                && (!bodyPart.HasValue || exercise.BodyPart == bodyPart.Value)
                && (normalizedSearch == null || exercise.NormalizedName.Contains(normalizedSearch))
                && (after == null
                    || string.Compare(exercise.Name, after.OrderingName) > 0
                    || (exercise.Name == after.OrderingName && exercise.Id.CompareTo(after.OrderingId) > 0))
            let latestImageId = images.OrderByDescending(image => image.Version).ThenByDescending(image => image.Id).Select(image => (Guid?)image.Id).FirstOrDefault()
            orderby exercise.Name, exercise.Id
            select new CatalogExerciseReadItem(
                new ExerciseSummaryDto(
                    exercise.Id,
                    exercise.Name,
                    exercise.BodyPart,
                    exercise.TrackingMode,
                    latestImageId != null
                        ? "/api/v1/media/exercise-images/" + latestImageId.Value.ToString() + "/thumbnail"
                        : null,
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
