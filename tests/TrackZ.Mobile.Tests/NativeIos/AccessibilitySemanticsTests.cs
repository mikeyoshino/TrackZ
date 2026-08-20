using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Features.Shared;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class AccessibilitySemanticsTests
{
    [Fact]
    public void Increment_and_decrement_actions_are_distinct_and_primary_targets_are_at_least_44_points()
    {
        var text = WorkoutResources.English;
        Assert.NotEqual(text.DecreaseWeight, text.IncreaseWeight);
        Assert.NotEqual(text.DecreaseAssistance, text.IncreaseAssistance);
        Assert.NotEqual(text.DecreaseReps, text.IncreaseReps);
        Assert.True(NativeAccessibility.MinimumActionTarget >= 44);
    }
}
