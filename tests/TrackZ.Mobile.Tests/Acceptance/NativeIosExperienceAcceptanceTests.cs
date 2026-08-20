using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Acceptance;

public sealed class NativeIosExperienceAcceptanceTests
{
    [Fact]
    public void Active_workout_row_carries_api_artwork_and_logged_sets_without_a_prescribed_target()
    {
        var row = new WorkoutExerciseDraftItem(
            Guid.NewGuid(), "Machine Shoulder Press", TrackingMode.Weighted, "Weight",
            "/bounded-cache/shoulder-press.jpg", LoggedSetCount: 2,
            LoggedSetText: "2 sets logged", LastText: "LAST 45 kg × 8",
            AccessibilitySummary: "Machine Shoulder Press, 2 sets logged");
        Assert.True(row.HasArtwork);
        Assert.Equal(2, row.LoggedSetCount);
        Assert.DoesNotContain("of", row.LoggedSetText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(typeof(ProgressReveal));
    }
}
