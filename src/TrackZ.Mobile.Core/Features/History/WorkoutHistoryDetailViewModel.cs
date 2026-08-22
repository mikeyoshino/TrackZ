using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.History;

public sealed class WorkoutHistoryDetailViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly WorkoutHistoryViewModel _source;
    private readonly IAccountSessionBoundary _boundary;
    private HistoryWorkoutItem? _workout;
    private bool _deactivated;

    public WorkoutHistoryDetailViewModel(
        WorkoutHistoryViewModel source,
        IAccountSessionBoundary boundary)
    {
        _source = source;
        _boundary = boundary;
        EditSetCommand = WrapAsync(_source.EditSetCommand);
        DeleteSetCommand = WrapAsync(_source.DeleteSetCommand);
        DeleteExerciseCommand = WrapAsync(_source.DeleteWorkoutExerciseCommand);
        DeleteWorkoutCommand = WrapAsync(_source.DeleteWorkoutCommand);
        UndoCommand = WrapAsync(_source.UndoCommand);
        KeepServerCommand = WrapAsync(_source.KeepServerCommand);
        ApplyLocalCommand = WrapAsync(_source.ApplyLocalCommand);
        _boundary.SessionReset += OnSessionReset;
    }

    public Guid WorkoutId { get; private set; }
    public HistoryWorkoutItem? Workout { get => _workout; private set => Set(ref _workout, value); }
    public WorkoutTextSet Text => _source.Text;
    public bool IsBusy => _source.IsBusy;
    public string? ErrorMessage => _source.ErrorMessage;
    public AsyncCommand EditSetCommand { get; }
    public AsyncCommand DeleteSetCommand { get; }
    public AsyncCommand DeleteExerciseCommand { get; }
    public AsyncCommand DeleteWorkoutCommand { get; }
    public AsyncCommand UndoCommand { get; }
    public AsyncCommand KeepServerCommand { get; }
    public AsyncCommand ApplyLocalCommand { get; }
    public bool IsReconciling => Workout?.IsReconciling == true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(Guid workoutId, CancellationToken cancellationToken = default)
    {
        if (_deactivated) return;
        if (workoutId == Guid.Empty) throw new ArgumentException("Workout ID is required.", nameof(workoutId));
        WorkoutId = workoutId;
        await ReloadAsync(cancellationToken);
    }

    public void Deactivate()
    {
        if (_deactivated) return;
        _deactivated = true;
        _boundary.SessionReset -= OnSessionReset;
        _source.Deactivate();
        Workout = null;
    }

    public void Dispose() => Deactivate();

    public static bool CanResolveDestructively(HistoryWorkoutItem workout) =>
        !workout.ActionsBlocked && !workout.IsReconciling;

    private AsyncCommand WrapAsync(AsyncCommand command) => new(
        async parameter =>
        {
            if (_deactivated || !command.CanExecute(parameter)) return;
            await command.ExecuteAsync(parameter);
            await ReloadAsync();
        },
        parameter => !_deactivated && command.CanExecute(parameter));

    private async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var generation = _boundary.Capture();
        await _source.LoadAsync(cancellationToken);
        if (_deactivated || _boundary.IsCancellationRequested(generation)) return;
        Workout = _source.Workouts.SingleOrDefault(item => item.WorkoutId == WorkoutId);
        OnPropertyChanged(nameof(IsReconciling));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        EditSetCommand.RaiseCanExecuteChanged();
        DeleteSetCommand.RaiseCanExecuteChanged();
        DeleteExerciseCommand.RaiseCanExecuteChanged();
        DeleteWorkoutCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        KeepServerCommand.RaiseCanExecuteChanged();
        ApplyLocalCommand.RaiseCanExecuteChanged();
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        Workout = null;
        RaiseCommands();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
