using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Workout;

public sealed record WorkoutExerciseDraftItem(
    Guid ExerciseDefinitionId,
    string Name,
    TrackingMode TrackingMode,
    string TrackingModeText,
    string? ThumbnailUri);

public sealed class WorkoutViewModel : INotifyPropertyChanged
{
    private readonly ActiveWorkoutCoordinator _coordinator;
    private readonly ExerciseCache _cache;
    private readonly IAccountSessionBoundary _boundary;
    private readonly WorkoutTextSet _text;
    private bool _isBusy;
    private bool _hasStarted;
    private string? _errorMessage;

    public WorkoutViewModel(
        ActiveWorkoutCoordinator coordinator,
        ExerciseCache cache,
        IAccountSessionBoundary boundary,
        WorkoutTextSet text)
    {
        _coordinator = coordinator;
        _cache = cache;
        _boundary = boundary;
        _text = text;
        RemoveExerciseCommand = new RelayCommand(RemoveExercise, item => !_hasStarted && !IsBusy && Id(item) != Guid.Empty);
        MoveUpCommand = new RelayCommand(item => Move(item, -1), item => CanMove(item, -1));
        MoveDownCommand = new RelayCommand(item => Move(item, 1), item => CanMove(item, 1));
        StartWorkoutCommand = new AsyncCommand(_ => StartAsync(), _ => Exercises.Count != 0 && !_hasStarted && !IsBusy);
        FinishWorkoutCommand = new AsyncCommand(_ => FinishAsync(), _ => _hasStarted && !IsBusy);
        _boundary.SessionReset += OnSessionReset;
    }

    public ObservableCollection<WorkoutExerciseDraftItem> Exercises { get; } = [];
    public ICommand RemoveExerciseCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public AsyncCommand StartWorkoutCommand { get; }
    public AsyncCommand FinishWorkoutCommand { get; }
    public WorkoutTextSet Text => _text;

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

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        var active = await _coordinator.RestoreActiveAsync(cancellationToken);
        if (active is null) return;
        var catalog = (await _cache.GetAllAsync(cancellationToken)).ToDictionary(item => item.Id);
        Exercises.Clear();
        foreach (var exercise in active.Exercises.Where(item => item.DeletedAt is null).OrderBy(item => item.Order))
        {
            catalog.TryGetValue(exercise.ExerciseDefinitionId, out var cached);
            Exercises.Add(new WorkoutExerciseDraftItem(
                exercise.ExerciseDefinitionId,
                cached?.Name ?? exercise.ExerciseDefinitionId.ToString("D"),
                exercise.TrackingMode,
                ModeText(exercise.TrackingMode),
                cached?.ThumbnailUri));
        }
        HasStarted = true;
    }

    public async Task AddExercisesAsync(
        IReadOnlyList<Guid> exerciseIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exerciseIds);
        if (HasStarted) throw new InvalidOperationException("Active-session exercise changes are not supported by this client.");
        var requested = exerciseIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var existing = Exercises.Select(item => item.ExerciseDefinitionId).ToHashSet();
        var catalog = (await _cache.GetAllAsync(cancellationToken)).ToDictionary(item => item.Id);
        foreach (var id in requested)
        {
            if (existing.Contains(id)) continue;
            if (!catalog.TryGetValue(id, out var exercise))
                throw new ArgumentException("Exercise is not available in the local catalog.", nameof(exerciseIds));
            Exercises.Add(new WorkoutExerciseDraftItem(
                id, exercise.Name, exercise.TrackingMode, ModeText(exercise.TrackingMode), exercise.ThumbnailUri));
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
            await _coordinator.StartAsync(Exercises.Select(item =>
                new WorkoutExerciseSelection(item.ExerciseDefinitionId, item.TrackingMode)).ToArray());
            if (!_boundary.IsCancellationRequested(generation)) HasStarted = true;
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
            await _coordinator.FinishAsync(cancellationToken: default);
            if (!_boundary.IsCancellationRequested(generation))
            {
                Exercises.Clear();
                HasStarted = false;
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

    private void RemoveExercise(object? item)
    {
        var id = Id(item);
        if (HasStarted || id == Guid.Empty) return;
        var existing = Exercises.SingleOrDefault(exercise => exercise.ExerciseDefinitionId == id);
        if (existing is not null) Exercises.Remove(existing);
        RaiseCommands();
    }

    private void Move(object? item, int delta)
    {
        if (!CanMove(item, delta)) return;
        var index = Exercises.IndexOf(Exercises.Single(exercise => exercise.ExerciseDefinitionId == Id(item)));
        Exercises.Move(index, index + delta);
        RaiseCommands();
    }

    private bool CanMove(object? item, int delta)
    {
        var id = Id(item);
        if (HasStarted || IsBusy || id == Guid.Empty) return false;
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

    private void OnSessionReset(object? sender, EventArgs eventArgs) => Clear();

    private void Clear()
    {
        Exercises.Clear();
        HasStarted = false;
        ErrorMessage = null;
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        StartWorkoutCommand.RaiseCanExecuteChanged();
        FinishWorkoutCommand.RaiseCanExecuteChanged();
        (RemoveExerciseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
