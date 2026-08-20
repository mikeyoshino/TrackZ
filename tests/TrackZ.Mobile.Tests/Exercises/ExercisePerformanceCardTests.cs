using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class ExercisePerformanceCardTests
{
    [Fact]
    public void Missing_api_artwork_uses_neutral_placeholder_state()
    {
        var item = new ExercisePickerItem(new CachedExercise
        {
            Id = Guid.NewGuid(),
            Name = "Cable Raise",
            BodyPart = BodyPart.Shoulders,
            TrackingMode = TrackingMode.Weighted,
            RemoteThumbnailRoute = "/api/v1/media/exercise-images/2/thumbnail",
            LastSyncedAt = DateTimeOffset.UtcNow
        }, WorkoutResources.English);

        Assert.False(item.HasArtwork);
        Assert.True(item.ShowsArtworkPlaceholder);
        Assert.NotEqual(ExerciseArtworkState.Ready, item.ArtworkState);
    }
}
