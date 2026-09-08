using TrackZ.Domain.Muscles;

namespace TrackZ.Domain.Tests;

public sealed class MuscleCoverageTests
{
    private static readonly Guid Bench = Guid.Parse("56f212f7-36b4-582a-9283-2cbb8fb264ab");
    private static readonly Guid Curl = Guid.Parse("2af9cf0e-7441-58bf-9cea-da3b9e9611d1");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
    private static MuscleSetFact Fact(Guid exercise, bool? warmup = false) =>
        new(Guid.NewGuid(), exercise, Now.AddHours(-1), warmup);

    [Fact]
    public void One_set_has_multiple_regions_but_total_is_not_duplicated()
    {
        var set = Fact(Bench);
        var result = Build(set, set);
        Assert.Equal(1, result.WorkingSets);
        Assert.Equal(1, result.Regions.Single(r => r.Id == "chest-middle").PrimarySets);
        Assert.Equal(1, result.Regions.Single(r => r.Id == "triceps").SecondarySets);
        Assert.Equal(MuscleTrainingStatus.Primary, result.Regions.Single(r => r.Id == "chest-middle").Status);
    }

    [Fact]
    public void Warmup_unknown_custom_and_future_records_do_not_invent_coverage()
    {
        var result = Build(Fact(Bench, true), Fact(Bench, null), Fact(Guid.NewGuid()),
            Fact(Bench) with { At = Now.AddDays(1) }, Fact(Bench) with { IsCustom = true });
        Assert.Equal(2, result.WorkingSets);
        Assert.Equal(1, result.UnclassifiedSets);
        Assert.Equal(2, result.UnmappedSets);
        Assert.All(result.Regions, region => Assert.Equal(MuscleTrainingStatus.NoRecord, region.Status));
    }

    [Fact]
    public void Local_midnight_controls_week_membership()
    {
        var result = Build(Fact(Curl) with { At = DateTimeOffset.Parse("2026-09-06T18:00:00Z") },
            Fact(Curl) with { At = DateTimeOffset.Parse("2026-09-06T16:59:59Z") });
        Assert.Equal(1, result.WorkingSets);
    }

    [Fact]
    public void Primary_takes_precedence_without_merging_secondary_counts()
    {
        var pulldown = Guid.Parse("80cf4c4d-420b-55b1-b534-28508a4a9cec");
        var result = Build(Fact(pulldown), Fact(Curl));
        var biceps = result.Regions.Single(r => r.Id == "biceps");
        Assert.Equal(MuscleTrainingStatus.Primary, biceps.Status);
        Assert.Equal(1, biceps.PrimarySets);
        Assert.Equal(1, biceps.SecondarySets);
    }

    [Fact]
    public void Leg_curl_is_recommended_for_hamstrings_not_quads()
    {
        var id = Guid.Parse("5f70df5c-ebd2-5414-947d-b98e4e23c95e");
        Assert.Contains(id, MuscleCatalog.Recommendations("hamstrings"));
        Assert.DoesNotContain(id, MuscleCatalog.Recommendations("quads"));
    }

    private static MuscleCoverageReport Build(params MuscleSetFact[] facts) =>
        MuscleCoverageCalculator.Build(facts, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 13),
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"), Now);
}
