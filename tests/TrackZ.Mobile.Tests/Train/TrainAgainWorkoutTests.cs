using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Train;

public sealed class TrainAgainWorkoutTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"trackz-train-again-{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(_root, "workouts.db");
    private string ExerciseCachePath => Path.Combine(_root, "exercises.db");

    public TrainAgainWorkoutTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Train_again_recreates_exact_ordered_modes_without_sets_after_sqlite_restart()
    {
        var weightedId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var assistedId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var bodyweightId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var selections = new WorkoutExerciseSelection[]
        {
            new(weightedId, TrackingMode.Weighted),
            new(assistedId, TrackingMode.Assisted),
            new(bodyweightId, TrackingMode.Bodyweight)
        };
        var seedCache = new ExerciseCache(ExerciseCachePath);
        await seedCache.ReplaceAllAsync([
            Exercise(weightedId, "Bench Press", BodyPart.Chest, TrackingMode.Weighted),
            Exercise(assistedId, "Assisted Pull-up", BodyPart.Back, TrackingMode.Assisted),
            Exercise(bodyweightId, "Push-up", BodyPart.Chest, TrackingMode.Bodyweight)
        ], new DateTimeOffset(2026, 8, 22, 7, 0, 0, TimeSpan.Zero));
        var seedBoundary = new AccountSessionBoundary();
        var seedDatabase = new TrackZLocalDatabase(DatabasePath);
        var seedRepository = new LocalWorkoutRepository(seedDatabase);
        var seedCoordinator = new ActiveWorkoutCoordinator(seedRepository, seedBoundary, new FixedClock());
        var completedId = (await seedCoordinator.StartAsync(selections)).Id;
        await seedCoordinator.SaveSetAsync(weightedId, new LocalSet(82.5m, null, 8));
        await seedCoordinator.SaveSetAsync(assistedId, new LocalSet(null, 24m, 10));
        await seedCoordinator.SaveSetAsync(bodyweightId, new LocalSet(null, null, 15));
        var completedBeforeRestart = await seedCoordinator.FinishAsync();

        Assert.Equal(completedId, completedBeforeRestart.Id);
        Assert.Equal(LocalWorkoutStatus.Completed, completedBeforeRestart.Status);
        Assert.Equal(3, completedBeforeRestart.Exercises.Sum(item => item.Sets.Count));

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var boundary = new AccountSessionBoundary();
        var navigator = new RecordingTrainNavigator();
        var restartedDatabase = new TrackZLocalDatabase(DatabasePath);
        var restartedRepository = new LocalWorkoutRepository(restartedDatabase);
        var restartedCache = new ExerciseCache(ExerciseCachePath);
        var source = new LocalTrainDashboardSource(restartedRepository, restartedCache);
        var viewModel = new TrainTodayViewModel(
            source,
            boundary,
            WorkoutResources.English,
            progress: null,
            connectivity: null,
            weightUnits: new FixedWeightUnitPreference(),
            gamificationText: GamificationResources.English,
            activeWorkouts: new ActiveWorkoutCoordinator(
                restartedRepository,
                boundary,
                new FixedClock()),
            navigator: navigator);
        await viewModel.LoadAsync();

        Assert.Equal(completedId, viewModel.RepeatWorkout?.SourceWorkoutId);
        Assert.Equal(
            selections,
            viewModel.RepeatWorkout!.Selections);

        await Task.WhenAll(
            viewModel.TrainAgainCommand.ExecuteAsync(),
            viewModel.TrainAgainCommand.ExecuteAsync());

        var active = await restartedRepository.GetActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(
            [weightedId, assistedId, bodyweightId],
            active.Exercises.OrderBy(item => item.Order).Select(item => item.ExerciseDefinitionId));
        Assert.Equal(
            [TrackingMode.Weighted, TrackingMode.Assisted, TrackingMode.Bodyweight],
            active.Exercises.OrderBy(item => item.Order).Select(item => item.TrackingMode));
        Assert.Empty(active.Exercises.SelectMany(item => item.Sets));
        Assert.Single(
            await new OutboxRepository(restartedDatabase).PendingAsync(),
            item => item.Type == OutboxOperationType.StartWorkout && item.EntityId == active.Id);
        Assert.Equal(1, navigator.OpenActiveWorkoutCount);
        Assert.NotEqual(completedId, active.Id);

        var originalHistory = Assert.Single(
            await restartedRepository.GetHistoryAsync(),
            item => item.Id == completedId);
        Assert.Equal(LocalWorkoutStatus.Completed, originalHistory.Status);
        Assert.Equal(completedBeforeRestart.CompletedAt, originalHistory.CompletedAt);
        Assert.Equal(
            [82.5m, null, null],
            originalHistory.Exercises.OrderBy(item => item.Order)
                .Select(item => item.Sets.Single().WeightKg));
        Assert.Equal(
            [null, 24m, null],
            originalHistory.Exercises.OrderBy(item => item.Order)
                .Select(item => item.Sets.Single().AssistedKg));
        Assert.Equal(
            [8, 10, 15],
            originalHistory.Exercises.OrderBy(item => item.Order)
                .Select(item => item.Sets.Single().Reps));
    }

    [Fact]
    public async Task Train_again_failure_does_not_navigate_or_create_a_partial_workout()
    {
        var source = new MutableTrainDashboardSource(new(
            null,
            new(Guid.NewGuid(), [BodyPart.Chest], DateTimeOffset.UtcNow, 1, 0, null,
                [new WorkoutExerciseSelection(Guid.Empty, TrackingMode.Weighted)])));
        var boundary = new AccountSessionBoundary();
        var database = new TrackZLocalDatabase(DatabasePath);
        var navigator = new RecordingTrainNavigator();
        var viewModel = CreateViewModel(source, boundary, database, navigator);
        await viewModel.LoadAsync();

        await viewModel.TrainAgainCommand.ExecuteAsync();

        Assert.Null(await new LocalWorkoutRepository(database).GetActiveAsync());
        Assert.Empty(await new OutboxRepository(database).PendingAsync());
        Assert.Equal(0, navigator.OpenActiveWorkoutCount);
        Assert.Equal("Could not repeat that workout. Try again.", viewModel.ErrorText);
        Assert.NotEqual(WorkoutResources.English.HomeLoadFailed, viewModel.ErrorText);
        Assert.True(viewModel.ShowTrainAgain);
        Assert.True(viewModel.CanMutate);
        Assert.True(viewModel.TrainAgainCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reload_that_removes_repeat_makes_train_again_unavailable()
    {
        var source = new MutableTrainDashboardSource(new(
            null,
            new(Guid.NewGuid(), [BodyPart.Chest], DateTimeOffset.UtcNow, 1, 0, null,
                [new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)])));
        var viewModel = CreateViewModel(
            source,
            new AccountSessionBoundary(),
            new TrackZLocalDatabase(DatabasePath),
            new RecordingTrainNavigator());
        await viewModel.LoadAsync();
        Assert.True(viewModel.ShowTrainAgain);

        source.Set(new TrainDashboardSnapshot(null, null));
        await viewModel.LoadAsync();

        Assert.False(viewModel.ShowTrainAgain);
        Assert.False(viewModel.TrainAgainCommand.CanExecute(null));
    }

    [Fact]
    public async Task Account_reset_while_repeat_start_is_committing_prevents_stale_navigation()
    {
        var source = new MutableTrainDashboardSource(new(
            null,
            new(Guid.NewGuid(), [BodyPart.Back], DateTimeOffset.UtcNow, 1, 0, null,
                [new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)])));
        var boundary = new AccountSessionBoundary();
        var database = new TrackZLocalDatabase(DatabasePath);
        var gatedRepository = new GatedSaveRepository(new LocalWorkoutRepository(database));
        var navigator = new RecordingTrainNavigator();
        var viewModel = new TrainTodayViewModel(
            source,
            boundary,
            WorkoutResources.English,
            progress: null,
            connectivity: null,
            weightUnits: new FixedWeightUnitPreference(),
            gamificationText: GamificationResources.English,
            activeWorkouts: new ActiveWorkoutCoordinator(gatedRepository, boundary, new FixedClock()),
            navigator: navigator);
        await viewModel.LoadAsync();

        var repeat = viewModel.TrainAgainCommand.ExecuteAsync();
        await gatedRepository.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var reset = boundary.ResetAsync(_ => Task.CompletedTask);
        gatedRepository.Release();
        await Task.WhenAll(repeat, reset);

        Assert.Equal(0, navigator.OpenActiveWorkoutCount);
        Assert.False(viewModel.CanMutate);
        Assert.Null(viewModel.ActiveWorkout);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var restartedDatabase = new TrackZLocalDatabase(DatabasePath);
        Assert.Null(await new LocalWorkoutRepository(restartedDatabase).GetActiveAsync());
        Assert.DoesNotContain(
            await new OutboxRepository(restartedDatabase).PendingAsync(),
            item => item.Type == OutboxOperationType.StartWorkout);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private static ExerciseSummaryDto Exercise(
        Guid id,
        string name,
        BodyPart bodyPart,
        TrackingMode trackingMode) =>
        new(id, name, bodyPart, trackingMode, null, null, null, null, false);

    private static TrainTodayViewModel CreateViewModel(
        ITrainDashboardSource source,
        IAccountSessionBoundary boundary,
        TrackZLocalDatabase database,
        ITrainNavigator navigator) => new(
        source,
        boundary,
        WorkoutResources.English,
        progress: null,
        connectivity: null,
        weightUnits: new FixedWeightUnitPreference(),
        gamificationText: GamificationResources.English,
        activeWorkouts: new ActiveWorkoutCoordinator(
            new LocalWorkoutRepository(database), boundary, new FixedClock()),
        navigator: navigator);

    private sealed class MutableTrainDashboardSource(TrainDashboardSnapshot snapshot)
        : ITrainDashboardSource
    {
        private TrainDashboardSnapshot _snapshot = snapshot;

        public Task<TrainDashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);

        public void Set(TrainDashboardSnapshot snapshot) => _snapshot = snapshot;
    }

    private sealed class RecordingTrainNavigator : ITrainNavigator
    {
        public int OpenActiveWorkoutCount { get; private set; }

        public Task OpenWorkoutPickerAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OpenActiveWorkoutAsync(CancellationToken cancellationToken = default)
        {
            OpenActiveWorkoutCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedWeightUnitPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
    }

    private sealed class GatedSaveRepository(ILocalWorkoutRepository inner) : ILocalWorkoutRepository
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SaveEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SaveWorkoutAndEnqueueAsync(
            LocalWorkout workout,
            OutboxOperation operation,
            CancellationToken cancellationToken = default)
        {
            SaveEntered.TrySetResult();
            await _release.Task;
            await inner.SaveWorkoutAndEnqueueAsync(workout, operation, cancellationToken);
        }

        public Task<LocalWorkout?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            inner.GetActiveAsync(cancellationToken);

        public Task<bool> IsExerciseHistorySessionInvalidatedAsync(
            Guid workoutId,
            Guid exerciseDefinitionId,
            CancellationToken cancellationToken = default) =>
            inner.IsExerciseHistorySessionInvalidatedAsync(workoutId, exerciseDefinitionId, cancellationToken);

        public Task<OutboxOperation?> GetOperationAsync(
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            inner.GetOperationAsync(operationId, cancellationToken);

        public Task<DateTimeOffset?> GetLatestOperationCreatedAtAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetLatestOperationCreatedAtAsync(cancellationToken);

        public Task ClearPrivateDataAsync(CancellationToken cancellationToken = default) =>
            inner.ClearPrivateDataAsync(cancellationToken);

        public void Release() => _release.TrySetResult();
    }
}
