using System.Globalization;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class HypertrophyLoadGuidancePolicyTests
{
    private static readonly Guid SavedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(7, SetEffortRating.Easy, HypertrophyGuidanceAction.Reduce, null)]
    [InlineData(7, SetEffortRating.Productive, HypertrophyGuidanceAction.Reduce, null)]
    [InlineData(8, SetEffortRating.Easy, HypertrophyGuidanceAction.IncreaseRepetitions, 9)]
    [InlineData(11, SetEffortRating.Easy, HypertrophyGuidanceAction.IncreaseRepetitions, 12)]
    [InlineData(8, SetEffortRating.Productive, HypertrophyGuidanceAction.Keep, 8)]
    [InlineData(11, SetEffortRating.Productive, HypertrophyGuidanceAction.Keep, 11)]
    [InlineData(12, SetEffortRating.Easy, HypertrophyGuidanceAction.CollectMoreData, null)]
    [InlineData(13, SetEffortRating.Easy, HypertrophyGuidanceAction.CollectMoreData, null)]
    [InlineData(12, SetEffortRating.Productive, HypertrophyGuidanceAction.CollectMoreData, null)]
    [InlineData(13, SetEffortRating.Productive, HypertrophyGuidanceAction.CollectMoreData, null)]
    public void Weighted_matrix_is_stable(
        int reps,
        SetEffortRating effort,
        HypertrophyGuidanceAction expectedAction,
        int? expectedReps)
    {
        var result = Evaluate(Weighted(70m, reps, effort));

        Assert.Equal(expectedAction, result.Action);
        Assert.Equal(expectedReps, result.SuggestedReps);
    }

    [Fact]
    public void Missing_effort_returns_none()
    {
        var result = Evaluate(Weighted(70m, 12, null));

        Assert.Equal(HypertrophyGuidanceAction.None, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.MissingEffort, result.Reason);
    }

    [Fact]
    public void Two_most_recent_qualifying_same_load_sets_increase_weight()
    {
        var saved = Weighted(70m, 12, SetEffortRating.Productive);
        var prior = Weighted(70m, 13, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-3));

        var result = Evaluate(saved, [prior], incrementKg: 2.5m);

        Assert.Equal(HypertrophyGuidanceAction.Increase, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.TwoQualifyingSets, result.Reason);
        Assert.Equal(72.5m, result.SuggestedWeightKg);
        Assert.False(result.RequiresIncrement);
    }

    [Fact]
    public void Different_load_or_legacy_effort_cannot_complete_readiness()
    {
        var saved = Weighted(70m, 12, SetEffortRating.Productive);
        var differentLoad = Weighted(67.5m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-1));
        var legacy = Weighted(70m, 12, null, Guid.NewGuid(), Now.AddMinutes(-2));

        var result = Evaluate(saved, [differentLoad, legacy], incrementKg: 2.5m);

        Assert.Equal(HypertrophyGuidanceAction.CollectMoreData, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.OneQualifyingSet, result.Reason);
    }

    [Fact]
    public void Missing_increment_keeps_the_action_but_returns_no_invented_number()
    {
        var result = Evaluate(
            Weighted(70m, 12, SetEffortRating.Productive),
            [Weighted(70m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-1))]);

        Assert.Equal(HypertrophyGuidanceAction.Increase, result.Action);
        Assert.True(result.RequiresIncrement);
        Assert.Null(result.SuggestedWeightKg);
        Assert.Equal(HypertrophyGuidanceReason.MissingIncrement, result.Reason);
    }

    [Fact]
    public void Assisted_difficulty_inverts_the_assistance_direction()
    {
        var saved = Assisted(30m, 12, SetEffortRating.Productive);
        var prior = Assisted(30m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-1));
        var increase = Evaluate(saved, [prior], 2.5m);
        var reduce = Evaluate(Assisted(30m, 8, SetEffortRating.TooHeavy), incrementKg: 2.5m);

        Assert.Equal(27.5m, increase.SuggestedAssistedKg);
        Assert.Equal(32.5m, reduce.SuggestedAssistedKg);
    }

    [Theory]
    [InlineData(8, SetEffortRating.Easy, HypertrophyGuidanceAction.IncreaseRepetitions, 9)]
    [InlineData(11, SetEffortRating.Productive, HypertrophyGuidanceAction.Keep, 11)]
    [InlineData(12, SetEffortRating.Productive, HypertrophyGuidanceAction.None, null)]
    [InlineData(8, SetEffortRating.TooHeavy, HypertrophyGuidanceAction.Reduce, null)]
    public void Bodyweight_never_invents_external_load(
        int reps,
        SetEffortRating effort,
        HypertrophyGuidanceAction action,
        int? suggestedReps)
    {
        var result = Evaluate(new HypertrophyGuidanceSet(
            SavedId, TrackingMode.Bodyweight, null, null, reps, effort, Now, 0));

        Assert.Equal(action, result.Action);
        Assert.Equal(suggestedReps, result.SuggestedReps);
        Assert.Null(result.SuggestedWeightKg);
        Assert.Null(result.SuggestedAssistedKg);
    }

    [Fact]
    public void Invalid_measurement_or_unrepresentable_result_returns_no_numeric_guidance()
    {
        var invalid = Evaluate(Weighted(null, 10, SetEffortRating.Productive));
        var underflow = Evaluate(Weighted(1m, 7, SetEffortRating.Productive), incrementKg: 2.5m);

        Assert.Equal(HypertrophyGuidanceAction.None, invalid.Action);
        Assert.Equal(HypertrophyGuidanceReason.InvalidInput, invalid.Reason);
        Assert.Equal(HypertrophyGuidanceAction.Reduce, underflow.Action);
        Assert.Null(underflow.SuggestedWeightKg);
        Assert.Equal(HypertrophyGuidanceReason.InvalidSuggestedMeasurement, underflow.Reason);
    }

    private static HypertrophyGuidanceResult Evaluate(
        HypertrophyGuidanceSet saved,
        IReadOnlyList<HypertrophyGuidanceSet>? prior = null,
        decimal? incrementKg = null) =>
        HypertrophyLoadGuidancePolicy.Evaluate(new(
            saved, prior ?? [], incrementKg));

    private static HypertrophyGuidanceSet Weighted(
        decimal? kilograms,
        int reps,
        SetEffortRating? effort,
        Guid? id = null,
        DateTimeOffset? completedAt = null) =>
        new(id ?? SavedId, TrackingMode.Weighted, kilograms, null, reps, effort,
            completedAt ?? Now, 0);

    private static HypertrophyGuidanceSet Assisted(
        decimal kilograms,
        int reps,
        SetEffortRating? effort,
        Guid? id = null,
        DateTimeOffset? completedAt = null) =>
        new(id ?? SavedId, TrackingMode.Assisted, null, kilograms, reps, effort,
            completedAt ?? Now, 0);

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void Too_heavy_always_reduces_weighted_difficulty(int reps)
    {
        var result = Evaluate(Weighted(70m, reps, SetEffortRating.TooHeavy), incrementKg: 2.5m);
        Assert.Equal(HypertrophyGuidanceAction.Reduce, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.TooHeavy, result.Reason);
        Assert.Equal(67.5m, result.SuggestedWeightKg);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0001")]
    public void Invalid_increment_requests_a_valid_equipment_increment(string text)
    {
        var prior = Weighted(70m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-1));
        var result = Evaluate(
            Weighted(70m, 12, SetEffortRating.Productive),
            [prior],
            decimal.Parse(text, CultureInfo.InvariantCulture));
        Assert.Equal(HypertrophyGuidanceAction.Increase, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.MissingIncrement, result.Reason);
        Assert.True(result.RequiresIncrement);
        Assert.Null(result.SuggestedWeightKg);
    }

    [Fact]
    public void A_nonqualifying_second_most_recent_set_blocks_an_older_qualifying_set()
    {
        var result = Evaluate(
            Weighted(70m, 13, SetEffortRating.Productive),
            [
                Weighted(70m, 11, SetEffortRating.Productive, Guid.NewGuid(), Now.AddMinutes(-1)),
                Weighted(70m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-2))
            ],
            2.5m);
        Assert.Equal(HypertrophyGuidanceAction.CollectMoreData, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.OneQualifyingSet, result.Reason);
    }

    [Fact]
    public void The_same_set_identity_cannot_be_counted_twice_for_readiness()
    {
        var saved = Weighted(70m, 12, SetEffortRating.Productive);
        var result = Evaluate(saved, [saved], 2.5m);

        Assert.Equal(HypertrophyGuidanceAction.CollectMoreData, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.OneQualifyingSet, result.Reason);
    }

    [Fact]
    public void Maximum_weight_overflow_is_non_numeric_and_never_clamped()
    {
        var maximum = SetMeasurement.MaximumKilograms;
        var result = Evaluate(
            Weighted(maximum, 13, SetEffortRating.Easy),
            [Weighted(maximum, 12, SetEffortRating.Productive, Guid.NewGuid(), Now.AddMinutes(-1))],
            .001m);
        Assert.Equal(HypertrophyGuidanceAction.Increase, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.InvalidSuggestedMeasurement, result.Reason);
        Assert.Null(result.SuggestedWeightKg);
        Assert.False(result.RequiresIncrement);
    }
}
