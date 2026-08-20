using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Gamification.EvaluateStreak;
using TrackZ.Domain.Gamification;

namespace TrackZ.Application.Tests.Gamification;

public sealed class EvaluateStreakTests
{
    [Fact]
    public async Task Evaluation_is_idempotent_and_history_removal_compensates_weekly_xp()
    {
        var userId = Guid.NewGuid();
        var week30 = Completion("10000000-0000-0000-0000-000000000000", "2026-07-21T10:00:00Z");
        var week31 = Completion("20000000-0000-0000-0000-000000000000", "2026-07-28T10:00:00Z");
        var week33 = Completion("30000000-0000-0000-0000-000000000000", "2026-08-11T10:00:00Z");
        var store = new InMemoryStreakStore(new StreakEvaluationInput(
            userId, WeeklyGoal: 1, "Asia/Bangkok", [week30, week31, week33]));
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-18T10:00:00Z"));
        var handler = new EvaluateStreakHandler(store, clock);

        await handler.Handle(new EvaluateStreakCommand(userId), CancellationToken.None);
        await handler.Handle(new EvaluateStreakCommand(userId), CancellationToken.None);

        var state = Assert.Single(store.Streaks);
        Assert.Equal(1, state.CurrentWeeks);
        Assert.Equal(2, state.BestWeeks);
        Assert.Equal(450, store.LedgerEntries.Sum(entry => entry.Amount));
        Assert.Equal(3, store.LedgerEntries.Count);

        store.Input = store.Input with { CompletedWorkouts = [week30, week31] };
        await handler.Handle(new EvaluateStreakCommand(userId), CancellationToken.None);

        Assert.Equal(0, state.CurrentWeeks);
        Assert.Equal(2, state.BestWeeks);
        Assert.Equal(300, store.LedgerEntries.Sum(entry => entry.Amount));
        Assert.Equal(-150, Assert.Single(store.LedgerEntries,
            entry => entry.Reason == XpLedgerReason.Correction).Amount);
        Assert.Equal(300, Assert.Single(store.Progress).TotalXp);
    }

    private static CompletedWorkoutInstant Completion(string id, string instant) =>
        new(Guid.Parse(id), DateTimeOffset.Parse(instant));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryStreakStore(StreakEvaluationInput input) : IStreakStore
    {
        public StreakEvaluationInput Input { get; set; } = input;
        public List<StreakState> Streaks { get; } = [];
        public List<XpLedgerEntry> LedgerEntries { get; } = [];
        public List<UserProgress> Progress { get; } = [];

        public Task<IAppDbTransaction> BeginStreakTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAppDbTransaction>(new Transaction());

        public Task AcquireUserProgressLockAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<StreakEvaluationInput?> GetStreakEvaluationInputAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<StreakEvaluationInput?>(Input.UserId == userId ? Input : null);

        public Task<StreakState?> FindStreakStateAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Streaks.SingleOrDefault(state => state.UserId == userId));

        public void AddStreakState(StreakState state) => Streaks.Add(state);

        public Task<IReadOnlyList<XpLedgerEntry>> ListWeeklyGoalXpEntriesAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<XpLedgerEntry>>(LedgerEntries
                .Where(entry => entry.UserId == userId
                    && (entry.Reason == XpLedgerReason.WeeklyGoal || entry.Reason == XpLedgerReason.Correction))
                .ToArray());

        public Task<int> GetUserTotalXpAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(LedgerEntries.Where(entry => entry.UserId == userId).Sum(entry => entry.Amount));

        public Task<IReadOnlyList<LevelThreshold>> ListLevelThresholdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LevelThreshold>>([LevelThreshold.Create(1, 0, 1), LevelThreshold.Create(2, 300, 1)]);

        public Task<UserProgress?> FindUserProgressAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Progress.SingleOrDefault(item => item.UserId == userId));

        public void AddXpLedgerEntry(XpLedgerEntry entry) => LedgerEntries.Add(entry);
        public void AddUserProgress(UserProgress progress) => Progress.Add(progress);
        public Task SaveStreakAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed class Transaction : IAppDbTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
