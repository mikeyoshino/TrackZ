using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Gamification;

namespace TrackZ.Infrastructure.Persistence.Seed;

public sealed class LevelThresholdSeeder(AppDbContext database)
{
    public static IReadOnlyList<LevelThreshold> Version1 { get; } = Enumerable
        .Range(1, 50)
        .Select(level => LevelThreshold.Create(
            Guid.Parse($"40000000-0000-0000-0000-{level:000000000000}"),
            level,
            RequiredXp(level),
            rulesVersion: 1))
        .ToArray();

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await database.LevelThresholds
            .Where(threshold => threshold.RulesVersion == 1)
            .OrderBy(threshold => threshold.Level)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0)
        {
            await database.LevelThresholds.AddRangeAsync(Version1, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            return;
        }

        if (existing.Count != Version1.Count
            || existing.Zip(Version1).Any(pair =>
                pair.First.Id != pair.Second.Id
                || pair.First.Level != pair.Second.Level
                || pair.First.RequiredXp != pair.Second.RequiredXp))
        {
            throw new InvalidOperationException("Level threshold rules version 1 conflicts with the installed seed data.");
        }
    }

    private static int RequiredXp(int level)
    {
        var completedLevels = level - 1;
        return checked(250 * completedLevels * (completedLevels + 1) / 2);
    }
}
