using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Muscles;

public enum MuscleTrainingStatus { NoRecord, Secondary, Primary }
public sealed record MuscleRegion(string Id, BodyPart BodyPart, string ThaiName, string EnglishName);
public sealed record MuscleSetFact(Guid SetId, Guid ExerciseId, DateTimeOffset At, bool? IsWarmup,
    bool IsCustom = false);
public sealed record MuscleRegionCoverage(string Id, BodyPart BodyPart, string ThaiName, string EnglishName,
    int PrimarySets, int SecondarySets)
{
    public MuscleTrainingStatus Status => PrimarySets > 0 ? MuscleTrainingStatus.Primary
        : SecondarySets > 0 ? MuscleTrainingStatus.Secondary : MuscleTrainingStatus.NoRecord;
}
public sealed record MuscleCoverageReport(DateOnly Start, DateOnly End, string TimeZoneId, int RulesVersion,
    int WorkingSets, int UnclassifiedSets, int UnmappedSets, IReadOnlyList<MuscleRegionCoverage> Regions);

/// <summary>Training exposure, not activation percentages, muscle growth or a diagnosis.</summary>
public static class MuscleCoverageCalculator
{
    public static MuscleCoverageReport Build(IEnumerable<MuscleSetFact> facts, DateOnly start, DateOnly end,
        TimeZoneInfo zone, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(zone);
        if (end < start || end.DayNumber - start.DayNumber > 365)
            throw new ArgumentOutOfRangeException(nameof(end));
        var sets = facts.Where(s => s.At <= now)
            .Where(s => { var d = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.At, zone).DateTime); return d >= start && d <= end; })
            .DistinctBy(s => s.SetId).ToArray();
        var working = sets.Where(s => s.IsWarmup == false).ToArray();
        var mapped = working.Select(s => s.IsCustom ? null : MuscleCatalog.Find(s.ExerciseId)).ToArray();
        return new(start, end, zone.Id, MuscleCatalog.Version, working.Length,
            sets.Count(s => s.IsWarmup is null), mapped.Count(m => m is null),
            MuscleCatalog.Regions.Select(region => new MuscleRegionCoverage(region.Id, region.BodyPart,
                region.ThaiName, region.EnglishName,
                mapped.Count(m => m?.Primary.Contains(region.Id) == true),
                mapped.Count(m => m?.Secondary.Contains(region.Id) == true && !m.Primary.Contains(region.Id))))
                .ToArray());
    }
}
