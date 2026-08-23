using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class ActiveWorkoutExerciseRowTests
{
    [Fact]
    public void Active_row_copy_is_descriptive_not_a_planned_total()
    {
        var row = new WorkoutExerciseDraftItem(
            Guid.NewGuid(),
            "Shoulder Press",
            TrackingMode.Weighted,
            "Weight",
            "/cache/press.jpg",
            LoggedSetCount: 2,
            LoggedSetText: "2 sets",
            LastText: "LAST  50 kg × 8",
            AccessibilitySummary: "Shoulder Press, 2 sets. Open set logger.");

        Assert.Equal(2, row.LoggedSetCount);
        Assert.DoesNotContain(" of ", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logged", row.LoggedSetText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LAST", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Weight", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open set logger", row.AccessibilitySummary, StringComparison.Ordinal);
        Assert.True(row.HasArtwork);
    }
}
