using System.Globalization;

namespace TrackZ.Domain.Gamification;

public readonly record struct YearWeek : IComparable<YearWeek>
{
    public YearWeek(int year, int week)
    {
        if (year is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(year));
        if (week < 1 || week > ISOWeek.GetWeeksInYear(year))
        {
            throw new ArgumentOutOfRangeException(nameof(week));
        }

        Year = year;
        Week = week;
    }

    public int Year { get; }
    public int Week { get; }

    public static YearWeek FromLocalDate(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        return new YearWeek(ISOWeek.GetYear(value), ISOWeek.GetWeekOfYear(value));
    }

    public bool IsImmediatelyAfter(YearWeek previous) =>
        StartDate == previous.StartDate.AddDays(7);

    public DateOnly StartDate => DateOnly.FromDateTime(ISOWeek.ToDateTime(Year, Week, DayOfWeek.Monday));

    public YearWeek Previous() => FromLocalDate(StartDate.AddDays(-7));

    public YearWeek Next() => FromLocalDate(StartDate.AddDays(7));

    public int CompareTo(YearWeek other) => StartDate.CompareTo(other.StartDate);
}
