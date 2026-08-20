using TrackZ.Domain.Gamification;

namespace TrackZ.Domain.Tests.Gamification;

public sealed class XpRulesTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(10, 150)]
    [InlineData(20, 200)]
    [InlineData(30, 200)]
    public void Completed_workout_awards_base_plus_five_per_set_capped_at_one_hundred_set_xp(
        int validSetCount,
        int expectedXp)
    {
        Assert.Equal(expectedXp, XpRules.ForCompletedWorkout(validSetCount));
    }

    [Fact]
    public void Negative_set_count_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => XpRules.ForCompletedWorkout(-1));
    }
}
