using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Gamification;

namespace TrackZ.Infrastructure.Persistence.Seed;

public sealed class BadgeDefinitionSeeder(AppDbContext database)
{
    public static IReadOnlyList<BadgeDefinition> Version1 { get; } =
    [
        Definition(1, "first-workout", BadgeCriteria.CompletedWorkouts, 1),
        Definition(2, "workouts-10", BadgeCriteria.CompletedWorkouts, 10),
        Definition(3, "workouts-25", BadgeCriteria.CompletedWorkouts, 25),
        Definition(4, "workouts-50", BadgeCriteria.CompletedWorkouts, 50),
        Definition(5, "streak-4", BadgeCriteria.BestStreakWeeks, 4),
        Definition(6, "streak-8", BadgeCriteria.BestStreakWeeks, 8),
        Definition(7, "streak-12", BadgeCriteria.BestStreakWeeks, 12),
        Definition(8, "exercises-10", BadgeCriteria.DistinctExercises, 10),
        Definition(9, "exercises-25", BadgeCriteria.DistinctExercises, 25),
        Definition(10, "first-pr", BadgeCriteria.PersonalRecords, 1),
        Definition(11, "prs-10", BadgeCriteria.PersonalRecords, 10)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await database.BadgeDefinitions
            .Where(definition => definition.CriteriaVersion == 1)
            .OrderBy(definition => definition.Key)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0)
        {
            await database.BadgeDefinitions.AddRangeAsync(Version1, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            return;
        }

        var expected = Version1.OrderBy(definition => definition.Key).ToArray();
        if (existing.Count != expected.Length || existing.Zip(expected).Any(pair =>
                pair.First.Id != pair.Second.Id
                || pair.First.Criteria != pair.Second.Criteria
                || pair.First.Threshold != pair.Second.Threshold
                || pair.First.NameResourceKey != pair.Second.NameResourceKey
                || pair.First.DescriptionResourceKey != pair.Second.DescriptionResourceKey
                || pair.First.IconKey != pair.Second.IconKey))
        {
            throw new InvalidOperationException("Badge criteria version 1 conflicts with the installed seed data.");
        }
    }

    private static BadgeDefinition Definition(int sequence, string key, BadgeCriteria criteria, int threshold)
    {
        var resourceSuffix = string.Concat(key.Split('-').Select(part =>
            char.ToUpperInvariant(part[0]) + part[1..]));
        return BadgeDefinition.Create(
            Guid.Parse($"50000000-0000-0000-0000-{sequence:000000000000}"),
            key,
            criteria,
            threshold,
            $"Badge_{resourceSuffix}_Name",
            $"Badge_{resourceSuffix}_Description",
            $"badge-{key}",
            criteriaVersion: 1);
    }
}
