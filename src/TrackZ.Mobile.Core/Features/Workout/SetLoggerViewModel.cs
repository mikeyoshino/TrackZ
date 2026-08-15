using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Features.Workout;

public enum WorkoutSyncState
{
    Synced = 1,
    Offline = 2,
    Pending = 3,
    Conflicted = 4,
    Syncing = 5
}

public enum WeightDisplayUnit
{
    Kilograms = 1,
    Pounds = 2
}

public interface IExerciseHistorySource
{
    Task<ExerciseHistorySessionDto?> GetMostRecentAsync(
        Guid exerciseId,
        bool refreshIfOnline,
        CancellationToken cancellationToken = default);
}

public interface ISetSavedFeedback
{
    Task SetSavedAsync(LocalSet savedSet, CancellationToken cancellationToken = default);
}

public interface IWorkoutSyncRunner
{
    Task<SyncRunStatus> RunOnceAsync(CancellationToken cancellationToken = default);
}

public sealed class WorkoutSyncRunner(SyncCoordinator coordinator) : IWorkoutSyncRunner
{
    public Task<SyncRunStatus> RunOnceAsync(CancellationToken cancellationToken = default) =>
        coordinator.RunOnceAsync(cancellationToken);
}

public sealed record SetDisplayRow(
    Guid Id,
    int Order,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    TrackingMode TrackingMode,
    string MeasurementText)
{
    public int SetNumber => Order + 1;
}

public sealed class SetLoggerViewModel : INotifyPropertyChanged
{
    private const decimal PoundsPerKilogram = 2.204622621848775807m;
    private readonly ActiveWorkoutCoordinator _coordinator;
    private readonly IExerciseHistorySource _history;
    private readonly ISetSavedFeedback _feedback;
    private readonly IAccountSessionBoundary _boundary;
    private readonly IConnectivityService _connectivity;
    private readonly OutboxRepository _outbox;
    private readonly WorkoutTextSet _text;
    private readonly IWorkoutSyncRunner? _syncRunner;
    private Guid _exerciseId;
    private string _exerciseName = string.Empty;
    private TrackingMode _trackingMode;
    private decimal? _weightKg;
    private decimal? _assistedKg;
    private int _reps;
    private bool _isBusy;
    private string? _errorMessage;
    private WorkoutSyncState _syncState = WorkoutSyncState.Synced;
    private WeightDisplayUnit _displayUnit;

    public SetLoggerViewModel(
        ActiveWorkoutCoordinator coordinator,
        IExerciseHistorySource history,
        ISetSavedFeedback feedback,
        IAccountSessionBoundary boundary,
        IConnectivityService connectivity,
        OutboxRepository outbox,
        WorkoutTextSet text,
        WeightDisplayUnit displayUnit = WeightDisplayUnit.Kilograms,
        IWorkoutSyncRunner? syncRunner = null)
    {
        _coordinator = coordinator;
        _history = history;
        _feedback = feedback;
        _boundary = boundary;
        _connectivity = connectivity;
        _outbox = outbox;
        _text = text;
        _syncRunner = syncRunner;
        _displayUnit = displayUnit;
        MatchLastCommand = new RelayCommand(_ => MatchLast(), _ => CanMatchLast);
        CompleteSetCommand = new AsyncCommand(_ => CompleteSetAsync(), _ => CanCompleteSet);
        IncrementWeightCommand = new RelayCommand(_ => DisplayWeight += WeightStep, _ => UsesWeight && !IsBusy);
        DecrementWeightCommand = new RelayCommand(_ => DisplayWeight = Math.Max(0m, DisplayWeight - WeightStep), _ => UsesWeight && !IsBusy);
        IncrementRepsCommand = new RelayCommand(_ => Reps = Math.Min(999, Reps + 1), _ => !IsBusy);
        DecrementRepsCommand = new RelayCommand(_ => Reps = Math.Max(0, Reps - 1), _ => !IsBusy);
        _boundary.SessionReset += OnSessionReset;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public ObservableCollection<SetDisplayRow> LastSets { get; } = [];
    public ObservableCollection<SetDisplayRow> TodaySets { get; } = [];
    public ICommand MatchLastCommand { get; }
    public AsyncCommand CompleteSetCommand { get; }
    public ICommand IncrementWeightCommand { get; }
    public ICommand DecrementWeightCommand { get; }
    public ICommand IncrementRepsCommand { get; }
    public ICommand DecrementRepsCommand { get; }
    public WorkoutTextSet Text => _text;
    public Task SyncCompletion { get; private set; } = Task.CompletedTask;

    public string ExerciseName
    {
        get => _exerciseName;
        private set => Set(ref _exerciseName, value);
    }

    public TrackingMode TrackingMode
    {
        get => _trackingMode;
        private set
        {
            if (!Set(ref _trackingMode, value)) return;
            OnPropertyChanged(nameof(UsesWeight));
            OnPropertyChanged(nameof(IsBodyweight));
            OnPropertyChanged(nameof(WeightCaption));
            OnPropertyChanged(nameof(TrackingModeLabel));
        }
    }

    public bool UsesWeight => TrackingMode is TrackingMode.Weighted or TrackingMode.Assisted;
    public bool IsBodyweight => TrackingMode == TrackingMode.Bodyweight;
    public string WeightCaption => TrackingMode == TrackingMode.Assisted ? _text.Assistance : _text.Weight;
    public string TrackingModeLabel => TrackingMode switch
    {
        TrackingMode.Weighted => _text.Weight,
        TrackingMode.Assisted => _text.Assistance,
        TrackingMode.Bodyweight => _text.Bodyweight,
        _ => string.Empty
    };
    public decimal WeightStep => DisplayUnit == WeightDisplayUnit.Kilograms ? 0.5m : 1m;

    public decimal? WeightKg
    {
        get => _weightKg;
        set
        {
            if (!Set(ref _weightKg, value)) return;
            OnPropertyChanged(nameof(DisplayWeight));
            MeasurementChanged();
        }
    }

    public decimal? AssistedKg
    {
        get => _assistedKg;
        set
        {
            if (!Set(ref _assistedKg, value)) return;
            OnPropertyChanged(nameof(DisplayWeight));
            MeasurementChanged();
        }
    }

    public decimal DisplayWeight
    {
        get
        {
            var kilograms = TrackingMode == TrackingMode.Assisted ? AssistedKg : WeightKg;
            if (kilograms is null) return 0m;
            return DisplayUnit == WeightDisplayUnit.Kilograms
                ? kilograms.Value
                : decimal.Round(kilograms.Value * PoundsPerKilogram, 2, MidpointRounding.AwayFromZero);
        }
        set
        {
            var kilograms = DisplayUnit == WeightDisplayUnit.Kilograms
                ? value
                : decimal.Round(value / PoundsPerKilogram, SetMeasurement.MaximumKilogramScale, MidpointRounding.AwayFromZero);
            if (TrackingMode == TrackingMode.Assisted) AssistedKg = kilograms;
            else WeightKg = kilograms;
        }
    }

    public string WeightUnitLabel => DisplayUnit == WeightDisplayUnit.Kilograms ? _text.Kilograms : _text.Pounds;

    public WeightDisplayUnit DisplayUnit
    {
        get => _displayUnit;
        set
        {
            if (!Set(ref _displayUnit, value)) return;
            OnPropertyChanged(nameof(DisplayWeight));
            OnPropertyChanged(nameof(WeightUnitLabel));
            OnPropertyChanged(nameof(WeightStep));
        }
    }

    public int Reps
    {
        get => _reps;
        set
        {
            if (!Set(ref _reps, value)) return;
            MeasurementChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanCompleteSet));
            RaiseCommands();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    public bool CanCompleteSet => !IsBusy && IsValidMeasurement();
    public bool CanMatchLast => !IsBusy && TodaySets.Count < LastSets.Count;
    public string? ValidationMessage => IsValidMeasurement() ? null : TrackingMode switch
    {
        TrackingMode.Weighted => _text.InvalidWeightedSet,
        TrackingMode.Assisted => _text.InvalidAssistedSet,
        _ => _text.InvalidBodyweightSet
    };

    public WorkoutSyncState SyncState
    {
        get => _syncState;
        private set
        {
            if (!Set(ref _syncState, value)) return;
            OnPropertyChanged(nameof(SyncStatusText));
        }
    }

    public string SyncStatusText => SyncState switch
    {
        WorkoutSyncState.Offline => _text.Offline,
        WorkoutSyncState.Pending => _text.Pending,
        WorkoutSyncState.Conflicted => _text.Conflicted,
        WorkoutSyncState.Syncing => _text.Syncing,
        _ => _text.Synced
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(
        Guid exerciseId,
        string exerciseName,
        CancellationToken cancellationToken = default)
    {
        if (exerciseId == Guid.Empty) throw new ArgumentException("Exercise ID is required.", nameof(exerciseId));
        ArgumentException.ThrowIfNullOrWhiteSpace(exerciseName);
        var generation = _boundary.Capture();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var active = await _coordinator.RestoreActiveAsync(cancellationToken)
                ?? throw new InvalidOperationException("No active workout exists.");
            var exercise = active.Exercises.SingleOrDefault(item =>
                item.DeletedAt is null && item.ExerciseDefinitionId == exerciseId)
                ?? throw new ArgumentException("Exercise is not in the active workout.", nameof(exerciseId));
            var previous = await _history.GetMostRecentAsync(
                exerciseId, _connectivity.IsOnline, cancellationToken);
            if (_boundary.IsCancellationRequested(generation)) return;

            _exerciseId = exerciseId;
            ExerciseName = exerciseName;
            TrackingMode = exercise.TrackingMode;
            LastSets.Clear();
            if (previous is not null)
            {
                if (previous.TrackingMode != exercise.TrackingMode)
                    throw new InvalidDataException("Cached history tracking mode does not match the workout.");
                foreach (var set in OrderedExact(previous.Sets))
                    LastSets.Add(Row(set, exercise.TrackingMode));
            }
            TodaySets.Clear();
            foreach (var set in exercise.Sets.Where(item => item.DeletedAt is null).OrderBy(item => item.Order))
                TodaySets.Add(Row(set, exercise.TrackingMode));
            MatchLast();
            await RefreshSyncStateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
            ClearPrivateState();
        }
        catch (Exception) when (!_boundary.IsCancellationRequested(generation))
        {
            _exerciseId = Guid.Empty;
            LastSets.Clear();
            TodaySets.Clear();
            ErrorMessage = _text.LoadFailed;
        }
        finally
        {
            IsBusy = false;
            MeasurementChanged();
        }
    }

    public void MarkSyncing() => SyncState = WorkoutSyncState.Syncing;

    public Task RefreshSyncStateAsync(CancellationToken cancellationToken = default) =>
        RefreshSyncStateCoreAsync(cancellationToken);

    private async Task CompleteSetAsync()
    {
        if (!IsValidMeasurement() || _exerciseId == Guid.Empty) return;
        var generation = _boundary.Capture();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var set = TrackingMode switch
            {
                TrackingMode.Weighted => new LocalSet(WeightKg, null, Reps),
                TrackingMode.Assisted => new LocalSet(null, AssistedKg, Reps),
                TrackingMode.Bodyweight => new LocalSet(null, null, Reps),
                _ => throw new InvalidOperationException("Tracking mode is invalid.")
            };
            var saved = await _coordinator.SaveSetAsync(_exerciseId, set);
            if (_boundary.IsCancellationRequested(generation)) return;
            TodaySets.Add(Row(saved, TrackingMode));
            OnPropertyChanged(nameof(CanMatchLast));
            await RefreshSyncStateCoreAsync(CancellationToken.None);
            try
            {
                await _feedback.SetSavedAsync(saved, CancellationToken.None);
            }
            catch (Exception) when (!_boundary.IsCancellationRequested(generation))
            {
                // Feedback is best-effort. The set is already durably persisted, so a
                // haptic or animation failure must never invite the user to save it again.
            }
            if (_boundary.IsCancellationRequested(generation)) return;
            if (_syncRunner is not null && _connectivity.IsOnline)
                SyncCompletion = SynchronizeBestEffortAsync(generation);
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
            ClearPrivateState();
        }
        catch (Exception) when (!_boundary.IsCancellationRequested(generation))
        {
            ErrorMessage = _text.SaveFailed;
        }
        finally
        {
            IsBusy = false;
            MeasurementChanged();
        }
    }

    private void MatchLast()
    {
        if (!CanMatchLast) return;
        var target = LastSets[TodaySets.Count];
        WeightKg = target.WeightKg;
        AssistedKg = target.AssistedKg;
        Reps = target.Reps;
    }

    private async Task RefreshSyncStateCoreAsync(CancellationToken cancellationToken)
    {
        if (!_connectivity.IsOnline)
        {
            SyncState = WorkoutSyncState.Offline;
            return;
        }
        if ((await _outbox.ConflictedAsync(cancellationToken)).Count != 0)
        {
            SyncState = WorkoutSyncState.Conflicted;
            return;
        }
        SyncState = (await _outbox.PendingAsync(cancellationToken)).Count == 0
            ? WorkoutSyncState.Synced
            : WorkoutSyncState.Pending;
    }

    private async Task SynchronizeBestEffortAsync(AccountSessionGeneration generation)
    {
        SyncState = WorkoutSyncState.Syncing;
        try
        {
            var result = await _syncRunner!.RunOnceAsync(CancellationToken.None);
            if (_boundary.IsCancellationRequested(generation)) return;
            if (result == SyncRunStatus.Offline)
            {
                SyncState = WorkoutSyncState.Offline;
                return;
            }
            await RefreshSyncStateCoreAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
        }
        catch (Exception)
        {
            if (!_boundary.IsCancellationRequested(generation))
                await RefreshSyncStateCoreAsync(CancellationToken.None);
        }
    }

    private bool IsValidMeasurement()
    {
        if (Reps is < 1 or > 999) return false;
        return TrackingMode switch
        {
            TrackingMode.Weighted => ValidKilograms(WeightKg) && AssistedKg is null,
            TrackingMode.Bodyweight => WeightKg is null && AssistedKg is null,
            TrackingMode.Assisted => WeightKg is null && ValidKilograms(AssistedKg),
            _ => false
        };
    }

    private static bool ValidKilograms(decimal? value) =>
        value is { } kilograms
        && kilograms is >= SetMeasurement.MinimumKilograms and <= SetMeasurement.MaximumKilograms
        && ((decimal.GetBits(kilograms)[3] >> 16) & 0xff) <= SetMeasurement.MaximumKilogramScale;

    private static IReadOnlyList<WorkoutSetDto> OrderedExact(IReadOnlyList<WorkoutSetDto> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        var ordered = sets.OrderBy(item => item.Order).ToArray();
        if (ordered.Select(item => item.Order).Where((order, index) => order != index).Any())
            throw new InvalidDataException("Previous-session set order is not contiguous.");
        return ordered;
    }

    private SetDisplayRow Row(WorkoutSetDto set, TrackingMode mode) =>
        new(set.Id, set.Order, set.WeightKg, set.AssistedKg, set.Reps, mode,
            Measurement(set.WeightKg, set.AssistedKg, set.Reps, mode));

    private SetDisplayRow Row(LocalSet set, TrackingMode mode) =>
        new(set.Id, set.Order, set.WeightKg, set.AssistedKg, set.Reps, mode,
            Measurement(set.WeightKg, set.AssistedKg, set.Reps, mode));

    private string Measurement(decimal? weight, decimal? assisted, int reps, TrackingMode mode) => mode switch
    {
        TrackingMode.Weighted => $"{weight:0.###} {_text.Kilograms} × {reps}",
        TrackingMode.Assisted => $"{assisted:0.###} {_text.Kilograms} · {reps} {_text.Reps}",
        TrackingMode.Bodyweight => $"{reps} {_text.Reps}",
        _ => string.Empty
    };

    private void MeasurementChanged()
    {
        OnPropertyChanged(nameof(CanCompleteSet));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanMatchLast));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        CompleteSetCommand.RaiseCanExecuteChanged();
        (MatchLastCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (IncrementWeightCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DecrementWeightCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (IncrementRepsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DecrementRepsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs) => ClearPrivateState();

    private void ClearPrivateState()
    {
        _exerciseId = Guid.Empty;
        ExerciseName = string.Empty;
        LastSets.Clear();
        TodaySets.Clear();
        WeightKg = null;
        AssistedKg = null;
        Reps = 0;
        ErrorMessage = null;
        SyncState = _connectivity.IsOnline ? WorkoutSyncState.Synced : WorkoutSyncState.Offline;
        MeasurementChanged();
    }

    private async void OnConnectivityChanged(object? sender, EventArgs eventArgs)
    {
        try { await RefreshSyncStateCoreAsync(CancellationToken.None); }
        catch (Exception) { SyncState = WorkoutSyncState.Offline; }
    }

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
