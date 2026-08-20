using TrackZ.Application.Gamification.ReconcileWorkoutXp;
using TrackZ.Domain.Gamification;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Application.Tests.Gamification;

public sealed class ReconcileWorkoutXpTests
{
    [Fact]
    public async Task Reconciling_same_completed_workout_twice_does_not_duplicate_xp()
    {
        var workoutId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var store = new InMemoryGamificationStore(
            new WorkoutXpState(userId, workoutId, 0, IsEligible: true, ValidSetCount: 10));
        var handler = new ReconcileWorkoutXpHandler(store, TimeProvider.System);

        await handler.Handle(new ReconcileWorkoutXpCommand(workoutId), CancellationToken.None);
        await handler.Handle(new ReconcileWorkoutXpCommand(workoutId), CancellationToken.None);

        Assert.Equal(150, store.LedgerEntries.Sum(entry => entry.Amount));
        Assert.Equal(2, store.LedgerEntries.Count);
        Assert.Equal(150, Assert.Single(store.Progress).TotalXp);
        Assert.Equal(1, store.CommitCount);
        Assert.Equal([userId, userId], store.UserLocks);
    }

    [Fact]
    public async Task Edit_and_delete_append_compensation_and_recalculate_level()
    {
        var workoutId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var store = new InMemoryGamificationStore(
            new WorkoutXpState(userId, workoutId, 0, IsEligible: true, ValidSetCount: 10));
        var handler = new ReconcileWorkoutXpHandler(store, TimeProvider.System);
        await handler.Handle(new ReconcileWorkoutXpCommand(workoutId), CancellationToken.None);

        store.Workout = store.Workout with { Version = 1, ValidSetCount = 20 };
        await handler.Handle(new ReconcileWorkoutXpCommand(workoutId), CancellationToken.None);

        Assert.Equal(200, store.LedgerEntries.Sum(entry => entry.Amount));
        Assert.Equal(2, Assert.Single(store.Progress).Level);

        store.Workout = store.Workout with { Version = 2, IsEligible = false };
        await handler.Handle(new ReconcileWorkoutXpCommand(workoutId), CancellationToken.None);

        Assert.Equal(0, store.LedgerEntries.Sum(entry => entry.Amount));
        var progress = Assert.Single(store.Progress);
        Assert.Equal(0, progress.TotalXp);
        Assert.Equal(1, progress.Level);
        Assert.Equal([50, -200], store.LedgerEntries
            .Where(entry => entry.Reason == XpLedgerReason.Correction)
            .Select(entry => entry.Amount));
    }

    private sealed class InMemoryGamificationStore(WorkoutXpState workout) : IGamificationStore
    {
        public WorkoutXpState Workout { get; set; } = workout;
        public List<XpLedgerEntry> LedgerEntries { get; } = [];
        public List<UserProgress> Progress { get; } = [];
        public int CommitCount { get; private set; }
        public List<Guid> UserLocks { get; } = [];

        public Task<IAppDbTransaction> BeginGamificationTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAppDbTransaction>(new RecordingTransaction(this));

        public Task AcquireWorkoutXpLockAsync(Guid workoutId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AcquireUserProgressLockAsync(Guid userId, CancellationToken cancellationToken)
        {
            UserLocks.Add(userId);
            return Task.CompletedTask;
        }

        public Task<WorkoutXpState?> GetWorkoutXpStateAsync(Guid workoutId, CancellationToken cancellationToken) =>
            Task.FromResult<WorkoutXpState?>(Workout.WorkoutId == workoutId ? Workout : null);

        public Task<IReadOnlyList<XpLedgerEntry>> ListWorkoutXpEntriesAsync(
            Guid userId,
            Guid workoutId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<XpLedgerEntry>>(LedgerEntries
                .Where(entry => entry.UserId == userId && entry.OriginId == workoutId)
                .ToArray());

        public Task<int> GetUserTotalXpAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(LedgerEntries.Where(entry => entry.UserId == userId).Sum(entry => entry.Amount));

        public Task<IReadOnlyList<LevelThreshold>> ListLevelThresholdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LevelThreshold>>([
                LevelThreshold.Create(1, 0, 1),
                LevelThreshold.Create(2, 200, 1)
            ]);

        public Task<UserProgress?> FindUserProgressAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Progress.SingleOrDefault(item => item.UserId == userId));

        public void AddXpLedgerEntry(XpLedgerEntry entry) => LedgerEntries.Add(entry);

        public void AddUserProgress(UserProgress progress) => Progress.Add(progress);

        public Task SaveGamificationAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed class RecordingTransaction(InMemoryGamificationStore owner) : IAppDbTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.CommitCount++;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
