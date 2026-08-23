using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Train;

public sealed record ActiveWorkoutCard(
    Guid WorkoutId,
    DateTimeOffset StartedAt,
    IReadOnlyList<BodyPart> BodyParts,
    int ExerciseCount,
    int LoggedExerciseCount,
    int LoggedSetCount);

public sealed record RepeatWorkoutShortcut(
    Guid SourceWorkoutId,
    IReadOnlyList<BodyPart> BodyParts,
    DateTimeOffset CompletedAt,
    int ExerciseCount,
    int LoggedSetCount,
    string? ThumbnailPath,
    IReadOnlyList<WorkoutExerciseSelection> Selections);

public sealed record TrainDashboardSnapshot(
    ActiveWorkoutCard? Active,
    RepeatWorkoutShortcut? Repeat);

public interface ITrainDashboardSource
{
    Task<TrainDashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IBodyAreaPicker
{
    Task<BodyPart?> PickAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns application navigation initiated from Momentum Home. The Core view model never reaches
/// into Shell directly, which keeps its state transitions testable and generation-fenced.
/// </summary>
public interface ITrainNavigator
{
    Task OpenWorkoutPickerAsync(CancellationToken cancellationToken = default);
    Task OpenActiveWorkoutAsync(CancellationToken cancellationToken = default);
    Task OpenProgressAsync(Guid exerciseId, CancellationToken cancellationToken = default);
}
