using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Exercises.GetHistory;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Workouts;
using TrackZ.Application.Workouts.GetWorkout;
using TrackZ.Application.Workouts.ListHistory;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Application.Tests.Workouts;

public sealed class WorkoutQueryTests
{
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();

    [Fact]
    public async Task Exercise_history_returns_every_set_in_original_order_and_counts_only_weighted_volume()
    {
        var completedAt = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
        var store = new FakeWorkoutReadStore
        {
            ExerciseHistory =
            [
                Session(Guid.NewGuid(), completedAt,
                    Exercise(TrackingMode.Weighted,
                        Set(2, 67.5m, null, 10),
                        Set(0, 70m, null, 10),
                        Set(1, 70m, null, 9))),
                Session(Guid.NewGuid(), completedAt.AddDays(-1),
                    Exercise(TrackingMode.Bodyweight, Set(0, null, null, 20))),
                Session(Guid.NewGuid(), completedAt.AddDays(-2),
                    Exercise(TrackingMode.Assisted, Set(0, null, 25m, 8)))
            ]
        };
        var handler = new GetExerciseHistoryHandler(
            store, new FakeCurrentUser(_ownerId), new FakeCursorCodec());

        var page = await handler.Handle(new GetExerciseHistoryQuery(_exerciseId, null, 20), default);

        Assert.Collection(page.Items[0].Sets,
            item => Assert.Equal((70m, 10), (item.WeightKg, item.Reps)),
            item => Assert.Equal((70m, 9), (item.WeightKg, item.Reps)),
            item => Assert.Equal((67.5m, 10), (item.WeightKg, item.Reps)));
        Assert.Equal(2005m, page.Items[0].WeightedVolumeKg);
        Assert.Equal(0m, page.Items[1].WeightedVolumeKg);
        Assert.Null(page.Items[1].Sets[0].WeightKg);
        Assert.Equal(0m, page.Items[2].WeightedVolumeKg);
        Assert.Equal(25m, page.Items[2].Sets[0].AssistedKg);
    }

    [Fact]
    public async Task Workout_history_uses_completed_at_then_id_cursor_without_duplicates_at_equal_times()
    {
        var timestamp = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
        var ids = new[] { Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), Guid.Parse("80000000-0000-0000-0000-000000000000"), Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var store = new FakeWorkoutReadStore
        {
            WorkoutHistory = ids.Select(id => Session(id, timestamp, Exercise(TrackingMode.Bodyweight, Set(0, null, null, 10)))).ToList()
        };
        var codec = new FakeCursorCodec();
        var handler = new ListWorkoutHistoryHandler(store, new FakeCurrentUser(_ownerId), codec);

        var first = await handler.Handle(new ListWorkoutHistoryQuery(null, 2), default);
        var second = await handler.Handle(new ListWorkoutHistoryQuery(first.NextCursor, 2), default);

        Assert.Equal(ids.Take(2), first.Items.Select(item => item.Id));
        Assert.Equal(ids.Skip(2), second.Items.Select(item => item.Id));
        Assert.Equal(3, first.Items.Concat(second.Items).Select(item => item.Id).Distinct().Count());
        Assert.Null(second.NextCursor);
        Assert.Equal(new WorkoutCursor(1, timestamp, ids[1]), store.LastWorkoutCursor);
    }

    [Fact]
    public async Task Workout_detail_preserves_exact_exercise_and_set_order_for_all_modes()
    {
        var workoutId = Guid.NewGuid();
        var store = new FakeWorkoutReadStore
        {
            Workout = Session(workoutId, DateTimeOffset.UtcNow,
                Exercise(TrackingMode.Assisted, 2, Set(0, null, 30m, 8)),
                Exercise(TrackingMode.Weighted, 0, Set(1, 60m, null, 7), Set(0, 65m, null, 5)),
                Exercise(TrackingMode.Bodyweight, 1, Set(0, null, null, 12)))
        };
        var handler = new GetWorkoutHandler(store, new FakeCurrentUser(_ownerId));

        var detail = await handler.Handle(new GetWorkoutQuery(workoutId), default);

        Assert.Equal([TrackingMode.Weighted, TrackingMode.Bodyweight, TrackingMode.Assisted],
            detail.Exercises.Select(exercise => exercise.TrackingMode));
        Assert.Equal([65m, 60m], detail.Exercises[0].Sets.Select(set => set.WeightKg));
    }

    [Fact]
    public async Task Unknown_or_foreign_workout_is_indistinguishable()
    {
        var handler = new GetWorkoutHandler(new FakeWorkoutReadStore(), new FakeCurrentUser(_ownerId));

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(new GetWorkoutQuery(Guid.NewGuid()), default));

        Assert.Equal(BusinessErrorCode.WorkoutNotFound, exception.Code);
        Assert.Equal(404, exception.StatusCode);
    }

    [Theory]
    [InlineData(WorkoutRuleViolation.WorkoutAlreadyCompleted, BusinessErrorCode.WorkoutAlreadyCompleted, 409)]
    [InlineData(WorkoutRuleViolation.InvalidSetValue, BusinessErrorCode.InvalidSetValue, 400)]
    public void Domain_semantic_violations_map_at_application_boundary(
        WorkoutRuleViolation violation,
        BusinessErrorCode expectedCode,
        int expectedStatus)
    {
        var mapped = WorkoutRuleExceptionMapper.ToBusinessException(
            new WorkoutRuleException(violation, "domain message"));

        Assert.Equal(expectedCode, mapped.Code);
        Assert.Equal(expectedStatus, mapped.StatusCode);
    }

    private WorkoutReadSession Session(Guid id, DateTimeOffset completedAt, params WorkoutExerciseReadRow[] exercises) =>
        new(id, _ownerId, WorkoutStatus.Completed, completedAt.AddHours(-1), completedAt, 7, exercises);

    private WorkoutExerciseReadRow Exercise(TrackingMode mode, params WorkoutSetReadRow[] sets) =>
        Exercise(mode, 0, sets);

    private WorkoutExerciseReadRow Exercise(TrackingMode mode, int order, params WorkoutSetReadRow[] sets) =>
        new(Guid.NewGuid(), _exerciseId, $"Exercise {mode}", mode, order, sets);

    private static WorkoutSetReadRow Set(int order, decimal? weightKg, decimal? assistedKg, int reps) =>
        new(Guid.NewGuid(), order, weightKg, assistedKg, reps,
            new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero), null);

    private sealed record FakeCurrentUser(Guid UserId) : ICurrentUser;

    private sealed class FakeCursorCodec : IWorkoutCursorCodec
    {
        private readonly Dictionary<string, WorkoutCursor> _values = [];

        public WorkoutCursor Decode(string cursor) => _values[cursor];

        public string Encode(WorkoutCursor cursor)
        {
            var value = Guid.NewGuid().ToString("N");
            _values[value] = cursor;
            return value;
        }
    }

    private sealed class FakeWorkoutReadStore : IWorkoutReadStore
    {
        public WorkoutReadSession? Workout { get; init; }
        public IReadOnlyList<WorkoutReadSession> WorkoutHistory { get; init; } = [];
        public IReadOnlyList<WorkoutReadSession> ExerciseHistory { get; init; } = [];
        public WorkoutCursor? LastWorkoutCursor { get; private set; }

        public Task<WorkoutReadSession?> GetOwnedWorkoutAsync(Guid ownerId, Guid workoutId, CancellationToken cancellationToken) =>
            Task.FromResult(Workout?.Id == workoutId ? Workout : null);

        public Task<IReadOnlyList<WorkoutReadSession>> ListOwnedCompletedWorkoutsAsync(
            Guid ownerId, WorkoutCursor? after, int take, CancellationToken cancellationToken)
        {
            LastWorkoutCursor = after;
            var query = WorkoutHistory.AsEnumerable();
            if (after is not null)
            {
                query = query.Where(item => item.CompletedAt < after.CompletedAt
                    || item.CompletedAt == after.CompletedAt && item.Id.CompareTo(after.WorkoutId) < 0);
            }

            return Task.FromResult<IReadOnlyList<WorkoutReadSession>>(query.Take(take).ToList());
        }

        public Task<IReadOnlyList<WorkoutReadSession>> ListOwnedExerciseHistoryAsync(
            Guid ownerId, Guid exerciseDefinitionId, WorkoutCursor? after, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkoutReadSession>>(ExerciseHistory.Take(take).ToList());
    }
}
