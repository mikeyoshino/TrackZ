using TrackZ.Domain.Exercises;

namespace TrackZ.Contracts.Exercises;

public sealed record ExerciseSummaryDto
{
    public ExerciseSummaryDto(
        Guid id,
        string name,
        BodyPart bodyPart,
        TrackingMode trackingMode,
        string? thumbnailUrl,
        DateTimeOffset? lastPerformedAt,
        PerformanceSetDto? lastBestSet,
        PerformanceSetDto? allTimeBest,
        bool isCustom)
    {
        Id = id;
        Name = name;
        BodyPart = bodyPart;
        TrackingMode = trackingMode;
        ThumbnailUrl = thumbnailUrl;
        LastPerformedAt = lastPerformedAt?.ToUniversalTime();
        LastBestSet = lastBestSet;
        AllTimeBest = allTimeBest;
        IsCustom = isCustom;
    }

    public Guid Id { get; }

    public string Name { get; }

    public BodyPart BodyPart { get; }

    public TrackingMode TrackingMode { get; }

    public string? ThumbnailUrl { get; }

    public DateTimeOffset? LastPerformedAt { get; }

    public PerformanceSetDto? LastBestSet { get; }

    public PerformanceSetDto? AllTimeBest { get; }

    public bool IsCustom { get; }
}
