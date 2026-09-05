using System.ComponentModel;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Profile;

namespace TrackZ.Mobile.Features.History;

public sealed record HistoryCalendarDay(DateOnly? Date, bool IsToday, bool IsSelected,
    bool HasTraining, bool HasMatchingTraining, bool IsPastWithoutTraining, bool IsPlanned = false);

/// <summary>Calendar projection only; never mutates or removes recorded workouts.</summary>
public sealed class HistoryCalendarState : INotifyPropertyChanged
{
    private readonly TimeZoneInfo _timeZone;
    private IReadOnlyList<HistoryWorkoutItem> _workouts = [];
    private HashSet<BodyPart> _filter = [];
    private TrainingSchedule _schedule = TrainingSchedule.Empty;

    public HistoryCalendarState(DateOnly today, TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
        Today = SelectedDate = today;
        Month = new DateOnly(today.Year, today.Month, 1);
        Rebuild();
    }

    public DateOnly Today { get; private set; }
    public DateOnly Month { get; private set; }
    public DateOnly SelectedDate { get; private set; }
    public IReadOnlySet<BodyPart> Filter => _filter;
    public IReadOnlyList<HistoryCalendarDay> Days { get; private set; } = [];
    public IReadOnlyList<HistoryWorkoutItem> SelectedWorkouts { get; private set; } = [];
    public int MonthWorkoutCount { get; private set; }
    public int MonthSetCount { get; private set; }
    public bool CanNextMonth => Month.Year < Today.Year || Month.Month < Today.Month;
    public bool CanPreviousMonth => Month.Year > 1 || Month.Month > 1;
    public bool HasFilter => _filter.Count > 0;
    public bool HasSchedule => _schedule.Revisions.Length > 0;
    public string MonthTitle => Month.ToString("MMMM yyyy");
    public string SelectedDateTitle => SelectedDate.ToString("ddd d MMM");
    public event PropertyChangedEventHandler? PropertyChanged;

    public DateOnly LocalDate(HistoryWorkoutItem workout) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(workout.CompletedAt, _timeZone).DateTime);

    public void ReplaceWorkouts(IEnumerable<HistoryWorkoutItem> workouts)
    {
        _workouts = workouts.ToArray();
        Rebuild();
    }

    public void SetSchedule(TrainingSchedule schedule)
    {
        _schedule = schedule;
        Rebuild();
    }

    public void RefreshToday(DateOnly today)
    {
        if (Today == today) return;
        var followedToday = SelectedDate == Today;
        Today = today;
        if (followedToday || Month > new DateOnly(today.Year, today.Month, 1)) GoToToday();
        else Rebuild();
    }

    public void ShowMonth(int year, int month)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12) return;
        var target = new DateOnly(year, month, 1);
        if (target > new DateOnly(Today.Year, Today.Month, 1)) return;
        Month = target;
        SelectedDate = new DateOnly(year, month, Math.Min(SelectedDate.Day, DateTime.DaysInMonth(year, month)));
        Rebuild();
    }

    public void MoveMonth(int delta)
    {
        if (delta is not (-1 or 1) || delta == 1 && !CanNextMonth || delta == -1 && !CanPreviousMonth) return;
        var target = Month.AddMonths(delta);
        ShowMonth(target.Year, target.Month);
    }

    public void SelectDate(DateOnly date)
    {
        if (date.Year != Month.Year || date.Month != Month.Month) return;
        SelectedDate = date;
        Rebuild();
    }

    public void GoToToday()
    {
        SelectedDate = Today;
        Month = new DateOnly(Today.Year, Today.Month, 1);
        Rebuild();
    }

    public void ApplyFilter(IEnumerable<BodyPart> filter)
    {
        _filter = filter.Where(part => Enum.IsDefined(part)).ToHashSet();
        Rebuild();
    }

    public void Reset(DateOnly today)
    {
        _workouts = [];
        _filter.Clear();
        _schedule = TrainingSchedule.Empty;
        Today = today;
        GoToToday();
    }

    private void Rebuild()
    {
        var monthWorkouts = _workouts.Where(workout => !workout.IsDeleted && workout.SetCount > 0)
            .Where(workout => LocalDate(workout).Year == Month.Year && LocalDate(workout).Month == Month.Month)
            .ToArray();
        var matching = monthWorkouts.Where(workout => !HasFilter || workout.Exercises.Any(exercise =>
            exercise.BodyPart is { } part && _filter.Contains(part) && exercise.Sets.Any(set => !set.IsDeleted)))
            .ToArray();
        MonthWorkoutCount = matching.Length;
        MonthSetCount = matching.Sum(workout => workout.SetCount);
        SelectedWorkouts = matching.Where(workout => LocalDate(workout) == SelectedDate)
            .OrderByDescending(workout => workout.CompletedAt).ToArray();
        var trained = monthWorkouts.Select(LocalDate).ToHashSet();
        var matchingDays = matching.Select(LocalDate).ToHashSet();
        var offset = ((int)Month.DayOfWeek + 6) % 7;
        var count = DateTime.DaysInMonth(Month.Year, Month.Month);
        var cells = ((offset + count + 6) / 7) * 7;
        Days = Enumerable.Range(0, cells).Select(index =>
        {
            if (index < offset || index >= offset + count)
                return new HistoryCalendarDay(null, false, false, false, false, false);
            var date = Month.AddDays(index - offset);
            return new HistoryCalendarDay(date, date == Today, date == SelectedDate,
                trained.Contains(date), matchingDays.Contains(date), date < Today && !trained.Contains(date), _schedule.IsPlanned(date));
        }).ToArray();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
