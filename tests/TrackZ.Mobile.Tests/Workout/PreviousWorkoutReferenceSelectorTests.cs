using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class PreviousWorkoutReferenceSelectorTests
{
    [Fact]
    public void Weighted_selects_heaviest_eligible_set_then_reps_then_later_order()
    {
        var selected = PreviousWorkoutReferenceSelector.Select(Session(
            TrackingMode.Weighted,
            Set(0, 80m, null, 7, SetEffortRating.Easy),
            Set(1, 70m, null, 10, SetEffortRating.Productive),
            Set(2, 70m, null, 12, SetEffortRating.Easy),
            Set(3, 75m, null, 8, SetEffortRating.TooHeavy),
            Set(4, 70m, null, 12, null)));

        Assert.NotNull(selected);
        Assert.Equal(70m, selected!.WeightKg);
        Assert.Equal(12, selected.Reps);
        Assert.Equal(4, selected.Order);
        Assert.False(selected.HasRatedEffort);
    }

    [Fact]
    public void Assisted_selects_least_assistance_and_bodyweight_selects_most_reps()
    {
        var assisted = PreviousWorkoutReferenceSelector.Select(Session(
            TrackingMode.Assisted,
            Set(0, null, 30m, 12, SetEffortRating.Productive),
            Set(1, null, 25m, 9, SetEffortRating.Easy)));
        var bodyweight = PreviousWorkoutReferenceSelector.Select(Session(
            TrackingMode.Bodyweight,
            Set(0, null, null, 9, SetEffortRating.Easy),
            Set(1, null, null, 12, SetEffortRating.Productive)));

        Assert.Equal(25m, assisted!.AssistedKg);
        Assert.Equal(12, bodyweight!.Reps);
    }

    [Fact]
    public void Returns_null_when_no_active_set_is_in_range_or_all_are_too_heavy()
    {
        var selected = PreviousWorkoutReferenceSelector.Select(Session(
            TrackingMode.Weighted,
            Set(0, 70m, null, 7, SetEffortRating.Easy),
            Set(1, 80m, null, 8, SetEffortRating.TooHeavy),
            Set(2, 60m, null, 13, null)));

        Assert.Null(selected);
    }

    private static readonly DateTimeOffset CompletedAt =
        new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private static ExerciseHistorySessionDto Session(
        TrackingMode mode,
        params WorkoutSetDto[] sets) =>
        new(Guid.NewGuid(), CompletedAt, mode, 0m, sets);

    private static WorkoutSetDto Set(
        int order,
        decimal? weightKg,
        decimal? assistedKg,
        int reps,
        SetEffortRating? effort) =>
        new(Guid.NewGuid(), order, weightKg, assistedKg, reps,
            CompletedAt.AddMinutes(order), null, effort);

    [Fact]
    public void Eight_and_twelve_are_inclusive_but_seven_and_thirteen_are_not()
    {
        var selected = PreviousWorkoutReferenceSelector.Select(Session(
            TrackingMode.Weighted,
            Set(0, 90m, null, 7, SetEffortRating.Easy),
            Set(1, 70m, null, 8, SetEffortRating.Easy),
            Set(2, 65m, null, 12, SetEffortRating.Productive),
            Set(3, 100m, null, 13, SetEffortRating.Easy)));
        Assert.Equal(70m, selected!.WeightKg);
        Assert.Equal(8, selected.Reps);
    }

    [Fact]
    public void Invalid_measurement_shape_is_never_selected()
    {
        var selected = PreviousWorkoutReferenceSelector.Select(Session(
            TrackingMode.Weighted,
            Set(0, null, 25m, 10, SetEffortRating.Easy)));
        Assert.Null(selected);
    }

    [Fact]
    public void Exact_tie_uses_later_set_order()
    {
        var selected = PreviousWorkoutReferenceSelector.Select(Session(
            TrackingMode.Weighted,
            Set(0, 70m, null, 10, SetEffortRating.Easy),
            Set(1, 70m, null, 10, SetEffortRating.Productive)));
        Assert.Equal(1, selected!.Order);
    }
}
