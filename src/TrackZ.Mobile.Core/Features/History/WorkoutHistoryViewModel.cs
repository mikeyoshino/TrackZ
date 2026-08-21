using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Features.History;

public interface IHistoryConfirmation
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept,
        string cancel,
        CancellationToken cancellationToken = default);
}

public sealed class HistorySetItem : INotifyPropertyChanged
{
    private const decimal PoundsPerKilogram = 2.204622621848775807m;
    private readonly IWeightUnitPreference? _unitPreference;
    private readonly WorkoutTextSet _text;
    private decimal? _weightKg;
    private decimal? _assistedKg;
    private int _reps;

    private HistorySetItem(IWeightUnitPreference? unitPreference, WorkoutTextSet text)
    {
        _unitPreference = unitPreference;
        _text = text;
    }

    public required Guid WorkoutId { get; init; }
    public required Guid WorkoutExerciseId { get; init; }
    public required Guid SetId { get; init; }
    public required TrackingMode TrackingMode { get; init; }
    public required int Order { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required bool IsDeleted { get; init; }
    public required bool WorkoutIsDeleted { get; init; }
    public required bool WorkoutActionsBlocked { get; init; }
    public int SetNumber => Order + 1;
    public bool UsesWeight => TrackingMode is TrackingMode.Weighted or TrackingMode.Assisted;
    public bool IsWeighted => TrackingMode == TrackingMode.Weighted;
    public bool IsAssisted => TrackingMode == TrackingMode.Assisted;
    public bool IsBodyweight => TrackingMode == TrackingMode.Bodyweight;

    public decimal? WeightKg
    {
        get => _weightKg;
        set
        {
            if (!Set(ref _weightKg, value)) return;
            OnPropertyChanged(nameof(DisplayWeight));
            OnPropertyChanged(nameof(MeasurementText));
        }
    }

    public decimal? AssistedKg
    {
        get => _assistedKg;
        set
        {
            if (!Set(ref _assistedKg, value)) return;
            OnPropertyChanged(nameof(DisplayWeight));
            OnPropertyChanged(nameof(MeasurementText));
        }
    }

    public int Reps
    {
        get => _reps;
        set
        {
            if (Set(ref _reps, value)) OnPropertyChanged(nameof(MeasurementText));
        }
    }

    public decimal? DisplayWeight
    {
        get
        {
            var kilograms = TrackingMode == TrackingMode.Assisted ? AssistedKg : WeightKg;
            if (kilograms is null) return null;
            return DisplayUnit == WeightDisplayUnit.Kilograms
                ? kilograms
                : decimal.Round(
                    kilograms.Value * PoundsPerKilogram,
                    2,
                    MidpointRounding.AwayFromZero);
        }
        set
        {
            var kilograms = value is null
                ? null
                : DisplayUnit == WeightDisplayUnit.Kilograms
                    ? value
                    : decimal.Round(
                        value.Value / PoundsPerKilogram,
                        3,
                        MidpointRounding.AwayFromZero);
            if (TrackingMode == TrackingMode.Assisted) AssistedKg = kilograms;
            else WeightKg = kilograms;
        }
    }

    public string WeightUnitLabel => DisplayUnit == WeightDisplayUnit.Kilograms
        ? _text.Kilograms
        : _text.Pounds;
    public string MeasurementText => TrackingMode switch
    {
        TrackingMode.Weighted => $"{FormattedDisplayWeight} {WeightUnitLabel} × {Reps}",
        TrackingMode.Assisted => $"{FormattedDisplayWeight} {WeightUnitLabel} · {Reps} {_text.Reps}",
        TrackingMode.Bodyweight => $"{Reps} {_text.Reps}",
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    internal static HistorySetItem From(
        Guid workoutId,
        bool workoutIsDeleted,
        bool workoutActionsBlocked,
        LocalWorkoutExercise exercise,
        LocalSet set,
        IWeightUnitPreference? unitPreference,
        WorkoutTextSet text) => new(unitPreference, text)
    {
        WorkoutId = workoutId,
        WorkoutExerciseId = exercise.Id,
        SetId = set.Id,
        TrackingMode = exercise.TrackingMode,
        Order = set.Order,
        CompletedAt = set.CompletedAt,
        IsDeleted = set.DeletedAt is not null,
        WorkoutIsDeleted = workoutIsDeleted,
        WorkoutActionsBlocked = workoutActionsBlocked,
        WeightKg = set.WeightKg,
        AssistedKg = set.AssistedKg,
        Reps = set.Reps
    };

    internal void RefreshUnit()
    {
        OnPropertyChanged(nameof(DisplayWeight));
        OnPropertyChanged(nameof(WeightUnitLabel));
        OnPropertyChanged(nameof(MeasurementText));
    }

    private WeightDisplayUnit DisplayUnit =>
        _unitPreference?.Current ?? WeightDisplayUnit.Kilograms;

    private string FormattedDisplayWeight => DisplayWeight?.ToString(
        DisplayUnit == WeightDisplayUnit.Pounds ? "0.00" : "0.###") ?? "—";

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record HistoryExerciseItem(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid ExerciseDefinitionId,
    string Name,
    TrackingMode TrackingMode,
    bool WorkoutIsDeleted,
    bool WorkoutActionsBlocked,
    IReadOnlyList<HistorySetItem> Sets);

public sealed record HistoryWorkoutItem(
    Guid WorkoutId,
    DateTimeOffset CompletedAt,
    bool IsDeleted,
    IReadOnlyList<HistoryExerciseItem> Exercises,
    WorkoutSyncState SyncState,
    string SyncStatusText,
    Guid? LastUndoOperationId,
    Guid? ConflictedOperationId,
    long? ConflictedServerVersion,
    string ExerciseCountText = "",
    string SetCountText = "",
    string ConflictOperationText = "",
    string ConflictLocalSummary = "",
    string ConflictServerSummary = "")
{
    public bool IsExpanded => false;
    public int ExerciseCount => Exercises.Count(exercise => exercise.Sets.Any(set => !set.IsDeleted));
    public int SetCount => Exercises.SelectMany(exercise => exercise.Sets).Count(set => !set.IsDeleted);
    public string MonthGroup => CompletedAt.ToString("MMMM yyyy");
    public bool HasConflict => ConflictedOperationId is not null && ConflictedServerVersion is not null;
    public bool HasPermanentFailure => SyncState == WorkoutSyncState.PermanentFailure;
    public bool IsReconciling => SyncState == WorkoutSyncState.Reconciling;
    public bool ActionsBlocked => HasPermanentFailure || IsReconciling;
}

public sealed class HistoryMonthGroup(string month, IEnumerable<HistoryWorkoutItem> workouts)
    : ObservableCollection<HistoryWorkoutItem>(workouts)
{
    public string Month { get; } = month;
}

public sealed class WorkoutHistoryViewModel : INotifyPropertyChanged
{
    private readonly WorkoutHistoryCoordinator _history;
    private readonly IHistoryOutboxStatusSource _outbox;
    private readonly IHistoryConfirmation _confirmation;
    private readonly IConflictResolution _conflicts;
    private readonly IAccountSessionBoundary _boundary;
    private readonly IConnectivityService _connectivity;
    private readonly ExerciseCache? _exerciseCache;
    private readonly IWeightUnitPreference? _unitPreference;
    private readonly Dictionary<Guid, WorkoutSyncState> _durableStates = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isBusy;
    private bool _deactivated;
    private string? _errorMessage;

    public WorkoutHistoryViewModel(
        WorkoutHistoryCoordinator history,
        IHistoryOutboxStatusSource outbox,
        IHistoryConfirmation confirmation,
        IAccountSessionBoundary boundary,
        IConnectivityService connectivity,
        WorkoutTextSet text,
        IConflictResolution conflicts,
        ExerciseCache? exerciseCache = null,
        IWeightUnitPreference? unitPreference = null)
    {
        _history = history;
        _outbox = outbox;
        _confirmation = confirmation;
        _conflicts = conflicts;
        _boundary = boundary;
        _connectivity = connectivity;
        _exerciseCache = exerciseCache;
        _unitPreference = unitPreference;
        Text = text;
        EditSetCommand = new AsyncCommand(EditSetAsync, CanMutateSet);
        DeleteSetCommand = new AsyncCommand(DeleteSetAsync, CanMutateSet);
        DeleteWorkoutCommand = new AsyncCommand(DeleteWorkoutAsync,
            item => !_deactivated && !IsBusy
                && item is HistoryWorkoutItem { IsDeleted: false, ActionsBlocked: false });
        DeleteWorkoutExerciseCommand = new AsyncCommand(
            DeleteWorkoutExerciseAsync,
            CanDeleteWorkoutExercise);
        UndoCommand = new AsyncCommand(UndoAsync,
            item => !_deactivated && !IsBusy
                && item is HistoryWorkoutItem
                {
                    LastUndoOperationId: not null,
                    IsReconciling: false
                });
        KeepServerCommand = new AsyncCommand(KeepServerAsync, CanResolveConflict);
        ApplyLocalCommand = new AsyncCommand(ApplyLocalAsync, CanResolveConflict);
        _boundary.SessionReset += OnSessionReset;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        if (_unitPreference is not null) _unitPreference.Changed += OnWeightUnitChanged;
    }

    public ObservableCollection<HistoryWorkoutItem> Workouts { get; } = [];
    public ObservableCollection<HistoryMonthGroup> WorkoutGroups { get; } = [];
    public WorkoutTextSet Text { get; }
    public AsyncCommand EditSetCommand { get; }
    public AsyncCommand DeleteSetCommand { get; }
    public AsyncCommand DeleteWorkoutCommand { get; }
    public AsyncCommand DeleteWorkoutExerciseCommand { get; }
    public AsyncCommand UndoCommand { get; }
    public AsyncCommand KeepServerCommand { get; }
    public AsyncCommand ApplyLocalCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            RaiseCommands();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_deactivated) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await ReloadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception) when (!_deactivated)
        {
            ErrorMessage = Text.LoadFailed;
        }
        finally
        {
            if (!_deactivated) IsBusy = false;
        }
    }

    public void Deactivate()
    {
        if (_deactivated) return;
        _deactivated = true;
        _boundary.SessionReset -= OnSessionReset;
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        if (_unitPreference is not null) _unitPreference.Changed -= OnWeightUnitChanged;
        _lifetime.Cancel();
        _durableStates.Clear();
        Workouts.Clear();
        WorkoutGroups.Clear();
        RaiseCommands();
    }

    private async Task EditSetAsync(object? parameter)
    {
        if (parameter is not HistorySetItem set || _deactivated) return;
        await MutateAsync(async token =>
        {
            await _history.EditSetAsync(
                set.WorkoutId,
                set.WorkoutExerciseId,
                set.SetId,
                new HistorySetMeasurement(set.WeightKg, set.AssistedKg, set.Reps),
                cancellationToken: token);
        }, Text.HistoryEditFailed);
    }

    private async Task DeleteSetAsync(object? parameter)
    {
        if (parameter is not HistorySetItem set || _deactivated) return;
        if (!await ConfirmAsync(Text.DeleteSetTitle, Text.DeleteSetMessage)) return;
        await MutateAsync(async token =>
        {
            await _history.DeleteSetAsync(
                set.WorkoutId, set.WorkoutExerciseId, set.SetId,
                cancellationToken: token);
        }, Text.HistoryDeleteFailed);
    }

    private async Task DeleteWorkoutAsync(object? parameter)
    {
        if (parameter is not HistoryWorkoutItem workout || _deactivated) return;
        if (!await ConfirmAsync(Text.DeleteWorkoutTitle, Text.DeleteWorkoutMessage)) return;
        await MutateAsync(async token =>
        {
            await _history.DeleteWorkoutAsync(workout.WorkoutId, cancellationToken: token);
        }, Text.HistoryDeleteFailed);
    }

    private async Task DeleteWorkoutExerciseAsync(object? parameter)
    {
        if (parameter is not HistoryExerciseItem exercise || _deactivated) return;
        if (!await ConfirmAsync(Text.DeleteExerciseTitle, Text.DeleteExerciseMessage)) return;
        await MutateAsync(async token =>
        {
            await _history.DeleteWorkoutExerciseAsync(
                exercise.WorkoutId,
                exercise.WorkoutExerciseId,
                cancellationToken: token);
        }, Text.HistoryDeleteFailed);
    }

    private async Task UndoAsync(object? parameter)
    {
        if (parameter is not HistoryWorkoutItem { LastUndoOperationId: { } operationId }
            || _deactivated) return;
        await MutateAsync(
            token => _history.UndoAsync(operationId, token),
            Text.UndoFailed);
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var generation = _boundary.Capture();
        using var lease = _boundary.CreateCancellationLease(generation, _lifetime.Token);
        try
        {
            var accepted = await _confirmation.ConfirmAsync(
                title, message, Text.Delete, Text.Cancel, lease.Token);
            return accepted && !_boundary.IsCancellationRequested(generation) && !_deactivated;
        }
        catch (OperationCanceledException) when (lease.Token.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task KeepServerAsync(object? parameter)
    {
        if (parameter is not HistoryWorkoutItem { ConflictedOperationId: { } operationId }
            || _deactivated) return;
        await MutateAsync(
            token => _conflicts.KeepServerAsync(operationId, token),
            Text.HistoryConflictFailed);
    }

    private async Task ApplyLocalAsync(object? parameter)
    {
        if (parameter is not HistoryWorkoutItem
            {
                ConflictedOperationId: { } operationId,
                ConflictedServerVersion: { } serverVersion
            } || _deactivated) return;
        await MutateAsync(async token =>
        {
            _ = await _conflicts.ApplyLocalAgainstVersionAsync(
                operationId, serverVersion, token);
        }, Text.HistoryConflictFailed);
    }

    private async Task MutateAsync(Func<CancellationToken, Task> mutation, string failureMessage)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await mutation(linked.Token);
            await ReloadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception) when (!_deactivated)
        {
            ErrorMessage = failureMessage;
        }
        finally
        {
            if (!_deactivated) IsBusy = false;
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var generation = _boundary.Capture();
        var history = await _history.GetHistoryAsync(cancellationToken);
        var names = _exerciseCache is null
            ? new Dictionary<Guid, string>()
            : (await _exerciseCache.GetAllAsync(cancellationToken))
                .ToDictionary(exercise => exercise.Id, exercise => exercise.Name);
        var projected = new List<HistoryWorkoutItem>(history.Count);
        var projectedStates = new Dictionary<Guid, WorkoutSyncState>(history.Count);
        foreach (var workout in history)
        {
            var operations = await _outbox.ForHistoryWorkoutAsync(
                workout.Id, cancellationToken);
            var durableState = DurableStatus(operations);
            projectedStates[workout.Id] = durableState;
            var state = DisplayStatus(durableState);
            var actionsBlocked = durableState is
                WorkoutSyncState.PermanentFailure or WorkoutSyncState.Reconciling;
            var exercises = workout.Exercises
                .Where(exercise => exercise.DeletedAt is null)
                .OrderBy(exercise => exercise.Order)
                .Select(exercise => new HistoryExerciseItem(
                    workout.Id,
                    exercise.Id,
                    exercise.ExerciseDefinitionId,
                    names.GetValueOrDefault(exercise.ExerciseDefinitionId, Text.UnknownExercise),
                    exercise.TrackingMode,
                    workout.DeletedAt is not null,
                    actionsBlocked,
                    exercise.Sets.OrderBy(set => set.Order)
                        .Select(set => HistorySetItem.From(
                            workout.Id,
                            workout.DeletedAt is not null,
                            actionsBlocked,
                            exercise,
                            set,
                            _unitPreference,
                            Text))
                        .ToArray()))
                .ToArray();
            var undo = operations.LastOrDefault(operation => operation.Type is
                OutboxOperationType.CompleteWorkout or OutboxOperationType.EditSet or OutboxOperationType.DeleteSet
                    or OutboxOperationType.DeleteWorkout
                    or OutboxOperationType.DeleteWorkoutExercise)?.OperationId;
            var conflict = operations.FirstOrDefault(operation =>
                operation.State == OutboxOperationState.Conflicted);
            projected.Add(new HistoryWorkoutItem(
                workout.Id,
                workout.CompletedAt!.Value,
                workout.DeletedAt is not null,
                exercises,
                state,
                StatusText(state),
                undo,
                conflict?.OperationId,
                conflict?.ServerVersion,
                string.Format(Text.ExerciseCountFormat, exercises.Count(exercise => exercise.Sets.Any(set => !set.IsDeleted))),
                string.Format(Text.SavedSetCountFormat, exercises.SelectMany(exercise => exercise.Sets).Count(set => !set.IsDeleted)),
                conflict?.Type.ToString() ?? string.Empty,
                conflict is null ? string.Empty : $"Local base {conflict.BaseVersion}",
                conflict?.ServerVersion is { } serverVersion ? $"Server version {serverVersion}" : string.Empty));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (_deactivated || _boundary.IsCancellationRequested(generation)) return;
        _durableStates.Clear();
        Workouts.Clear();
        WorkoutGroups.Clear();
        foreach (var workout in projected)
        {
            _durableStates[workout.WorkoutId] = projectedStates[workout.WorkoutId];
            Workouts.Add(workout);
        }
        RebuildGroups();
    }

    private static WorkoutSyncState DurableStatus(IReadOnlyList<OutboxOperation> operations)
    {
        if (operations.Any(operation => operation.State == OutboxOperationState.Pending
                && operation.SendStartedAt is not null))
            return WorkoutSyncState.Reconciling;
        if (operations.Any(operation => operation.State == OutboxOperationState.Rejected))
            return WorkoutSyncState.PermanentFailure;
        if (operations.Any(operation => operation.State == OutboxOperationState.Conflicted))
            return WorkoutSyncState.Conflicted;
        if (operations.Any(operation => operation.State == OutboxOperationState.Applied))
            return WorkoutSyncState.Syncing;
        if (operations.Any(operation => operation.State == OutboxOperationState.Pending))
            return WorkoutSyncState.Pending;
        return WorkoutSyncState.Synced;
    }

    private WorkoutSyncState DisplayStatus(WorkoutSyncState durableState) =>
        durableState is WorkoutSyncState.PermanentFailure
            or WorkoutSyncState.Conflicted
            or WorkoutSyncState.Reconciling
            ? durableState
            : _connectivity.IsOnline ? durableState : WorkoutSyncState.Offline;

    private string StatusText(WorkoutSyncState state) => state switch
    {
        WorkoutSyncState.Pending => Text.Pending,
        WorkoutSyncState.Conflicted => Text.Conflicted,
        WorkoutSyncState.Syncing => Text.Syncing,
        WorkoutSyncState.Reconciling => Text.Reconciling,
        WorkoutSyncState.PermanentFailure => Text.PermanentFailure,
        WorkoutSyncState.Offline => Text.Offline,
        _ => Text.Synced
    };

    private bool CanMutateSet(object? parameter) =>
        !_deactivated && !IsBusy
            && parameter is HistorySetItem
            {
                IsDeleted: false,
                WorkoutIsDeleted: false,
                WorkoutActionsBlocked: false
            };

    private bool CanResolveConflict(object? parameter) =>
        !_deactivated && !IsBusy
            && parameter is HistoryWorkoutItem { HasConflict: true, IsReconciling: false };

    private bool CanDeleteWorkoutExercise(object? parameter) =>
        !_deactivated && !IsBusy
            && parameter is HistoryExerciseItem
            {
                WorkoutIsDeleted: false,
                WorkoutActionsBlocked: false
            };

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        Workouts.Clear();
        WorkoutGroups.Clear();
        _durableStates.Clear();
        ErrorMessage = null;
        RaiseCommands();
    }

    private void OnConnectivityChanged(object? sender, EventArgs eventArgs)
    {
        if (_deactivated) return;
        for (var index = 0; index < Workouts.Count; index++)
        {
            var workout = Workouts[index];
            if (!_durableStates.TryGetValue(workout.WorkoutId, out var durableState)) continue;
            var state = DisplayStatus(durableState);
            Workouts[index] = workout with
            {
                SyncState = state,
                SyncStatusText = StatusText(state)
            };
        }
        RebuildGroups();
        RaiseCommands();
    }

    private void RebuildGroups()
    {
        WorkoutGroups.Clear();
        foreach (var group in Workouts
                     .GroupBy(item => new DateTime(item.CompletedAt.Year, item.CompletedAt.Month, 1))
                     .OrderByDescending(group => group.Key))
            WorkoutGroups.Add(new HistoryMonthGroup(
                group.Key.ToString("MMMM yyyy"),
                group.OrderByDescending(item => item.CompletedAt)));
    }

    private void OnWeightUnitChanged(object? sender, EventArgs eventArgs)
    {
        if (_deactivated) return;
        foreach (var set in Workouts.SelectMany(workout => workout.Exercises).SelectMany(exercise => exercise.Sets))
            set.RefreshUnit();
    }

    private void RaiseCommands()
    {
        EditSetCommand.RaiseCanExecuteChanged();
        DeleteSetCommand.RaiseCanExecuteChanged();
        DeleteWorkoutCommand.RaiseCanExecuteChanged();
        DeleteWorkoutExerciseCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        KeepServerCommand.RaiseCanExecuteChanged();
        ApplyLocalCommand.RaiseCanExecuteChanged();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
