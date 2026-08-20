using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Application.Gamification.UpdatePreferences;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Gamification;
using TrackZ.Domain.Workouts;
using TrackZ.Infrastructure.Persistence;

namespace TrackZ.Infrastructure.Progress;

public sealed class ProgressReadStore(AppDbContext database, TimeProvider timeProvider) :
    IProgressReadStore,
    IMotivationPreferenceStore
{
    public async Task UpdateAsync(
        Guid userId,
        int weeklyGoal,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        var user = await database.Users.SingleAsync(item => item.Id == userId, cancellationToken);
        user.UpdateMotivationPreferences(weeklyGoal, timeZoneId);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProgressSummaryDto> GetProgressSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preferences = await GetPreferencesAsync(userId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var completedWorkouts = await ListCompletedWorkoutsAsync(userId, cancellationToken);
        var weightedSets = await (
            from workout in database.WorkoutSessions.AsNoTracking()
            join exercise in database.WorkoutExercises.AsNoTracking() on workout.Id equals exercise.WorkoutSessionId
            join set in database.SetEntries.AsNoTracking() on exercise.Id equals set.WorkoutExerciseId
            where workout.OwnerId == userId
                && workout.Status == WorkoutStatus.Completed
                && workout.CompletedAt != null
                && workout.DeletedAt == null
                && exercise.DeletedAt == null
                && exercise.TrackingMode == TrackingMode.Weighted
                && set.DeletedAt == null
                && set.WeightKg != null
            select new
            {
                WorkoutId = workout.Id,
                CompletedAt = workout.CompletedAt!.Value,
                VolumeKg = set.WeightKg!.Value * set.Reps
            })
            .ToListAsync(cancellationToken);

        var totalVolume = weightedSets.Sum(item => item.VolumeKg);
        var currentWeekWorkoutIds = completedWorkouts
            .Where(completion => WeeklyGoalCalculator.Evaluate(
                1,
                preferences.TimeZoneId,
                [completion],
                now).CurrentWeekGoalMet)
            .Select(completion => completion.WorkoutId)
            .ToHashSet();
        var weeklyVolume = weightedSets
            .Where(item => currentWeekWorkoutIds.Contains(item.WorkoutId))
            .Sum(item => item.VolumeKg);

        var records = await (
            from performance in database.ExercisePerformances.AsNoTracking()
            join exercise in database.Exercises.AsNoTracking()
                on performance.ExerciseDefinitionId equals exercise.Id
            where performance.UserId == userId && performance.LastPerformedAt != null
            orderby performance.LastPerformedAt descending, performance.ExerciseDefinitionId
            select new ExerciseProgressSummaryDto(
                performance.ExerciseDefinitionId,
                exercise.Name,
                performance.TrackingMode,
                performance.LastPerformedAt!.Value,
                performance.LastBestWeightKg,
                performance.LastBestAssistedKg,
                performance.LastBestReps ?? 0,
                performance.AllTimeBestWeightKg,
                performance.AllTimeBestAssistedKg,
                performance.AllTimeBestReps ?? 0))
            .ToListAsync(cancellationToken);

        return new ProgressSummaryDto(
            totalVolume,
            weeklyVolume,
            completedWorkouts.Count,
            records.Count,
            records);
    }

    public async Task<GamificationProfileDto> GetGamificationProfileAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preferences = await GetPreferencesAsync(userId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var completedWorkouts = await ListCompletedWorkoutsAsync(userId, cancellationToken);
        var weeklyEvaluation = WeeklyGoalCalculator.Evaluate(
            preferences.WeeklyWorkoutGoal,
            preferences.TimeZoneId,
            completedWorkouts,
            now);
        var progress = await database.UserProgress.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new { item.TotalXp, item.Level, item.RulesVersion })
            .SingleOrDefaultAsync(cancellationToken);
        var rulesVersion = progress?.RulesVersion
            ?? await database.LevelThresholds.AsNoTracking()
                .MaxAsync(item => (int?)item.RulesVersion, cancellationToken)
            ?? 1;
        var thresholds = await database.LevelThresholds.AsNoTracking()
            .Where(item => item.RulesVersion == rulesVersion)
            .OrderBy(item => item.Level)
            .Select(item => new { item.Level, item.RequiredXp })
            .ToListAsync(cancellationToken);
        var level = progress?.Level ?? 1;
        var currentThreshold = thresholds
            .Where(item => item.Level <= level)
            .Select(item => item.RequiredXp)
            .DefaultIfEmpty(0)
            .Max();
        var nextThreshold = thresholds
            .Where(item => item.Level > level)
            .Select(item => (int?)item.RequiredXp)
            .FirstOrDefault();
        var streak = await database.StreakStates.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new { item.CurrentWeeks, item.BestWeeks })
            .SingleOrDefaultAsync(cancellationToken);
        var badges = await (
            from userBadge in database.UserBadges.AsNoTracking()
            join definition in database.BadgeDefinitions.AsNoTracking()
                on userBadge.BadgeDefinitionId equals definition.Id
            where userBadge.UserId == userId
            orderby userBadge.EarnedAt descending, definition.Key
            select new EarnedBadgeDto(
                definition.Key,
                definition.NameResourceKey,
                definition.DescriptionResourceKey,
                definition.IconKey,
                userBadge.EarnedAt))
            .ToListAsync(cancellationToken);
        var earnedKeys = badges.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var newlyEarned = await database.BadgeAuditEvents.AsNoTracking()
            .Where(item => item.UserId == userId
                && item.Action == BadgeAuditAction.Awarded
                && item.OccurredAt >= now.AddHours(-24))
            .OrderByDescending(item => item.OccurredAt)
            .ThenBy(item => item.BadgeKey)
            .Select(item => item.BadgeKey)
            .ToListAsync(cancellationToken);

        return new GamificationProfileDto(
            progress?.TotalXp ?? 0,
            level,
            currentThreshold,
            nextThreshold,
            preferences.WeeklyWorkoutGoal,
            weeklyEvaluation.CurrentWeekCompletedWorkouts,
            streak?.CurrentWeeks ?? 0,
            streak?.BestWeeks ?? 0,
            badges,
            newlyEarned.Where(earnedKeys.Contains).Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<UserPreferences> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken) =>
        await database.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserPreferences(user.WeeklyWorkoutGoal, user.TimeZoneId))
            .SingleAsync(cancellationToken);

    private async Task<List<CompletedWorkoutInstant>> ListCompletedWorkoutsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await database.WorkoutSessions.AsNoTracking()
            .Where(workout => workout.OwnerId == userId
                && workout.Status == WorkoutStatus.Completed
                && workout.CompletedAt != null
                && workout.DeletedAt == null)
            .Select(workout => new CompletedWorkoutInstant(workout.Id, workout.CompletedAt!.Value))
            .ToListAsync(cancellationToken);

    private sealed record UserPreferences(int WeeklyWorkoutGoal, string TimeZoneId);
}
