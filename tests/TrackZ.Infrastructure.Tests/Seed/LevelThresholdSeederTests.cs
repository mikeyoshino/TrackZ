using TrackZ.Domain.Gamification;
using TrackZ.Infrastructure.Persistence.Seed;

namespace TrackZ.Infrastructure.Tests.Seed;

public sealed class LevelThresholdSeederTests
{
    [Fact]
    public void Version_one_curve_is_monotonic_and_resolves_early_levels()
    {
        var thresholds = LevelThresholdSeeder.Version1;

        Assert.Equal(50, thresholds.Count);
        Assert.Equal(Enumerable.Range(1, 50), thresholds.Select(item => item.Level));
        Assert.Equal(0, thresholds[0].RequiredXp);
        Assert.Equal(250, thresholds[1].RequiredXp);
        Assert.Equal(750, thresholds[2].RequiredXp);
        Assert.True(thresholds.Zip(thresholds.Skip(1), (left, right) =>
            left.RequiredXp < right.RequiredXp).All(value => value));
        Assert.Equal(3, LevelThreshold.ResolveLevel(749 + 1, thresholds));
    }
}
