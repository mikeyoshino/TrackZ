using System.Globalization;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Train;

public sealed class TrainTodayViewModelTests
{
    [Fact]
    public async Task Today_loads_active_workout_and_exact_repeat_shortcut()
    {
        var activeId = Guid.NewGuid();
        var repeatId = Guid.NewGuid();
        var source = new RecordingTrainDashboardSource(new(
            new(activeId, At(9), [BodyPart.Chest], 2, 1, 4),
            new(repeatId, [BodyPart.Shoulders, BodyPart.Back], At(8), 6, 18,
                "/cache/shoulder.png", [
                    new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted),
                    new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Assisted)
                ])));
        var viewModel = new TrainTodayViewModel(
            source,
            new AccountSessionBoundary(),
            WorkoutResources.English);

        await viewModel.LoadAsync();

        Assert.Equal(activeId, viewModel.ActiveWorkout?.WorkoutId);
        Assert.Equal(4, viewModel.ActiveWorkout?.LoggedSetCount);
        Assert.Equal(repeatId, viewModel.RepeatWorkout?.SourceWorkoutId);
        Assert.Single(viewModel.RecentWorkouts);
        Assert.Equal("Shoulders + Back", viewModel.RecentWorkouts[0].Title);
        Assert.Equal(1, source.LoadCount);
    }

    [Theory]
    [InlineData("en-US", "Shoulders + Back")]
    [InlineData("th-TH", "ไหล่ + หลัง")]
    public async Task Recent_title_uses_current_localized_body_part_names(
        string cultureName,
        string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var source = new RecordingTrainDashboardSource(new(
                null,
                new(Guid.NewGuid(), [BodyPart.Shoulders, BodyPart.Back], At(8), 2, 1, null,
                    [new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)])));
            var viewModel = new TrainTodayViewModel(
                source,
                new AccountSessionBoundary(),
                WorkoutResources.Current);

            await viewModel.LoadAsync();

            Assert.Equal(expected, Assert.Single(viewModel.RecentWorkouts).Title);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task Account_reset_during_load_does_not_commit_stale_dashboard()
    {
        var boundary = new AccountSessionBoundary();
        var source = new GatedTrainDashboardSource();
        var viewModel = new TrainTodayViewModel(source, boundary, WorkoutResources.English);
        var load = viewModel.LoadAsync();
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await boundary.ResetAsync(_ => Task.CompletedTask);
        source.Release.TrySetResult(new TrainDashboardSnapshot(
            new(Guid.NewGuid(), At(9), [BodyPart.Chest], 1, 1, 1),
            null));
        await load;

        Assert.Null(viewModel.ActiveWorkout);
        Assert.Empty(viewModel.RecentWorkouts);
    }

    private static DateTimeOffset At(int hour) =>
        new(2026, 8, 20, hour, 0, 0, TimeSpan.Zero);

    private sealed class RecordingTrainDashboardSource(TrainDashboardSnapshot snapshot)
        : ITrainDashboardSource
    {
        public int LoadCount { get; private set; }

        public Task<TrainDashboardSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class GatedTrainDashboardSource : ITrainDashboardSource
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TrainDashboardSnapshot> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TrainDashboardSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return Release.Task;
        }
    }
}
