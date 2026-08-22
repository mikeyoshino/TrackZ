using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Train;

public sealed class TrainTodayViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ITrainDashboardSource _source;
    private readonly IAccountSessionBoundary _boundary;
    private readonly IConnectivityService? _connectivity;
    private readonly IProgressSnapshotSource? _progress;
    private readonly IWeightUnitPreference _weightUnits;
    private readonly GamificationTextSet _gamificationText;
    private readonly ActiveWorkoutCoordinator? _activeWorkouts;
    private readonly ITrainNavigator? _navigator;
    private readonly IClock _clock;
    private readonly TimeZoneInfo _localTimeZone;
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
    private bool _hasLoadRetry;
    private bool _disposed;

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
        ITrainNavigator? navigator = null,
        IClock? clock = null,
        TimeZoneInfo? localTimeZone = null)
    {
        _source = source;
        _boundary = boundary;
        _connectivity = connectivity;
        _progress = progress;
        _weightUnits = weightUnits;
        _gamificationText = gamificationText;
        _activeWorkouts = activeWorkouts;
        _navigator = navigator;
        _clock = clock ?? new SystemClock();
        _localTimeZone = localTimeZone ?? TimeZoneInfo.Local;
        Text = text;
        HeroActionCommand = new AsyncCommand(_ => ExecuteHeroActionAsync(), _ => CanMutate);
        TrainAgainCommand = new AsyncCommand(_ => ExecuteTrainAgainAsync(), _ => CanMutate && ShowTrainAgain);
        RetryCommand = new AsyncCommand(_ => LoadAsync(), _ => HasLoadRetry && !IsBusy);
        _boundary.SessionReset += OnSessionReset;
        if (_connectivity is not null) _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public WorkoutTextSet Text { get; }
    public AsyncCommand HeroActionCommand { get; }
    public AsyncCommand TrainAgainCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public bool HasActiveWorkout => ActiveWorkout is not null;
    public bool ShowStartHero => _isDashboardKnown && !HasActiveWorkout;
    public bool ShowContinueHero => _isDashboardKnown && HasActiveWorkout;
    public bool ShowTrainAgain => _isDashboardKnown && !HasActiveWorkout && RepeatWorkout is not null;
    public string HomeHeadlineText => HasActiveWorkout ? Text.YouAreInMotion : Text.ReadyWhenYouAre;
    public string HomeContextText => string.Format(
        CultureInfo.CurrentCulture,
        Text.HomeContextFormat,
        ISOWeek.GetWeekOfYear(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, _localTimeZone)));
    public string HeroEyebrowText => HasActiveWorkout ? Text.WorkoutInProgress : Text.StartTraining;
    public string HeroTitleText => ActiveWorkout is { BodyParts.Count: > 0 } active
        ? FormatBodyParts(active.BodyParts)
        : HasActiveWorkout ? Text.WorkoutInProgress : Text.ChooseTodaysWorkout;
    public string HeroSupportingText => ActiveWorkout is { } active
        ? string.Format(
            CultureInfo.CurrentCulture,
            Text.HomeExerciseProgressFormat,
            active.LoggedExerciseCount,
            active.ExerciseCount,
            active.LoggedSetCount)
        : Text.ChooseWorkoutSupporting;
    public double ActiveWorkoutProgress => ActiveWorkout is { ExerciseCount: > 0 } active
        ? Math.Clamp((double)active.LoggedExerciseCount / active.ExerciseCount, 0d, 1d)
        : 0d;
    public string HeroActionText => HasActiveWorkout ? Text.ContinueWorkout : Text.StartWorkout;
    public string WeeklyGoalProgressText => $"{WeeklyCompletedWorkouts}/{WeeklyGoal}";
    public string StreakValueText => CurrentStreakWeeks.ToString(CultureInfo.CurrentCulture);
    public string LevelXpText => string.Format(CultureInfo.CurrentCulture, Text.LevelXpFormat, Level, TotalXp);
    public string RepeatWorkoutTitle => RepeatWorkout is { BodyParts.Count: > 0 } repeat
        ? FormatBodyParts(repeat.BodyParts)
        : string.Empty;
    public string RepeatWorkoutMetaText => RepeatWorkout is { } repeat
        ? string.Join(
            " · ",
            TimeZoneInfo.ConvertTime(repeat.CompletedAt, _localTimeZone)
                .ToString("dddd", CultureInfo.CurrentUICulture),
            string.Format(CultureInfo.CurrentCulture, Text.ExerciseCountFormat, repeat.ExerciseCount),
            string.Format(CultureInfo.CurrentCulture, Text.SetsLoggedFormat, repeat.LoggedSetCount))
        : string.Empty;
    public string RepeatWorkoutAccessibilityText => RepeatWorkout is { } repeat
        ? string.Format(
            CultureInfo.CurrentCulture,
            Text.RepeatWorkoutAccessibilityFormat,
            RepeatWorkoutTitle,
            repeat.ExerciseCount,
            repeat.LoggedSetCount)
        : string.Empty;
    public string RecentMomentumText => RecentMomentum is { } recent
        ? string.Format(CultureInfo.CurrentCulture, Text.LastBestFormat, recent.LastText, recent.BestText)
        : string.Empty;
    public bool CanMutate => !_disposed
        && _isDashboardKnown
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
    public int WeeklyCompletedWorkouts
    {
        get => _weeklyCompletedWorkouts;
        private set { if (Set(ref _weeklyCompletedWorkouts, value)) OnPropertyChanged(nameof(WeeklyGoalProgressText)); }
    }
    public int WeeklyGoal
    {
        get => _weeklyGoal;
        private set { if (Set(ref _weeklyGoal, value)) OnPropertyChanged(nameof(WeeklyGoalProgressText)); }
    }
    public int CurrentStreakWeeks
    {
        get => _currentStreakWeeks;
        private set { if (Set(ref _currentStreakWeeks, value)) OnPropertyChanged(nameof(StreakValueText)); }
    }
    public int Level
    {
        get => _level;
        private set { if (Set(ref _level, value)) OnPropertyChanged(nameof(LevelXpText)); }
    }
    public int TotalXp
    {
        get => _totalXp;
        private set { if (Set(ref _totalXp, value)) OnPropertyChanged(nameof(LevelXpText)); }
    }
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
            if (_recentMomentum is not null)
            {
                _recentMomentum.PropertyChanged -= OnRecentMomentumChanged;
                _recentMomentum.Dispose();
            }
            _recentMomentum = value;
            if (_recentMomentum is not null)
                _recentMomentum.PropertyChanged += OnRecentMomentumChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRecentMomentum));
            OnPropertyChanged(nameof(RecentMomentumText));
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
                NotifyHeroPresentation();
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
            OnPropertyChanged(nameof(RepeatWorkoutTitle));
            OnPropertyChanged(nameof(RepeatWorkoutMetaText));
            OnPropertyChanged(nameof(RepeatWorkoutAccessibilityText));
            RefreshCommandState();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) RetryCommand.RaiseCanExecuteChanged();
        }
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (!Set(ref _errorText, value)) return;
            OnPropertyChanged(nameof(HasError));
            RetryCommand.RaiseCanExecuteChanged();
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool HasLoadRetry
    {
        get => _hasLoadRetry;
        private set
        {
            if (Set(ref _hasLoadRetry, value)) RetryCommand.RaiseCanExecuteChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || IsBusy) return;
        var generation = _boundary.Capture();
        if (!_boundary.TryStartSessionPhase(generation, () =>
        {
            IsBusy = true;
            OnPropertyChanged(nameof(HomeContextText));
            _isDashboardKnown = false;
            OnPropertyChanged(nameof(ShowStartHero));
            OnPropertyChanged(nameof(ShowContinueHero));
            OnPropertyChanged(nameof(ShowTrainAgain));
            OnPropertyChanged(nameof(CanMutate));
            RefreshCommandState();
            HasLoadRetry = false;
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
                OnPropertyChanged(nameof(IsOffline));
                return Task.CompletedTask;
            }, lease.Token);

            if (_progress is not null)
                await LoadProgressAsync(generation, lease.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
        }
        catch (Exception)
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                ErrorText = Text.HomeLoadFailed;
                HasLoadRetry = true;
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
        HasLoadRetry = false;
        ErrorText = null;
        IsBusy = false;
        IsProgressLoading = false;
        OnPropertyChanged(nameof(ShowStartHero));
        OnPropertyChanged(nameof(ShowContinueHero));
        OnPropertyChanged(nameof(ShowTrainAgain));
        OnPropertyChanged(nameof(CanMutate));
        RefreshCommandState();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _boundary.SessionReset -= OnSessionReset;
        if (_connectivity is not null)
            _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        RecentMomentum = null;
        RefreshCommandState();
    }

    private async Task ExecuteHeroActionAsync()
    {
        var generation = _boundary.Capture();
        if (!CanMutate || _boundary.IsCancellationRequested(generation)) return;

        if (ActiveWorkout is not null)
        {
            await OpenActiveWorkoutWithRecoveryAsync(generation);
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

        HasLoadRetry = false;
        ErrorText = null;
        SetCommandMutation(true);
        try
        {
            using var lease = _boundary.CreateCancellationLease(generation);
            try
            {
                var created = await _activeWorkouts.StartAsync(repeat.Selections, cancellationToken: lease.Token);
                var committed = await _boundary.TryCommitAsync(generation, _ =>
                {
                    ActiveWorkout = ToActiveCard(created, repeat.BodyParts);
                    HasLoadRetry = false;
                    ErrorText = null;
                    return Task.CompletedTask;
                }, lease.Token);
                if (!committed) return;
            }
            catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
            {
                return;
            }
            catch (Exception)
            {
                await _boundary.TryCommitAsync(generation, _ =>
                {
                    HasLoadRetry = false;
                    ErrorText = Text.HomeRepeatFailed;
                    return Task.CompletedTask;
                }, CancellationToken.None);
                return;
            }

            await OpenActiveWorkoutWithRecoveryAsync(generation, lease.Token);
        }
        finally
        {
            SetCommandMutation(false);
        }
    }

    private async Task OpenActiveWorkoutWithRecoveryAsync(
        AccountSessionGeneration generation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await NavigateAsync(generation, static (navigator, token) =>
                navigator.OpenActiveWorkoutAsync(token), cancellationToken);
            await _boundary.TryCommitAsync(generation, _ =>
            {
                HasLoadRetry = false;
                ErrorText = null;
                return Task.CompletedTask;
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (_boundary.IsCancellationRequested(generation))
        {
        }
        catch (Exception)
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                HasLoadRetry = false;
                ErrorText = Text.HomeOpenWorkoutFailed;
                return Task.CompletedTask;
            }, CancellationToken.None);
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

    private void OnRecentMomentumChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(HomeMomentumItem.LastText) or nameof(HomeMomentumItem.BestText))
            OnPropertyChanged(nameof(RecentMomentumText));
    }

    private void NotifyHeroPresentation()
    {
        OnPropertyChanged(nameof(HomeHeadlineText));
        OnPropertyChanged(nameof(HeroEyebrowText));
        OnPropertyChanged(nameof(HeroTitleText));
        OnPropertyChanged(nameof(HeroSupportingText));
        OnPropertyChanged(nameof(ActiveWorkoutProgress));
    }

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
