using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;
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
    private readonly ActiveWorkoutCoordinator? _activeWorkouts;
    private readonly ITrainNavigator? _navigator;
    private bool _isBusy;
    private bool _isDashboardKnown;
    private bool _isCommandMutation;
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
        GamificationTextSet gamificationText,
        ActiveWorkoutCoordinator? activeWorkouts = null,
        ITrainNavigator? navigator = null)
    {
        _source = source;
        _boundary = boundary;
        _connectivity = connectivity;
        _progress = progress;
        _weightUnits = weightUnits;
        _gamificationText = gamificationText;
        _activeWorkouts = activeWorkouts;
        _navigator = navigator;
        Text = text;
        HeroActionCommand = new AsyncCommand(_ => ExecuteHeroActionAsync(), _ => CanMutate);
        TrainAgainCommand = new AsyncCommand(_ => ExecuteTrainAgainAsync(), _ => CanMutate && ShowTrainAgain);
        _boundary.SessionReset += OnSessionReset;
        if (_connectivity is not null) _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public WorkoutTextSet Text { get; }
    public AsyncCommand HeroActionCommand { get; }
    public AsyncCommand TrainAgainCommand { get; }
    public ObservableCollection<RecentWorkoutItem> RecentWorkouts { get; } = [];
    public bool HasRecentWorkouts => RecentWorkouts.Count > 0;
    public bool HasActiveWorkout => ActiveWorkout is not null;
    public bool ShowStartHero => _isDashboardKnown && !HasActiveWorkout;
    public bool ShowContinueHero => _isDashboardKnown && HasActiveWorkout;
    public bool ShowTrainAgain => _isDashboardKnown && !HasActiveWorkout && RepeatWorkout is not null;
    public string HeroActionText => HasActiveWorkout ? Text.Continue : Text.StartWorkout;
    public bool CanMutate => _isDashboardKnown
        && !_isCommandMutation
        && !_boundary.IsCancellationRequested(_boundary.Capture());
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
                OnPropertyChanged(nameof(ShowStartHero));
                OnPropertyChanged(nameof(ShowContinueHero));
                OnPropertyChanged(nameof(ShowTrainAgain));
                OnPropertyChanged(nameof(HeroActionText));
                RefreshCommandState();
            }
        }
    }

    public RepeatWorkoutShortcut? RepeatWorkout
    {
        get => _repeatWorkout;
        private set
        {
            if (!Set(ref _repeatWorkout, value)) return;
            OnPropertyChanged(nameof(ShowTrainAgain));
            RefreshCommandState();
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
        if (!_boundary.TryStartSessionPhase(generation, () =>
        {
            IsBusy = true;
            _isDashboardKnown = false;
            OnPropertyChanged(nameof(ShowStartHero));
            OnPropertyChanged(nameof(ShowContinueHero));
            OnPropertyChanged(nameof(ShowTrainAgain));
            OnPropertyChanged(nameof(CanMutate));
            RefreshCommandState();
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
                _isDashboardKnown = true;
                OnPropertyChanged(nameof(ShowStartHero));
                OnPropertyChanged(nameof(ShowContinueHero));
                OnPropertyChanged(nameof(ShowTrainAgain));
                OnPropertyChanged(nameof(CanMutate));
                RefreshCommandState();
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
        _isDashboardKnown = false;
        _isCommandMutation = false;
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
        OnPropertyChanged(nameof(ShowStartHero));
        OnPropertyChanged(nameof(ShowContinueHero));
        OnPropertyChanged(nameof(ShowTrainAgain));
        OnPropertyChanged(nameof(CanMutate));
        RefreshCommandState();
    }

    private async Task ExecuteHeroActionAsync()
    {
        var generation = _boundary.Capture();
        if (!CanMutate || _boundary.IsCancellationRequested(generation)) return;

        if (ActiveWorkout is not null)
        {
            await NavigateAsync(generation, static (navigator, token) =>
                navigator.OpenActiveWorkoutAsync(token));
            return;
        }

        await NavigateAsync(generation, static (navigator, token) =>
            navigator.OpenWorkoutPickerAsync(token));
    }

    private async Task ExecuteTrainAgainAsync()
    {
        var generation = _boundary.Capture();
        var repeat = RepeatWorkout;
        if (!CanMutate || repeat is null || ActiveWorkout is not null
            || _activeWorkouts is null || _boundary.IsCancellationRequested(generation)) return;

        SetCommandMutation(true);
        try
        {
            using var lease = _boundary.CreateCancellationLease(generation);
            var created = await _activeWorkouts.StartAsync(repeat.Selections, cancellationToken: lease.Token);
            var committed = await _boundary.TryCommitAsync(generation, _ =>
            {
                ActiveWorkout = ToActiveCard(created, repeat.BodyParts);
                return Task.CompletedTask;
            }, lease.Token);
            if (!committed) return;

            await NavigateAsync(generation, static (navigator, token) =>
                navigator.OpenActiveWorkoutAsync(token), lease.Token);
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
        }
        catch (Exception)
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                ErrorText = Text.LoadFailed;
                return Task.CompletedTask;
            }, CancellationToken.None);
        }
        finally
        {
            SetCommandMutation(false);
        }
    }

    private async Task NavigateAsync(
        AccountSessionGeneration generation,
        Func<ITrainNavigator, CancellationToken, Task> navigate,
        CancellationToken cancellationToken = default)
    {
        if (_navigator is null || _boundary.IsCancellationRequested(generation)) return;
        _ = await _boundary.TryCommitAsync(generation,
            token => navigate(_navigator, token), cancellationToken);
    }

    private void SetCommandMutation(bool value)
    {
        if (_isCommandMutation == value) return;
        _isCommandMutation = value;
        OnPropertyChanged(nameof(CanMutate));
        RefreshCommandState();
    }

    private static ActiveWorkoutCard ToActiveCard(
        LocalWorkout workout,
        IReadOnlyList<BodyPart> fallbackBodyParts) => new(
        workout.Id,
        workout.StartedAt,
        fallbackBodyParts,
        workout.Exercises.Count(item => item.DeletedAt is null),
        0,
        0);

    private void RefreshCommandState()
    {
        HeroActionCommand.RaiseCanExecuteChanged();
        TrainAgainCommand.RaiseCanExecuteChanged();
    }

    private void OnConnectivityChanged(object? sender, EventArgs eventArgs) =>
        _ = ApplyConnectivityChangeAsync();

    private async Task ApplyConnectivityChangeAsync()
    {
        try
        {
            var generation = _boundary.Capture();
            await _boundary.TryCommitAsync(generation, _ =>
            {
                if (IsOffline) IsProgressLoading = false;
                OnPropertyChanged(nameof(IsOffline));
                return Task.CompletedTask;
            }, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Connectivity notifications are best effort and must not leak event-handler exceptions.
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

    private sealed class FixedWeightUnitPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
    }
}
