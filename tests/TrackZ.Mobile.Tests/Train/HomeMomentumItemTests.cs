using System.ComponentModel;
using System.Globalization;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Train;

public sealed class HomeMomentumItemTests
{
    [Theory]
    [InlineData("en-US", "75 kg × 8 reps", "80 kg × 8 reps")]
    [InlineData("th-TH", "75 กก. × 8 ครั้ง", "80 กก. × 8 ครั้ง")]
    public void Weighted_latest_and_best_are_factual_and_localized(
        string cultureName,
        string expectedLatest,
        string expectedBest)
    {
        using var item = CreateItem(
            CultureInfo.GetCultureInfo(cultureName),
            new MutableWeightPreference(WeightDisplayUnit.Kilograms),
            TrackingMode.Weighted,
            75m,
            null,
            8,
            80m,
            null,
            8);

        Assert.Equal(expectedLatest, item.LatestValueText);
        Assert.Equal(expectedBest, item.BestValueText);
    }

    [Theory]
    [InlineData("en-US", "20 kg assistance × 8 reps", "15.5 kg assistance × 10 reps")]
    [InlineData("th-TH", "แรงช่วย 20 กก. × 8 ครั้ง", "แรงช่วย 15.5 กก. × 10 ครั้ง")]
    public void Assisted_latest_and_best_are_factual_and_localized(
        string cultureName,
        string expectedLatest,
        string expectedBest)
    {
        using var item = CreateItem(
            CultureInfo.GetCultureInfo(cultureName),
            new MutableWeightPreference(WeightDisplayUnit.Kilograms),
            TrackingMode.Assisted,
            null,
            20m,
            8,
            null,
            15.5m,
            10);

        Assert.Equal(expectedLatest, item.LatestValueText);
        Assert.Equal(expectedBest, item.BestValueText);
    }

    [Theory]
    [InlineData("en-US", "12 reps", "15 reps")]
    [InlineData("th-TH", "12 ครั้ง", "15 ครั้ง")]
    public void Bodyweight_latest_and_best_are_factual_and_localized(
        string cultureName,
        string expectedLatest,
        string expectedBest)
    {
        using var item = CreateItem(
            CultureInfo.GetCultureInfo(cultureName),
            new MutableWeightPreference(WeightDisplayUnit.Kilograms),
            TrackingMode.Bodyweight,
            null,
            null,
            12,
            null,
            null,
            15);

        Assert.Equal(expectedLatest, item.LatestValueText);
        Assert.Equal(expectedBest, item.BestValueText);
    }

    [Fact]
    public void Preference_change_updates_both_text_properties_without_replacing_item()
    {
        var preference = new MutableWeightPreference(WeightDisplayUnit.Kilograms);
        using var item = CreateItem(
            CultureInfo.GetCultureInfo("en-US"),
            preference,
            TrackingMode.Weighted,
            70.125m,
            null,
            8,
            72.5m,
            null,
            6);
        var raised = new List<string?>();
        item.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        preference.Set(WeightDisplayUnit.Pounds);

        Assert.Equal("154.60 lb × 8 reps", item.LatestValueText);
        Assert.Equal("159.84 lb × 6 reps", item.BestValueText);
        Assert.Contains(nameof(HomeMomentumItem.LatestValueText), raised);
        Assert.Contains(nameof(HomeMomentumItem.BestValueText), raised);
    }

    [Fact]
    public void Dispose_unsubscribes_from_the_shared_weight_preference()
    {
        var preference = new MutableWeightPreference(WeightDisplayUnit.Kilograms);
        var item = CreateItem(
            CultureInfo.GetCultureInfo("en-US"),
            preference,
            TrackingMode.Weighted,
            70m,
            null,
            8,
            72m,
            null,
            6);
        var raised = new List<string?>();
        item.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        item.Dispose();
        preference.Set(WeightDisplayUnit.Pounds);

        Assert.Empty(raised);
    }

    private static HomeMomentumItem CreateItem(
        CultureInfo culture,
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
        WorkoutResources.ForCulture(culture));

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
