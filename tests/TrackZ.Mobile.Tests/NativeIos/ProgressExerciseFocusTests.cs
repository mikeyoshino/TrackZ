using System.Globalization;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Progress;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class ProgressExerciseFocusTests
{
    [Fact]
    public async Task Requested_exercise_is_matched_once_after_progress_load()
    {
        var requestedExerciseId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var viewModel = CreateViewModel();
        var page = new ExerciseProgressPage(viewModel);

        page.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["exerciseId"] = requestedExerciseId.ToString("D", CultureInfo.InvariantCulture)
        });
        await viewModel.LoadAsync();

        Assert.Equal(requestedExerciseId, page.ConsumeRequestedExercise()!.ExerciseId);
        Assert.Null(page.ConsumeRequestedExercise());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task Invalid_requested_exercise_is_not_consumed(string exerciseId)
    {
        var page = new ExerciseProgressPage(CreateViewModel());

        page.ApplyQueryAttributes(new Dictionary<string, object> { ["exerciseId"] = exerciseId });

        Assert.Null(page.ConsumeRequestedExercise());
    }

    [Fact]
    public void Missing_requested_exercise_is_not_consumed()
    {
        var page = new ExerciseProgressPage(CreateViewModel());

        page.ApplyQueryAttributes(new Dictionary<string, object>());

        Assert.Null(page.ConsumeRequestedExercise());
    }

    private static ProgressDashboardViewModel CreateViewModel() => new(
        new FixedProgressSnapshotSource(new ProgressSnapshot(
            new ProgressSummaryDto(
                1000m,
                500m,
                2,
                1,
                [
                    Exercise("66666666-6666-6666-6666-666666666666", "Back Squat"),
                    Exercise("77777777-7777-7777-7777-777777777777", "Bench Press")
                ]),
            new GamificationProfileDto(640, 8, 600, 800, 3, 2, 4, 4, [], []),
            new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.Zero))),
        new OfflineConnectivity(),
        new KilogramPreference(),
        new AccountSessionBoundary(),
        GamificationResources.English);

    private static ExerciseProgressSummaryDto Exercise(string id, string name) => new(
        Guid.Parse(id),
        name,
        TrackingMode.Weighted,
        new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero),
        70m,
        null,
        8,
        72.5m,
        null,
        6);

    private sealed class FixedProgressSnapshotSource(ProgressSnapshot snapshot) : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(snapshot);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class KilogramPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
    }
}
