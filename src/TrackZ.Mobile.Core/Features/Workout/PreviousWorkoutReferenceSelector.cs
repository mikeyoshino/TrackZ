using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Mobile.Features.Workout;

public sealed record PreviousWorkoutReference(
    TrackingMode TrackingMode,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    int Order,
    bool HasRatedEffort,
    int? PlateCount = null);

public static class PreviousWorkoutReferenceSelector
{
    public static PreviousWorkoutReference? Select(ExerciseHistorySessionDto? session)
    {
        if (session is null || !Enum.IsDefined(session.TrackingMode)) return null;
        var eligible = session.Sets
            .Where(set => set.Reps is >= 8 and <= 12)
            .Where(set => set.Effort != SetEffortRating.TooHeavy)
            .Where(set => set.Effort is null || Enum.IsDefined(set.Effort.Value))
            .Where(set => ValidForMode(session.TrackingMode, set))
            .ToArray();
        var selected = session.TrackingMode switch
        {
            TrackingMode.Weighted => eligible
                .OrderByDescending(set => set.PlateCount ?? int.MinValue)
                .ThenByDescending(set => set.WeightKg)
                .ThenByDescending(set => set.Reps)
                .ThenByDescending(set => set.Order)
                .FirstOrDefault(),
            TrackingMode.Assisted => eligible
                .OrderBy(set => set.PlateCount ?? int.MaxValue)
                .ThenBy(set => set.AssistedKg)
                .ThenByDescending(set => set.Reps)
                .ThenByDescending(set => set.Order)
                .FirstOrDefault(),
            TrackingMode.Bodyweight => eligible
                .OrderByDescending(set => set.Reps)
                .ThenByDescending(set => set.Order)
                .FirstOrDefault(),
            _ => null
        };
        return selected is null ? null : new(
            session.TrackingMode,
            selected.WeightKg,
            selected.AssistedKg,
            selected.Reps,
            selected.Order,
            selected.Effort is not null,
            selected.PlateCount);
    }

    private static bool ValidForMode(TrackingMode mode, WorkoutSetDto set) =>
        set.Id != Guid.Empty
        && set.Order >= 0
        && set.CompletedAt != default
        && (mode switch
        {
            TrackingMode.Weighted => set.AssistedKg is null
                && ((Representable(set.WeightKg) && set.PlateCount is null)
                    || (set.WeightKg is null && ValidPlateCount(set.PlateCount))),
            TrackingMode.Assisted => set.WeightKg is null
                && ((Representable(set.AssistedKg) && set.PlateCount is null)
                    || (set.AssistedKg is null && ValidPlateCount(set.PlateCount))),
            TrackingMode.Bodyweight => set.WeightKg is null
                && set.AssistedKg is null && set.PlateCount is null,
            _ => false
        });

    private static bool Representable(decimal? value) =>
        value is { } kilograms
        && kilograms is >= SetMeasurement.MinimumKilograms
            and <= SetMeasurement.MaximumKilograms
        && ((decimal.GetBits(kilograms)[3] >> 16) & 0xff)
            <= SetMeasurement.MaximumKilogramScale;

    private static bool ValidPlateCount(int? value) =>
        value is >= SetMeasurement.MinimumPlateCount and <= SetMeasurement.MaximumPlateCount;
}
