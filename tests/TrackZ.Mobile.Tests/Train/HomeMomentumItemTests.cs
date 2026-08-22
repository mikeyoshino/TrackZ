using System.ComponentModel;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Train;

public sealed class HomeMomentumItemTests
{
    [Theory]
    [InlineData(WeightDisplayUnit.Kilograms, "Last 70.125 kg × 8", "Best 72.5 kg × 6")]
    [InlineData(WeightDisplayUnit.Pounds, "Last 154.60 lb × 8", "Best 159.84 lb × 6")]
    public void Formats_exact_weighted_last_and_best(
        WeightDisplayUnit unit,
        string expectedLast,
        string expectedBest)
    {
        using var item = CreateItem(unit, TrackingMode.Weighted, 70.125m, null, 8, 72.5m, null, 6);

        Assert.Equal(expectedLast, item.LastText);
        Assert.Equal(expectedBest, item.BestText);
    }

    [Theory]
    [InlineData(WeightDisplayUnit.Kilograms, "Last 20 kg assistance × 8", "Best 15.5 kg assistance × 10")]
    [InlineData(WeightDisplayUnit.Pounds, "Last 44.09 lb assistance × 8", "Best 34.17 lb assistance × 10")]
    public void Formats_exact_assisted_last_and_best(
        WeightDisplayUnit unit,
        string expectedLast,
        string expectedBest)
    {
        using var item = CreateItem(unit, TrackingMode.Assisted, null, 20m, 8, null, 15.5m, 10);

        Assert.Equal(expectedLast, item.LastText);
        Assert.Equal(expectedBest, item.BestText);
    }

    [Fact]
    public void Formats_bodyweight_as_reps_without_fabricating_weight()
    {
        using var item = CreateItem(WeightDisplayUnit.Kilograms, TrackingMode.Bodyweight, null, null, 12, null, null, 15);

        Assert.Equal("Last 12 reps", item.LastText);
        Assert.Equal("Best 15 reps", item.BestText);
    }

    [Fact]
    public void Preference_change_updates_both_text_properties_without_replacing_item()
    {
        var preference = new MutableWeightPreference(WeightDisplayUnit.Kilograms);
        using var item = CreateItem(preference, TrackingMode.Weighted, 70.125m, null, 8, 72.5m, null, 6);
        var raised = new List<string?>();
        item.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        preference.Set(WeightDisplayUnit.Pounds);

        Assert.Equal("Last 154.60 lb × 8", item.LastText);
        Assert.Equal("Best 159.84 lb × 6", item.BestText);
        Assert.Contains(nameof(HomeMomentumItem.LastText), raised);
        Assert.Contains(nameof(HomeMomentumItem.BestText), raised);
    }

    [Fact]
    public void Dispose_unsubscribes_from_the_shared_weight_preference()
    {
        var preference = new MutableWeightPreference(WeightDisplayUnit.Kilograms);
        var item = CreateItem(preference, TrackingMode.Weighted, 70m, null, 8, 72m, null, 6);
        var raised = new List<string?>();
        item.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        item.Dispose();
        preference.Set(WeightDisplayUnit.Pounds);

        Assert.Empty(raised);
    }

    [Fact]
    public void Resource_contract_has_distinct_english_and_thai_momentum_copy()
    {
        Assert.Equal("Last", GamificationResources.English.Last);
        Assert.Equal("Best", GamificationResources.English.Best);
        Assert.Equal("assistance", GamificationResources.English.Assistance);
        Assert.Equal("ล่าสุด", GamificationResources.Thai.Last);
        Assert.Equal("สูงสุด", GamificationResources.Thai.Best);
        Assert.Equal("น้ำหนักช่วย", GamificationResources.Thai.Assistance);
    }

    private static HomeMomentumItem CreateItem(
        WeightDisplayUnit unit,
        TrackingMode mode,
        decimal? lastWeight,
        decimal? lastAssisted,
        int lastReps,
        decimal? bestWeight,
        decimal? bestAssisted,
        int bestReps) => CreateItem(
        new MutableWeightPreference(unit), mode, lastWeight, lastAssisted, lastReps, bestWeight, bestAssisted, bestReps);

    private static HomeMomentumItem CreateItem(
        IWeightUnitPreference preference,
        TrackingMode mode,
        decimal? lastWeight,
        decimal? lastAssisted,
        int lastReps,
        decimal? bestWeight,
        decimal? bestAssisted,
        int bestReps) => new(
        new ExerciseProgressSummaryDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Bench Press",
            mode,
            DateTimeOffset.Parse("2026-08-20T09:00:00Z"),
            lastWeight,
            lastAssisted,
            lastReps,
            bestWeight,
            bestAssisted,
            bestReps),
        preference,
        GamificationResources.English);

    private sealed class MutableWeightPreference(WeightDisplayUnit current) : IWeightUnitPreference
    {
        public WeightDisplayUnit Current { get; private set; } = current;
        public event EventHandler? Changed;

        public void Set(WeightDisplayUnit unit)
        {
            if (Current == unit) return;
            Current = unit;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
