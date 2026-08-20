using TrackZ.Domain.Gamification;
using TrackZ.Infrastructure.Persistence.Seed;

namespace TrackZ.Infrastructure.Tests.Seed;

public sealed class BadgeDefinitionSeederTests
{
    [Fact]
    public void Version_one_contains_the_exact_mvp_badge_families()
    {
        var definitions = BadgeDefinitionSeeder.Version1;

        Assert.Equal(
            [
                "exercises-10", "exercises-25", "first-pr", "first-workout", "prs-10",
                "streak-12", "streak-4", "streak-8", "workouts-10", "workouts-25", "workouts-50"
            ],
            definitions.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.All(definitions, definition =>
        {
            Assert.Equal(1, definition.CriteriaVersion);
            Assert.False(string.IsNullOrWhiteSpace(definition.NameResourceKey));
            Assert.False(string.IsNullOrWhiteSpace(definition.DescriptionResourceKey));
            Assert.StartsWith("badge-", definition.IconKey, StringComparison.Ordinal);
        });
        Assert.Equal(BadgeCriteria.BestStreakWeeks,
            definitions.Single(item => item.Key == "streak-4").Criteria);
    }
}
