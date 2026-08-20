using TrackZ.Domain.Exercises;

namespace TrackZ.Mobile.Features.Train;

public sealed record ActiveWorkoutCard(
    Guid WorkoutId,
    DateTimeOffset StartedAt,
    int ExerciseCount,
    int LoggedSetCount);

public sealed record RecentWorkoutShortcut(
    Guid WorkoutId,
    IReadOnlyList<BodyPart> BodyParts,
    DateTimeOffset CompletedAt,
    int ExerciseCount,
    string? ThumbnailPath);

public sealed record TrainDashboardSnapshot(
    ActiveWorkoutCard? Active,
    IReadOnlyList<RecentWorkoutShortcut> Recent);

public interface ITrainDashboardSource
{
    Task<TrainDashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IBodyAreaPicker
{
    Task<BodyPart?> PickAsync(CancellationToken cancellationToken = default);
}
