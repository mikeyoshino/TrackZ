using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Gamification;

public sealed record ProgressSnapshot(
    ProgressSummaryDto Summary,
    GamificationProfileDto Profile,
    DateTimeOffset CachedAt);

public sealed record CompletedWorkoutSummary(
    Guid WorkoutId,
    decimal TotalVolumeKg,
    int CompletedSets,
    int TotalReps);

public interface IProgressSnapshotSource
{
    Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default);
    Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
    Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default);
}

public interface ICompletedWorkoutSummarySource
{
    Task<CompletedWorkoutSummary?> GetAsync(Guid workoutId, CancellationToken cancellationToken = default);
}

public sealed record GamificationTextSet(
    string SummaryTitle,
    string ProgressTitle,
    string ProfileTitle,
    string PendingServerConfirmation,
    string Confirmed,
    string TotalVolume,
    string Sets,
    string Reps,
    string Level,
    string Xp,
    string WeeklyGoal,
    string CurrentStreak,
    string BestStreak,
    string Badges,
    string Save,
    string Kilograms,
    string Pounds,
    string LoadFailed,
    string SaveFailed);

public static class GamificationResources
{
    public static GamificationTextSet English { get; } = new(
        "Workout summary", "Progress", "Profile", "Progress pending server confirmation",
        "Progress confirmed", "Total volume", "Sets", "Reps", "Level", "XP", "Weekly goal",
        "Current streak", "Best streak", "Badges", "Save", "kg", "lb",
        "Could not load progress", "Could not save weekly goal");

    public static GamificationTextSet Thai { get; } = new(
        "สรุปการออกกำลังกาย", "ความก้าวหน้า", "โปรไฟล์", "ความก้าวหน้ารอยืนยันจากเซิร์ฟเวอร์",
        "ยืนยันความก้าวหน้าแล้ว", "ปริมาณรวม", "เซ็ต", "ครั้ง", "เลเวล", "XP", "เป้าหมายรายสัปดาห์",
        "สตรีคปัจจุบัน", "สตรีคสูงสุด", "เหรียญรางวัล", "บันทึก", "กก.", "ปอนด์",
        "โหลดความก้าวหน้าไม่สำเร็จ", "บันทึกเป้าหมายไม่สำเร็จ");

    public static GamificationTextSet Current =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th" ? Thai : English;
}

public abstract class GamificationViewModelBase : INotifyPropertyChanged
{
    private bool _isBusy;
    private bool _isProgressProvisional;
    private string? _errorMessage;

    protected GamificationViewModelBase(GamificationTextSet text) => Text = text;

    public GamificationTextSet Text { get; }
    public bool IsBusy { get => _isBusy; protected set => Set(ref _isBusy, value); }
    public bool IsProgressProvisional { get => _isProgressProvisional; protected set { if (Set(ref _isProgressProvisional, value)) OnPropertyChanged(nameof(SyncAccessibilityText)); } }
    public string SyncAccessibilityText => IsProgressProvisional ? Text.PendingServerConfirmation : Text.Confirmed;
    public string? ErrorMessage { get => _errorMessage; protected set => Set(ref _errorMessage, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class WorkoutSummaryViewModel(
    ICompletedWorkoutSummarySource workouts,
    IProgressSnapshotSource snapshots,
    IConnectivityService connectivity,
    GamificationTextSet text) : GamificationViewModelBase(text)
{
    private decimal _totalVolumeKg;
    private int _completedSets;
    private int _totalReps;
    private int _totalXp;
    private int _level = 1;

    public decimal TotalVolumeKg { get => _totalVolumeKg; private set => Set(ref _totalVolumeKg, value); }
    public int CompletedSets { get => _completedSets; private set => Set(ref _completedSets, value); }
    public int TotalReps { get => _totalReps; private set => Set(ref _totalReps, value); }
    public int TotalXp { get => _totalXp; private set => Set(ref _totalXp, value); }
    public int Level { get => _level; private set => Set(ref _level, value); }

    public async Task LoadAsync(Guid workoutId, CancellationToken cancellationToken = default)
    {
        if (workoutId == Guid.Empty) throw new ArgumentException("Workout ID is required.", nameof(workoutId));
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (await workouts.GetAsync(workoutId, cancellationToken) is { } local)
            {
                TotalVolumeKg = local.TotalVolumeKg;
                CompletedSets = local.CompletedSets;
                TotalReps = local.TotalReps;
            }
            var snapshot = await snapshots.GetCachedAsync(cancellationToken);
            if (snapshot is not null) Apply(snapshot);
            IsProgressProvisional = true;
            if (connectivity.IsOnline)
            {
                Apply(await snapshots.RefreshAsync(cancellationToken));
                IsProgressProvisional = false;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            ErrorMessage = Text.LoadFailed;
            IsProgressProvisional = true;
        }
        finally { IsBusy = false; }
    }

    private void Apply(ProgressSnapshot snapshot)
    {
        TotalXp = snapshot.Profile.TotalXp;
        Level = snapshot.Profile.Level;
    }
}

public sealed class ExerciseProgressItem : INotifyPropertyChanged
{
    private const decimal PoundsPerKilogram = 2.204622621848775807m;
    private readonly IWeightUnitPreference _preference;
    private readonly GamificationTextSet _text;

    public ExerciseProgressItem(ExerciseProgressSummaryDto source, IWeightUnitPreference preference, GamificationTextSet text)
    {
        Source = source;
        _preference = preference;
        _text = text;
        _preference.Changed += OnUnitChanged;
    }

    public ExerciseProgressSummaryDto Source { get; }
    public Guid ExerciseId => Source.ExerciseId;
    public string ExerciseName => Source.ExerciseName;
    public decimal? BestWeightKg => Source.BestWeightKg;
    public decimal? BestAssistedKg => Source.BestAssistedKg;
    public int BestReps => Source.BestReps;
    public string PersonalRecordText => Source.TrackingMode switch
    {
        TrackingMode.Weighted => $"{FormatWeight(Source.BestWeightKg)} × {Source.BestReps}",
        TrackingMode.Assisted => $"{FormatWeight(Source.BestAssistedKg)} assist × {Source.BestReps}",
        _ => $"{Source.BestReps} {_text.Reps}"
    };
    public event PropertyChangedEventHandler? PropertyChanged;

    private string FormatWeight(decimal? kilograms)
    {
        if (kilograms is null) return "—";
        return _preference.Current == WeightDisplayUnit.Pounds
            ? $"{decimal.Round(kilograms.Value * PoundsPerKilogram, 2):0.00} {_text.Pounds}"
            : $"{kilograms.Value:0.###} {_text.Kilograms}";
    }

    private void OnUnitChanged(object? sender, EventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PersonalRecordText)));
}

public sealed class ExerciseProgressViewModel(
    IProgressSnapshotSource snapshots,
    IConnectivityService connectivity,
    IWeightUnitPreference weightUnit,
    GamificationTextSet text) : GamificationViewModelBase(text)
{
    public ObservableCollection<ExerciseProgressItem> Exercises { get; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var snapshot = await snapshots.GetCachedAsync(cancellationToken);
            if (snapshot is not null) Apply(snapshot);
            IsProgressProvisional = !connectivity.IsOnline;
            if (connectivity.IsOnline)
            {
                Apply(await snapshots.RefreshAsync(cancellationToken));
                IsProgressProvisional = false;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { ErrorMessage = Text.LoadFailed; IsProgressProvisional = true; }
        finally { IsBusy = false; }
    }

    private void Apply(ProgressSnapshot snapshot)
    {
        Exercises.Clear();
        foreach (var item in snapshot.Summary.PersonalRecords)
            Exercises.Add(new ExerciseProgressItem(item, weightUnit, Text));
    }
}

public sealed class ProfileViewModel : GamificationViewModelBase
{
    private readonly IProgressSnapshotSource _snapshots;
    private readonly IConnectivityService _connectivity;
    private int _totalXp;
    private int _level = 1;
    private int _weeklyGoal = 3;
    private int _weeklyCompletedWorkouts;
    private int _currentStreakWeeks;
    private int _bestStreakWeeks;
    private int _currentLevelRequiredXp;
    private int? _nextLevelRequiredXp;

    public ProfileViewModel(IProgressSnapshotSource snapshots, IConnectivityService connectivity, GamificationTextSet text)
        : base(text)
    {
        _snapshots = snapshots;
        _connectivity = connectivity;
        SaveWeeklyGoalCommand = new AsyncCommand(_ => SaveWeeklyGoalAsync(), _ => !IsBusy && WeeklyGoal is >= 1 and <= 7 && _connectivity.IsOnline);
    }

    public ObservableCollection<EarnedBadgeDto> Badges { get; } = [];
    public AsyncCommand SaveWeeklyGoalCommand { get; }
    public int TotalXp { get => _totalXp; private set => Set(ref _totalXp, value); }
    public int Level { get => _level; private set => Set(ref _level, value); }
    public int CurrentLevelRequiredXp { get => _currentLevelRequiredXp; private set => Set(ref _currentLevelRequiredXp, value); }
    public int? NextLevelRequiredXp { get => _nextLevelRequiredXp; private set => Set(ref _nextLevelRequiredXp, value); }
    public double LevelProgress => NextLevelRequiredXp is not { } next || next <= CurrentLevelRequiredXp
        ? 1d
        : Math.Clamp((double)(TotalXp - CurrentLevelRequiredXp) / (next - CurrentLevelRequiredXp), 0d, 1d);
    public int WeeklyGoal { get => _weeklyGoal; set { if (Set(ref _weeklyGoal, value)) SaveWeeklyGoalCommand.RaiseCanExecuteChanged(); } }
    public int WeeklyCompletedWorkouts { get => _weeklyCompletedWorkouts; private set => Set(ref _weeklyCompletedWorkouts, value); }
    public int CurrentStreakWeeks { get => _currentStreakWeeks; private set => Set(ref _currentStreakWeeks, value); }
    public int BestStreakWeeks { get => _bestStreakWeeks; private set => Set(ref _bestStreakWeeks, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var snapshot = await _snapshots.GetCachedAsync(cancellationToken);
            if (snapshot is not null) Apply(snapshot);
            IsProgressProvisional = !_connectivity.IsOnline;
            if (_connectivity.IsOnline) { Apply(await _snapshots.RefreshAsync(cancellationToken)); IsProgressProvisional = false; }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { ErrorMessage = Text.LoadFailed; IsProgressProvisional = true; }
        finally { IsBusy = false; SaveWeeklyGoalCommand.RaiseCanExecuteChanged(); }
    }

    private async Task SaveWeeklyGoalAsync()
    {
        if (WeeklyGoal is < 1 or > 7 || !_connectivity.IsOnline) return;
        IsBusy = true;
        ErrorMessage = null;
        try { Apply(await _snapshots.UpdateWeeklyGoalAsync(WeeklyGoal)); IsProgressProvisional = false; }
        catch (Exception) { ErrorMessage = Text.SaveFailed; }
        finally { IsBusy = false; SaveWeeklyGoalCommand.RaiseCanExecuteChanged(); }
    }

    private void Apply(ProgressSnapshot snapshot)
    {
        var profile = snapshot.Profile;
        TotalXp = profile.TotalXp;
        Level = profile.Level;
        CurrentLevelRequiredXp = profile.CurrentLevelRequiredXp;
        NextLevelRequiredXp = profile.NextLevelRequiredXp;
        OnPropertyChanged(nameof(LevelProgress));
        WeeklyGoal = profile.WeeklyGoal;
        WeeklyCompletedWorkouts = profile.WeeklyCompletedWorkouts;
        CurrentStreakWeeks = profile.CurrentStreakWeeks;
        BestStreakWeeks = profile.BestStreakWeeks;
        Badges.Clear();
        foreach (var badge in profile.Badges) Badges.Add(badge);
    }
}
