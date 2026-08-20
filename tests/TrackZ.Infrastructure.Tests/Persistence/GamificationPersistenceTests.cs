using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Gamification;
using TrackZ.Infrastructure.Persistence;

namespace TrackZ.Infrastructure.Tests.Persistence;

public sealed class GamificationPersistenceTests
{
    [Fact]
    public void Model_enforces_immutable_source_keys_and_one_progress_row_per_user()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=trackz_model;Username=trackz;Password=unused")
            .Options);

        var ledger = db.Model.FindEntityType(typeof(XpLedgerEntry));
        Assert.NotNull(ledger);
        Assert.Contains(ledger.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(XpLedgerEntry.UserId),
                nameof(XpLedgerEntry.Reason),
                nameof(XpLedgerEntry.SourceId)
            ]));

        var progress = db.Model.FindEntityType(typeof(UserProgress));
        Assert.NotNull(progress);
        Assert.Contains(progress.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(UserProgress.UserId)]));

        var threshold = db.Model.FindEntityType(typeof(LevelThreshold));
        Assert.NotNull(threshold);
        Assert.Contains(threshold.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(LevelThreshold.RulesVersion),
                nameof(LevelThreshold.Level)
            ]));
    }

    [Fact]
    public void Model_persists_one_streak_and_motivation_preference_snapshot_per_user()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=trackz_model;Username=trackz;Password=unused")
            .Options);

        var streak = db.Model.FindEntityType(typeof(StreakState));
        Assert.NotNull(streak);
        Assert.Contains(streak.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(StreakState.UserId)]));

        var user = db.Model.FindEntityType(typeof(TrackZ.Domain.Identity.User));
        Assert.NotNull(user);
        Assert.NotNull(user.FindProperty(nameof(TrackZ.Domain.Identity.User.WeeklyWorkoutGoal)));
        Assert.NotNull(user.FindProperty(nameof(TrackZ.Domain.Identity.User.TimeZoneId)));
    }
}
