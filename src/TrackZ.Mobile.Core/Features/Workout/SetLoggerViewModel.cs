using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using TrackZ.Contracts.Sync;
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Features.Workout;

public enum WorkoutSyncState
{
    Synced = 1,
    Offline = 2,
    Pending = 3,
    Conflicted = 4,
    Syncing = 5,
    PermanentFailure = 6,
    Reconciling = 7
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
    Task SetSavedAsync(SetSavedPresentation presentation, SetSavedFeedbackSession session);
}

public enum SetSavedOutcome
{
    Saved = 1,
    MatchedPrevious = 2,
    PersonalRecord = 3
}

public sealed record SetSavedPresentation(
    LocalSet Set,
    SetSavedOutcome Outcome,
    string PrimaryText,
    string SecondaryText);

public sealed class SetSavedFeedbackSession
{
    private readonly Func<Action, bool> _tryStartPhase;

    public SetSavedFeedbackSession(
        CancellationToken cancellationToken,
        Func<Action, bool> tryStartPhase)
    {
        CancellationToken = cancellationToken;
        _tryStartPhase = tryStartPhase ?? throw new ArgumentNullException(nameof(tryStartPhase));
    }

    public CancellationToken CancellationToken { get; }

    public bool TryStartPhase(Action phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        CancellationToken.ThrowIfCancellationRequested();
        return _tryStartPhase(phase);
    }
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
    private readonly IWorkoutOutboxStatusSource _outbox;
    private readonly WorkoutTextSet _text;
    private readonly IWorkoutSyncRunner? _syncRunner;
    private readonly IWeightUnitPreference? _unitPreference;
    private readonly IConflictResolution? _conflicts;
    private readonly ExerciseCache? _exerciseCache;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _loadCancellation;
    private Guid _workoutId;
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
    private bool _disposed;
    private int _loadGeneration;
    private DateTimeOffset? _lastHistoryCompletedAt;
    private OutboxOperation? _activeConflict;
    private string? _conflictLocalSummary;
    private string? _conflictServerSummary;
    private CachedExercise? _cachedExercise;
    private string? _thumbnailUri;
    private string _exerciseMetadataText = string.Empty;
    private string _previousBestText = string.Empty;
    private string _allTimePrText = string.Empty;
    private bool _hasDraftSet;
    private decimal? _draftBaselineWeightKg;
    private decimal? _draftBaselineAssistedKg;
    private int _draftBaselineReps;

    public SetLoggerViewModel(
        ActiveWorkoutCoordinator coordinator,
        IExerciseHistorySource history,
        ISetSavedFeedback feedback,
        IAccountSessionBoundary boundary,
        IConnectivityService connectivity,
        IWorkoutOutboxStatusSource outbox,
        WorkoutTextSet text,
        IWorkoutSyncRunner? syncRunner = null,
        IWeightUnitPreference? unitPreference = null,
        IConflictResolution? conflicts = null,
        ExerciseCache? exerciseCache = null)
    {
        _coordinator = coordinator;
        _history = history;
        _feedback = feedback;
        _boundary = boundary;
        _connectivity = connectivity;
        _outbox = outbox;
        _text = text;
        _syncRunner = syncRunner;
        _unitPreference = unitPreference;
        _conflicts = conflicts;
        _exerciseCache = exerciseCache;
        _displayUnit = unitPreference?.Current ?? WeightDisplayUnit.Kilograms;
        MatchLastCommand = new RelayCommand(_ => MatchLast(), _ => CanMatchLast);
        CompleteSetCommand = new AsyncCommand(_ => CompleteSetAsync(), _ => CanCompleteSet);
        BeginSetCommand = new RelayCommand(_ => BeginSet(), _ => CanBeginSet);
        CancelDraftSetCommand = new RelayCommand(_ => CancelDraftSet(), _ => CanCancelDraftSet);
        SaveDraftSetCommand = new AsyncCommand(_ => CompleteSetAsync(), _ => CanSaveDraftSet);
        IncrementWeightCommand = new RelayCommand(_ => DisplayWeight += WeightStep, _ => !_disposed && UsesWeight && !IsBusy);
        DecrementWeightCommand = new RelayCommand(_ => DisplayWeight = Math.Max(0m, DisplayWeight - WeightStep), _ => !_disposed && UsesWeight && !IsBusy);
        IncrementRepsCommand = new RelayCommand(_ => Reps = Math.Min(999, Reps + 1), _ => !_disposed && !IsBusy);
        DecrementRepsCommand = new RelayCommand(_ => Reps = Math.Max(0, Reps - 1), _ => !_disposed && !IsBusy);
        UseKilogramsCommand = new RelayCommand(_ => DisplayUnit = WeightDisplayUnit.Kilograms, _ => !_disposed && !IsBusy);
        UsePoundsCommand = new RelayCommand(_ => DisplayUnit = WeightDisplayUnit.Pounds, _ => !_disposed && !IsBusy);
        KeepServerCommand = new AsyncCommand(_ => ResolveConflictAsync(keepServer: true), _ => CanResolveConflict);
        ApplyLocalCommand = new AsyncCommand(_ => ResolveConflictAsync(keepServer: false), _ => CanResolveConflict);
        _boundary.SessionReset += OnSessionReset;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        if (_unitPreference is not null) _unitPreference.Changed += OnWeightUnitChanged;
    }

    public ObservableCollection<SetDisplayRow> LastSets { get; } = [];
    public ObservableCollection<SetDisplayRow> TodaySets { get; } = [];
    public ICommand MatchLastCommand { get; }
    public AsyncCommand CompleteSetCommand { get; }
    public ICommand BeginSetCommand { get; }
    public ICommand CancelDraftSetCommand { get; }
    public AsyncCommand SaveDraftSetCommand { get; }
    public ICommand IncrementWeightCommand { get; }
    public ICommand DecrementWeightCommand { get; }
    public ICommand IncrementRepsCommand { get; }
    public ICommand DecrementRepsCommand { get; }
    public ICommand UseKilogramsCommand { get; }
    public ICommand UsePoundsCommand { get; }
    public AsyncCommand KeepServerCommand { get; }
    public AsyncCommand ApplyLocalCommand { get; }
    public WorkoutTextSet Text => _text;
    public Task SyncCompletion { get; private set; } = Task.CompletedTask;
    public Task HistoryRefreshCompletion { get; private set; } = Task.CompletedTask;
    public bool HasConflict => _activeConflict is not null;
    public bool IsReconciling => SyncState == WorkoutSyncState.Reconciling;
    public string? ConflictLocalSummary
    {
        get => _conflictLocalSummary;
        private set => Set(ref _conflictLocalSummary, value);
    }
    public string? ConflictServerSummary
    {
        get => _conflictServerSummary;
        private set => Set(ref _conflictServerSummary, value);
    }
    private bool CanResolveConflict => !_disposed && !IsBusy && !IsReconciling
        && _conflicts is not null && _activeConflict?.ServerVersion is not null;

    public string ExerciseName
    {
        get => _exerciseName;
        private set => Set(ref _exerciseName, value);
    }

    public string? ThumbnailUri
    {
        get => _thumbnailUri;
        private set
        {
            if (!Set(ref _thumbnailUri, value)) return;
            OnPropertyChanged(nameof(HasArtwork));
            OnPropertyChanged(nameof(ShowsArtworkPlaceholder));
        }
    }

    public bool HasArtwork => !string.IsNullOrWhiteSpace(ThumbnailUri);
    public bool ShowsArtworkPlaceholder => !HasArtwork;
    public string ExerciseMetadataText { get => _exerciseMetadataText; private set => Set(ref _exerciseMetadataText, value); }
    public string PreviousBestText { get => _previousBestText; private set => Set(ref _previousBestText, value); }
    public string AllTimePrText { get => _allTimePrText; private set => Set(ref _allTimePrText, value); }

    public TrackingMode TrackingMode
    {
        get => _trackingMode;
        private set
        {
            if (!Set(ref _trackingMode, value)) return;
            OnPropertyChanged(nameof(UsesWeight));
            OnPropertyChanged(nameof(IsBodyweight));
            OnPropertyChanged(nameof(IsAssisted));
            OnPropertyChanged(nameof(WeightCaption));
            OnPropertyChanged(nameof(TrackingModeLabel));
            OnPropertyChanged(nameof(DecrementWeightDescription));
            OnPropertyChanged(nameof(IncrementWeightDescription));
        }
    }

    public bool UsesWeight => TrackingMode is TrackingMode.Weighted or TrackingMode.Assisted;
    public bool IsBodyweight => TrackingMode == TrackingMode.Bodyweight;
    public bool IsAssisted => TrackingMode == TrackingMode.Assisted;
    public string WeightCaption => TrackingMode == TrackingMode.Assisted ? _text.Assistance : _text.Weight;
    public string DecrementWeightDescription => TrackingMode == TrackingMode.Assisted
        ? _text.DecreaseAssistance
        : _text.DecreaseWeight;
    public string IncrementWeightDescription => TrackingMode == TrackingMode.Assisted
        ? _text.IncreaseAssistance
        : _text.IncreaseWeight;
    public string DecrementRepsDescription => _text.DecreaseReps;
    public string IncrementRepsDescription => _text.IncreaseReps;
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
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (!Set(ref _displayUnit, value)) return;
            _unitPreference?.Set(value);
            OnPropertyChanged(nameof(DisplayWeight));
            OnPropertyChanged(nameof(WeightUnitLabel));
            OnPropertyChanged(nameof(WeightStep));
            OnPropertyChanged(nameof(IsKilograms));
            OnPropertyChanged(nameof(IsPounds));
            RefreshMeasurementRows();
        }
    }

    public bool IsKilograms => DisplayUnit == WeightDisplayUnit.Kilograms;
    public bool IsPounds => DisplayUnit == WeightDisplayUnit.Pounds;

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
            OnPropertyChanged(nameof(CanBeginSet));
            OnPropertyChanged(nameof(CanCancelDraftSet));
            OnPropertyChanged(nameof(CanSaveDraftSet));
            RaiseCommands();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    public bool CanCompleteSet => !_disposed && !IsBusy && IsValidMeasurement();
    public bool HasDraftSet
    {
        get => _hasDraftSet;
        private set
        {
            if (!Set(ref _hasDraftSet, value)) return;
            OnPropertyChanged(nameof(CanBeginSet));
            OnPropertyChanged(nameof(CanCancelDraftSet));
            OnPropertyChanged(nameof(CanSaveDraftSet));
            OnPropertyChanged(nameof(HasNoDraftSet));
            OnPropertyChanged(nameof(ShowsSetComparison));
            RaiseCommands();
        }
    }
    public bool HasNoDraftSet => !HasDraftSet;
    public bool ShowsNoSetHistory => LastSets.Count == 0 && TodaySets.Count == 0;
    public bool ShowsSetComparison => !HasDraftSet && !ShowsNoSetHistory;
    public bool CanBeginSet => !_disposed && !IsBusy && _exerciseId != Guid.Empty && !HasDraftSet;
    public bool CanCancelDraftSet => !_disposed && !IsBusy && HasDraftSet;
    public bool CanSaveDraftSet => HasDraftSet && CanCompleteSet;
    public int DraftSetNumber => NextSetNumber;
    public bool CanMatchLast => !_disposed && !IsBusy && TodaySets.Count < LastSets.Count;
    public int NextSetNumber => TodaySets.Count + 1;
    public string NextSetText => string.Format(
        CultureInfo.CurrentCulture,
        _text.NextSetFormat,
        NextSetNumber);
    public string SaveDraftSetText => string.Format(
        CultureInfo.CurrentCulture,
        _text.SaveSetNumberFormat,
        NextSetNumber);
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
            OnPropertyChanged(nameof(ShowsSyncStatus));
            OnPropertyChanged(nameof(IsReconciling));
            RaiseCommands();
        }
    }

    public string SyncStatusText => SyncState switch
    {
        WorkoutSyncState.Offline => _text.Offline,
        WorkoutSyncState.Pending => _text.Pending,
        WorkoutSyncState.Conflicted => _text.Conflicted,
        WorkoutSyncState.Syncing => _text.Syncing,
        WorkoutSyncState.Reconciling => _text.Reconciling,
        WorkoutSyncState.PermanentFailure => _text.PermanentFailure,
        _ => _text.Synced
    };
    public bool ShowsSyncStatus => SyncState != WorkoutSyncState.Synced;
    public bool HasPreviousBest => LastSets.Count > 0 || _cachedExercise?.LastBestSet is not null;
    public bool HasAllTimePr => _cachedExercise?.AllTimeBest is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(
        Guid exerciseId,
        string exerciseName,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        if (exerciseId == Guid.Empty) throw new ArgumentException("Exercise ID is required.", nameof(exerciseId));
        ArgumentException.ThrowIfNullOrWhiteSpace(exerciseName);
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        var token = _loadCancellation.Token;
        var loadGeneration = Interlocked.Increment(ref _loadGeneration);
        var generation = _boundary.Capture();
        var loaded = false;
        HasDraftSet = false;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var active = await _coordinator.RestoreActiveAsync(token)
                ?? throw new InvalidOperationException("No active workout exists.");
            var exercise = active.Exercises.SingleOrDefault(item =>
                item.DeletedAt is null && item.ExerciseDefinitionId == exerciseId)
                ?? throw new ArgumentException("Exercise is not in the active workout.", nameof(exerciseId));
            var previous = await _history.GetMostRecentAsync(
                exerciseId, refreshIfOnline: false, token);
            token.ThrowIfCancellationRequested();
            if (_disposed || _boundary.IsCancellationRequested(generation)) return;

            _workoutId = active.Id;
            _exerciseId = exerciseId;
            ExerciseName = exerciseName;
            TrackingMode = exercise.TrackingMode;
            _cachedExercise = _exerciseCache is null
                ? null
                : await _exerciseCache.FindExerciseAsync(exerciseId, token);
            if (_cachedExercise is not null && _cachedExercise.TrackingMode != exercise.TrackingMode)
                throw new InvalidDataException("Cached exercise tracking mode does not match the workout.");
            ThumbnailUri = _cachedExercise?.ThumbnailUri;
            ExerciseMetadataText = _cachedExercise is null
                ? TrackingModeLabel
                : $"{BodyPartText(_cachedExercise.BodyPart)} · {TrackingModeLabel}";
            _lastHistoryCompletedAt = previous?.CompletedAt;
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
            RefreshExerciseContext();
            PublishSetPresentationState();
            MatchLast();
            await RefreshSyncStateCoreAsync(token);
            loaded = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested
            || _boundary.IsCancellationRequested(generation))
        {
            if (!_disposed && _boundary.IsCancellationRequested(generation))
                ClearPrivateState();
        }
        catch (Exception) when (!_disposed && !_boundary.IsCancellationRequested(generation))
        {
            _exerciseId = Guid.Empty;
            _cachedExercise = null;
            PreviousBestText = string.Empty;
            AllTimePrText = string.Empty;
            LastSets.Clear();
            TodaySets.Clear();
            PublishSetPresentationState();
            ErrorMessage = _text.LoadFailed;
        }
        finally
        {
            if (!_disposed)
            {
                IsBusy = false;
                MeasurementChanged();
            }
        }
        if (loaded && _connectivity.IsOnline && !_disposed)
            HistoryRefreshCompletion = RefreshHistoryInBackgroundAsync(
                exerciseId,
                activeWorkoutId: _workoutId,
                expectedMode: TrackingMode,
                generation,
                loadGeneration,
                token);
        else
            HistoryRefreshCompletion = Task.CompletedTask;
    }

    private async Task RefreshHistoryInBackgroundAsync(
        Guid exerciseId,
        Guid activeWorkoutId,
        TrackingMode expectedMode,
        AccountSessionGeneration generation,
        int loadGeneration,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            var refreshed = await _history.GetMostRecentAsync(
                exerciseId, refreshIfOnline: true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (refreshed is null
                || refreshed.TrackingMode != expectedMode
                || refreshed.CompletedAt <= _lastHistoryCompletedAt
                || _disposed
                || loadGeneration != Volatile.Read(ref _loadGeneration)
                || _boundary.IsCancellationRequested(generation)
                || _exerciseId != exerciseId
                || _workoutId != activeWorkoutId)
                return;

            var ordered = OrderedExact(refreshed.Sets);
            LastSets.Clear();
            foreach (var set in ordered) LastSets.Add(Row(set, expectedMode));
            _lastHistoryCompletedAt = refreshed.CompletedAt;
            RefreshExerciseContext();
            PublishSetPresentationState();
            OnPropertyChanged(nameof(CanMatchLast));
            MeasurementChanged();
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested
            || _boundary.IsCancellationRequested(generation))
        {
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException
            || exception is MobileApiException { IsRetryable: true })
        {
            // Cached/local history is already visible; offline refresh is normal.
        }
        catch (Exception)
        {
            if (!_disposed
                && loadGeneration == Volatile.Read(ref _loadGeneration)
                && !_boundary.IsCancellationRequested(generation)
                && _exerciseId == exerciseId
                && _workoutId == activeWorkoutId)
                ErrorMessage = _text.LoadFailed;
        }
    }

    public void MarkSyncing()
    {
        if (!_disposed) SyncState = WorkoutSyncState.Syncing;
    }

    public async Task RefreshSyncStateAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        try
        {
            await RefreshSyncStateCoreAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task CompleteSetAsync()
    {
        if (_disposed || !IsValidMeasurement() || _exerciseId == Guid.Empty) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = lifetime.Token;
        var generation = _boundary.Capture();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            LocalSet saved;
            try
            {
                var set = TrackingMode switch
                {
                    TrackingMode.Weighted => new LocalSet(WeightKg, null, Reps),
                    TrackingMode.Assisted => new LocalSet(null, AssistedKg, Reps),
                    TrackingMode.Bodyweight => new LocalSet(null, null, Reps),
                    _ => throw new InvalidOperationException("Tracking mode is invalid.")
                };
                saved = await _coordinator.SaveSetAsync(_exerciseId, set, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested
                || _boundary.IsCancellationRequested(generation))
            {
                if (!_disposed && _boundary.IsCancellationRequested(generation))
                    ClearPrivateState();
                return;
            }
            catch (Exception) when (!_disposed && !_boundary.IsCancellationRequested(generation))
            {
                ErrorMessage = _text.SaveFailed;
                return;
            }

            if (_disposed || _boundary.IsCancellationRequested(generation)) return;
            var presentation = CreateSavedPresentation(saved);
            TodaySets.Add(Row(saved, TrackingMode));
            HasDraftSet = false;
            OnPropertyChanged(nameof(CanMatchLast));
            PublishSetPresentationState();
            try
            {
                await RefreshSyncStateCoreAsync(token);
            }
            catch (Exception)
            {
                // Status projection is best-effort after the durable set commit.
            }
            if (_disposed || _boundary.IsCancellationRequested(generation)) return;
            try
            {
                using var feedbackLease = _boundary.CreateCancellationLease(generation, token);
                var feedbackSession = new SetSavedFeedbackSession(
                    feedbackLease.Token,
                    phase => _boundary.TryStartSessionPhase(
                        generation, phase, feedbackLease.Token));
                await _feedback.SetSavedAsync(presentation, feedbackSession);
            }
            catch (Exception)
            {
                // Feedback is best-effort. The set is already durably persisted, so a
                // haptic or animation failure must never invite the user to save it again.
            }
            if (_disposed || _boundary.IsCancellationRequested(generation)) return;
            if (_syncRunner is not null && _connectivity.IsOnline)
                SyncCompletion = SynchronizeBestEffortAsync(generation);
        }
        finally
        {
            if (!_disposed)
            {
                IsBusy = false;
                MeasurementChanged();
            }
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

    private void BeginSet()
    {
        if (!CanBeginSet) return;
        var suggestion = TodaySets.Count < LastSets.Count
            ? LastSets[TodaySets.Count]
            : TodaySets.LastOrDefault();
        _draftBaselineWeightKg = suggestion?.WeightKg;
        _draftBaselineAssistedKg = suggestion?.AssistedKg;
        _draftBaselineReps = suggestion?.Reps ?? 0;
        RestoreDraftBaseline();
        ErrorMessage = null;
        HasDraftSet = true;
    }

    private void CancelDraftSet()
    {
        if (!CanCancelDraftSet) return;
        RestoreDraftBaseline();
        HasDraftSet = false;
        ErrorMessage = null;
    }

    private void RestoreDraftBaseline()
    {
        WeightKg = _draftBaselineWeightKg;
        AssistedKg = _draftBaselineAssistedKg;
        Reps = _draftBaselineReps;
    }

    private SetSavedPresentation CreateSavedPresentation(LocalSet saved)
    {
        var comparable = TodaySets.Count < LastSets.Count ? LastSets[TodaySets.Count] : null;
        var existing = LastSets.Concat(TodaySets).ToArray();
        var knownPerformances = existing.AsEnumerable();
        if (_cachedExercise?.AllTimeBest is { } allTime)
            knownPerformances = knownPerformances.Append(new SetDisplayRow(
                Guid.Empty,
                0,
                allTime.WeightKg,
                allTime.AssistedKg,
                allTime.Reps,
                TrackingMode,
                string.Empty));
        var known = knownPerformances.ToArray();
        var outcome = known.Length == 0 || known.All(row => Outranks(saved, row))
            ? SetSavedOutcome.PersonalRecord
            : comparable is not null && SameMeasurement(saved, comparable)
                ? SetSavedOutcome.MatchedPrevious
                : SetSavedOutcome.Saved;
        return new SetSavedPresentation(
            saved,
            outcome,
            outcome switch
            {
                SetSavedOutcome.PersonalRecord => _text.PersonalRecordSaved,
                SetSavedOutcome.MatchedPrevious => _text.MatchedPreviousSet,
                _ => _text.SetSaved
            },
            outcome switch
            {
                SetSavedOutcome.PersonalRecord => _text.PersonalRecordSecondary,
                SetSavedOutcome.MatchedPrevious => _text.MatchedPreviousSecondary,
                _ => _text.SetSavedSecondary
            });
    }

    private bool Outranks(LocalSet candidate, SetDisplayRow existing) => TrackingMode switch
    {
        TrackingMode.Weighted => candidate.WeightKg > existing.WeightKg
            || candidate.WeightKg == existing.WeightKg && candidate.Reps > existing.Reps,
        TrackingMode.Assisted => candidate.AssistedKg < existing.AssistedKg
            || candidate.AssistedKg == existing.AssistedKg && candidate.Reps > existing.Reps,
        TrackingMode.Bodyweight => candidate.Reps > existing.Reps,
        _ => false
    };

    private bool SameMeasurement(LocalSet candidate, SetDisplayRow existing) =>
        candidate.WeightKg == existing.WeightKg
        && candidate.AssistedKg == existing.AssistedKg
        && candidate.Reps == existing.Reps;

    private async Task RefreshSyncStateCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return;
        var pending = await _outbox.PendingAsync(_workoutId, cancellationToken);
        if (pending.Any(operation => operation.SendStartedAt is not null))
        {
            ClearActiveConflict();
            SyncState = WorkoutSyncState.Reconciling;
            return;
        }
        var conflicts = await _outbox.ConflictedAsync(_workoutId, cancellationToken);
        if (conflicts.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;
            SetActiveConflict(conflicts.OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.OperationId).First());
            SyncState = WorkoutSyncState.Conflicted;
            return;
        }
        ClearActiveConflict();
        if ((await _outbox.RejectedAsync(_workoutId, cancellationToken)).Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;
            SyncState = WorkoutSyncState.PermanentFailure;
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return;
        SyncState = !_connectivity.IsOnline
            ? WorkoutSyncState.Offline
            : pending.Count == 0
                ? WorkoutSyncState.Synced
                : WorkoutSyncState.Pending;
    }

    private async Task ResolveConflictAsync(bool keepServer)
    {
        var conflict = _activeConflict;
        if (!CanResolveConflict || conflict?.ServerVersion is not { } serverVersion) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (keepServer)
                await _conflicts!.KeepServerAsync(conflict.OperationId, _lifetime.Token);
            else
                _ = await _conflicts!.ApplyLocalAgainstVersionAsync(
                    conflict.OperationId, serverVersion, _lifetime.Token);
            await RefreshSyncStateCoreAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception) when (!_disposed)
        {
            ErrorMessage = _text.HistoryConflictFailed;
        }
        finally
        {
            if (!_disposed) IsBusy = false;
        }
    }

    private void SetActiveConflict(OutboxOperation operation)
    {
        _activeConflict = operation;
        ConflictLocalSummary = LocalConflictSummary(operation);
        ConflictServerSummary = ServerConflictSummary(operation);
        OnPropertyChanged(nameof(HasConflict));
        RaiseCommands();
    }

    private string LocalConflictSummary(OutboxOperation operation) => operation.Type switch
    {
        OutboxOperationType.StartWorkout =>
            $"Local StartWorkout · {Plural(operation.DeserializePayload<StartWorkoutOutboxPayload>().Exercises.Count, "exercise")} · base {operation.BaseVersion}",
        OutboxOperationType.SaveSet => SaveSetConflictSummary(operation),
        OutboxOperationType.CompleteWorkout =>
            $"Local CompleteWorkout · base {operation.BaseVersion}",
        OutboxOperationType.AddExercise =>
            $"Local AddExercise · base {operation.BaseVersion}",
        OutboxOperationType.RemoveExercise =>
            $"Local RemoveExercise · base {operation.BaseVersion}",
        OutboxOperationType.ReorderExercises =>
            $"Local ReorderExercises · {Plural(operation.DeserializePayload<ReorderExercisesOutboxPayload>().WorkoutExerciseIds.Count, "exercise")} · base {operation.BaseVersion}",
        _ => $"Local {operation.Type} · base {operation.BaseVersion}"
    };

    private string SaveSetConflictSummary(OutboxOperation operation)
    {
        var payload = operation.DeserializePayload<SaveSetOutboxPayload>();
        var weight = ParseCanonical(payload.WeightKg);
        var assisted = ParseCanonical(payload.AssistedKg);
        var mode = weight is not null
            ? TrackingMode.Weighted
            : assisted is not null
                ? TrackingMode.Assisted
                : TrackingMode.Bodyweight;
        return $"Local SaveSet · {Measurement(weight, assisted, payload.Reps, mode)} · base {operation.BaseVersion}";
    }

    private static string ServerConflictSummary(OutboxOperation operation)
    {
        if (operation.ServerPayload is null)
            return $"Server version {operation.ServerVersion}";
        try
        {
            var graph = JsonSerializer.Deserialize<SyncWorkoutDto>(
                operation.ServerPayload, JsonSerializerOptions.Web);
            if (graph is null) return $"Server version {operation.ServerVersion}";
            var exercises = graph.Exercises.Count(item => item.DeletedAt is null);
            var sets = graph.Exercises
                .Where(item => item.DeletedAt is null)
                .SelectMany(item => item.Sets)
                .Count(item => item.DeletedAt is null);
            return $"Server version {operation.ServerVersion} · {Plural(exercises, "exercise")} · {Plural(sets, "set")}";
        }
        catch (JsonException)
        {
            return $"Server version {operation.ServerVersion}";
        }
    }

    private static decimal? ParseCanonical(string? value) => value is null
        ? null
        : decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string Plural(int count, string noun) =>
        $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    private void ClearActiveConflict()
    {
        if (_activeConflict is null && ConflictLocalSummary is null && ConflictServerSummary is null) return;
        _activeConflict = null;
        ConflictLocalSummary = null;
        ConflictServerSummary = null;
        OnPropertyChanged(nameof(HasConflict));
        RaiseCommands();
    }

    private async Task SynchronizeBestEffortAsync(AccountSessionGeneration generation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = lifetime.Token;
        if (_disposed || token.IsCancellationRequested) return;
        SyncState = WorkoutSyncState.Syncing;
        try
        {
            var result = await _syncRunner!.RunOnceAsync(token);
            if (_disposed || token.IsCancellationRequested
                || _boundary.IsCancellationRequested(generation)) return;
            if (result == SyncRunStatus.Offline)
            {
                SyncState = WorkoutSyncState.Offline;
                return;
            }
            await RefreshSyncStateCoreAsync(token);
        }
        catch (OperationCanceledException) when (_disposed || token.IsCancellationRequested
            || _boundary.IsCancellationRequested(generation))
        {
        }
        catch (Exception)
        {
            if (_disposed || token.IsCancellationRequested
                || _boundary.IsCancellationRequested(generation)) return;
            try
            {
                await RefreshSyncStateCoreAsync(token);
            }
            catch (Exception) when (_disposed || token.IsCancellationRequested)
            {
            }
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
        TrackingMode.Weighted => $"{MeasurementValue(weight)} {WeightUnitLabel} × {reps}",
        TrackingMode.Assisted => $"{MeasurementValue(assisted)} {WeightUnitLabel} · {reps} {_text.Reps}",
        TrackingMode.Bodyweight => $"{reps} {_text.Reps}",
        _ => string.Empty
    };

    private decimal? DisplayKilograms(decimal? kilograms) => kilograms is null
        ? null
        : DisplayUnit == WeightDisplayUnit.Kilograms
            ? kilograms
            : decimal.Round(kilograms.Value * PoundsPerKilogram, 2, MidpointRounding.AwayFromZero);

    private string MeasurementValue(decimal? kilograms) =>
        DisplayKilograms(kilograms)?.ToString(
            DisplayUnit == WeightDisplayUnit.Kilograms ? "0.###" : "0.00",
            CultureInfo.CurrentCulture) ?? string.Empty;

    private void RefreshMeasurementRows()
    {
        for (var index = 0; index < LastSets.Count; index++)
        {
            var row = LastSets[index];
            LastSets[index] = row with
            {
                MeasurementText = Measurement(row.WeightKg, row.AssistedKg, row.Reps, row.TrackingMode)
            };
        }
        for (var index = 0; index < TodaySets.Count; index++)
        {
            var row = TodaySets[index];
            TodaySets[index] = row with
            {
                MeasurementText = Measurement(row.WeightKg, row.AssistedKg, row.Reps, row.TrackingMode)
            };
        }
        RefreshExerciseContext();
    }

    private void RefreshExerciseContext()
    {
        var best = Best(LastSets);
        var cachedPresentation = _cachedExercise is null
            ? null
            : new ExercisePickerItem(_cachedExercise, _text, _unitPreference);
        PreviousBestText = best is not null
            ? string.Format(
                CultureInfo.CurrentCulture,
                _text.PerformanceLastFormat,
                best.MeasurementText)
            : _cachedExercise?.LastBestSet is not null
                ? cachedPresentation!.LastText
                : string.Empty;
        AllTimePrText = _cachedExercise?.AllTimeBest is not null
            ? cachedPresentation!.PersonalRecordText
            : string.Empty;
    }

    private void PublishSetPresentationState()
    {
        OnPropertyChanged(nameof(ShowsNoSetHistory));
        OnPropertyChanged(nameof(ShowsSetComparison));
        OnPropertyChanged(nameof(HasPreviousBest));
        OnPropertyChanged(nameof(HasAllTimePr));
        OnPropertyChanged(nameof(NextSetNumber));
        OnPropertyChanged(nameof(NextSetText));
        OnPropertyChanged(nameof(DraftSetNumber));
        OnPropertyChanged(nameof(SaveDraftSetText));
    }

    private SetDisplayRow? Best(IEnumerable<SetDisplayRow> rows) => TrackingMode switch
    {
        TrackingMode.Weighted => rows.OrderByDescending(row => row.WeightKg).ThenByDescending(row => row.Reps).FirstOrDefault(),
        TrackingMode.Assisted => rows.OrderBy(row => row.AssistedKg).ThenByDescending(row => row.Reps).FirstOrDefault(),
        TrackingMode.Bodyweight => rows.OrderByDescending(row => row.Reps).FirstOrDefault(),
        _ => null
    };

    private string BodyPartText(BodyPart bodyPart) => bodyPart switch
    {
        BodyPart.Chest => _text.BodyPartChest,
        BodyPart.Back => _text.BodyPartBack,
        BodyPart.Shoulders => _text.BodyPartShoulders,
        BodyPart.Arms => _text.BodyPartArms,
        BodyPart.Legs => _text.BodyPartLegs,
        BodyPart.Core => _text.BodyPartCore,
        _ => string.Empty
    };

    private void MeasurementChanged()
    {
        OnPropertyChanged(nameof(CanCompleteSet));
        OnPropertyChanged(nameof(CanSaveDraftSet));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanMatchLast));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        CompleteSetCommand.RaiseCanExecuteChanged();
        SaveDraftSetCommand.RaiseCanExecuteChanged();
        (BeginSetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelDraftSetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MatchLastCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (IncrementWeightCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DecrementWeightCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (IncrementRepsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DecrementRepsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UseKilogramsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UsePoundsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        KeepServerCommand.RaiseCanExecuteChanged();
        ApplyLocalCommand.RaiseCanExecuteChanged();
    }

    public void Deactivate()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _loadGeneration);
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _lifetime.Cancel();
        _boundary.SessionReset -= OnSessionReset;
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        if (_unitPreference is not null) _unitPreference.Changed -= OnWeightUnitChanged;
        HasDraftSet = false;
        if (_isBusy)
        {
            _isBusy = false;
            OnPropertyChanged(nameof(IsBusy));
        }
        MeasurementChanged();
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        if (!_disposed) ClearPrivateState();
    }

    private void ClearPrivateState()
    {
        _workoutId = Guid.Empty;
        _exerciseId = Guid.Empty;
        ExerciseName = string.Empty;
        _cachedExercise = null;
        ThumbnailUri = null;
        ExerciseMetadataText = string.Empty;
        PreviousBestText = string.Empty;
        AllTimePrText = string.Empty;
        LastSets.Clear();
        TodaySets.Clear();
        HasDraftSet = false;
        PublishSetPresentationState();
        WeightKg = null;
        AssistedKg = null;
        Reps = 0;
        ErrorMessage = null;
        SyncState = _connectivity.IsOnline ? WorkoutSyncState.Synced : WorkoutSyncState.Offline;
        ClearActiveConflict();
        MeasurementChanged();
    }

    private async void OnConnectivityChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;
        try { await RefreshSyncStateAsync(_lifetime.Token); }
        catch (Exception) { if (!_disposed) SyncState = WorkoutSyncState.Offline; }
    }

    private void OnWeightUnitChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed || _unitPreference is null) return;
        DisplayUnit = _unitPreference.Current;
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
