using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Mobile.Features.Workout;

public enum HypertrophyGuidanceAction
{
    None = 0,
    Increase = 1,
    Keep = 2,
    Reduce = 3,
    IncreaseRepetitions = 4,
    CollectMoreData = 5
}

public enum HypertrophyGuidanceReason
{
    None = 0,
    MissingEffort = 1,
    InvalidInput = 2,
    TooHeavy = 3,
    BelowRepRange = 4,
    EasyWithinRange = 5,
    ProductiveWithinRange = 6,
    OneQualifyingSet = 7,
    TwoQualifyingSets = 8,
    BodyweightRangeCompleted = 9,
    MissingIncrement = 10,
    InvalidSuggestedMeasurement = 11,
    Pain = 12
}

public sealed record HypertrophyGuidanceSet(
    Guid SetId,
    TrackingMode TrackingMode,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    SetEffortRating? Effort,
    DateTimeOffset CompletedAt,
    int Order,
    int? EffortScore = null,
    bool? HasPain = null);

public sealed record HypertrophyGuidanceRequest(
    HypertrophyGuidanceSet SavedSet,
    IReadOnlyList<HypertrophyGuidanceSet> PriorCandidatesNewestFirst,
    decimal? IncrementKg);

public sealed record HypertrophyGuidanceResult(
    HypertrophyGuidanceAction Action,
    HypertrophyGuidanceReason Reason,
    decimal? SuggestedWeightKg = null,
    decimal? SuggestedAssistedKg = null,
    int? SuggestedReps = null,
    bool RequiresIncrement = false);

public static class HypertrophyLoadGuidancePolicy
{
    public static HypertrophyGuidanceResult Evaluate(HypertrophyGuidanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SavedSet);
        ArgumentNullException.ThrowIfNull(request.PriorCandidatesNewestFirst);
        var saved = request.SavedSet;
        if (!Valid(saved)) return Result(HypertrophyGuidanceAction.None, HypertrophyGuidanceReason.InvalidInput);
        if (saved.HasPain == true) return Result(HypertrophyGuidanceAction.None, HypertrophyGuidanceReason.Pain);
        var effectiveEffort = EffectiveEffort(saved);
        if (effectiveEffort is null) return Result(HypertrophyGuidanceAction.None, HypertrophyGuidanceReason.MissingEffort);
        saved = saved with { Effort = effectiveEffort };
        if (saved.TrackingMode == TrackingMode.Bodyweight)
            return Bodyweight(saved);
        if (saved.Effort == SetEffortRating.TooHeavy)
            return ChangeLoad(saved, request.IncrementKg, HypertrophyGuidanceAction.Reduce, HypertrophyGuidanceReason.TooHeavy);
        if (saved.Reps < 8)
            return ChangeLoad(saved, request.IncrementKg, HypertrophyGuidanceAction.Reduce, HypertrophyGuidanceReason.BelowRepRange);
        if (saved.Reps <= 11 && saved.Effort == SetEffortRating.Easy)
            return Result(HypertrophyGuidanceAction.IncreaseRepetitions,
                HypertrophyGuidanceReason.EasyWithinRange, reps: saved.Reps + 1);
        if (saved.Reps <= 11 && saved.Effort == SetEffortRating.Productive)
            return Result(HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange, reps: saved.Reps);

        var recentAtSameLoad = new[] { saved }
            .Concat(request.PriorCandidatesNewestFirst)
            .Where(candidate => Valid(candidate) && SameLoad(saved, candidate))
            .DistinctBy(candidate => candidate.SetId)
            .Take(2)
            .ToArray();
        var ready = recentAtSameLoad.Length == 2 && recentAtSameLoad.All(QualifiesForIncrease);
        if (!ready)
            return Result(HypertrophyGuidanceAction.CollectMoreData,
                HypertrophyGuidanceReason.OneQualifyingSet);
        return ChangeLoad(saved, request.IncrementKg, HypertrophyGuidanceAction.Increase,
            HypertrophyGuidanceReason.TwoQualifyingSets);
    }

    private static HypertrophyGuidanceResult Bodyweight(HypertrophyGuidanceSet saved)
    {
        if (saved.Effort == SetEffortRating.TooHeavy)
            return Result(HypertrophyGuidanceAction.Reduce,
                HypertrophyGuidanceReason.TooHeavy,
                reps: saved.Reps > 8 ? Math.Min(12, saved.Reps - 1) : null);
        if (saved.Reps < 8)
            return Result(HypertrophyGuidanceAction.Reduce,
                HypertrophyGuidanceReason.BelowRepRange);
        if (saved.Reps <= 11 && saved.Effort == SetEffortRating.Easy)
            return Result(HypertrophyGuidanceAction.IncreaseRepetitions,
                HypertrophyGuidanceReason.EasyWithinRange, reps: saved.Reps + 1);
        if (saved.Reps <= 11)
            return Result(HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange, reps: saved.Reps);
        return Result(HypertrophyGuidanceAction.None,
            HypertrophyGuidanceReason.BodyweightRangeCompleted);
    }

    private static bool Valid(HypertrophyGuidanceSet set)
    {
        if (set.SetId == Guid.Empty
            || !Enum.IsDefined(set.TrackingMode)
            || set.Effort is { } effort && !Enum.IsDefined(effort)
            || set.EffortScore is < 0 or > 100
            || set.Reps is < 1 or > 999
            || set.CompletedAt == default
            || set.Order < 0)
            return false;

        return set.TrackingMode switch
        {
            TrackingMode.Weighted => Representable(set.WeightKg)
                && set.AssistedKg is null,
            TrackingMode.Assisted => set.WeightKg is null
                && Representable(set.AssistedKg),
            TrackingMode.Bodyweight => set.WeightKg is null
                && set.AssistedKg is null,
            _ => false
        };
    }

    private static bool SameLoad(
        HypertrophyGuidanceSet left,
        HypertrophyGuidanceSet right) =>
        left.TrackingMode == right.TrackingMode
        && (left.TrackingMode switch
        {
            TrackingMode.Weighted => left.WeightKg == right.WeightKg,
            TrackingMode.Assisted => left.AssistedKg == right.AssistedKg,
            TrackingMode.Bodyweight => true,
            _ => false
        });

    private static bool QualifiesForIncrease(HypertrophyGuidanceSet set) =>
        set.Reps >= 12
        && EffectiveEffort(set) is SetEffortRating.Easy or SetEffortRating.Productive
        && set.HasPain != true;

    private static SetEffortRating? EffectiveEffort(HypertrophyGuidanceSet set) =>
        set.EffortScore switch
        {
            <= 33 => SetEffortRating.Easy,
            <= 79 => SetEffortRating.Productive,
            >= 80 => SetEffortRating.TooHeavy,
            _ => set.Effort
        };

    private static HypertrophyGuidanceResult ChangeLoad(
        HypertrophyGuidanceSet saved,
        decimal? incrementKg,
        HypertrophyGuidanceAction action,
        HypertrophyGuidanceReason reason)
    {
        if (!ValidIncrement(incrementKg))
            return new(action, HypertrophyGuidanceReason.MissingIncrement,
                RequiresIncrement: true);

        var increment = incrementKg!.Value;
        var suggestion = (saved.TrackingMode, action) switch
        {
            (TrackingMode.Weighted, HypertrophyGuidanceAction.Increase) =>
                saved.WeightKg!.Value + increment,
            (TrackingMode.Weighted, HypertrophyGuidanceAction.Reduce) =>
                saved.WeightKg!.Value - increment,
            (TrackingMode.Assisted, HypertrophyGuidanceAction.Increase) =>
                saved.AssistedKg!.Value - increment,
            (TrackingMode.Assisted, HypertrophyGuidanceAction.Reduce) =>
                saved.AssistedKg!.Value + increment,
            _ => throw new InvalidOperationException("The load action is invalid for this mode.")
        };
        if (!Representable(suggestion))
            return new(action,
                HypertrophyGuidanceReason.InvalidSuggestedMeasurement);

        return saved.TrackingMode == TrackingMode.Weighted
            ? new(action, reason, SuggestedWeightKg: suggestion)
            : new(action, reason, SuggestedAssistedKg: suggestion);
    }

    private static HypertrophyGuidanceResult Result(
        HypertrophyGuidanceAction action,
        HypertrophyGuidanceReason reason,
        int? reps = null) =>
        new(action, reason, SuggestedReps: reps);

    private static bool ValidIncrement(decimal? value) =>
        value is { } increment
        && increment > 0m
        && increment <= SetMeasurement.MaximumKilograms
        && DecimalScale(increment) <= SetMeasurement.MaximumKilogramScale;

    private static bool Representable(decimal? value) =>
        value is { } kilograms
        && kilograms is >= SetMeasurement.MinimumKilograms
            and <= SetMeasurement.MaximumKilograms
        && DecimalScale(kilograms) <= SetMeasurement.MaximumKilogramScale;

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0xff;
}
