using System.Globalization;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.History;

public sealed class WorkoutHistoryViewModelTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"trackz-history-vm-{Guid.NewGuid():N}.db");
    private readonly MutableClock _clock = new(
        new DateTimeOffset(2026, 8, 16, 8, 0, 0, TimeSpan.Zero));
    private readonly AccountSessionBoundary _boundary = new();

    [Fact]
    public async Task Edit_and_confirmed_delete_use_stable_ids_and_show_durable_pending_state()
    {
        var completed = await CompletedWorkoutAsync(TrackingMode.Weighted, 70m, null, 10);
        var confirmation = new RecordingConfirmation { Result = false };
        var viewModel = ViewModel(WorkoutResources.English, confirmation);
        await viewModel.LoadAsync();
        var workout = Assert.Single(viewModel.Workouts);
        var set = Assert.Single(Assert.Single(workout.Exercises).Sets);
        Assert.Equal(WorkoutSyncState.Pending, workout.SyncState);

        set.WeightKg = 75.125m;
        set.Reps = 6;
        await viewModel.EditSetCommand.ExecuteAsync(set);
        Assert.Equal(75.125m, set.WeightKg);
        Assert.Equal(6, set.Reps);
        Assert.Equal(completed.Id, workout.WorkoutId);
        Assert.Equal(WorkoutSyncState.Pending, workout.SyncState);

        await viewModel.DeleteSetCommand.ExecuteAsync(set);
        Assert.False(set.IsDeleted);
        Assert.Equal("Delete set?", confirmation.Title);
        Assert.Contains("cannot disappear silently", confirmation.Message);

        confirmation.Result = true;
        await viewModel.DeleteSetCommand.ExecuteAsync(set);
        var reloaded = Assert.Single(viewModel.Workouts);
        Assert.True(Assert.Single(
            Assert.Single(reloaded.Exercises).Sets,
            item => item.SetId == set.SetId).IsDeleted);
        Assert.NotNull(reloaded.LastUndoOperationId);
        Assert.Equal(WorkoutSyncState.Pending, reloaded.SyncState);

        var persisted = Assert.Single(await new WorkoutHistoryCoordinator(
            Repository(), _boundary, _clock).GetHistoryAsync());
        Assert.NotNull(Assert.Single(persisted.Exercises).Sets
            .Single(item => item.Id == set.SetId).DeletedAt);
    }

    [Fact]
    public async Task Thai_delete_confirmation_and_permanent_failure_remain_visible_after_restart()
    {
        _ = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 12);
        var confirmation = new RecordingConfirmation { Result = true };
        var thai = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));
        var first = ViewModel(thai, confirmation);
        await first.LoadAsync();
        var workout = Assert.Single(first.Workouts);
        await first.DeleteWorkoutCommand.ExecuteAsync(workout);
        Assert.Equal("ลบการฝึก?", confirmation.Title);
        Assert.NotNull(Assert.Single(first.Workouts).LastUndoOperationId);

        var deletion = (await new OutboxRepository(Database()).PendingAsync())
            .Single(operation => operation.Type == OutboxOperationType.DeleteWorkout);
        await RejectAsync(deletion.OperationId);
        first.Deactivate();

        var restarted = ViewModel(thai, new RecordingConfirmation());
        await restarted.LoadAsync();
        var rejected = Assert.Single(restarted.Workouts);
        Assert.Equal(WorkoutSyncState.PermanentFailure, rejected.SyncState);
        Assert.Equal(thai.PermanentFailure, rejected.SyncStatusText);
        Assert.NotNull(rejected.LastUndoOperationId);
        Assert.False(restarted.DeleteWorkoutCommand.CanExecute(rejected));
        Assert.False(restarted.EditSetCommand.CanExecute(
            Assert.Single(Assert.Single(rejected.Exercises).Sets)));
    }

    [Fact]
    public async Task Deactivation_and_account_reset_cancel_commands_and_clear_private_rows()
    {
        _ = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 10);
        var viewModel = ViewModel(WorkoutResources.English, new RecordingConfirmation());
        await viewModel.LoadAsync();
        var set = Assert.Single(Assert.Single(Assert.Single(viewModel.Workouts).Exercises).Sets);

        viewModel.Deactivate();
        Assert.False(viewModel.EditSetCommand.CanExecute(set));
        await _boundary.ResetAsync(_ => Task.CompletedTask);
        Assert.Empty(viewModel.Workouts);
    }

    [Fact]
    public async Task Connectivity_transition_is_observable_and_subscription_is_released_on_deactivate()
    {
        _ = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 10);
        var connectivity = new MutableConnectivity(isOnline: true);
        var viewModel = ViewModel(
            WorkoutResources.English, new RecordingConfirmation(), connectivity);
        await viewModel.LoadAsync();
        Assert.Equal(WorkoutSyncState.Pending, Assert.Single(viewModel.Workouts).SyncState);
        Assert.Equal(1, connectivity.SubscriptionCount);

        connectivity.SetOnline(false);
        Assert.Equal(WorkoutSyncState.Offline, Assert.Single(viewModel.Workouts).SyncState);
        Assert.Equal(WorkoutResources.English.Offline, Assert.Single(viewModel.Workouts).SyncStatusText);

        connectivity.SetOnline(true);
        Assert.Equal(WorkoutSyncState.Pending, Assert.Single(viewModel.Workouts).SyncState);
        viewModel.Deactivate();
        Assert.Equal(0, connectivity.SubscriptionCount);
    }

    [Fact]
    public async Task Permanent_completion_failure_after_restart_disables_repeat_mutations_but_keeps_undo()
    {
        var completed = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 10);
        var completion = (await new OutboxRepository(Database()).PendingAsync())
            .Single(operation => operation.Type == OutboxOperationType.CompleteWorkout);
        await RejectAsync(completion.OperationId);
        var restarted = ViewModel(
            WorkoutResources.English,
            new RecordingConfirmation(),
            new MutableConnectivity(isOnline: true));

        await restarted.LoadAsync();

        var workout = Assert.Single(restarted.Workouts);
        var set = Assert.Single(Assert.Single(workout.Exercises).Sets);
        Assert.Equal(completed.Id, workout.WorkoutId);
        Assert.Equal(WorkoutSyncState.PermanentFailure, workout.SyncState);
        Assert.False(restarted.EditSetCommand.CanExecute(set));
        Assert.False(restarted.DeleteSetCommand.CanExecute(set));
        Assert.False(restarted.DeleteWorkoutCommand.CanExecute(workout));
        Assert.True(restarted.UndoCommand.CanExecute(workout));
    }

    [Fact]
    public async Task Account_reset_during_confirmation_cancels_old_session_delete_before_mutation()
    {
        _ = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 10);
        var confirmation = new GatedConfirmation();
        var viewModel = ViewModel(WorkoutResources.English, confirmation);
        await viewModel.LoadAsync();
        var set = Assert.Single(Assert.Single(Assert.Single(viewModel.Workouts).Exercises).Sets);

        var deleting = viewModel.DeleteSetCommand.ExecuteAsync(set);
        await confirmation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _boundary.ResetAsync(_ => Task.CompletedTask);
        confirmation.Release.TrySetResult(true);
        await deleting;

        Assert.DoesNotContain(await new OutboxRepository(Database()).PendingAsync(),
            operation => operation.Type == OutboxOperationType.DeleteSet);
        var unchanged = Assert.Single(await new WorkoutHistoryCoordinator(
            Repository(), _boundary, _clock).GetHistoryAsync());
        Assert.Null(Assert.Single(Assert.Single(unchanged.Exercises).Sets).DeletedAt);
    }

    [Fact]
    public async Task Assisted_editor_uses_assistance_field_and_conflict_actions_keep_stable_operation()
    {
        var completed = await CompletedWorkoutAsync(TrackingMode.Assisted, null, 25m, 10);
        var operationId = Guid.NewGuid();
        var operation = OutboxOperation.Create(
            operationId,
            completed.Id,
            OutboxOperationType.EditSet,
            new EditSetOutboxPayload(
                completed.Id,
                Assert.Single(completed.Exercises).Id,
                Assert.Single(Assert.Single(completed.Exercises).Sets).Id,
                null,
                "20",
                12,
                _clock.UtcNow.AddMinutes(1)),
            completed.Version,
            _clock.UtcNow.AddMinutes(1)) with
        {
            State = OutboxOperationState.Conflicted,
            ServerVersion = 9
        };
        var conflicts = new RecordingConflictResolution();
        var viewModel = new WorkoutHistoryViewModel(
            new WorkoutHistoryCoordinator(Repository(), _boundary, _clock),
            new FixedHistoryStatusSource(operation),
            new RecordingConfirmation(),
            _boundary,
            new MutableConnectivity(isOnline: true),
            WorkoutResources.English,
            conflicts);

        await viewModel.LoadAsync();

        var workout = Assert.Single(viewModel.Workouts);
        var set = Assert.Single(Assert.Single(workout.Exercises).Sets);
        Assert.True(set.IsAssisted);
        Assert.False(set.IsWeighted);
        Assert.Equal(25m, set.AssistedKg);
        Assert.True(workout.HasConflict);
        Assert.True(viewModel.ApplyLocalCommand.CanExecute(workout));
        Assert.True(viewModel.KeepServerCommand.CanExecute(workout));

        await viewModel.ApplyLocalCommand.ExecuteAsync(workout);
        await viewModel.KeepServerCommand.ExecuteAsync(workout);

        Assert.Equal((operationId, 9L), conflicts.Applied);
        Assert.Equal(operationId, conflicts.Kept);
    }

    private async Task<LocalWorkout> CompletedWorkoutAsync(
        TrackingMode mode,
        decimal? weight,
        decimal? assisted,
        int reps)
    {
        var active = new ActiveWorkoutCoordinator(Repository(), _boundary, _clock);
        var exerciseId = Guid.NewGuid();
        await active.StartAsync([new WorkoutExerciseSelection(exerciseId, mode)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await active.SaveSetAsync(exerciseId, new LocalSet(weight, assisted, reps));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        return await active.FinishAsync();
    }

    private WorkoutHistoryViewModel ViewModel(
        WorkoutTextSet text,
        IHistoryConfirmation confirmation,
        IConnectivityService? connectivity = null) => new(
        new WorkoutHistoryCoordinator(Repository(), _boundary, _clock),
        new OutboxRepository(Database()),
        confirmation,
        _boundary,
        connectivity ?? new MutableConnectivity(isOnline: true),
        text,
        new RecordingConflictResolution());

    private TrackZLocalDatabase Database() => new(_path);
    private LocalWorkoutRepository Repository() => new(Database());

    private async Task RejectAsync(Guid operationId)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboxOperation
            SET State = 3, DeletedAt = $at, Version = Version + 1
            WHERE OperationId = $id;
            """;
        command.Parameters.AddWithValue("$at", _clock.UtcNow.AddMinutes(1).ToString("O"));
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _path + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
        return ValueTask.CompletedTask;
    }

    private sealed class RecordingConfirmation : IHistoryConfirmation
    {
        public bool Result { get; set; }
        public string? Title { get; private set; }
        public string? Message { get; private set; }

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string accept,
            string cancel,
            CancellationToken cancellationToken = default)
        {
            Title = title;
            Message = message;
            return Task.FromResult(Result);
        }
    }

    private sealed class FixedHistoryStatusSource(params OutboxOperation[] operations)
        : IHistoryOutboxStatusSource
    {
        public Task<IReadOnlyList<OutboxOperation>> ForHistoryWorkoutAsync(
            Guid workoutId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxOperation>>(
                operations.Where(item => item.EntityId == workoutId).ToArray());
    }

    private sealed class GatedConfirmation : IHistoryConfirmation
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> ConfirmAsync(
            string title,
            string message,
            string accept,
            string cancel,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class RecordingConflictResolution : IConflictResolution
    {
        public (Guid OperationId, long ServerVersion)? Applied { get; private set; }
        public Guid? Kept { get; private set; }

        public Task<OutboxOperation> ApplyLocalAgainstVersionAsync(
            Guid operationId,
            long serverVersion,
            CancellationToken cancellationToken = default)
        {
            Applied = (operationId, serverVersion);
            return Task.FromResult(OutboxOperation.Create(
                Guid.NewGuid(), Guid.NewGuid(), OutboxOperationType.EditSet,
                new { value = 1 }, serverVersion, DateTimeOffset.UtcNow));
        }

        public Task KeepServerAsync(
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            Kept = operationId;
            return Task.CompletedTask;
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class MutableConnectivity(bool isOnline) : IConnectivityService
    {
        private EventHandler? _changed;

        public bool IsOnline { get; private set; } = isOnline;
        public int SubscriptionCount { get; private set; }

        public event EventHandler? ConnectivityChanged
        {
            add
            {
                _changed += value;
                SubscriptionCount++;
            }
            remove
            {
                _changed -= value;
                SubscriptionCount--;
            }
        }

        public void SetOnline(bool isOnline)
        {
            IsOnline = isOnline;
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
