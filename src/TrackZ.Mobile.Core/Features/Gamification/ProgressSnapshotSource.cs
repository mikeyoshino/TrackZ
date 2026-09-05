using System.Net.Http.Json;
using System.Text.Json;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Features.Profile;

namespace TrackZ.Mobile.Features.Gamification;

public interface IProgressApi
{
    Task<ProgressSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<GamificationProfileDto> GetProfileAsync(CancellationToken cancellationToken = default);
    Task<GamificationProfileDto> UpdatePreferencesAsync(int weeklyGoal, string timeZoneId, CancellationToken cancellationToken = default);
}

public sealed class TrackZProgressApiClient(HttpClient httpClient) : IProgressApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ProgressSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<ProgressSummaryDto>("/api/v1/progress/summary", cancellationToken);

    public Task<GamificationProfileDto> GetProfileAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<GamificationProfileDto>("/api/v1/gamification/profile", cancellationToken);

    public async Task<GamificationProfileDto> UpdatePreferencesAsync(
        int weeklyGoal,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            "/api/v1/gamification/preferences",
            new UpdateMotivationPreferencesRequest(weeklyGoal, timeZoneId),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GamificationProfileDto>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The gamification profile response was empty.");
    }

    private async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The progress response was empty.");
    }
}

public sealed class ProgressSnapshotCache(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.GetFullPath(path);

    public async Task<ProgressSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return null;
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<ProgressSnapshot>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("The cached progress snapshot is invalid.");
        }
        finally { _gate.Release(); }
    }

    public async Task WriteAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { if (File.Exists(_path)) File.Delete(_path); }
        finally { _gate.Release(); }
    }
}

public sealed class ProgressSnapshotSource(
    IProgressApi api,
    ProgressSnapshotCache cache,
    TimeProvider timeProvider,
    IAccountSessionBoundary boundary,
    TrainingScheduleStore? schedules = null) : IProgressSnapshotSource
{
    public async Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default)
    {
        var generation = boundary.Capture();
        var snapshot = await cache.ReadAsync(cancellationToken);
        return boundary.IsCancellationRequested(generation) || snapshot is null ? null
            : await WithScheduleAsync(snapshot, generation, cancellationToken);
    }

    public async Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var generation = boundary.Capture();
        var summary = await api.GetSummaryAsync(cancellationToken);
        var profile = await api.GetProfileAsync(cancellationToken);
        var snapshot = new ProgressSnapshot(summary, profile, timeProvider.GetUtcNow());
        if (!await boundary.TryCommitAsync(
                generation,
                token => cache.WriteAsync(snapshot, token),
                cancellationToken))
            throw new OperationCanceledException("The account session changed while progress was refreshing.");
        return await WithScheduleAsync(snapshot, generation, cancellationToken);
    }

    public async Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default)
    {
        if (weeklyGoal is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(weeklyGoal));
        var generation = boundary.Capture();
        var profile = await api.UpdatePreferencesAsync(weeklyGoal, TimeZoneInfo.Local.Id, cancellationToken);
        var current = await cache.ReadAsync(cancellationToken);
        var summary = current?.Summary ?? await api.GetSummaryAsync(cancellationToken);
        var snapshot = new ProgressSnapshot(summary, profile, timeProvider.GetUtcNow());
        if (!await boundary.TryCommitAsync(
                generation,
                token => cache.WriteAsync(snapshot, token),
                cancellationToken))
            throw new OperationCanceledException("The account session changed while preferences were saving.");
        return await WithScheduleAsync(snapshot, generation, cancellationToken);
    }

    private async Task<ProgressSnapshot> WithScheduleAsync(ProgressSnapshot snapshot,
        AccountSessionGeneration generation, CancellationToken token)
    {
        if (schedules is null) return snapshot;
        var plan = await schedules.ReadAsync(token);
        if (boundary.IsCancellationRequested(generation)) throw new OperationCanceledException();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZoneInfo.Local).DateTime);
        return snapshot with { Profile = snapshot.Profile with { WeeklyGoal = plan.GoalForWeek(today, snapshot.Profile.WeeklyGoal) } };
    }
}

public sealed class CompletedWorkoutSummarySource(ILocalWorkoutRepository workouts) : ICompletedWorkoutSummarySource
{
    public async Task<CompletedWorkoutSummary?> GetAsync(Guid workoutId, CancellationToken cancellationToken = default)
    {
        var workout = await workouts.GetHistoryWorkoutAsync(workoutId, cancellationToken);
        if (workout is null) return null;
        var sets = workout.Exercises
            .Where(exercise => exercise.DeletedAt is null)
            .SelectMany(exercise => exercise.Sets
                .Where(set => set.DeletedAt is null)
                .Select(set => new { exercise.TrackingMode, Set = set }))
            .ToArray();
        return new CompletedWorkoutSummary(
            workout.Id,
            sets.Where(item => item.TrackingMode == TrackingMode.Weighted)
                .Sum(item => (item.Set.WeightKg ?? 0m) * item.Set.Reps),
            sets.Length,
            sets.Sum(item => item.Set.Reps));
    }
}
