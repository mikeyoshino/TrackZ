using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Gamification;
using TrackZ.Domain.Identity;
using TrackZ.Domain.Progress;
using TrackZ.Domain.Workouts;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Infrastructure.Persistence.Seed;
using TrackZ.Infrastructure.Progress;
using TrackZ.Infrastructure.Tests.Persistence;

namespace TrackZ.Infrastructure.Tests.Progress;

public sealed class ProgressReadStoreTests : IAsyncLifetime
{
    private PostgreSqlFixture? _database;

    public async Task InitializeAsync() => _database = await PostgreSqlFixture.StartAsync();
    public async Task DisposeAsync()
    {
        if (_database is not null) await _database.DisposeAsync();
    }

    [Fact]
    public async Task Summary_and_profile_are_calculated_from_only_the_owned_canonical_history()
    {
        var now = DateTimeOffset.Parse("2026-08-20T06:00:00Z");
        var user = User.Create("progress@example.com", "hash", now.AddDays(-30));
        user.UpdateMotivationPreferences(4, "Asia/Bangkok");
        var exercise = ExerciseDefinition.CreateSystem("Progress Press", BodyPart.Chest, TrackingMode.Weighted);
        var workout = WorkoutSession.Start(user.Id, Guid.NewGuid(), now.AddMinutes(-20));
        var workoutExerciseId = Guid.NewGuid();
        workout.AddExercise(workoutExerciseId, exercise.Id, TrackingMode.Weighted, 0);
        workout.CompleteSet(workoutExerciseId, Guid.NewGuid(), new SetMeasurement(70m, null, 10), now.AddMinutes(-10));
        workout.CompleteSet(workoutExerciseId, Guid.NewGuid(), new SetMeasurement(75m, null, 8), now.AddMinutes(-5));
        workout.Complete(now);
        var performance = ExercisePerformance.Create(
            user.Id, exercise.Id, TrackingMode.Weighted, now,
            new ExercisePerformanceSet(75m, null, 8),
            new ExercisePerformanceSet(75m, null, 8));
        var streak = StreakState.Create(user.Id);
        streak.Recalculate(2, 5, YearWeek.FromLocalDate(DateOnly.Parse("2026-08-20")), now);
        var progress = UserProgress.Create(user.Id, 750, 3, 1, now);

        var db = _database!.Db;
        await new LevelThresholdSeeder(db).SeedAsync();
        await new BadgeDefinitionSeeder(db).SeedAsync();
        db.Users.Add(user);
        db.Exercises.Add(exercise);
        db.WorkoutSessions.Add(workout);
        db.ExercisePerformances.Add(performance);
        db.StreakStates.Add(streak);
        db.UserProgress.Add(progress);
        await db.SaveChangesAsync();
        var badgeDefinition = await db.BadgeDefinitions.SingleAsync(item => item.Key == "streak-4");
        db.UserBadges.Add(UserBadge.Create(user.Id, badgeDefinition, now));
        db.BadgeAuditEvents.Add(BadgeAuditEvent.Create(user.Id, badgeDefinition, BadgeAuditAction.Awarded, now));
        await db.SaveChangesAsync();

        var store = new ProgressReadStore(db, new FixedTimeProvider(now));
        var summary = await store.GetProgressSummaryAsync(user.Id, CancellationToken.None);
        var profile = await store.GetGamificationProfileAsync(user.Id, CancellationToken.None);

        Assert.Equal(1300m, summary.TotalVolumeKg);
        Assert.Equal(1300m, summary.WeeklyVolumeKg);
        Assert.Equal(1, summary.CompletedWorkouts);
        Assert.Equal(1, summary.ExercisesProgressing);
        Assert.Equal(75m, Assert.Single(summary.PersonalRecords).BestWeightKg);
        Assert.Equal(750, profile.TotalXp);
        Assert.Equal(3, profile.Level);
        Assert.Equal(750, profile.CurrentLevelRequiredXp);
        Assert.Equal(1500, profile.NextLevelRequiredXp);
        Assert.Equal(4, profile.WeeklyGoal);
        Assert.Equal(1, profile.WeeklyCompletedWorkouts);
        Assert.Equal(2, profile.CurrentStreakWeeks);
        Assert.Equal(5, profile.BestStreakWeeks);
        Assert.Equal("streak-4", Assert.Single(profile.Badges).Key);
        Assert.Equal(["streak-4"], profile.NewlyEarnedBadgeKeys);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
