using System.Collections.ObjectModel;
using TrackZ.Contracts.Gamification;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Gamification;

public sealed class ProgressDashboardViewModel : GamificationViewModelBase, IDisposable
{
    private readonly IProgressSnapshotSource _snapshots;
    private readonly IConnectivityService _connectivity;
    private readonly IWeightUnitPreference _weightUnit;
    private readonly IAccountSessionBoundary _boundary;
    private int _totalXp;
    private int _level = 1;
    private int _weeklyGoal = 3;
    private int _weeklyCompletedWorkouts;
    private int _currentStreakWeeks;
    private int _bestStreakWeeks;
    private int _currentLevelRequiredXp;
    private int? _nextLevelRequiredXp;
    private bool _hasAuthoritativeProgressData;
    private bool _disposed;

    public ProgressDashboardViewModel(
        IProgressSnapshotSource snapshots,
        IConnectivityService connectivity,
        IWeightUnitPreference weightUnit,
        IAccountSessionBoundary boundary,
        GamificationTextSet text) : base(text)
    {
        _snapshots = snapshots;
        _connectivity = connectivity;
        _weightUnit = weightUnit;
        _boundary = boundary;
        _boundary.SessionReset += OnSessionReset;
    }

    public ObservableCollection<EarnedBadgePresentation> Badges { get; } = [];
    public ObservableCollection<ExerciseProgressItem> Exercises { get; } = [];
    public int TotalXp { get => _totalXp; private set => Set(ref _totalXp, value); }
    public int Level { get => _level; private set => Set(ref _level, value); }
    public int WeeklyGoal { get => _weeklyGoal; private set => Set(ref _weeklyGoal, value); }
    public int WeeklyCompletedWorkouts { get => _weeklyCompletedWorkouts; private set => Set(ref _weeklyCompletedWorkouts, value); }
    public int CurrentStreakWeeks { get => _currentStreakWeeks; private set => Set(ref _currentStreakWeeks, value); }
    public int BestStreakWeeks { get => _bestStreakWeeks; private set => Set(ref _bestStreakWeeks, value); }
    public double WeeklyProgress => Math.Clamp((double)WeeklyCompletedWorkouts / Math.Max(1, WeeklyGoal), 0d, 1d);
    public string WeeklyProgressText => $"{WeeklyCompletedWorkouts}/{WeeklyGoal}";
    public bool HasAuthoritativeProgressData
    {
        get => _hasAuthoritativeProgressData;
        private set
        {
            if (Set(ref _hasAuthoritativeProgressData, value))
                OnPropertyChanged(nameof(LevelProgress));
        }
    }
    public double LevelProgress => !HasAuthoritativeProgressData
        ? 0d
        : NextLevelRequiredXp is not { } next || next <= _currentLevelRequiredXp
        ? 1d
        : Math.Clamp((double)(TotalXp - _currentLevelRequiredXp) / (next - _currentLevelRequiredXp), 0d, 1d);
    public int? NextLevelRequiredXp { get => _nextLevelRequiredXp; private set => Set(ref _nextLevelRequiredXp, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        var generation = _boundary.Capture();
        if (!_boundary.TryStartSessionPhase(generation, () =>
        {
            IsBusy = true;
            ErrorMessage = null;
        }, cancellationToken)) return;
        try
        {
            using var lease = _boundary.CreateCancellationLease(generation, cancellationToken);
            var cached = await _snapshots.GetCachedAsync(lease.Token);
            if (!await _boundary.TryCommitAsync(generation, _ =>
            {
                if (cached is not null) Apply(cached);
                IsProgressProvisional = true;
                return Task.CompletedTask;
            }, lease.Token)) return;

            if (_connectivity.IsOnline)
            {
                var refreshed = await _snapshots.RefreshAsync(lease.Token);
                await _boundary.TryCommitAsync(generation, _ =>
                {
                    Apply(refreshed);
                    IsProgressProvisional = false;
                    return Task.CompletedTask;
                }, lease.Token);
            }
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
                ErrorMessage = Text.LoadFailed;
                IsProgressProvisional = true;
                return Task.CompletedTask;
            }, CancellationToken.None);
        }
        finally
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                IsBusy = false;
                return Task.CompletedTask;
            }, CancellationToken.None);
        }
    }

    public void Deactivate()
    {
        if (_disposed) return;
        _disposed = true;
        _boundary.SessionReset -= OnSessionReset;
        Clear();
    }

    public void Dispose() => Deactivate();

    private void Apply(ProgressSnapshot snapshot)
    {
        TotalXp = snapshot.Profile.TotalXp;
        Level = snapshot.Profile.Level;
        _currentLevelRequiredXp = snapshot.Profile.CurrentLevelRequiredXp;
        NextLevelRequiredXp = snapshot.Profile.NextLevelRequiredXp;
        HasAuthoritativeProgressData = true;
        WeeklyGoal = snapshot.Profile.WeeklyGoal;
        WeeklyCompletedWorkouts = snapshot.Profile.WeeklyCompletedWorkouts;
        CurrentStreakWeeks = snapshot.Profile.CurrentStreakWeeks;
        BestStreakWeeks = snapshot.Profile.BestStreakWeeks;
        OnPropertyChanged(nameof(LevelProgress));
        OnPropertyChanged(nameof(WeeklyProgress));
        OnPropertyChanged(nameof(WeeklyProgressText));
        Badges.Clear();
        foreach (var badge in snapshot.Profile.Badges) Badges.Add(EarnedBadgePresentation.From(badge, System.Globalization.CultureInfo.CurrentUICulture));
        foreach (var existing in Exercises) existing.Deactivate();
        Exercises.Clear();
        foreach (var exercise in snapshot.Summary.PersonalRecords)
            Exercises.Add(new ExerciseProgressItem(exercise, _weightUnit, Text));
    }

    private void Clear()
    {
        Badges.Clear();
        foreach (var existing in Exercises) existing.Deactivate();
        Exercises.Clear();
        TotalXp = 0;
        Level = 1;
        _currentLevelRequiredXp = 0;
        NextLevelRequiredXp = null;
        HasAuthoritativeProgressData = false;
        WeeklyGoal = 3;
        WeeklyCompletedWorkouts = 0;
        CurrentStreakWeeks = 0;
        BestStreakWeeks = 0;
        IsProgressProvisional = false;
        ErrorMessage = null;
        IsBusy = false;
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs) => Clear();
}
