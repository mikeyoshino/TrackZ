using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Gamification;
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
    private readonly IProgressSnapshotSource? _progress;
    private readonly IWeightUnitPreference _weightUnits;
    private readonly GamificationTextSet _gamificationText;
    private bool _isBusy;
    private bool _isProgressLoading;
    private ActiveWorkoutCard? _activeWorkout;
    private RepeatWorkoutShortcut? _repeatWorkout;
    private HomeMomentumItem? _recentMomentum;
    private bool _hasAuthoritativeProgress;
    private int _weeklyCompletedWorkouts;
    private int _weeklyGoal;
    private int _currentStreakWeeks;
    private int _level;
    private int _totalXp;
    private int _currentLevelRequiredXp;
    private int? _nextLevelRequiredXp;
    private string? _errorText;

    public TrainTodayViewModel(
        ITrainDashboardSource source,
        IAccountSessionBoundary boundary,
        WorkoutTextSet text,
        IConnectivityService? connectivity = null)
        : this(source, boundary, text, null, connectivity, new FixedWeightUnitPreference(), GamificationResources.Current)
    {
    }

    public TrainTodayViewModel(
        ITrainDashboardSource source,
        IAccountSessionBoundary boundary,
        WorkoutTextSet text,
        IProgressSnapshotSource? progress,
        IConnectivityService? connectivity,
        IWeightUnitPreference weightUnits,
        GamificationTextSet gamificationText)
    {
        _source = source;
        _boundary = boundary;
        _connectivity = connectivity;
        _progress = progress;
        _weightUnits = weightUnits;
        _gamificationText = gamificationText;
        Text = text;
        _boundary.SessionReset += OnSessionReset;
    }

    public WorkoutTextSet Text { get; }
    public ObservableCollection<RecentWorkoutItem> RecentWorkouts { get; } = [];
    public bool HasRecentWorkouts => RecentWorkouts.Count > 0;
    public bool HasActiveWorkout => ActiveWorkout is not null;
    public bool IsOffline => _connectivity is { IsOnline: false };
    public bool HasAuthoritativeProgress
    {
        get => _hasAuthoritativeProgress;
        private set
        {
            if (Set(ref _hasAuthoritativeProgress, value))
                OnPropertyChanged(nameof(LevelProgress));
        }
    }
    public int WeeklyCompletedWorkouts { get => _weeklyCompletedWorkouts; private set => Set(ref _weeklyCompletedWorkouts, value); }
    public int WeeklyGoal { get => _weeklyGoal; private set => Set(ref _weeklyGoal, value); }
    public int CurrentStreakWeeks { get => _currentStreakWeeks; private set => Set(ref _currentStreakWeeks, value); }
    public int Level { get => _level; private set => Set(ref _level, value); }
    public int TotalXp { get => _totalXp; private set => Set(ref _totalXp, value); }
    public double LevelProgress => !HasAuthoritativeProgress
        ? 0d
        : _nextLevelRequiredXp is not { } next || next <= _currentLevelRequiredXp
            ? 1d
            : Math.Clamp((double)(TotalXp - _currentLevelRequiredXp) / (next - _currentLevelRequiredXp), 0d, 1d);
    public HomeMomentumItem? RecentMomentum
    {
        get => _recentMomentum;
        private set
        {
            if (ReferenceEquals(_recentMomentum, value)) return;
            _recentMomentum?.Dispose();
            _recentMomentum = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRecentMomentum));
        }
    }
    public bool HasRecentMomentum => RecentMomentum is not null;
    public bool IsProgressLoading { get => _isProgressLoading; private set => Set(ref _isProgressLoading, value); }
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

    public RepeatWorkoutShortcut? RepeatWorkout
    {
        get => _repeatWorkout;
        private set => Set(ref _repeatWorkout, value);
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
        if (!_boundary.TryStartSessionPhase(generation, () =>
        {
            IsBusy = true;
            ErrorText = null;
        }, cancellationToken)) return;
        try
        {
            using var lease = _boundary.CreateCancellationLease(generation, cancellationToken);
            var snapshot = await _source.LoadAsync(lease.Token);
            _ = await _boundary.TryCommitAsync(generation, _ =>
            {
                ActiveWorkout = snapshot.Active;
                RepeatWorkout = snapshot.Repeat;
                RecentWorkouts.Clear();
                if (snapshot.Repeat is { } item)
                {
                    RecentWorkouts.Add(new RecentWorkoutItem(
                        item.SourceWorkoutId,
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

            if (_progress is not null)
                await LoadProgressAsync(generation, lease.Token);
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                ErrorText = Text.LoadFailed;
                return Task.CompletedTask;
            }, CancellationToken.None);
        }
        finally
        {
            try
            {
                await _boundary.TryCommitAsync(generation, _ =>
                {
                    IsBusy = false;
                    IsProgressLoading = false;
                    return Task.CompletedTask;
                }, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task LoadProgressAsync(AccountSessionGeneration generation, CancellationToken cancellationToken)
    {
        try
        {
            var cached = await _progress!.GetCachedAsync(cancellationToken);
            if (cached is not null)
            {
                await _boundary.TryCommitAsync(generation, _ =>
                {
                    ApplyProgress(cached);
                    return Task.CompletedTask;
                }, cancellationToken);
            }

            if (_connectivity is not { IsOnline: true }) return;

            await _boundary.TryCommitAsync(generation, _ =>
            {
                IsProgressLoading = true;
                return Task.CompletedTask;
            }, cancellationToken);
            try
            {
                var refreshed = await _progress.RefreshAsync(cancellationToken);
                await _boundary.TryCommitAsync(generation, _ =>
                {
                    ApplyProgress(refreshed);
                    return Task.CompletedTask;
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Cached progress, when present, remains authoritative and visible.
            }
            finally
            {
                await _boundary.TryCommitAsync(generation, _ =>
                {
                    IsProgressLoading = false;
                    return Task.CompletedTask;
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Local training remains usable when progress is unavailable.
        }
    }

    private void ApplyProgress(ProgressSnapshot snapshot)
    {
        var profile = snapshot.Profile;
        WeeklyCompletedWorkouts = profile.WeeklyCompletedWorkouts;
        WeeklyGoal = profile.WeeklyGoal;
        CurrentStreakWeeks = profile.CurrentStreakWeeks;
        Level = profile.Level;
        TotalXp = profile.TotalXp;
        _currentLevelRequiredXp = profile.CurrentLevelRequiredXp;
        _nextLevelRequiredXp = profile.NextLevelRequiredXp;
        HasAuthoritativeProgress = true;
        OnPropertyChanged(nameof(LevelProgress));

        var recent = snapshot.Summary.PersonalRecords
            .OrderByDescending(item => item.LastPerformedAt)
            .ThenByDescending(item => item.ExerciseId)
            .FirstOrDefault();
        RecentMomentum = recent is null ? null : new HomeMomentumItem(recent, _weightUnits, _gamificationText);
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        ActiveWorkout = null;
        RepeatWorkout = null;
        RecentWorkouts.Clear();
        OnPropertyChanged(nameof(HasRecentWorkouts));
        HasAuthoritativeProgress = false;
        WeeklyCompletedWorkouts = 0;
        WeeklyGoal = 0;
        CurrentStreakWeeks = 0;
        Level = 0;
        TotalXp = 0;
        _currentLevelRequiredXp = 0;
        _nextLevelRequiredXp = null;
        OnPropertyChanged(nameof(LevelProgress));
        RecentMomentum = null;
        ErrorText = null;
        IsBusy = false;
        IsProgressLoading = false;
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

    private sealed class FixedWeightUnitPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
    }
}
