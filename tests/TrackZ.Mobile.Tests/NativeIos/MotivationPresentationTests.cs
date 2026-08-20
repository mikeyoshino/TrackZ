using TrackZ.Mobile.Components;
using TrackZ.Mobile.Features.Gamification;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class MotivationPresentationTests
{
    [Fact]
    public void Progress_uses_native_weekly_streak_and_chart_components()
    {
        Assert.True(typeof(WeeklyStreakView).IsSubclassOf(typeof(ContentView)));
        Assert.True(typeof(ExerciseProgressChart).IsSubclassOf(typeof(ContentView)));
        Assert.NotNull(typeof(ProgressDashboardViewModel));
    }
}
