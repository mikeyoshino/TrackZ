using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Domain.Tests.Workouts;

public sealed class WorkoutSessionTests
{
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _workoutId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _setId = Guid.NewGuid();
    private readonly DateTimeOffset _startedAt = new(2026, 8, 15, 9, 0, 0, TimeSpan.FromHours(7));

    [Theory]
    [InlineData(WorkoutStatus.Draft, 1)]
    [InlineData(WorkoutStatus.Active, 2)]
    [InlineData(WorkoutStatus.Completed, 3)]
    public void Workout_status_wire_values_are_stable(WorkoutStatus status, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)status);
    }

    [Fact]
    public void Start_uses_client_identity_and_normalizes_timestamp_to_utc()
    {
        var workout = WorkoutSession.Start(_ownerId, _workoutId, _startedAt);

        Assert.Equal(_workoutId, workout.Id);
        Assert.Equal(_ownerId, workout.OwnerId);
        Assert.Equal(_startedAt.ToUniversalTime(), workout.StartedAt);
        Assert.Equal(TimeSpan.Zero, workout.StartedAt.Offset);
        Assert.Equal(WorkoutStatus.Active, workout.Status);
        Assert.Equal(0, workout.Version);
        Assert.Empty(workout.Exercises);
        Assert.Empty(workout.ExerciseEntries);
    }

    [Fact]
    public void Start_rejects_empty_identity_and_default_timestamp()
    {
        Assert.Throws<ArgumentException>(() => WorkoutSession.Start(Guid.Empty, _workoutId, _startedAt));
        Assert.Throws<ArgumentException>(() => WorkoutSession.Start(_ownerId, Guid.Empty, _startedAt));
        Assert.Throws<ArgumentException>(() => WorkoutSession.Start(_ownerId, _workoutId, default));
    }

    [Fact]
    public void Exercise_collections_are_read_only_views()
    {
        var workout = StartWorkout();
        workout.AddExercise(_itemId, _exerciseId, TrackingMode.Weighted, 0);

        Assert.False(workout.Exercises is ICollection<WorkoutExercise> { IsReadOnly: false });
        Assert.False(workout.ExerciseEntries is ICollection<WorkoutExercise> { IsReadOnly: false });
    }

    [Fact]
    public void Add_exercise_uses_client_id_inserts_at_requested_order_and_versions_once()
    {
        var workout = StartWorkout();
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();

        workout.AddExercise(firstItemId, Guid.NewGuid(), TrackingMode.Weighted, 0);
        workout.AddExercise(secondItemId, Guid.NewGuid(), TrackingMode.Bodyweight, 0);

        Assert.Collection(
            workout.Exercises,
            item =>
            {
                Assert.Equal(secondItemId, item.Id);
                Assert.Equal(0, item.Order);
            },
            item =>
            {
                Assert.Equal(firstItemId, item.Id);
                Assert.Equal(1, item.Order);
            });
        Assert.Equal(2, workout.Version);
    }

    [Fact]
    public void Add_exercise_rejects_invalid_or_duplicate_identity_mode_and_order_without_mutating_version()
    {
        var workout = StartWorkout();
        workout.AddExercise(_itemId, _exerciseId, TrackingMode.Weighted, 0);
        var version = workout.Version;

        Assert.Throws<ArgumentException>(() => workout.AddExercise(Guid.Empty, Guid.NewGuid(), TrackingMode.Weighted, 1));
        Assert.Throws<ArgumentException>(() => workout.AddExercise(Guid.NewGuid(), Guid.Empty, TrackingMode.Weighted, 1));
        Assert.Throws<ArgumentException>(() => workout.AddExercise(_itemId, Guid.NewGuid(), TrackingMode.Weighted, 1));
        Assert.Throws<ArgumentException>(() => workout.AddExercise(Guid.NewGuid(), _exerciseId, TrackingMode.Weighted, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => workout.AddExercise(Guid.NewGuid(), Guid.NewGuid(), (TrackingMode)999, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => workout.AddExercise(Guid.NewGuid(), Guid.NewGuid(), TrackingMode.Weighted, 2));

        Assert.Equal(version, workout.Version);
        Assert.Single(workout.Exercises);
    }

    [Fact]
    public void Reorder_exercises_requires_an_exact_unique_id_set_and_keeps_contiguous_order()
    {
        var workout = StartWorkout();
        var first = AddExercise(workout, TrackingMode.Weighted);
        var second = AddExercise(workout, TrackingMode.Bodyweight);
        var third = AddExercise(workout, TrackingMode.Assisted);
        var version = workout.Version;

        workout.ReorderExercises([third.Id, first.Id, second.Id]);

        Assert.Equal([third.Id, first.Id, second.Id], workout.Exercises.Select(x => x.Id));
        Assert.Equal([0, 1, 2], workout.Exercises.Select(x => x.Order));
        Assert.Equal(version + 1, workout.Version);

        version = workout.Version;
        Assert.Throws<ArgumentException>(() => workout.ReorderExercises([first.Id, first.Id, third.Id]));
        Assert.Throws<ArgumentException>(() => workout.ReorderExercises([first.Id, second.Id]));
        Assert.Throws<ArgumentException>(() => workout.ReorderExercises([first.Id, second.Id, Guid.NewGuid()]));
        Assert.Equal(version, workout.Version);
    }

    [Fact]
    public void Reordering_to_current_order_is_a_no_op()
    {
        var workout = StartWorkout();
        var first = AddExercise(workout, TrackingMode.Weighted);
        var second = AddExercise(workout, TrackingMode.Bodyweight);
        var version = workout.Version;

        workout.ReorderExercises([first.Id, second.Id]);

        Assert.Equal(version, workout.Version);
    }

    [Theory]
    [MemberData(nameof(ValidMeasurements))]
    public void Complete_set_accepts_only_mode_appropriate_measurements(
        TrackingMode mode,
        SetMeasurement measurement)
    {
        var workout = StartWorkout();
        var item = AddExercise(workout, mode);
        var completedAt = _startedAt.AddHours(1);

        workout.CompleteSet(item.Id, _setId, measurement, completedAt);

        var set = Assert.Single(item.Sets);
        Assert.Equal(_setId, set.Id);
        Assert.Equal(measurement, set.Measurement);
        Assert.Equal(0, set.Order);
        Assert.Equal(completedAt.ToUniversalTime(), set.CompletedAt);
        Assert.Equal(1, set.Version);
        Assert.Equal(2, workout.Version);
    }

    public static TheoryData<TrackingMode, SetMeasurement> ValidMeasurements => new()
    {
        { TrackingMode.Weighted, new SetMeasurement(0.001m, null, 1) },
        { TrackingMode.Weighted, new SetMeasurement(250m, null, 999) },
        { TrackingMode.Bodyweight, new SetMeasurement(null, null, 20) },
        { TrackingMode.Assisted, new SetMeasurement(null, 0.001m, 10) }
    };

    [Theory]
    [MemberData(nameof(InvalidMeasurements))]
    public void Invalid_set_measurement_raises_domain_owned_reason_and_does_not_mutate(
        TrackingMode mode,
        SetMeasurement measurement)
    {
        var workout = StartWorkout();
        var item = AddExercise(workout, mode);
        var version = workout.Version;

        var exception = Assert.Throws<WorkoutRuleException>(() =>
            workout.CompleteSet(item.Id, _setId, measurement, _startedAt.AddMinutes(1)));

        Assert.Equal(WorkoutRuleViolation.InvalidSetValue, exception.Violation);
        Assert.Empty(item.Sets);
        Assert.Equal(version, workout.Version);
    }

    public static TheoryData<TrackingMode, SetMeasurement> InvalidMeasurements => new()
    {
        { TrackingMode.Weighted, new SetMeasurement(null, null, 10) },
        { TrackingMode.Weighted, new SetMeasurement(0m, null, 10) },
        { TrackingMode.Weighted, new SetMeasurement(-1m, null, 10) },
        { TrackingMode.Weighted, new SetMeasurement(50m, 10m, 10) },
        { TrackingMode.Bodyweight, new SetMeasurement(50m, null, 10) },
        { TrackingMode.Bodyweight, new SetMeasurement(null, 10m, 10) },
        { TrackingMode.Assisted, new SetMeasurement(null, null, 10) },
        { TrackingMode.Assisted, new SetMeasurement(null, 0m, 10) },
        { TrackingMode.Assisted, new SetMeasurement(null, -1m, 10) },
        { TrackingMode.Assisted, new SetMeasurement(50m, 10m, 10) },
        { TrackingMode.Weighted, new SetMeasurement(50m, null, 0) },
        { TrackingMode.Weighted, new SetMeasurement(50m, null, 1000) }
    };

    [Fact]
    public void Null_measurement_raises_invalid_set_reason_without_mutating()
    {
        var workout = StartWorkout();
        var item = AddExercise(workout, TrackingMode.Weighted);
        var version = workout.Version;

        var exception = Assert.Throws<WorkoutRuleException>(() =>
            workout.CompleteSet(item.Id, _setId, null!, _startedAt.AddMinutes(1)));

        Assert.Equal(WorkoutRuleViolation.InvalidSetValue, exception.Violation);
        Assert.Empty(item.Sets);
        Assert.Equal(version, workout.Version);
    }

    [Fact]
    public void Complete_set_rejects_empty_unknown_and_duplicate_ids_or_time_before_workout()
    {
        var workout = StartWorkout();
        var first = AddExercise(workout, TrackingMode.Weighted);
        var second = AddExercise(workout, TrackingMode.Weighted);
        var measurement = new SetMeasurement(70m, null, 10);
        workout.CompleteSet(first.Id, _setId, measurement, _startedAt.AddMinutes(1));
        var version = workout.Version;

        Assert.Throws<ArgumentException>(() => workout.CompleteSet(Guid.Empty, Guid.NewGuid(), measurement, _startedAt.AddMinutes(2)));
        Assert.Throws<ArgumentException>(() => workout.CompleteSet(Guid.NewGuid(), Guid.NewGuid(), measurement, _startedAt.AddMinutes(2)));
        Assert.Throws<ArgumentException>(() => workout.CompleteSet(first.Id, Guid.Empty, measurement, _startedAt.AddMinutes(2)));
        Assert.Throws<ArgumentException>(() => workout.CompleteSet(second.Id, _setId, measurement, _startedAt.AddMinutes(2)));
        Assert.Throws<ArgumentException>(() => workout.CompleteSet(first.Id, Guid.NewGuid(), measurement, _startedAt.AddMinutes(-1)));

        Assert.Equal(version, workout.Version);
        Assert.Single(first.Sets);
        Assert.Empty(second.Sets);
    }

    [Fact]
    public void Record_set_effort_changes_only_effort_and_versions_each_aggregate_once()
    {
        var workout = StartWorkout();
        workout.AddExercise(_itemId, _exerciseId, TrackingMode.Weighted, 0);
        workout.CompleteSet(_itemId, _setId, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
        var beforeWorkout = workout.Version;
        var exercise = workout.Exercises.Single();
        var beforeExercise = exercise.Version;
        var set = exercise.Sets.Single();
        var beforeSet = set.Version;

        workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Productive, _startedAt.AddMinutes(2));

        Assert.Equal(SetEffortRating.Productive, set.Effort);
        Assert.Equal(70m, set.WeightKg);
        Assert.Equal(10, set.Reps);
        Assert.Equal(beforeSet + 1, set.Version);
        Assert.Equal(beforeExercise + 1, exercise.Version);
        Assert.Equal(beforeWorkout + 1, workout.Version);
    }

    [Fact]
    public void Measurement_edit_preserves_recorded_effort()
    {
        var workout = StartWorkout();
        workout.AddExercise(_itemId, _exerciseId, TrackingMode.Weighted, 0);
        workout.CompleteSet(_itemId, _setId, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
        workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Easy, _startedAt.AddMinutes(2));

        workout.EditSet(_itemId, _setId, new SetMeasurement(72.5m, null, 9), _startedAt.AddMinutes(3));

        var set = workout.Exercises.Single().Sets.Single();
        Assert.Equal(SetEffortRating.Easy, set.Effort);
        Assert.Equal(72.5m, set.WeightKg);
    }

    [Fact]
    public void Record_set_effort_is_idempotent_for_same_value_and_rejects_invalid_identity_or_time()
    {
        var workout = StartWorkout();
        workout.AddExercise(_itemId, _exerciseId, TrackingMode.Weighted, 0);
        workout.CompleteSet(_itemId, _setId, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
        workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Productive, _startedAt.AddMinutes(2));
        var exercise = workout.Exercises.Single();
        var set = exercise.Sets.Single();
        var workoutVersion = workout.Version;
        var exerciseVersion = exercise.Version;
        var setVersion = set.Version;
        var updatedAt = set.UpdatedAt;

        workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Productive, _startedAt.AddMinutes(3));
        workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Productive, _startedAt);

        Assert.Equal(workoutVersion, workout.Version);
        Assert.Equal(exerciseVersion, exercise.Version);
        Assert.Equal(setVersion, set.Version);
        Assert.Equal(updatedAt, set.UpdatedAt);
        Assert.Throws<ArgumentException>(() => workout.RecordSetEffort(_itemId, Guid.NewGuid(), SetEffortRating.Easy, _startedAt.AddMinutes(4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => workout.RecordSetEffort(_itemId, _setId, (SetEffortRating)99, _startedAt.AddMinutes(4)));
        Assert.Throws<ArgumentException>(() => workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Easy, _startedAt));

        workout.Complete(_startedAt.AddMinutes(5));
        Assert.Throws<WorkoutRuleException>(() => workout.RecordSetEffort(
            _itemId, _setId, SetEffortRating.Easy, _startedAt.AddMinutes(6)));
    }

    [Fact]
    public void Set_collections_are_read_only_views()
    {
        var workout = StartWorkout();
        var item = AddExercise(workout, TrackingMode.Bodyweight);
        workout.CompleteSet(item.Id, _setId, new SetMeasurement(null, null, 10), _startedAt.AddMinutes(1));

        Assert.False(item.Sets is ICollection<SetEntry> { IsReadOnly: false });
        Assert.False(item.SetEntries is ICollection<SetEntry> { IsReadOnly: false });
    }

    [Fact]
    public void Delete_set_keeps_a_versioned_tombstone_and_reindexes_active_sets()
    {
        var workout = StartWorkout();
        var item = AddExercise(workout, TrackingMode.Weighted);
        var firstSetId = Guid.NewGuid();
        var secondSetId = Guid.NewGuid();
        var thirdSetId = Guid.NewGuid();
        workout.CompleteSet(item.Id, firstSetId, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
        workout.CompleteSet(item.Id, secondSetId, new SetMeasurement(70m, null, 9), _startedAt.AddMinutes(2));
        workout.CompleteSet(item.Id, thirdSetId, new SetMeasurement(67.5m, null, 10), _startedAt.AddMinutes(3));
        var version = workout.Version;
        var deletedAt = _startedAt.AddHours(2);

        workout.DeleteSet(item.Id, secondSetId, deletedAt);

        Assert.Equal([firstSetId, thirdSetId], item.Sets.Select(x => x.Id));
        Assert.Equal([0, 1], item.Sets.Select(x => x.Order));
        var tombstone = Assert.Single(item.SetEntries, x => x.Id == secondSetId);
        Assert.True(tombstone.IsDeleted);
        Assert.Equal(deletedAt.ToUniversalTime(), tombstone.DeletedAt);
        Assert.Equal(2, tombstone.Version);
        Assert.Equal(version + 1, workout.Version);

        version = workout.Version;
        workout.DeleteSet(item.Id, secondSetId, deletedAt.AddMinutes(1));
        Assert.Equal(version, workout.Version);
    }

    [Fact]
    public void Delete_exercise_keeps_nested_tombstones_and_reindexes_active_exercises()
    {
        var workout = StartWorkout();
        var first = AddExercise(workout, TrackingMode.Weighted);
        var second = AddExercise(workout, TrackingMode.Bodyweight);
        var third = AddExercise(workout, TrackingMode.Assisted);
        workout.CompleteSet(second.Id, _setId, new SetMeasurement(null, null, 10), _startedAt.AddMinutes(1));
        var version = workout.Version;
        var deletedAt = _startedAt.AddHours(1);

        workout.DeleteExercise(second.Id, deletedAt);

        Assert.Equal([first.Id, third.Id], workout.Exercises.Select(x => x.Id));
        Assert.Equal([0, 1], workout.Exercises.Select(x => x.Order));
        var exerciseTombstone = Assert.Single(workout.ExerciseEntries, x => x.Id == second.Id);
        Assert.True(exerciseTombstone.IsDeleted);
        Assert.Equal(deletedAt.ToUniversalTime(), exerciseTombstone.DeletedAt);
        var setTombstone = Assert.Single(exerciseTombstone.SetEntries);
        Assert.True(setTombstone.IsDeleted);
        Assert.Equal(version + 1, workout.Version);
    }

    [Fact]
    public void Rejected_exercise_delete_does_not_partially_delete_nested_sets()
    {
        var workout = StartWorkout();
        var item = AddExercise(workout, TrackingMode.Weighted);
        workout.CompleteSet(item.Id, Guid.NewGuid(), new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
        workout.CompleteSet(item.Id, Guid.NewGuid(), new SetMeasurement(70m, null, 9), _startedAt.AddMinutes(3));
        var aggregateVersion = workout.Version;
        var exerciseVersion = item.Version;
        var setVersions = item.Sets.Select(set => set.Version).ToArray();

        Assert.Throws<ArgumentException>(() => workout.DeleteExercise(item.Id, _startedAt.AddMinutes(2)));

        Assert.False(item.IsDeleted);
        Assert.Equal(2, item.Sets.Count);
        Assert.All(item.Sets, set => Assert.False(set.IsDeleted));
        Assert.Equal(setVersions, item.Sets.Select(set => set.Version));
        Assert.Equal(exerciseVersion, item.Version);
        Assert.Equal(aggregateVersion, workout.Version);
    }

    [Fact]
    public void Complete_requires_a_valid_active_session_and_versions_once()
    {
        var emptyWorkout = StartWorkout();
        Assert.Throws<InvalidOperationException>(() => emptyWorkout.Complete(_startedAt.AddHours(1)));
        Assert.Equal(0, emptyWorkout.Version);

        var workout = StartWorkout();
        var item = AddExercise(workout, TrackingMode.Bodyweight);
        Assert.Throws<InvalidOperationException>(() => workout.Complete(_startedAt.AddMinutes(1)));
        workout.CompleteSet(item.Id, _setId, new SetMeasurement(null, null, 10), _startedAt.AddMinutes(2));
        var version = workout.Version;
        var completedAt = _startedAt.AddHours(1);

        workout.Complete(completedAt);

        Assert.Equal(WorkoutStatus.Completed, workout.Status);
        Assert.Equal(completedAt.ToUniversalTime(), workout.CompletedAt);
        Assert.Equal(version + 1, workout.Version);
    }

    [Fact]
    public void Completed_workout_rejects_add_set_and_reorder_with_stable_reason_and_version()
    {
        var workout = CompletedWorkout();
        var item = Assert.Single(workout.Exercises);
        var version = workout.Version;

        AssertAlreadyCompleted(() => workout.AddExercise(Guid.NewGuid(), Guid.NewGuid(), TrackingMode.Weighted, 1));
        AssertAlreadyCompleted(() => workout.CompleteSet(item.Id, Guid.NewGuid(), new SetMeasurement(70m, null, 10), _startedAt.AddHours(2)));
        AssertAlreadyCompleted(() => workout.ReorderExercises([item.Id]));
        AssertAlreadyCompleted(() => workout.Complete(_startedAt.AddHours(2)));
        Assert.Equal(version, workout.Version);
    }

    [Fact]
    public void Completed_workout_allows_versioned_historical_set_edit_and_delete()
    {
        var workout = CompletedWorkout();
        var item = Assert.Single(workout.Exercises);
        var set = Assert.Single(item.Sets);
        var aggregateVersion = workout.Version;
        var setVersion = set.Version;
        var editedAt = _startedAt.AddHours(2);

        workout.EditSet(item.Id, set.Id, new SetMeasurement(75m, null, 8), editedAt);

        Assert.Equal(new SetMeasurement(75m, null, 8), set.Measurement);
        Assert.Equal(editedAt.ToUniversalTime(), set.UpdatedAt);
        Assert.Equal(setVersion + 1, set.Version);
        Assert.Equal(aggregateVersion + 1, workout.Version);

        aggregateVersion = workout.Version;
        setVersion = set.Version;
        workout.EditSet(item.Id, set.Id, new SetMeasurement(75m, null, 8), editedAt.AddMinutes(1));
        Assert.Equal(aggregateVersion, workout.Version);
        Assert.Equal(setVersion, set.Version);

        workout.DeleteSet(item.Id, set.Id, editedAt.AddMinutes(2));
        Assert.Empty(item.Sets);
        Assert.True(set.IsDeleted);
        Assert.Equal(aggregateVersion + 1, workout.Version);
    }

    [Fact]
    public void Edit_set_revalidates_measurement_against_immutable_tracking_mode()
    {
        var workout = CompletedWorkout();
        var item = Assert.Single(workout.Exercises);
        var set = Assert.Single(item.Sets);
        var version = workout.Version;

        var exception = Assert.Throws<WorkoutRuleException>(() =>
            workout.EditSet(item.Id, set.Id, new SetMeasurement(null, null, 10), _startedAt.AddHours(2)));

        Assert.Equal(WorkoutRuleViolation.InvalidSetValue, exception.Violation);
        Assert.Equal(new SetMeasurement(70m, null, 10), set.Measurement);
        Assert.Equal(version, workout.Version);
    }

    [Fact]
    public void Delete_is_a_soft_tombstone_and_repeating_it_is_a_no_op()
    {
        var workout = CompletedWorkout();
        var version = workout.Version;
        var deletedAt = _startedAt.AddHours(3);

        workout.Delete(deletedAt);

        Assert.True(workout.IsDeleted);
        Assert.Equal(deletedAt.ToUniversalTime(), workout.DeletedAt);
        Assert.Equal(version + 1, workout.Version);

        workout.Delete(deletedAt.AddMinutes(1));
        Assert.Equal(version + 1, workout.Version);
        Assert.Equal(deletedAt.ToUniversalTime(), workout.DeletedAt);
    }

    [Fact]
    public void Deleted_workout_rejects_further_mutation_without_changing_version()
    {
        var workout = CompletedWorkout();
        var item = Assert.Single(workout.Exercises);
        var set = Assert.Single(item.Sets);
        workout.Delete(_startedAt.AddHours(2));
        var version = workout.Version;

        Assert.Throws<InvalidOperationException>(() => workout.EditSet(item.Id, set.Id, new SetMeasurement(75m, null, 8), _startedAt.AddHours(3)));
        Assert.Throws<InvalidOperationException>(() => workout.DeleteSet(item.Id, set.Id, _startedAt.AddHours(3)));
        Assert.Equal(version, workout.Version);
    }

    private WorkoutSession StartWorkout() => WorkoutSession.Start(_ownerId, _workoutId, _startedAt);

    private WorkoutSession CompletedWorkout()
    {
        var workout = StartWorkout();
        var item = AddExercise(workout, TrackingMode.Weighted);
        workout.CompleteSet(item.Id, _setId, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(30));
        workout.Complete(_startedAt.AddHours(1));
        return workout;
    }

    private static WorkoutExercise AddExercise(WorkoutSession workout, TrackingMode mode)
    {
        var itemId = Guid.NewGuid();
        workout.AddExercise(itemId, Guid.NewGuid(), mode, workout.Exercises.Count);
        return Assert.Single(workout.Exercises, x => x.Id == itemId);
    }

    private static void AssertAlreadyCompleted(Action action)
    {
        var exception = Assert.Throws<WorkoutRuleException>(action);
        Assert.Equal(WorkoutRuleViolation.WorkoutAlreadyCompleted, exception.Violation);
    }
}
