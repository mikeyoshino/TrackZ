namespace TrackZ.Domain.Gamification;

public sealed class StreakState
{
    private StreakState()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public int CurrentWeeks { get; private set; }
    public int BestWeeks { get; private set; }
    public int LastEvaluatedIsoYear { get; private set; }
    public int LastEvaluatedIsoWeek { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public YearWeek? LastEvaluatedWeek => LastEvaluatedIsoYear == 0
        ? null
        : new YearWeek(LastEvaluatedIsoYear, LastEvaluatedIsoWeek);

    public static StreakState Create(Guid userId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        return new StreakState { Id = Guid.NewGuid(), UserId = userId };
    }

    public void ApplyWeek(bool goalMet, YearWeek week, DateTimeOffset updatedAt)
    {
        if (updatedAt == default) throw new ArgumentOutOfRangeException(nameof(updatedAt));
        var previous = LastEvaluatedWeek;
        if (previous is not null && week.CompareTo(previous.Value) <= 0) return;

        CurrentWeeks = goalMet
            ? previous is not null && week.IsImmediatelyAfter(previous.Value)
                ? CurrentWeeks + 1
                : 1
            : 0;
        BestWeeks = Math.Max(BestWeeks, CurrentWeeks);
        LastEvaluatedIsoYear = week.Year;
        LastEvaluatedIsoWeek = week.Week;
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public void Recalculate(
        int currentWeeks,
        int bestWeeks,
        YearWeek lastEvaluatedWeek,
        DateTimeOffset updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentWeeks);
        ArgumentOutOfRangeException.ThrowIfLessThan(bestWeeks, currentWeeks);
        if (updatedAt == default) throw new ArgumentOutOfRangeException(nameof(updatedAt));
        CurrentWeeks = currentWeeks;
        BestWeeks = bestWeeks;
        LastEvaluatedIsoYear = lastEvaluatedWeek.Year;
        LastEvaluatedIsoWeek = lastEvaluatedWeek.Week;
        UpdatedAt = updatedAt.ToUniversalTime();
    }
}
