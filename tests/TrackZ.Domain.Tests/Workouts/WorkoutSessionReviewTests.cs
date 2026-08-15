using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Domain.Tests.Workouts;

public sealed class WorkoutSessionReviewTests
{
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _workoutId = Guid.NewGuid();
    private readonly DateTimeOffset _startedAt = new(2026, 8, 15, 2, 0, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(RepresentableWeightBoundaries))]
    public void Kilogram_boundaries_are_accepted_exactly(
        TrackingMode mode,
        SetMeasurement measurement,
        decimal expectedKilograms)
    {
        var (workout, exercise) = WorkoutWithExercise(mode);

        workout.CompleteSet(exercise.Id, Guid.NewGuid(), measurement, _startedAt.AddMinutes(1));

        var set = Assert.Single(exercise.Sets);
        var storedKilograms = mode == TrackingMode.Weighted ? set.WeightKg : set.AssistedKg;
        Assert.Equal(expectedKilograms, storedKilograms);
        Assert.Equal(2, workout.Version);
    }

    public static TheoryData<TrackingMode, SetMeasurement, decimal> RepresentableWeightBoundaries => new()
    {
        { TrackingMode.Weighted, new SetMeasurement(0.001m, null, 10), 0.001m },
        { TrackingMode.Weighted, new SetMeasurement(99999.999m, null, 10), 99999.999m },
        { TrackingMode.Assisted, new SetMeasurement(null, 0.001m, 10), 0.001m },
        { TrackingMode.Assisted, new SetMeasurement(null, 99999.999m, 10), 99999.999m }
    };

    [Theory]
    [MemberData(nameof(UnrepresentableKilograms))]
    public void Kilograms_outside_numeric_8_3_are_rejected_atomically(
        TrackingMode mode,
        SetMeasurement measurement)
    {
        var (workout, exercise) = WorkoutWithExercise(mode);
        var aggregateVersion = workout.Version;
        var exerciseVersion = exercise.Version;

        var exception = Assert.Throws<WorkoutRuleException>(() =>
            workout.CompleteSet(exercise.Id, Guid.NewGuid(), measurement, _startedAt.AddMinutes(1)));

        Assert.Equal(WorkoutRuleViolation.InvalidSetValue, exception.Violation);
        Assert.Empty(exercise.SetEntries);
        Assert.Equal(exerciseVersion, exercise.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    public static TheoryData<TrackingMode, SetMeasurement> UnrepresentableKilograms => new()
    {
        // Below minimum, above maximum, non-zero fourth decimals, and scale-four trailing zeros
        // are independent persistence-compatibility cases.
        { TrackingMode.Weighted, new SetMeasurement(0m, null, 10) },
        { TrackingMode.Weighted, new SetMeasurement(100000m, null, 10) },
        { TrackingMode.Weighted, new SetMeasurement(1.2345m, null, 10) },
        { TrackingMode.Weighted, new SetMeasurement(1.2300m, null, 10) },
        { TrackingMode.Assisted, new SetMeasurement(null, 0m, 10) },
        { TrackingMode.Assisted, new SetMeasurement(null, 100000m, 10) },
        { TrackingMode.Assisted, new SetMeasurement(null, 1.2345m, 10) },
        { TrackingMode.Assisted, new SetMeasurement(null, 1.2300m, 10) }
    };

    [Theory]
    [InlineData(TrackingMode.Weighted)]
    [InlineData(TrackingMode.Assisted)]
    public void Four_decimal_edit_is_rejected_even_when_its_numeric_value_is_unchanged(TrackingMode mode)
    {
        var (workout, exercise) = WorkoutWithExercise(mode);
        var initial = mode == TrackingMode.Weighted
            ? new SetMeasurement(70.000m, null, 10)
            : new SetMeasurement(null, 70.000m, 10);
        var fourDecimal = mode == TrackingMode.Weighted
            ? new SetMeasurement(70.0000m, null, 10)
            : new SetMeasurement(null, 70.0000m, 10);
        var set = AddSet(workout, exercise, initial, _startedAt.AddMinutes(1));
        var aggregateVersion = workout.Version;
        var exerciseVersion = exercise.Version;
        var setVersion = set.Version;

        var exception = Assert.Throws<WorkoutRuleException>(() => workout.EditSet(
            exercise.Id,
            set.Id,
            fourDecimal,
            set.CompletedAt.AddMinutes(1)));

        Assert.Equal(WorkoutRuleViolation.InvalidSetValue, exception.Violation);
        Assert.Equal(initial, set.Measurement);
        Assert.Null(set.UpdatedAt);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(exerciseVersion, exercise.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void No_op_edit_still_rejects_timestamp_before_set_completion()
    {
        var (workout, exercise, set) = WorkoutWithSet();
        var aggregateVersion = workout.Version;
        var exerciseVersion = exercise.Version;
        var setVersion = set.Version;

        Assert.Throws<ArgumentException>(() => workout.EditSet(
            exercise.Id,
            set.Id,
            set.Measurement,
            set.CompletedAt.AddTicks(-1)));

        Assert.Null(set.UpdatedAt);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(exerciseVersion, exercise.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Edit_set_cannot_precede_its_prior_update()
    {
        var (workout, exercise, set) = WorkoutWithSet();
        var firstUpdate = set.CompletedAt.AddMinutes(2);
        workout.EditSet(exercise.Id, set.Id, new SetMeasurement(75m, null, 8), firstUpdate);
        var aggregateVersion = workout.Version;
        var exerciseVersion = exercise.Version;
        var setVersion = set.Version;

        Assert.Throws<ArgumentException>(() => workout.EditSet(
            exercise.Id,
            set.Id,
            new SetMeasurement(80m, null, 6),
            firstUpdate.AddTicks(-1)));

        Assert.Equal(new SetMeasurement(75m, null, 8), set.Measurement);
        Assert.Equal(firstUpdate, set.UpdatedAt);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(exerciseVersion, exercise.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Delete_set_cannot_precede_its_prior_update()
    {
        var (workout, exercise, set) = WorkoutWithSet();
        var updateAt = set.CompletedAt.AddMinutes(2);
        workout.EditSet(exercise.Id, set.Id, new SetMeasurement(75m, null, 8), updateAt);
        var aggregateVersion = workout.Version;
        var exerciseVersion = exercise.Version;
        var setVersion = set.Version;

        Assert.Throws<ArgumentException>(() => workout.DeleteSet(
            exercise.Id,
            set.Id,
            updateAt.AddTicks(-1)));

        Assert.False(set.IsDeleted);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(exerciseVersion, exercise.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Historical_edit_cannot_be_backdated_before_workout_completion()
    {
        var (workout, exercise, set) = CompletedWorkout();
        var aggregateVersion = workout.Version;
        var setVersion = set.Version;

        Assert.Throws<ArgumentException>(() => workout.EditSet(
            exercise.Id,
            set.Id,
            new SetMeasurement(75m, null, 8),
            workout.CompletedAt!.Value.AddTicks(-1)));

        Assert.Equal(new SetMeasurement(70m, null, 10), set.Measurement);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Historical_set_delete_cannot_be_backdated_before_workout_completion()
    {
        var (workout, exercise, set) = CompletedWorkout();
        var aggregateVersion = workout.Version;
        var setVersion = set.Version;

        Assert.Throws<ArgumentException>(() => workout.DeleteSet(
            exercise.Id,
            set.Id,
            workout.CompletedAt!.Value.AddTicks(-1)));

        Assert.False(set.IsDeleted);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Exercise_delete_cannot_precede_a_child_set_update()
    {
        var (workout, exercise, set) = WorkoutWithSet();
        var updateAt = set.CompletedAt.AddMinutes(2);
        workout.EditSet(exercise.Id, set.Id, new SetMeasurement(75m, null, 8), updateAt);
        var aggregateVersion = workout.Version;
        var exerciseVersion = exercise.Version;
        var setVersion = set.Version;

        Assert.Throws<ArgumentException>(() => workout.DeleteExercise(exercise.Id, updateAt.AddTicks(-1)));

        Assert.False(exercise.IsDeleted);
        Assert.False(set.IsDeleted);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(exerciseVersion, exercise.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Exercise_delete_cannot_precede_a_child_set_tombstone()
    {
        var (workout, exercise, set) = WorkoutWithSet();
        var setDeletedAt = set.CompletedAt.AddMinutes(2);
        workout.DeleteSet(exercise.Id, set.Id, setDeletedAt);
        var aggregateVersion = workout.Version;
        var exerciseVersion = exercise.Version;
        var setVersion = set.Version;

        Assert.Throws<ArgumentException>(() => workout.DeleteExercise(exercise.Id, setDeletedAt.AddTicks(-1)));

        Assert.False(exercise.IsDeleted);
        Assert.True(set.IsDeleted);
        Assert.Equal(setDeletedAt, set.DeletedAt);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(exerciseVersion, exercise.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Historical_exercise_delete_cannot_be_backdated_before_workout_completion()
    {
        var (workout, exercise, set) = CompletedWorkout();
        var aggregateVersion = workout.Version;
        var exerciseVersion = exercise.Version;
        var setVersion = set.Version;

        Assert.Throws<ArgumentException>(() => workout.DeleteExercise(
            exercise.Id,
            workout.CompletedAt!.Value.AddTicks(-1)));

        Assert.False(exercise.IsDeleted);
        Assert.False(set.IsDeleted);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(exerciseVersion, exercise.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Workout_complete_cannot_precede_a_descendant_update()
    {
        var (workout, exercise, set) = WorkoutWithSet();
        var updateAt = set.CompletedAt.AddMinutes(2);
        workout.EditSet(exercise.Id, set.Id, new SetMeasurement(75m, null, 8), updateAt);
        var aggregateVersion = workout.Version;

        Assert.Throws<ArgumentException>(() => workout.Complete(updateAt.AddTicks(-1)));

        Assert.Equal(WorkoutStatus.Active, workout.Status);
        Assert.Null(workout.CompletedAt);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Workout_complete_cannot_precede_a_tombstoned_descendant_mutation()
    {
        var workout = StartWorkout();
        var deletedExercise = AddExercise(workout, TrackingMode.Weighted);
        var activeExercise = AddExercise(workout, TrackingMode.Bodyweight);
        var deletedSet = AddSet(workout, deletedExercise, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
        AddSet(workout, activeExercise, new SetMeasurement(null, null, 10), _startedAt.AddMinutes(1));
        var tombstonedAt = _startedAt.AddMinutes(4);
        workout.DeleteSet(deletedExercise.Id, deletedSet.Id, tombstonedAt.AddTicks(-1));
        workout.DeleteExercise(deletedExercise.Id, tombstonedAt);
        var aggregateVersion = workout.Version;

        Assert.Throws<ArgumentException>(() => workout.Complete(tombstonedAt.AddTicks(-1)));

        Assert.Equal(WorkoutStatus.Active, workout.Status);
        Assert.Null(workout.CompletedAt);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Workout_delete_cannot_precede_workout_completion()
    {
        var (workout, _, _) = CompletedWorkout();
        var aggregateVersion = workout.Version;

        Assert.Throws<ArgumentException>(() => workout.Delete(workout.CompletedAt!.Value.AddTicks(-1)));

        Assert.False(workout.IsDeleted);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Workout_delete_cannot_precede_a_historical_descendant_update()
    {
        var (workout, exercise, set) = CompletedWorkout();
        var updateAt = workout.CompletedAt!.Value.AddMinutes(2);
        workout.EditSet(exercise.Id, set.Id, new SetMeasurement(75m, null, 8), updateAt);
        var aggregateVersion = workout.Version;

        Assert.Throws<ArgumentException>(() => workout.Delete(updateAt.AddTicks(-1)));

        Assert.False(workout.IsDeleted);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Workout_delete_cannot_precede_a_descendant_tombstone()
    {
        var (workout, exercise, set) = CompletedWorkout();
        var tombstonedAt = workout.CompletedAt!.Value.AddMinutes(2);
        workout.DeleteSet(exercise.Id, set.Id, tombstonedAt);
        var aggregateVersion = workout.Version;

        Assert.Throws<ArgumentException>(() => workout.Delete(tombstonedAt.AddTicks(-1)));

        Assert.False(workout.IsDeleted);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    private WorkoutSession StartWorkout() => WorkoutSession.Start(_ownerId, _workoutId, _startedAt);

    private (WorkoutSession Workout, WorkoutExercise Exercise) WorkoutWithExercise(TrackingMode mode)
    {
        var workout = StartWorkout();
        var exercise = AddExercise(workout, mode);
        return (workout, exercise);
    }

    private (WorkoutSession Workout, WorkoutExercise Exercise, SetEntry Set) WorkoutWithSet()
    {
        var (workout, exercise) = WorkoutWithExercise(TrackingMode.Weighted);
        var set = AddSet(workout, exercise, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
        return (workout, exercise, set);
    }

    private (WorkoutSession Workout, WorkoutExercise Exercise, SetEntry Set) CompletedWorkout()
    {
        var (workout, exercise, set) = WorkoutWithSet();
        workout.Complete(_startedAt.AddMinutes(5));
        return (workout, exercise, set);
    }

    private static WorkoutExercise AddExercise(WorkoutSession workout, TrackingMode mode)
    {
        var itemId = Guid.NewGuid();
        workout.AddExercise(itemId, Guid.NewGuid(), mode, workout.Exercises.Count);
        return Assert.Single(workout.Exercises, item => item.Id == itemId);
    }

    private static SetEntry AddSet(
        WorkoutSession workout,
        WorkoutExercise exercise,
        SetMeasurement measurement,
        DateTimeOffset completedAt)
    {
        var setId = Guid.NewGuid();
        workout.CompleteSet(exercise.Id, setId, measurement, completedAt);
        return Assert.Single(exercise.Sets, set => set.Id == setId);
    }
}
