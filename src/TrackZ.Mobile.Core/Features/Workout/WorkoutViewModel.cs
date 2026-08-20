using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Workout;

public sealed record WorkoutExerciseDraftItem(
    Guid ExerciseDefinitionId,
    string Name,
    TrackingMode TrackingMode,
    string TrackingModeText,
    string? ThumbnailUri,
    Guid WorkoutExerciseId = default,
    int LoggedSetCount = 0,
    string LoggedSetText = "",
    string LastText = "",
    string AccessibilitySummary = "")
{
    public bool HasArtwork => !string.IsNullOrWhiteSpace(ThumbnailUri);
    public bool ShowsArtworkPlaceholder => !HasArtwork;
}

public sealed class WorkoutViewModel : INotifyPropertyChanged
{
    private readonly ActiveWorkoutCoordinator _coordinator;
    private readonly ExerciseCache _cache;
    private readonly IAccountSessionBoundary _boundary;
    private readonly WorkoutTextSet _text;
    private readonly IHistoryConfirmation? _confirmation;
    private readonly IWeightUnitPreference? _unitPreference;
    private IReadOnlyDictionary<Guid, CachedExercise> _catalog = new Dictionary<Guid, CachedExercise>();
    private bool _isBusy;
    private bool _hasStarted;
    private string? _errorMessage;

    public WorkoutViewModel(
        ActiveWorkoutCoordinator coordinator,
        ExerciseCache cache,
        IAccountSessionBoundary boundary,
        WorkoutTextSet text,
        IHistoryConfirmation? confirmation = null,
        IWeightUnitPreference? unitPreference = null)
    {
        _coordinator = coordinator;
        _cache = cache;
        _boundary = boundary;
        _text = text;
        _confirmation = confirmation;
        _unitPreference = unitPreference;
        RemoveExerciseCommand = new AsyncCommand(RemoveExerciseAsync,
            item => !IsBusy && Id(item) != Guid.Empty && (!_hasStarted || Exercises.Count > 1));
        MoveUpCommand = new AsyncCommand(item => MoveAsync(item, -1), item => CanMove(item, -1));
        MoveDownCommand = new AsyncCommand(item => MoveAsync(item, 1), item => CanMove(item, 1));
        StartWorkoutCommand = new AsyncCommand(_ => StartAsync(), _ => Exercises.Count != 0 && !_hasStarted && !IsBusy);
        FinishWorkoutCommand = new AsyncCommand(_ => FinishAsync(), _ => _hasStarted && !IsBusy);
        _boundary.SessionReset += OnSessionReset;
        if (_unitPreference is not null) _unitPreference.Changed += OnWeightUnitChanged;
    }

    public ObservableCollection<WorkoutExerciseDraftItem> Exercises { get; } = [];
    public AsyncCommand RemoveExerciseCommand { get; }
    public AsyncCommand MoveUpCommand { get; }
    public AsyncCommand MoveDownCommand { get; }
    public AsyncCommand StartWorkoutCommand { get; }
    public AsyncCommand FinishWorkoutCommand { get; }
    public WorkoutTextSet Text => _text;
    public Guid? CompletedWorkoutId { get; private set; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            RaiseCommands();
        }
    }

    public bool HasStarted
    {
        get => _hasStarted;
        private set
        {
            if (!Set(ref _hasStarted, value)) return;
            RaiseCommands();
        }
    }

    public bool IsDraft => !HasStarted;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<Guid>? WorkoutFinished;

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        var active = await _coordinator.RestoreActiveAsync(cancellationToken);
        if (active is null) return;
        _catalog = (await _cache.GetAllAsync(cancellationToken)).ToDictionary(item => item.Id);
        Exercises.Clear();
        foreach (var exercise in active.Exercises.Where(item => item.DeletedAt is null).OrderBy(item => item.Order))
        {
            _catalog.TryGetValue(exercise.ExerciseDefinitionId, out var cached);
            Exercises.Add(CreateItem(
                exercise.ExerciseDefinitionId,
                exercise.TrackingMode,
                cached,
                exercise.Id,
                exercise.Sets.Count(set => set.DeletedAt is null)));
        }
        HasStarted = true;
    }

    public async Task AddExercisesAsync(
        IReadOnlyList<Guid> exerciseIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exerciseIds);
        var requested = exerciseIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var existing = Exercises.Select(item => item.ExerciseDefinitionId).ToHashSet();
        _catalog = (await _cache.GetAllAsync(cancellationToken)).ToDictionary(item => item.Id);
        foreach (var id in requested)
        {
            if (existing.Contains(id)) continue;
            if (!_catalog.TryGetValue(id, out var exercise))
                throw new ArgumentException("Exercise is not available in the local catalog.", nameof(exerciseIds));
            var workoutExerciseId = Guid.Empty;
            if (HasStarted)
            {
                var added = await _coordinator.AddExerciseAsync(
                    new WorkoutExerciseSelection(id, exercise.TrackingMode),
                    cancellationToken: cancellationToken);
                workoutExerciseId = added.Id;
            }
            Exercises.Add(CreateItem(
                id,
                exercise.TrackingMode,
                exercise,
                workoutExerciseId,
                0));
            existing.Add(id);
        }
        RaiseCommands();
    }

    private async Task StartAsync()
    {
        if (Exercises.Count == 0 || HasStarted) return;
        var generation = _boundary.Capture();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var started = await _coordinator.StartAsync(Exercises.Select(item =>
                new WorkoutExerciseSelection(item.ExerciseDefinitionId, item.TrackingMode)).ToArray());
            if (!_boundary.IsCancellationRequested(generation))
            {
                for (var index = 0; index < Exercises.Count; index++)
                {
                    var exercise = Exercises[index];
                    Exercises[index] = exercise with
                    {
                        WorkoutExerciseId = started.Exercises.Single(item =>
                            item.ExerciseDefinitionId == exercise.ExerciseDefinitionId).Id
                    };
                }
                HasStarted = true;
            }
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
            Clear();
        }
        catch (Exception) when (!_boundary.IsCancellationRequested(generation))
        {
            ErrorMessage = _text.SaveFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FinishAsync()
    {
        if (!HasStarted) return;
        var generation = _boundary.Capture();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var completed = await _coordinator.FinishAsync(cancellationToken: default);
            if (!_boundary.IsCancellationRequested(generation))
            {
                CompletedWorkoutId = completed.Id;
                Exercises.Clear();
                HasStarted = false;
                WorkoutFinished?.Invoke(this, completed.Id);
            }
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
            Clear();
        }
        catch (Exception) when (!_boundary.IsCancellationRequested(generation))
        {
            ErrorMessage = _text.SaveFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveExerciseAsync(object? item)
    {
        var id = Id(item);
        if (id == Guid.Empty) return;
        var selected = Exercises.SingleOrDefault(exercise => exercise.ExerciseDefinitionId == id);
        if (selected is null) return;
        if (HasStarted)
        {
            if (selected.WorkoutExerciseId == Guid.Empty) return;
            if (_confirmation is not null && !await _confirmation.ConfirmAsync(
                    _text.DeleteExerciseTitle,
                    _text.DeleteExerciseMessage,
                    _text.Delete,
                    _text.Cancel)) return;
            await _coordinator.RemoveExerciseAsync(selected.WorkoutExerciseId);
        }
        Exercises.Remove(selected);
        RaiseCommands();
    }

    private async Task MoveAsync(object? item, int delta)
    {
        if (!CanMove(item, delta)) return;
        var index = Exercises.IndexOf(Exercises.Single(exercise => exercise.ExerciseDefinitionId == Id(item)));
        if (HasStarted)
        {
            var ordered = Exercises.Select(exercise => exercise.WorkoutExerciseId).ToList();
            var moved = ordered[index];
            ordered.RemoveAt(index);
            ordered.Insert(index + delta, moved);
            await _coordinator.ReorderExercisesAsync(ordered);
        }
        Exercises.Move(index, index + delta);
        RaiseCommands();
    }

    private bool CanMove(object? item, int delta)
    {
        var id = Id(item);
        if (IsBusy || id == Guid.Empty) return false;
        var existing = Exercises.SingleOrDefault(exercise => exercise.ExerciseDefinitionId == id);
        if (existing is null) return false;
        var target = Exercises.IndexOf(existing) + delta;
        return target >= 0 && target < Exercises.Count;
    }

    private static Guid Id(object? item) => item switch
    {
        WorkoutExerciseDraftItem exercise => exercise.ExerciseDefinitionId,
        Guid id => id,
        _ => Guid.Empty
    };

    private string ModeText(TrackingMode mode) => mode switch
    {
        TrackingMode.Weighted => _text.Weight,
        TrackingMode.Assisted => _text.Assistance,
        TrackingMode.Bodyweight => _text.Bodyweight,
        _ => string.Empty
    };

    private WorkoutExerciseDraftItem CreateItem(
        Guid exerciseDefinitionId,
        TrackingMode trackingMode,
        CachedExercise? cached,
        Guid workoutExerciseId,
        int loggedSetCount)
    {
        var name = cached?.Name ?? exerciseDefinitionId.ToString("D");
        var mode = ModeText(trackingMode);
        var last = cached is null
            ? string.Format(_text.PerformanceLastFormat, "—")
            : new ExercisePickerItem(cached, _text, _unitPreference).LastText;
        var logged = string.Format(_text.SetsLoggedFormat, loggedSetCount);
        return new WorkoutExerciseDraftItem(
            exerciseDefinitionId,
            name,
            trackingMode,
            mode,
            cached?.ThumbnailUri,
            workoutExerciseId,
            loggedSetCount,
            logged,
            last,
            string.Join(", ", name, mode, logged, last));
    }

    private void OnWeightUnitChanged(object? sender, EventArgs eventArgs)
    {
        for (var index = 0; index < Exercises.Count; index++)
        {
            var current = Exercises[index];
            _catalog.TryGetValue(current.ExerciseDefinitionId, out var cached);
            Exercises[index] = CreateItem(
                current.ExerciseDefinitionId,
                current.TrackingMode,
                cached,
                current.WorkoutExerciseId,
                current.LoggedSetCount);
        }
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs) => Clear();

    private void Clear()
    {
        Exercises.Clear();
        HasStarted = false;
        ErrorMessage = null;
        CompletedWorkoutId = null;
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        StartWorkoutCommand.RaiseCanExecuteChanged();
        FinishWorkoutCommand.RaiseCanExecuteChanged();
        RemoveExerciseCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsDraft));
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
