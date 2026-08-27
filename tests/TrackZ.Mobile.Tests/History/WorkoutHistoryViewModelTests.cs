using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.History;

public sealed class WorkoutHistoryViewModelTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"trackz-history-vm-{Guid.NewGuid():N}.db");
    private readonly string _exercisePath = Path.Combine(
        Path.GetTempPath(), $"trackz-history-exercises-{Guid.NewGuid():N}.db");
    private readonly MutableClock _clock = new(
        new DateTimeOffset(2026, 8, 16, 8, 0, 0, TimeSpan.Zero));
    private readonly AccountSessionBoundary _boundary = new();

    [Fact]
    public async Task Detail_loads_one_workout_reloads_after_edit_and_clears_on_account_reset()
    {
        var completed = await CompletedWorkoutAsync(TrackingMode.Weighted, 70m, null, 10);
        var detail = new WorkoutHistoryDetailViewModel(
            ViewModel(WorkoutResources.English, new RecordingConfirmation { Result = true }),
            _boundary);

        await detail.LoadAsync(completed.Id);
        var set = Assert.Single(Assert.Single(detail.Workout!.Exercises).Sets);
        set.WeightKg = 72.5m;
        await detail.EditSetCommand.ExecuteAsync(set);

        Assert.Equal(72.5m, Assert.Single(Assert.Single(detail.Workout!.Exercises).Sets).WeightKg);
        await _boundary.ResetAsync(_ => Task.CompletedTask);
        Assert.Null(detail.Workout);
    }

    [Fact]
    public async Task Root_history_projects_collapsed_month_groups_without_expanding_sets()
    {
        await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 12);
        var viewModel = ViewModel(WorkoutResources.English, new RecordingConfirmation());

        await viewModel.LoadAsync();

        var group = Assert.Single(viewModel.WorkoutGroups);
        var workout = Assert.Single(group);
        Assert.False(workout.IsExpanded);
        Assert.Equal(1, workout.ExerciseCount);
        Assert.Equal(1, workout.SetCount);
        Assert.False(string.IsNullOrWhiteSpace(group.Month));
    }

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
    public async Task History_uses_cached_name_and_persisted_pound_edit_without_losing_canonical_kg()
    {
        var completed = await CompletedWorkoutAsync(TrackingMode.Weighted, 75.125m, null, 10);
        var definitionId = Assert.Single(completed.Exercises).ExerciseDefinitionId;
        await CacheAsync(definitionId, "Incline press", TrackingMode.Weighted);
        var preferenceStore = new DictionaryPreferenceStore();
        var preference = new WeightUnitPreference(preferenceStore);
        preference.Set(WeightDisplayUnit.Pounds);
        var viewModel = ViewModel(
            WorkoutResources.English,
            new RecordingConfirmation(),
            unitPreference: preference);

        await viewModel.LoadAsync();

        var exercise = Assert.Single(Assert.Single(viewModel.Workouts).Exercises);
        var set = Assert.Single(exercise.Sets);
        Assert.Equal("Incline press", exercise.Name);
        Assert.DoesNotContain(definitionId.ToString("D"), exercise.Name);
        Assert.Equal(165.62m, set.DisplayWeight);
        Assert.Equal(75.125m, set.WeightKg);
        Assert.Equal(WorkoutResources.English.Pounds, set.WeightUnitLabel);

        preference.Set(WeightDisplayUnit.Kilograms);
        Assert.Equal(75.125m, set.DisplayWeight);
        Assert.Equal(75.125m, set.WeightKg);
        preference.Set(WeightDisplayUnit.Pounds);
        set.DisplayWeight = 170m;
        await viewModel.EditSetCommand.ExecuteAsync(set);

        var persisted = Assert.Single(await new WorkoutHistoryCoordinator(
            Repository(), _boundary, _clock).GetHistoryAsync());
        Assert.Equal(77.111m, Assert.Single(Assert.Single(persisted.Exercises).Sets).WeightKg);
        Assert.Equal(77.111m, Assert.Single(Assert.Single(viewModel.Workouts).Exercises)
            .Sets.Single().WeightKg);
    }

    [Fact]
    public async Task History_formats_pounds_with_the_same_two_decimal_precision_as_training()
    {
        _ = await CompletedWorkoutAsync(TrackingMode.Weighted, 70.125m, null, 10);
        var preference = new WeightUnitPreference(new DictionaryPreferenceStore());
        preference.Set(WeightDisplayUnit.Pounds);
        var viewModel = ViewModel(
            WorkoutResources.English,
            new RecordingConfirmation(),
            unitPreference: preference);

        await viewModel.LoadAsync();

        var set = Assert.Single(Assert.Single(Assert.Single(viewModel.Workouts).Exercises).Sets);
        Assert.Equal(70.125m, set.WeightKg);
        Assert.Equal("154.60 lb × 10", set.MeasurementText);
    }

    [Fact]
    public async Task Missing_cached_exercise_uses_localized_fallback_instead_of_identifier()
    {
        var completed = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 12);
        var definitionId = Assert.Single(completed.Exercises).ExerciseDefinitionId;
        var viewModel = ViewModel(WorkoutResources.English, new RecordingConfirmation());

        await viewModel.LoadAsync();

        var exercise = Assert.Single(Assert.Single(viewModel.Workouts).Exercises);
        Assert.Equal(WorkoutResources.English.UnknownExercise, exercise.Name);
        Assert.DoesNotContain(definitionId.ToString("D"), exercise.Name);
    }

    [Fact]
    public async Task Confirmed_exercise_delete_disappears_locally_and_undo_restores_it()
    {
        var completed = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 10);
        var definitionId = Assert.Single(completed.Exercises).ExerciseDefinitionId;
        await CacheAsync(definitionId, "Pull-up", TrackingMode.Bodyweight);
        var confirmation = new RecordingConfirmation { Result = false };
        var viewModel = ViewModel(WorkoutResources.English, confirmation);
        await viewModel.LoadAsync();
        var workout = Assert.Single(viewModel.Workouts);
        var exercise = Assert.Single(workout.Exercises);

        await viewModel.DeleteWorkoutExerciseCommand.ExecuteAsync(exercise);
        Assert.Single(Assert.Single(viewModel.Workouts).Exercises);
        Assert.Equal("Delete exercise?", confirmation.Title);

        confirmation.Result = true;
        await viewModel.DeleteWorkoutExerciseCommand.ExecuteAsync(exercise);
        var deleted = Assert.Single(viewModel.Workouts);
        Assert.Empty(deleted.Exercises);
        Assert.NotNull(deleted.LastUndoOperationId);
        var operation = (await new OutboxRepository(Database()).PendingAsync())
            .Single(item => item.Type == OutboxOperationType.DeleteWorkoutExercise);
        Assert.Equal(exercise.WorkoutExerciseId,
            operation.DeserializePayload<DeleteWorkoutExerciseOutboxPayload>().WorkoutExerciseId);

        await viewModel.UndoCommand.ExecuteAsync(deleted);

        Assert.Equal("Pull-up", Assert.Single(Assert.Single(viewModel.Workouts).Exercises).Name);
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
    public async Task Offline_workout_without_durable_sync_work_remains_synced()
    {
        _ = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 10);
        var viewModel = new WorkoutHistoryViewModel(
            new WorkoutHistoryCoordinator(Repository(), _boundary, _clock),
            new FixedHistoryStatusSource(),
            new RecordingConfirmation(),
            _boundary,
            new MutableConnectivity(isOnline: false),
            WorkoutResources.English,
            new RecordingConflictResolution());

        await viewModel.LoadAsync();

        Assert.Equal(WorkoutSyncState.Synced, Assert.Single(viewModel.Workouts).SyncState);
        viewModel.Deactivate();
    }

    [Fact]
    public async Task Completed_sync_refreshes_visible_workout_without_reopening_history()
    {
        _ = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 10);
        var status = new MutableHistoryStatusSource(
            (await new OutboxRepository(Database()).PendingAsync()).ToArray());
        var notifications = new RecordingSyncStatusNotifications();
        using var services = new ServiceCollection()
            .AddSingleton<IWorkoutSyncStatusNotifications>(notifications)
            .AddSingleton<IUiDispatcher, InlineUiDispatcher>()
            .BuildServiceProvider();
        var viewModel = ActivatorUtilities.CreateInstance<WorkoutHistoryViewModel>(
            services,
            new WorkoutHistoryCoordinator(Repository(), _boundary, _clock),
            status,
            new RecordingConfirmation(),
            _boundary,
            new MutableConnectivity(isOnline: true),
            WorkoutResources.English,
            new RecordingConflictResolution());

        await viewModel.LoadAsync();
        Assert.Equal(WorkoutSyncState.Pending, Assert.Single(viewModel.Workouts).SyncState);
        Assert.Equal(1, notifications.SubscriptionCount);

        status.Replace();
        notifications.Raise();

        await EventuallyAsync(() =>
            Assert.Single(viewModel.Workouts).SyncState == WorkoutSyncState.Synced);
        viewModel.Deactivate();
        Assert.Equal(0, notifications.SubscriptionCount);
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
            ServerVersion = 9,
            SendStartedAt = _clock.UtcNow.AddMinutes(1)
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
        Assert.Equal(WorkoutSyncState.Conflicted, workout.SyncState);
        Assert.True(workout.HasConflict);
        Assert.True(viewModel.ApplyLocalCommand.CanExecute(workout));
        Assert.True(viewModel.KeepServerCommand.CanExecute(workout));

        await viewModel.ApplyLocalCommand.ExecuteAsync(workout);
        await viewModel.KeepServerCommand.ExecuteAsync(workout);

        Assert.Equal((operationId, 9L), conflicts.Applied);
        Assert.Equal(operationId, conflicts.Kept);
    }

    [Theory]
    [InlineData("en-US", "Reconciling an earlier sync…")]
    [InlineData("th-TH", "กำลังตรวจสอบผลการซิงค์ก่อนหน้า…")]
    public async Task Ambiguous_sent_replacement_is_localized_and_disables_every_history_action(
        string cultureName,
        string expectedStatus)
    {
        var completed = await CompletedWorkoutAsync(TrackingMode.Bodyweight, null, null, 10);
        var originalId = Guid.NewGuid();
        var original = OutboxOperation.Create(
            originalId,
            completed.Id,
            OutboxOperationType.EditSet,
            new { value = 1 },
            completed.Version,
            _clock.UtcNow.AddMinutes(1)) with
        {
            State = OutboxOperationState.Conflicted,
            ServerVersion = completed.Version + 1
        };
        var replacement = OutboxOperation.Create(
            Guid.NewGuid(),
            completed.Id,
            OutboxOperationType.EditSet,
            new { value = 2 },
            completed.Version + 1,
            _clock.UtcNow.AddMinutes(2)) with
        {
            ReplacesOperationId = originalId,
            SendStartedAt = _clock.UtcNow.AddMinutes(3)
        };
        var connectivity = new MutableConnectivity(isOnline: true);
        var text = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo(cultureName));
        var viewModel = new WorkoutHistoryViewModel(
            new WorkoutHistoryCoordinator(Repository(), _boundary, _clock),
            new FixedHistoryStatusSource(original, replacement),
            new RecordingConfirmation(),
            _boundary,
            connectivity,
            text,
            new RecordingConflictResolution());

        await viewModel.LoadAsync();

        var workout = Assert.Single(viewModel.Workouts);
        var set = Assert.Single(Assert.Single(workout.Exercises).Sets);
        Assert.Equal(WorkoutSyncState.Reconciling, workout.SyncState);
        Assert.Equal(expectedStatus, workout.SyncStatusText);
        Assert.Equal(text.Reconciling, workout.SyncStatusText);
        Assert.True(workout.HasConflict);
        Assert.False(viewModel.EditSetCommand.CanExecute(set));
        Assert.False(viewModel.DeleteSetCommand.CanExecute(set));
        Assert.False(viewModel.DeleteWorkoutCommand.CanExecute(workout));
        Assert.False(viewModel.UndoCommand.CanExecute(workout));
        Assert.False(viewModel.KeepServerCommand.CanExecute(workout));
        Assert.False(viewModel.ApplyLocalCommand.CanExecute(workout));

        connectivity.SetOnline(false);
        Assert.Equal(WorkoutSyncState.Reconciling, Assert.Single(viewModel.Workouts).SyncState);
        Assert.Equal(expectedStatus, Assert.Single(viewModel.Workouts).SyncStatusText);
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
        IConnectivityService? connectivity = null,
        IWeightUnitPreference? unitPreference = null) => new(
        new WorkoutHistoryCoordinator(Repository(), _boundary, _clock),
        new OutboxRepository(Database()),
        confirmation,
        _boundary,
        connectivity ?? new MutableConnectivity(isOnline: true),
        text,
        new RecordingConflictResolution(),
        new ExerciseCache(_exercisePath),
        unitPreference ?? new WeightUnitPreference(new DictionaryPreferenceStore()));

    private Task CacheAsync(Guid id, string name, TrackingMode mode) =>
        new ExerciseCache(_exercisePath).ReplaceAllAsync([
            new ExerciseSummaryDto(
                id, name, BodyPart.Chest, mode, null, null, null, null, false)
        ], _clock.UtcNow);

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
            var exercisePath = _exercisePath + suffix;
            if (File.Exists(exercisePath)) File.Delete(exercisePath);
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

    private sealed class MutableHistoryStatusSource(params OutboxOperation[] operations)
        : IHistoryOutboxStatusSource
    {
        private OutboxOperation[] _operations = operations;

        public void Replace(params OutboxOperation[] replacement) => _operations = replacement;

        public Task<IReadOnlyList<OutboxOperation>> ForHistoryWorkoutAsync(
            Guid workoutId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxOperation>>(
                _operations.Where(item => item.EntityId == workoutId).ToArray());
    }

    private sealed class RecordingSyncStatusNotifications : IWorkoutSyncStatusNotifications
    {
        private EventHandler? _statusChanged;

        public int SubscriptionCount { get; private set; }

        public event EventHandler? StatusChanged
        {
            add { _statusChanged += value; SubscriptionCount++; }
            remove { _statusChanged -= value; SubscriptionCount--; }
        }

        public void Raise() => _statusChanged?.Invoke(this, EventArgs.Empty);
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

    private sealed class DictionaryPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Get(string key) => _values.GetValueOrDefault(key);

        public void Set(string key, string value) => _values[key] = value;
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition(), "The expected asynchronous state was not observed.");
    }
}
