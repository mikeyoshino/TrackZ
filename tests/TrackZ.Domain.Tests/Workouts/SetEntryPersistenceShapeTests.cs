using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Domain.Tests.Workouts;

public sealed class SetEntryPersistenceShapeTests
{
    [Theory]
    [InlineData(TrackingMode.Weighted)]
    [InlineData(TrackingMode.Bodyweight)]
    [InlineData(TrackingMode.Assisted)]
    public void Completed_set_carries_its_parent_tracking_mode_for_database_integrity(TrackingMode mode)
    {
        var now = DateTimeOffset.UtcNow;
        var workout = WorkoutSession.Start(Guid.NewGuid(), Guid.NewGuid(), now);
        var itemId = Guid.NewGuid();
        workout.AddExercise(itemId, Guid.NewGuid(), mode, 0);
        var measurement = mode switch
        {
            TrackingMode.Weighted => new SetMeasurement(50m, null, 10),
            TrackingMode.Bodyweight => new SetMeasurement(null, null, 10),
            TrackingMode.Assisted => new SetMeasurement(null, 20m, 10),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        workout.CompleteSet(itemId, Guid.NewGuid(), measurement, now.AddMinutes(1));

        Assert.Equal(mode, workout.Exercises.Single().Sets.Single().TrackingMode);
    }
}
