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
using TrackZ.Application.Workouts;
using TrackZ.Domain.Workouts;
using TrackZ.Application.Sync;
using TrackZ.Domain.Sync;
using TrackZ.Application.Sync.Pull;
using System.Security.Cryptography;
using System.Text;
using TrackZ.Application.Gamification.ReconcileWorkoutXp;
using TrackZ.Domain.Gamification;
using TrackZ.Application.Gamification.EvaluateStreak;
using TrackZ.Application.Gamification.EvaluateBadges;
using TrackZ.Application.Progress.ReconcileUserProgress;

namespace TrackZ.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext, IExerciseCatalogReadStore, ICustomExerciseStore, IExerciseImageUploadStore, IWorkoutReadStore, ISyncPushStore, ISyncPullStore, IGamificationStore, IStreakStore, IBadgeStore, IUserProgressReconciliationStore
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ExerciseDefinition> Exercises => Set<ExerciseDefinition>();

    public DbSet<ExerciseImage> ExerciseImages => Set<ExerciseImage>();

    public DbSet<ExercisePerformance> ExercisePerformances => Set<ExercisePerformance>();
    public DbSet<ImageUploadTicket> ImageUploadTickets => Set<ImageUploadTicket>();

    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();

    public DbSet<WorkoutExercise> WorkoutExercises => Set<WorkoutExercise>();

    public DbSet<SetEntry> SetEntries => Set<SetEntry>();

    public DbSet<ProcessedClientOperation> ProcessedClientOperations => Set<ProcessedClientOperation>();

    public DbSet<SyncChange> SyncChanges => Set<SyncChange>();

    public DbSet<XpLedgerEntry> XpLedgerEntries => Set<XpLedgerEntry>();

    public DbSet<UserProgress> UserProgress => Set<UserProgress>();

    public DbSet<LevelThreshold> LevelThresholds => Set<LevelThreshold>();

    public DbSet<StreakState> StreakStates => Set<StreakState>();

    public DbSet<BadgeDefinition> BadgeDefinitions => Set<BadgeDefinition>();

    public DbSet<UserBadge> UserBadges => Set<UserBadge>();

    public DbSet<BadgeAuditEvent> BadgeAuditEvents => Set<BadgeAuditEvent>();

    public async Task ReconcileAsync(
        Guid userId,
        Guid workoutId,
        IReadOnlyCollection<Guid> exerciseDefinitionIds,
        DateTimeOffset reconciledAt,
        CancellationToken cancellationToken)
    {
        await AcquireSyncLockAsync($"user-progress:{userId:D}", cancellationToken);
        await RecomputeExercisePerformancesAsync(userId, exerciseDefinitionIds, cancellationToken);
        await SaveChangesAsync(cancellationToken);

        var now = reconciledAt.ToUniversalTime();
        var workoutState = await GetWorkoutXpStateAsync(workoutId, cancellationToken);
        if (workoutState is not null && workoutState.UserId == userId)
        {
            var entries = await ListWorkoutXpEntriesAsync(userId, workoutId, cancellationToken);
            var desiredXp = workoutState.IsEligible ? XpRules.ForCompletedWorkout(workoutState.ValidSetCount) : 0;
            var existingXp = entries.Sum(entry => entry.Amount);
            if (entries.Count == 0 && desiredXp > 0)
            {
                XpLedgerEntries.Add(XpLedgerEntry.Create(
                    userId, XpLedgerReason.WorkoutCompleted, workoutId, workoutId,
                    XpRules.CompletedWorkoutXp, now));
                var setXp = desiredXp - XpRules.CompletedWorkoutXp;
                if (setXp > 0)
                    XpLedgerEntries.Add(XpLedgerEntry.Create(
                        userId, XpLedgerReason.WorkoutSets,
                        DeriveReconciliationSourceId(workoutId, "sets"), workoutId, setXp, now));
            }
            else if (desiredXp != existingXp)
            {
                XpLedgerEntries.Add(XpLedgerEntry.Create(
                    userId, XpLedgerReason.Correction,
                    DeriveReconciliationSourceId(workoutId, $"correction:{workoutState.Version}:{desiredXp}"),
                    workoutId, desiredXp - existingXp, now));
            }
            await SaveChangesAsync(cancellationToken);
        }

        var streakInput = await GetStreakEvaluationInputAsync(userId, cancellationToken);
        if (streakInput is not null)
        {
            var rebuilt = WeeklyGoalCalculator.RebuildStreak(
                streakInput.WeeklyGoal,
                streakInput.TimeZoneId,
                streakInput.CompletedWorkouts,
                now);
            var streak = await FindStreakStateAsync(userId, cancellationToken);
            if (streak is null)
            {
                streak = StreakState.Create(userId);
                StreakStates.Add(streak);
            }
            streak.Recalculate(rebuilt.CurrentWeeks, rebuilt.BestWeeks, rebuilt.LastEvaluatedWeek, now);

            var weeklyEntries = await ListWeeklyGoalXpEntriesAsync(userId, cancellationToken);
            var weeklyGroups = weeklyEntries.GroupBy(entry => entry.OriginId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var desiredWeeklyOrigins = rebuilt.GoalMetWeeks.ToDictionary(
                week => DeriveReconciliationSourceId(userId, $"iso-week:{week.Year:D4}-{week.Week:D2}"));
            foreach (var originId in weeklyGroups.Keys.Concat(desiredWeeklyOrigins.Keys).Distinct().Order())
            {
                var currentXp = weeklyGroups.GetValueOrDefault(originId)?.Sum(entry => entry.Amount) ?? 0;
                var desiredXp = desiredWeeklyOrigins.ContainsKey(originId) ? XpRules.WeeklyGoalXp : 0;
                if (currentXp == desiredXp) continue;
                var hasAward = weeklyGroups.GetValueOrDefault(originId)?
                    .Any(entry => entry.Reason == XpLedgerReason.WeeklyGoal) == true;
                XpLedgerEntries.Add(XpLedgerEntry.Create(
                    userId,
                    hasAward ? XpLedgerReason.Correction : XpLedgerReason.WeeklyGoal,
                    hasAward ? Guid.NewGuid() : originId,
                    originId,
                    desiredXp - currentXp,
                    now));
            }
            await SaveChangesAsync(cancellationToken);
        }

        var totalXp = await GetUserTotalXpAsync(userId, cancellationToken);
        var thresholds = await ListLevelThresholdsAsync(cancellationToken);
        var rulesVersion = thresholds.Select(item => item.RulesVersion).DefaultIfEmpty(1).Max();
        var level = LevelThreshold.ResolveLevel(
            totalXp,
            thresholds.Where(item => item.RulesVersion == rulesVersion));
        var progress = await FindUserProgressAsync(userId, cancellationToken);
        if (progress is null)
            UserProgress.Add(TrackZ.Domain.Gamification.UserProgress.Create(userId, totalXp, level, rulesVersion, now));
        else
            progress.Recalculate(totalXp, level, rulesVersion, now);
        await SaveChangesAsync(cancellationToken);

        var facts = await GetBadgeFactsAsync(userId, cancellationToken);
        if (facts is null) return;
        var definitions = await ListBadgeDefinitionsAsync(cancellationToken);
        var badges = await ListUserBadgesAsync(userId, cancellationToken);
        var badgesByDefinition = badges.ToDictionary(item => item.BadgeDefinitionId);
        foreach (var definition in definitions.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var shouldBeEarned = definition.IsEarned(
                facts.CompletedWorkoutCount,
                facts.BestStreakWeeks,
                facts.DistinctExerciseCount,
                facts.PersonalRecordCount);
            var isEarned = badgesByDefinition.TryGetValue(definition.Id, out var badge);
            if (shouldBeEarned && !isEarned)
            {
                UserBadges.Add(UserBadge.Create(userId, definition, now));
                BadgeAuditEvents.Add(BadgeAuditEvent.Create(
                    userId, definition, BadgeAuditAction.Awarded, now));
            }
            else if (!shouldBeEarned && isEarned)
            {
                UserBadges.Remove(badge!);
                BadgeAuditEvents.Add(BadgeAuditEvent.Create(
                    userId, definition, BadgeAuditAction.Revoked, now));
            }
        }
        await SaveChangesAsync(cancellationToken);
    }

    private static Guid DeriveReconciliationSourceId(Guid originId, string discriminator)
    {
        var bytes = Encoding.UTF8.GetBytes($"{originId:D}:{discriminator}");
        return new Guid(SHA256.HashData(bytes).AsSpan(0, 16));
    }

    public async Task<IAppDbTransaction> BeginBadgeTransactionAsync(CancellationToken cancellationToken) =>
        new AppDbTransaction(await Database.BeginTransactionAsync(cancellationToken));

    public Task AcquireBadgeLockAsync(Guid userId, CancellationToken cancellationToken) =>
        AcquireSyncLockAsync($"user-badges:{userId:D}", cancellationToken);

    public async Task<BadgeFacts?> GetBadgeFactsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken)) return null;
        var completedWorkoutCount = await WorkoutSessions.AsNoTracking().CountAsync(workout =>
            workout.OwnerId == userId
            && workout.Status == WorkoutStatus.Completed
            && workout.DeletedAt == null,
            cancellationToken);
        var bestStreakWeeks = await StreakStates.AsNoTracking()
            .Where(state => state.UserId == userId)
            .Select(state => (int?)state.BestWeeks)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        var distinctExerciseCount = await (
            from workout in WorkoutSessions.AsNoTracking()
            join exercise in WorkoutExercises.AsNoTracking() on workout.Id equals exercise.WorkoutSessionId
            join set in SetEntries.AsNoTracking() on exercise.Id equals set.WorkoutExerciseId
            where workout.OwnerId == userId
                && workout.Status == WorkoutStatus.Completed
                && workout.DeletedAt == null
                && exercise.DeletedAt == null
                && set.DeletedAt == null
            select exercise.ExerciseDefinitionId)
            .Distinct()
            .CountAsync(cancellationToken);
        var personalRecordCount = await ExercisePerformances.AsNoTracking()
            .CountAsync(performance => performance.UserId == userId, cancellationToken);
        return new BadgeFacts(
            completedWorkoutCount,
            bestStreakWeeks,
            distinctExerciseCount,
            personalRecordCount);
    }

    public async Task<IReadOnlyList<BadgeDefinition>> ListBadgeDefinitionsAsync(CancellationToken cancellationToken) =>
        await BadgeDefinitions.AsNoTracking().OrderBy(definition => definition.Key).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserBadge>> ListUserBadgesAsync(Guid userId, CancellationToken cancellationToken) =>
        await UserBadges.Where(badge => badge.UserId == userId).ToListAsync(cancellationToken);

    public void AddUserBadge(UserBadge badge) => UserBadges.Add(badge);

    public void RemoveUserBadge(UserBadge badge) => UserBadges.Remove(badge);

    public void AddBadgeAuditEvent(BadgeAuditEvent auditEvent) => BadgeAuditEvents.Add(auditEvent);

    public Task SaveBadgesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public async Task<IAppDbTransaction> BeginStreakTransactionAsync(CancellationToken cancellationToken) =>
        new AppDbTransaction(await Database.BeginTransactionAsync(cancellationToken));

    public async Task<StreakEvaluationInput?> GetStreakEvaluationInputAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preferences = await Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.WeeklyWorkoutGoal, user.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken);
        if (preferences is null) return null;

        var completions = await WorkoutSessions.AsNoTracking()
            .Where(workout => workout.OwnerId == userId
                && workout.Status == WorkoutStatus.Completed
                && workout.CompletedAt != null
                && workout.DeletedAt == null)
            .Select(workout => new CompletedWorkoutInstant(workout.Id, workout.CompletedAt!.Value))
            .ToListAsync(cancellationToken);
        return new StreakEvaluationInput(
            userId,
            preferences.WeeklyWorkoutGoal,
            preferences.TimeZoneId,
            completions);
    }

    public Task<StreakState?> FindStreakStateAsync(Guid userId, CancellationToken cancellationToken) =>
        StreakStates.SingleOrDefaultAsync(state => state.UserId == userId, cancellationToken);

    public void AddStreakState(StreakState state) => StreakStates.Add(state);

    public async Task<IReadOnlyList<XpLedgerEntry>> ListWeeklyGoalXpEntriesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entries = await XpLedgerEntries.AsNoTracking()
            .Where(entry => entry.UserId == userId
                && (entry.Reason == XpLedgerReason.WeeklyGoal || entry.Reason == XpLedgerReason.Correction))
            .ToListAsync(cancellationToken);
        var weeklyOrigins = entries
            .Where(entry => entry.Reason == XpLedgerReason.WeeklyGoal)
            .Select(entry => entry.OriginId)
            .ToHashSet();
        return entries.Where(entry => weeklyOrigins.Contains(entry.OriginId)).ToArray();
    }

    public Task SaveStreakAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public async Task<IAppDbTransaction> BeginGamificationTransactionAsync(CancellationToken cancellationToken) =>
        new AppDbTransaction(await Database.BeginTransactionAsync(cancellationToken));

    public Task AcquireWorkoutXpLockAsync(Guid workoutId, CancellationToken cancellationToken) =>
        AcquireSyncLockAsync($"workout-xp:{workoutId:D}", cancellationToken);

    public Task AcquireUserProgressLockAsync(Guid userId, CancellationToken cancellationToken) =>
        AcquireSyncLockAsync($"user-progress:{userId:D}", cancellationToken);

    public async Task<WorkoutXpState?> GetWorkoutXpStateAsync(
        Guid workoutId,
        CancellationToken cancellationToken)
    {
        var workout = await WorkoutSessions.AsNoTracking()
            .Where(item => item.Id == workoutId)
            .Select(item => new
            {
                item.OwnerId,
                item.Id,
                item.Version,
                item.Status,
                item.DeletedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (workout is null) return null;

        var validSetCount = await (
            from exercise in WorkoutExercises.AsNoTracking()
            join set in SetEntries.AsNoTracking() on exercise.Id equals set.WorkoutExerciseId
            where exercise.WorkoutSessionId == workoutId
                && exercise.DeletedAt == null
                && set.DeletedAt == null
            select set.Id)
            .CountAsync(cancellationToken);
        return new WorkoutXpState(
            workout.OwnerId,
            workout.Id,
            workout.Version,
            workout.Status == WorkoutStatus.Completed && workout.DeletedAt == null,
            validSetCount);
    }

    public async Task<IReadOnlyList<XpLedgerEntry>> ListWorkoutXpEntriesAsync(
        Guid userId,
        Guid workoutId,
        CancellationToken cancellationToken) =>
        await XpLedgerEntries.AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.OriginId == workoutId)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

    public async Task<int> GetUserTotalXpAsync(Guid userId, CancellationToken cancellationToken) =>
        await XpLedgerEntries.AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .SumAsync(entry => (int?)entry.Amount, cancellationToken) ?? 0;

    public async Task<IReadOnlyList<LevelThreshold>> ListLevelThresholdsAsync(CancellationToken cancellationToken) =>
        await LevelThresholds.AsNoTracking()
            .OrderBy(threshold => threshold.RulesVersion)
            .ThenBy(threshold => threshold.Level)
            .ToListAsync(cancellationToken);

    public Task<UserProgress?> FindUserProgressAsync(Guid userId, CancellationToken cancellationToken) =>
        UserProgress.SingleOrDefaultAsync(progress => progress.UserId == userId, cancellationToken);

    public void AddXpLedgerEntry(XpLedgerEntry entry) => XpLedgerEntries.Add(entry);

    public void AddUserProgress(UserProgress progress) => UserProgress.Add(progress);

    public Task SaveGamificationAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public async Task<IAppDbTransaction> BeginSyncTransactionAsync(CancellationToken cancellationToken) =>
        new AppDbTransaction(await Database.BeginTransactionAsync(cancellationToken));

    public Task AcquireOperationLockAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken) =>
        AcquireSyncLockAsync($"operation:{userId:D}:{operationId:D}", cancellationToken);

    public Task AcquireWorkoutLockAsync(Guid workoutId, CancellationToken cancellationToken) =>
        AcquireSyncLockAsync($"workout:{workoutId:D}", cancellationToken);

    public Task<ProcessedClientOperation?> FindProcessedOperationAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken) =>
        ProcessedClientOperations.SingleOrDefaultAsync(
            operation => operation.UserId == userId && operation.OperationId == operationId,
            cancellationToken);

    public Task<WorkoutSession?> FindOwnedWorkoutAsync(
        Guid userId,
        Guid workoutId,
        CancellationToken cancellationToken) =>
        WorkoutSessions
            .Include("_exercises._sets")
            .SingleOrDefaultAsync(
                workout => workout.Id == workoutId && workout.OwnerId == userId,
                cancellationToken);

    public Task<Guid?> FindWorkoutOwnerAsync(Guid workoutId, CancellationToken cancellationToken) =>
        WorkoutSessions.AsNoTracking()
            .Where(workout => workout.Id == workoutId)
            .Select(workout => (Guid?)workout.OwnerId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, TrackingMode>> GetAvailableExerciseTrackingModesAsync(
        Guid userId,
        IReadOnlyList<Guid> exerciseDefinitionIds,
        CancellationToken cancellationToken)
    {
        if (exerciseDefinitionIds.Count == 0
            || exerciseDefinitionIds.Any(id => id == Guid.Empty)
            || exerciseDefinitionIds.Distinct().Count() != exerciseDefinitionIds.Count)
        {
            return new Dictionary<Guid, TrackingMode>();
        }

        return await Exercises.AsNoTracking()
            .Where(exercise => exerciseDefinitionIds.Contains(exercise.Id)
                && !exercise.IsArchived
                && (exercise.OwnerId == null || exercise.OwnerId == userId))
            .ToDictionaryAsync(exercise => exercise.Id, exercise => exercise.TrackingMode, cancellationToken);
    }

    public async Task<bool> AreWorkoutExerciseIdentifiersAvailableAsync(
        IReadOnlyList<Guid> workoutExerciseIds,
        CancellationToken cancellationToken)
    {
        if (workoutExerciseIds.Count == 0
            || workoutExerciseIds.Any(id => id == Guid.Empty)
            || workoutExerciseIds.Distinct().Count() != workoutExerciseIds.Count)
        {
            return false;
        }

        foreach (var workoutExerciseId in workoutExerciseIds.Order())
        {
            await AcquireSyncLockAsync($"workout-exercise:{workoutExerciseId:D}", cancellationToken);
        }

        return !await WorkoutExercises.AsNoTracking()
            .AnyAsync(exercise => workoutExerciseIds.Contains(exercise.Id), cancellationToken);
    }

    public async Task<bool> IsSetIdentifierAvailableAsync(Guid setId, CancellationToken cancellationToken)
    {
        await AcquireSyncLockAsync($"set:{setId:D}", cancellationToken);
        return !await SetEntries.AsNoTracking().AnyAsync(set => set.Id == setId, cancellationToken);
    }

    public void AddWorkout(WorkoutSession workout) => WorkoutSessions.Add(workout);

    public void AddProcessedOperation(ProcessedClientOperation operation) =>
        ProcessedClientOperations.Add(operation);

    public void AddSyncChange(SyncChange change) => SyncChanges.Add(change);

    public async Task RecomputeExercisePerformancesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> exerciseDefinitionIds,
        CancellationToken cancellationToken)
    {
        var affectedIds = exerciseDefinitionIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Order()
            .ToArray();
        if (affectedIds.Length == 0) return;

        foreach (var exerciseDefinitionId in affectedIds)
        {
            await AcquireSyncLockAsync(
                $"exercise-performance:{userId:D}:{exerciseDefinitionId:D}",
                cancellationToken);
        }

        var modes = await Exercises.AsNoTracking()
            .Where(exercise => affectedIds.Contains(exercise.Id)
                && (exercise.OwnerId == null || exercise.OwnerId == userId))
            .ToDictionaryAsync(
                exercise => exercise.Id,
                exercise => exercise.TrackingMode,
                cancellationToken);
        var rows = await (
            from workout in WorkoutSessions.AsNoTracking()
            join exercise in WorkoutExercises.AsNoTracking()
                on workout.Id equals exercise.WorkoutSessionId
            join set in SetEntries.AsNoTracking()
                on exercise.Id equals set.WorkoutExerciseId
            where workout.OwnerId == userId
                && workout.Status == WorkoutStatus.Completed
                && workout.CompletedAt != null
                && workout.DeletedAt == null
                && affectedIds.Contains(exercise.ExerciseDefinitionId)
                && exercise.DeletedAt == null
                && set.DeletedAt == null
            select new ExercisePerformanceRow(
                exercise.ExerciseDefinitionId,
                exercise.TrackingMode,
                workout.Id,
                workout.CompletedAt!.Value,
                set.Id,
                set.Order,
                set.WeightKg,
                set.AssistedKg,
                set.Reps))
            .ToListAsync(cancellationToken);

        foreach (var exerciseDefinitionId in affectedIds)
        {
            var existing = await ExercisePerformances.SingleOrDefaultAsync(
                performance => performance.UserId == userId
                    && performance.ExerciseDefinitionId == exerciseDefinitionId,
                cancellationToken);
            if (!modes.TryGetValue(exerciseDefinitionId, out var trackingMode))
            {
                if (existing is not null) ExercisePerformances.Remove(existing);
                continue;
            }

            var validRows = rows.Where(row =>
                row.ExerciseDefinitionId == exerciseDefinitionId
                && row.TrackingMode == trackingMode).ToArray();
            if (validRows.Length == 0)
            {
                if (existing is not null) ExercisePerformances.Remove(existing);
                continue;
            }

            var snapshot = PerformanceCalculator.Calculate(
                trackingMode,
                validRows.Select(row => new ExercisePerformanceSample(
                    row.WorkoutId,
                    row.CompletedAt,
                    row.SetId,
                    row.Order,
                    new ExercisePerformanceSet(row.WeightKg, row.AssistedKg, row.Reps))))!;
            if (existing is null)
            {
                ExercisePerformances.Add(ExercisePerformance.Create(
                    userId,
                    exerciseDefinitionId,
                    trackingMode,
                    snapshot.LastPerformedAt,
                    snapshot.LastBestSet,
                    snapshot.AllTimeBest));
            }
            else
            {
                existing.Recalculate(
                    trackingMode,
                    snapshot.LastPerformedAt,
                    snapshot.LastBestSet,
                    snapshot.AllTimeBest);
            }
        }
    }

    public async Task<IReadOnlyList<SyncChange>> ReadChangesAsync(
        Guid ownerId,
        long afterSequence,
        int take,
        CancellationToken cancellationToken) => await SyncChanges.AsNoTracking()
        .Where(change => change.OwnerId == ownerId && change.Sequence > afterSequence)
        .OrderBy(change => change.Sequence)
        .Take(take)
        .ToListAsync(cancellationToken);

    public Task SaveSyncChangesAsync(CancellationToken cancellationToken) =>
        SaveChangesAsync(cancellationToken);

    public bool IsTransient(Exception exception) => exception switch
    {
        TimeoutException => true,
        NpgsqlException postgres => postgres.IsTransient,
        DbUpdateException update when update.InnerException is not null => IsTransient(update.InnerException),
        _ when exception.InnerException is not null => IsTransient(exception.InnerException),
        _ => false
    };

    public void ClearSyncTracking() => ChangeTracker.Clear();

    private Task AcquireSyncLockAsync(string resource, CancellationToken cancellationToken)
    {
        var lockKey = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(resource)), 0);
        return Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
    }

    private sealed record ExercisePerformanceRow(
        Guid ExerciseDefinitionId,
        TrackingMode TrackingMode,
        Guid WorkoutId,
        DateTimeOffset CompletedAt,
        Guid SetId,
        int Order,
        decimal? WeightKg,
        decimal? AssistedKg,
        int Reps);

    public async Task<WorkoutReadSession?> GetOwnedWorkoutAsync(
        Guid ownerId,
        Guid workoutId,
        CancellationToken cancellationToken)
    {
        var workout = await WorkoutSessions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == workoutId && item.OwnerId == ownerId && item.DeletedAt == null,
            cancellationToken);
        return workout is null
            ? null
            : (await LoadWorkoutReadSessionsAsync([workout], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<WorkoutReadSession>> ListOwnedCompletedWorkoutsAsync(
        Guid ownerId,
        WorkoutCursor? after,
        int take,
        CancellationToken cancellationToken)
    {
        var query = WorkoutSessions.AsNoTracking().Where(workout =>
            workout.OwnerId == ownerId
            && workout.Status == WorkoutStatus.Completed
            && workout.CompletedAt != null
            && workout.DeletedAt == null);
        if (after is not null)
        {
            query = query.Where(workout =>
                workout.CompletedAt < after.CompletedAt
                || workout.CompletedAt == after.CompletedAt && workout.Id.CompareTo(after.WorkoutId) < 0);
        }

        var workouts = await query
            .OrderByDescending(workout => workout.CompletedAt)
            .ThenByDescending(workout => workout.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return await LoadWorkoutReadSessionsAsync(workouts, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkoutReadSession>> ListOwnedExerciseHistoryAsync(
        Guid ownerId,
        Guid exerciseDefinitionId,
        WorkoutCursor? after,
        int take,
        CancellationToken cancellationToken)
    {
        var query =
            from workout in WorkoutSessions.AsNoTracking()
            join exercise in WorkoutExercises.AsNoTracking() on workout.Id equals exercise.WorkoutSessionId
            where workout.OwnerId == ownerId
                && workout.Status == WorkoutStatus.Completed
                && workout.CompletedAt != null
                && workout.DeletedAt == null
                && exercise.ExerciseDefinitionId == exerciseDefinitionId
                && exercise.DeletedAt == null
                && SetEntries.Any(set => set.WorkoutExerciseId == exercise.Id && set.DeletedAt == null)
            select workout;
        if (after is not null)
        {
            query = query.Where(workout =>
                workout.CompletedAt < after.CompletedAt
                || workout.CompletedAt == after.CompletedAt && workout.Id.CompareTo(after.WorkoutId) < 0);
        }

        var workouts = await query
            .OrderByDescending(workout => workout.CompletedAt)
            .ThenByDescending(workout => workout.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return await LoadWorkoutReadSessionsAsync(workouts, cancellationToken, exerciseDefinitionId);
    }

    private async Task<IReadOnlyList<WorkoutReadSession>> LoadWorkoutReadSessionsAsync(
        IReadOnlyList<WorkoutSession> workouts,
        CancellationToken cancellationToken,
        Guid? onlyExerciseDefinitionId = null)
    {
        if (workouts.Count == 0) return [];
        var workoutIds = workouts.Select(workout => workout.Id).ToArray();
        var exerciseQuery = WorkoutExercises.AsNoTracking().Where(exercise =>
            workoutIds.Contains(exercise.WorkoutSessionId)
            && exercise.DeletedAt == null);
        if (onlyExerciseDefinitionId is not null)
        {
            exerciseQuery = exerciseQuery.Where(exercise => exercise.ExerciseDefinitionId == onlyExerciseDefinitionId);
        }

        var workoutExercises = await exerciseQuery.OrderBy(exercise => exercise.Order).ToListAsync(cancellationToken);
        var workoutExerciseIds = workoutExercises.Select(exercise => exercise.Id).ToArray();
        var sets = await SetEntries.AsNoTracking()
            .Where(set => workoutExerciseIds.Contains(set.WorkoutExerciseId) && set.DeletedAt == null)
            .OrderBy(set => set.Order)
            .ToListAsync(cancellationToken);
        var definitionIds = workoutExercises.Select(exercise => exercise.ExerciseDefinitionId).Distinct().ToArray();
        var names = await Exercises.AsNoTracking()
            .Where(exercise => definitionIds.Contains(exercise.Id))
            .ToDictionaryAsync(exercise => exercise.Id, exercise => exercise.Name, cancellationToken);

        return workouts.Select(workout => new WorkoutReadSession(
            workout.Id,
            workout.OwnerId,
            workout.Status,
            workout.StartedAt,
            workout.CompletedAt,
            workout.Version,
            workoutExercises
                .Where(exercise => exercise.WorkoutSessionId == workout.Id)
                .OrderBy(exercise => exercise.Order)
                .Select(exercise => new WorkoutExerciseReadRow(
                    exercise.Id,
                    exercise.ExerciseDefinitionId,
                    names[exercise.ExerciseDefinitionId],
                    exercise.TrackingMode,
                    exercise.Order,
                    sets.Where(set => set.WorkoutExerciseId == exercise.Id)
                        .OrderBy(set => set.Order)
                        .Select(set => new WorkoutSetReadRow(
                            set.Id,
                            set.Order,
                            set.WeightKg,
                            set.AssistedKg,
                            set.Reps,
                            set.CompletedAt,
                            set.UpdatedAt,
                            set.Effort))
                        .ToList()))
                .ToList()))
            .ToList();
    }

    public Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken) => Exercises.SingleOrDefaultAsync(x => x.Id == exerciseId && x.OwnerId == ownerId && !x.IsArchived, cancellationToken);
    public Task AddTicketAsync(ImageUploadTicket ticket, CancellationToken cancellationToken) => ImageUploadTickets.AddAsync(ticket, cancellationToken).AsTask();
    public Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken) => ImageUploadTickets.SingleOrDefaultAsync(x => x.Id == ticketId && x.OwnerId == ownerId, cancellationToken);
    public Task<ImageUploadTicket?> FindOwnedTicketSnapshotAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken) =>
        ImageUploadTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId && x.OwnerId == ownerId, cancellationToken);
    public Task<ExerciseImage?> FindImageAsync(Guid imageId, CancellationToken cancellationToken) => ExerciseImages.SingleOrDefaultAsync(x => x.Id == imageId, cancellationToken);
    public Task<ExerciseImage?> FindOwnedImageAsync(Guid imageId, Guid ownerId, CancellationToken cancellationToken) => ExerciseImages.SingleOrDefaultAsync(x => x.Id == imageId && x.OwnerId == ownerId && x.IsPrivate, cancellationToken);
    public Task<ExerciseImage?> FindReadableImageAsync(Guid imageId, Guid ownerId, CancellationToken cancellationToken) =>
        ExerciseImages.SingleOrDefaultAsync(image => image.Id == imageId && (
            (image.OwnerId == ownerId && image.IsPrivate && image.Source == ExerciseImageSource.UserUpload)
            || (image.OwnerId == null
                && !image.IsPrivate
                && image.Source == ExerciseImageSource.SystemArtwork
                && image.ReviewState == ExerciseImageReviewState.Published
                && image.AnatomyApproved
                && image.MovementApproved
                && image.RightsApproved
                && image.ReviewedByUserId != null
                && image.ReviewedAt != null
                && image.ReviewedAt >= image.CreatedAt
                && image.RightsReference != null && image.RightsReference != ""
                && image.PublishedAt != null
                && image.PublishedAt >= image.ReviewedAt)), cancellationToken);
    public Task<ExerciseImage?> FindSignedReadableImageAsync(Guid imageId, CancellationToken cancellationToken) =>
        ExerciseImages.SingleOrDefaultAsync(image => image.Id == imageId && (
            (image.OwnerId != null
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
            || (image.OwnerId == null
                && !image.IsPrivate
                && image.Source == ExerciseImageSource.SystemArtwork
                && image.ReviewState == ExerciseImageReviewState.Published
                && image.AnatomyApproved
                && image.MovementApproved
                && image.RightsApproved
                && image.ReviewedByUserId != null
                && image.ReviewedAt != null
                && image.ReviewedAt >= image.CreatedAt
                && image.RightsReference != null && image.RightsReference != ""
                && image.PublishedAt != null
                && image.PublishedAt >= image.ReviewedAt)), cancellationToken);
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
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new UploadTransitionCommitAmbiguousException(exception);
        }
        return StagingUploadTransition.Uploaded;
    }
    public async Task<UploadClaim> TryClaimUploadAsync(Guid ticketId, Guid ownerId, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAsync(ticketId, ownerId, cancellationToken) ?? throw new InvalidOperationException("Ticket is unavailable.");
        var originalConcurrencyToken = ticket.ConcurrencyToken;
        if (!ticket.TryClaimUpload(DateTimeOffset.UtcNow, lease, out var leaseId, out var key))
        {
            // An expired Uploading lease transitions back to Pending while scheduling its
            // exact staging key for cleanup. That rejection is stateful and must survive the
            // failed reclaim; otherwise the object is orphaned and a later claim overwrites it.
            if (!ReferenceEquals(originalConcurrencyToken, ticket.ConcurrencyToken))
            {
                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
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
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new UploadTransitionCommitAmbiguousException(exception);
        }
        return StagingUploadTransition.Uploaded;
    }
    public async Task<bool> IsAcceptedUploadAttemptDurableAsync(
        Guid ticketId,
        Guid ownerId,
        string stagingObjectKey,
        string contentType,
        long length,
        CancellationToken cancellationToken)
    {
        ChangeTracker.Clear();
        return await ImageUploadTickets.AsNoTracking().AnyAsync(ticket =>
            ticket.Id == ticketId
            && ticket.OwnerId == ownerId
            && (ticket.State == ImageUploadState.Uploaded
                || ticket.State == ImageUploadState.Processing
                || ticket.State == ImageUploadState.Completed)
            && ticket.StagingObjectKey == stagingObjectKey
            && ticket.DeclaredContentType == contentType
            && ticket.DeclaredLength == length
            && ticket.UploadLeaseId == null
            && ticket.UploadLeaseExpiresAt == null,
            cancellationToken);
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
        var staleBefore = now.AddMinutes(-5);
        return await ImageUploadTickets.AsNoTracking()
            .Where(x => (x.CleanupClaimId == null || x.CleanupClaimedAt <= staleBefore) && (x.CleanupStagingObjectKey != null || x.CleanupProcessingLeaseId != null ||
                ((x.State == ImageUploadState.Pending || x.State == ImageUploadState.Uploading || x.State == ImageUploadState.Uploaded) && x.ExpiresAt <= now) ||
                (x.State == ImageUploadState.Processing && x.LeaseExpiresAt <= now)))
            .Select(x => new ImageUploadCleanupCandidate(x.Id, x.OwnerId, x.ExerciseDefinitionId, x.State,
                x.CleanupStagingObjectKey ?? (x.ExpiresAt <= now ? x.StagingObjectKey : null),
                x.CleanupProcessingLeaseId ?? (x.State == ImageUploadState.Processing && x.LeaseExpiresAt <= now ? x.ProcessingLeaseId : null)))
            .ToListAsync(cancellationToken);
    }
    public async Task<ImageUploadCleanupCandidate?> TryClaimCleanupAsync(ImageUploadCleanupCandidate candidate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAnyAsync(candidate.TicketId, cancellationToken);
        if (ticket is null || !ticket.TryClaimCleanup(now, candidate.StagingKey, candidate.ProcessingLeaseId, out var claimId))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return null;
        }
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ImageUploadCleanupCandidate(ticket.Id, ticket.OwnerId, ticket.ExerciseDefinitionId, ticket.State, ticket.CleanupStagingObjectKey, ticket.CleanupProcessingLeaseId, claimId);
    }
    public async Task<bool> CompleteCleanupClaimAsync(Guid ticketId, Guid cleanupClaimId, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAnyAsync(ticketId, cancellationToken);
        if (ticket is null || !ticket.CompleteCleanupClaim(cleanupClaimId)) { await transaction.RollbackAsync(CancellationToken.None); return false; }
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    public async Task ReleaseCleanupClaimAsync(Guid ticketId, Guid cleanupClaimId, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAnyAsync(ticketId, cancellationToken);
        if (ticket is null || !ticket.ReleaseCleanupClaim(cleanupClaimId)) { await transaction.RollbackAsync(CancellationToken.None); return; }
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task MarkCleanupCompleteAsync(Guid ticketId, string? stagingKey, Guid? processingLeaseId, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var ticket = await LockTicketAnyAsync(ticketId, cancellationToken);
        if (ticket is null || ticket.CleanupClaimId is not null) { await transaction.RollbackAsync(CancellationToken.None); return; }
        ticket.MarkCleanupComplete(stagingKey, processingLeaseId);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public Task<bool> TryFailClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken) =>
        TransitionClaimAsync(ticketId, ownerId, processingLeaseId, ticket => ticket.Fail(processingLeaseId), cancellationToken);
    public Task<bool> TryReleaseClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken, bool retainAttemptForCleanup = false) =>
        TransitionClaimAsync(ticketId, ownerId, processingLeaseId, ticket => ticket.ReleaseForRetry(processingLeaseId, retainAttemptForCleanup), cancellationToken);
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
    private async Task<ImageUploadTicket?> LockTicketAnyAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        ChangeTracker.Clear();
        return await ImageUploadTickets.FromSqlInterpolated($"SELECT * FROM image_upload_tickets WHERE \"Id\" = {ticketId} FOR UPDATE")
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

    public Task<ExerciseDefinition?> FindCustomByOperationAsync(
        Guid ownerId,
        Guid operationId,
        CancellationToken cancellationToken) =>
        Exercises.AsNoTracking().SingleOrDefaultAsync(
            exercise => exercise.OwnerId == ownerId && exercise.ClientOperationId == operationId,
            cancellationToken);

    public Task<ExerciseDefinition?> FindAnyExerciseByIdAsync(
        Guid exerciseId,
        CancellationToken cancellationToken) =>
        Exercises.AsNoTracking().SingleOrDefaultAsync(
            exercise => exercise.Id == exerciseId,
            cancellationToken);

    public Task<ExerciseImage?> FindPublishedLibraryImageAsync(Guid imageId, CancellationToken cancellationToken) =>
        ExerciseImages.AsNoTracking().SingleOrDefaultAsync(image =>
            image.Id == imageId
            && image.Source == ExerciseImageSource.SystemArtwork
            && !image.IsPrivate
            && image.OwnerId == null
            && image.ReviewState == ExerciseImageReviewState.Published
            && image.AnatomyApproved
            && image.MovementApproved
            && image.RightsApproved
            && image.ReviewedByUserId != null
            && image.ReviewedAt != null
            && image.ReviewedAt >= image.CreatedAt
            && image.RightsReference != null && image.RightsReference != ""
            && image.PublishedAt != null
            && image.PublishedAt >= image.ReviewedAt,
            cancellationToken);

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
            let publishedSystemImageId = ExerciseImages.AsNoTracking()
                .Where(image => image.ExerciseDefinitionId == exercise.Id
                    && image.OwnerId == null
                    && !image.IsPrivate
                    && image.Source == ExerciseImageSource.SystemArtwork
                    && image.ReviewState == ExerciseImageReviewState.Published
                    && image.AnatomyApproved && image.MovementApproved && image.RightsApproved
                    && image.ReviewedByUserId != null && image.ReviewedAt != null
                    && image.ReviewedAt >= image.CreatedAt
                    && image.RightsReference != null && image.RightsReference != ""
                    && image.PublishedAt != null && image.PublishedAt >= image.ReviewedAt)
                .OrderByDescending(image => image.Version).ThenByDescending(image => image.Id)
                .Select(image => (Guid?)image.Id).FirstOrDefault()
            let selectedLibraryImageId = ExerciseImages.AsNoTracking()
                .Where(image => image.Id == exercise.LibraryImageId
                    && image.OwnerId == null
                    && !image.IsPrivate
                    && image.Source == ExerciseImageSource.SystemArtwork
                    && image.ReviewState == ExerciseImageReviewState.Published
                    && image.AnatomyApproved && image.MovementApproved && image.RightsApproved
                    && image.ReviewedByUserId != null && image.ReviewedAt != null
                    && image.ReviewedAt >= image.CreatedAt
                    && image.RightsReference != null && image.RightsReference != ""
                    && image.PublishedAt != null && image.PublishedAt >= image.ReviewedAt)
                .Select(image => (Guid?)image.Id).FirstOrDefault()
            let displayImageId = exercise.OwnerId != null && selectedLibraryImageId != null
                ? selectedLibraryImageId
                : latestImageId ?? publishedSystemImageId
            orderby exercise.Name, exercise.Id
            select new CatalogExerciseReadItem(
                new ExerciseSummaryDto(
                    exercise.Id,
                    exercise.Name,
                    exercise.BodyPart,
                    exercise.TrackingMode,
                    displayImageId != null
                        ? "/api/v1/media/exercise-images/" + displayImageId.Value.ToString() + "/thumbnail"
                        : null,
                    performance == null ? null : performance.LastPerformedAt,
                    performance == null || performance.TrackingMode != exercise.TrackingMode || performance.LastBestReps == null
                        ? null
                        : new PerformanceSetDto(
                            exercise.TrackingMode == TrackingMode.Weighted ? performance.LastBestWeightKg : null,
                            exercise.TrackingMode == TrackingMode.Assisted ? performance.LastBestAssistedKg : null,
                            performance.LastBestReps.GetValueOrDefault()),
                    performance == null || performance.TrackingMode != exercise.TrackingMode || performance.AllTimeBestReps == null
                        ? null
                        : new PerformanceSetDto(
                            exercise.TrackingMode == TrackingMode.Weighted ? performance.AllTimeBestWeightKg : null,
                            exercise.TrackingMode == TrackingMode.Assisted ? performance.AllTimeBestAssistedKg : null,
                            performance.AllTimeBestReps.GetValueOrDefault()),
                    exercise.OwnerId != null,
                    exercise.OwnerId == null ? publishedSystemImageId : selectedLibraryImageId),
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
        catch (DbUpdateException exception) when (
            IsActiveCustomNameUniqueConstraintViolation(exception)
            || IsCustomOperationUniqueConstraintViolation(exception)
            || IsExerciseIdentifierUniqueConstraintViolation(exception))
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

    private static bool IsCustomOperationUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_exercise_definitions_OwnerId_ClientOperationId"
        };

    private static bool IsExerciseIdentifierUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "PK_exercise_definitions"
        };

    private sealed class AppDbTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        : IAppDbTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
