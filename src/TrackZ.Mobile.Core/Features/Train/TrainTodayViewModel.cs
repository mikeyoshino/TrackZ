using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Train;

public sealed record RecentWorkoutItem(
    Guid WorkoutId,
    string Title,
    DateTimeOffset CompletedAt,
    int ExerciseCount,
    string? ThumbnailPath,
    string ExerciseCountText);

public sealed class TrainTodayViewModel : INotifyPropertyChanged
{
    private readonly ITrainDashboardSource _source;
    private readonly IAccountSessionBoundary _boundary;
    private readonly IConnectivityService? _connectivity;
    private bool _isBusy;
    private ActiveWorkoutCard? _activeWorkout;
    private string? _errorText;

    public TrainTodayViewModel(
        ITrainDashboardSource source,
        IAccountSessionBoundary boundary,
        WorkoutTextSet text,
        IConnectivityService? connectivity = null)
    {
        _source = source;
        _boundary = boundary;
        _connectivity = connectivity;
        Text = text;
    }

    public WorkoutTextSet Text { get; }
    public ObservableCollection<RecentWorkoutItem> RecentWorkouts { get; } = [];
    public bool HasRecentWorkouts => RecentWorkouts.Count > 0;
    public bool HasActiveWorkout => ActiveWorkout is not null;
    public bool IsOffline => _connectivity is { IsOnline: false };
    public string SavedSetCountText => string.Format(
        Text.SavedSetCountFormat,
        ActiveWorkout?.LoggedSetCount ?? 0);

    public ActiveWorkoutCard? ActiveWorkout
    {
        get => _activeWorkout;
        private set
        {
            if (Set(ref _activeWorkout, value))
            {
                OnPropertyChanged(nameof(HasActiveWorkout));
                OnPropertyChanged(nameof(SavedSetCountText));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        var generation = _boundary.Capture();
        IsBusy = true;
        ErrorText = null;
        try
        {
            using var lease = _boundary.CreateCancellationLease(generation, cancellationToken);
            var snapshot = await _source.LoadAsync(lease.Token);
            _ = await _boundary.TryCommitAsync(generation, _ =>
            {
                ActiveWorkout = snapshot.Active;
                RecentWorkouts.Clear();
                foreach (var item in snapshot.Recent.Take(3))
                {
                    RecentWorkouts.Add(new RecentWorkoutItem(
                        item.WorkoutId,
                        FormatBodyParts(item.BodyParts),
                        item.CompletedAt,
                        item.ExerciseCount,
                        item.ThumbnailPath,
                        string.Format(Text.ExerciseCountFormat, item.ExerciseCount)));
                }
                OnPropertyChanged(nameof(HasRecentWorkouts));
                OnPropertyChanged(nameof(IsOffline));
                return Task.CompletedTask;
            }, lease.Token);
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            if (!_boundary.IsCancellationRequested(generation)) ErrorText = Text.LoadFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string FormatBodyParts(IReadOnlyList<BodyPart> bodyParts) =>
        string.Join(" + ", bodyParts.Select(BodyPartLabel));

    private string BodyPartLabel(BodyPart bodyPart) => bodyPart switch
    {
        BodyPart.Chest => Text.BodyPartChest,
        BodyPart.Back => Text.BodyPartBack,
        BodyPart.Shoulders => Text.BodyPartShoulders,
        BodyPart.Arms => Text.BodyPartArms,
        BodyPart.Legs => Text.BodyPartLegs,
        BodyPart.Core => Text.BodyPartCore,
        _ => throw new ArgumentOutOfRangeException(nameof(bodyPart))
    };

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
