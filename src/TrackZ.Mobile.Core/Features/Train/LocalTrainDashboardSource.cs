using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Domain.Exercises;

namespace TrackZ.Mobile.Features.Train;

public sealed class LocalTrainDashboardSource(
    ILocalWorkoutRepository workouts,
    ExerciseCache exercises) : ITrainDashboardSource
{
    public async Task<TrainDashboardSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var active = await workouts.GetActiveAsync(cancellationToken);
        var history = await workouts.GetHistoryAsync(cancellationToken);
        var definitions = (await exercises.GetAllAsync(cancellationToken))
            .ToDictionary(item => item.Id);

        var liveActive = active?.Exercises
            .Where(item => item.DeletedAt is null)
            .OrderBy(item => item.Order)
            .ToArray() ?? [];
        var activeCard = active is null
            ? null
            : new ActiveWorkoutCard(
                active.Id,
                active.StartedAt,
                ResolveBodyParts(liveActive, definitions),
                liveActive.Length,
                liveActive.Count(item => item.Sets.Any(set => set.DeletedAt is null)),
                liveActive.Sum(item => item.Sets.Count(set => set.DeletedAt is null)));

        RepeatWorkoutShortcut? repeat = null;
        foreach (var workout in history
                     .Where(item => item.DeletedAt is null && item.CompletedAt is not null)
                     .OrderByDescending(item => item.CompletedAt)
                     .ThenByDescending(item => item.Id))
        {
            var liveExercises = workout.Exercises
                .Where(item => item.DeletedAt is null)
                .OrderBy(item => item.Order)
                .ToArray();
            var resolved = liveExercises
                .Select(item => (WorkoutExercise: item, Definition: definitions.GetValueOrDefault(item.ExerciseDefinitionId)))
                .ToArray();
            if (resolved.Any(item => item.Definition is null ||
                                     item.Definition.TrackingMode != item.WorkoutExercise.TrackingMode)) continue;

            repeat = new RepeatWorkoutShortcut(
                workout.Id,
                resolved.Select(item => item.Definition!.BodyPart).Distinct().ToArray(),
                workout.CompletedAt!.Value,
                liveExercises.Length,
                liveExercises.Sum(item => item.Sets.Count(set => set.DeletedAt is null)),
                resolved.Length == 0 || !IsLocalThumbnail(resolved[0].Definition!.ThumbnailUri)
                    ? null
                    : resolved[0].Definition!.ThumbnailUri,
                resolved.Select(item => new WorkoutExerciseSelection(
                    item.WorkoutExercise.ExerciseDefinitionId,
                    item.WorkoutExercise.TrackingMode)).ToArray());
            break;
        }

        return new TrainDashboardSnapshot(activeCard, repeat);
    }

    private static IReadOnlyList<BodyPart> ResolveBodyParts(
        IReadOnlyList<LocalWorkoutExercise> exercises,
        IReadOnlyDictionary<Guid, CachedExercise> definitions) =>
        exercises
            .Select(item => definitions.GetValueOrDefault(item.ExerciseDefinitionId))
            .Where(item => item is not null)
            .Select(item => item!.BodyPart)
            .Distinct()
            .ToArray();

    private static bool IsLocalThumbnail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return !Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsFile;
    }
}
