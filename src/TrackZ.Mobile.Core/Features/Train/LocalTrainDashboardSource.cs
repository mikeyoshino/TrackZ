using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises.Data;

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

        var activeCard = active is null
            ? null
            : new ActiveWorkoutCard(
                active.Id,
                active.StartedAt,
                active.Exercises.Count(item => item.DeletedAt is null),
                active.Exercises
                    .Where(item => item.DeletedAt is null)
                    .Sum(item => item.Sets.Count(set => set.DeletedAt is null)));

        var recent = history
            .Where(item => item.DeletedAt is null && item.CompletedAt is not null)
            .OrderByDescending(item => item.CompletedAt)
            .ThenByDescending(item => item.Id)
            .Take(3)
            .Select(workout =>
            {
                var liveExercises = workout.Exercises
                    .Where(item => item.DeletedAt is null)
                    .OrderBy(item => item.Order)
                    .ToArray();
                var matched = liveExercises
                    .Select(item => definitions.GetValueOrDefault(item.ExerciseDefinitionId))
                    .Where(item => item is not null)
                    .ToArray();
                return new RecentWorkoutShortcut(
                    workout.Id,
                    matched.Select(item => item!.BodyPart).Distinct().ToArray(),
                    workout.CompletedAt!.Value,
                    liveExercises.Length,
                    matched.Select(item => item!.ThumbnailUri)
                        .FirstOrDefault(IsLocalThumbnail));
            })
            .ToArray();

        return new TrainDashboardSnapshot(activeCard, recent);
    }

    private static bool IsLocalThumbnail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return !Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsFile;
    }
}
