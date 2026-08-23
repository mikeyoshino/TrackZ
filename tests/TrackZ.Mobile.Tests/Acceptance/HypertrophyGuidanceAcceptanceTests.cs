using System.Globalization;
using System.Text.Json;
using TrackZ.Contracts.Sync;
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Acceptance;

public sealed class HypertrophyGuidanceAcceptanceTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"trackz-guidance-{Guid.NewGuid():N}.db");
    private readonly string _historyPath = Path.Combine(
        Path.GetTempPath(), $"trackz-guidance-history-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Offline_save_rate_kill_restore_preserves_set_and_ordered_effort_operation()
    {
        var exerciseId = Guid.NewGuid();
        var clock = new MutableClock(new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero));
        var boundary = new AccountSessionBoundary();
        var firstDatabase = new TrackZLocalDatabase(_path);
        var firstRepository = new LocalWorkoutRepository(firstDatabase);
        var first = new ActiveWorkoutCoordinator(firstRepository, boundary, clock);
        await first.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)]);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var saved = await first.SaveSetAsync(exerciseId, new LocalSet(70m, null, 12));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var effortOperationId = Guid.NewGuid();
        await first.RecordSetEffortAsync(
            exerciseId, saved.Id, SetEffortRating.Productive, effortOperationId);
        var previous = PreviousSession(
            TrackingMode.Weighted, weightKg: 70m,
            assistedKg: null, effort: SetEffortRating.Easy);
        var beforeRestart = GuidanceFrom(
            (await first.RestoreActiveAsync())!, previous, 2.5m);

        var restoredDatabase = new TrackZLocalDatabase(_path);
        var restoredRepository = new LocalWorkoutRepository(restoredDatabase);
        var restoredWorkout = (await restoredRepository.GetActiveAsync(default))!;
        var restoredSet = Assert.Single(Assert.Single(restoredWorkout.Exercises).Sets);
        var pending = await new OutboxRepository(restoredDatabase).PendingAsync();
        var afterRestart = GuidanceFrom(restoredWorkout, previous, 2.5m);

        Assert.Equal(SetEffortRating.Productive, restoredSet.Effort);
        Assert.Equal(70m, restoredSet.WeightKg);
        Assert.Equal(12, restoredSet.Reps);
        Assert.Equal(beforeRestart, afterRestart);
        Assert.Equal(HypertrophyGuidanceAction.Increase, afterRestart.Action);
        Assert.Equal(72.5m, afterRestart.SuggestedWeightKg);
        Assert.Equal(
            [OutboxOperationType.StartWorkout, OutboxOperationType.SaveSet,
                OutboxOperationType.RecordSetEffort],
            pending.Select(operation => operation.Type));
        Assert.True(Array.IndexOf(pending.Select(item => item.OperationId).ToArray(), saved.OperationId)
            < Array.IndexOf(pending.Select(item => item.OperationId).ToArray(), effortOperationId));
    }

    [Fact]
    public async Task Two_comparable_sets_recommend_increase_but_only_use_action_changes_next_draft()
    {
        var exerciseId = Guid.NewGuid();
        var clock = new MutableClock(
            new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero));
        var boundary = new AccountSessionBoundary();
        var database = new TrackZLocalDatabase(_path);
        var repository = new LocalWorkoutRepository(database);
        var coordinator = new ActiveWorkoutCoordinator(repository, boundary, clock);
        await coordinator.StartAsync([
            new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
        ]);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var saved = await coordinator.SaveSetAsync(
            exerciseId, new LocalSet(70m, null, 12));
        var previous = PreviousSession(
            TrackingMode.Weighted, weightKg: 70m,
            assistedKg: null, effort: SetEffortRating.Easy);
        var raw = new MemoryWorkoutPreferenceStore();
        var preferences = new ExerciseGuidancePreferenceStore(raw);
        preferences.SetIncrementKg(exerciseId, 2.5m);
        var units = new WeightUnitPreference(raw);
        var outbox = new OutboxRepository(database);
        var logger = CreateLogger(
            coordinator, previous, boundary, outbox, units);
        await logger.LoadAsync(exerciseId, "Bench Press");
        var prompt = new SetEffortPromptViewModel(
            coordinator, preferences, units, boundary, WorkoutResources.English);
        prompt.Initialize(
            new SetEffortPromptRequest(
                exerciseId, TrackingMode.Weighted, saved, previous, Guid.NewGuid()),
            logger.TryApplyGuidanceToNextDraft);

        await prompt.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(HypertrophyGuidanceAction.Increase, prompt.Guidance!.Action);
        Assert.Equal(72.5m, prompt.Guidance.SuggestedWeightKg);
        Assert.False(logger.HasDraftSet);
        var pendingBeforeUse = await outbox.PendingAsync();

        prompt.UseSuggestion();

        Assert.True(logger.HasDraftSet);
        Assert.Equal(72.5m, logger.WeightKg);
        Assert.Equal(12, logger.Reps);
        Assert.Equal(
            pendingBeforeUse.Select(operation => operation.OperationId),
            (await outbox.PendingAsync()).Select(operation => operation.OperationId));
        Assert.Single(logger.TodaySets);
    }

    [Fact]
    public async Task Sync_pull_and_process_recreation_recompute_from_stored_facts()
    {
        var exerciseId = Guid.NewGuid();
        var clock = new MutableClock(
            new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero));
        var boundary = new AccountSessionBoundary();
        var database = new TrackZLocalDatabase(_path);
        var repository = new LocalWorkoutRepository(database);
        var coordinator = new ActiveWorkoutCoordinator(repository, boundary, clock);
        await coordinator.StartAsync([
            new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
        ]);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var saved = await coordinator.SaveSetAsync(
            exerciseId, new LocalSet(70m, null, 12));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await coordinator.RecordSetEffortAsync(
            exerciseId, saved.Id, SetEffortRating.Productive, Guid.NewGuid());
        var previous = PreviousSession(
            TrackingMode.Weighted, weightKg: 70m,
            assistedKg: null, effort: SetEffortRating.Easy);
        var history = new ExerciseHistoryCache(_historyPath);
        await history.ReplaceAsync(exerciseId, previous);
        var local = (await repository.GetActiveAsync())!;
        var before = GuidanceFrom(local, previous, 2.5m);
        var outbox = new OutboxRepository(database);
        var expected = await outbox.PendingAsync();
        var authority = ToSyncWorkout(local);
        var api = new RecordingSyncApi(expected, authority);

        Assert.Equal(
            SyncRunStatus.Completed,
            await new SyncCoordinator(database, api, boundary, clock).RunOnceAsync());

        var recreatedDatabase = new TrackZLocalDatabase(_path);
        await recreatedDatabase.InitializeAsync();
        var recreatedRepository = new LocalWorkoutRepository(recreatedDatabase);
        var recreatedHistory = new ExerciseHistoryCache(_historyPath);
        var restored = (await recreatedRepository.GetActiveAsync())!;
        var restoredPrevious = (await recreatedHistory.GetMostRecentAsync(exerciseId))!;
        var after = GuidanceFrom(restored, restoredPrevious, 2.5m);

        Assert.Equal(before, after);
        Assert.Equal(HypertrophyGuidanceAction.Increase, after.Action);
        Assert.Equal(72.5m, after.SuggestedWeightKg);
        var serializedAuthority = JsonSerializer.Serialize(authority);
        Assert.DoesNotContain("guidance", serializedAuthority,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recommendation", serializedAuthority,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_measurement_edit_after_effort_preserves_effort()
    {
        var exerciseId = Guid.NewGuid();
        var clock = new MutableClock(
            new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero));
        var boundary = new AccountSessionBoundary();
        var database = new TrackZLocalDatabase(_path);
        var repository = new LocalWorkoutRepository(database);
        var coordinator = new ActiveWorkoutCoordinator(repository, boundary, clock);
        await coordinator.StartAsync([
            new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
        ]);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var saved = await coordinator.SaveSetAsync(
            exerciseId, new LocalSet(70m, null, 12));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await coordinator.RecordSetEffortAsync(
            exerciseId, saved.Id, SetEffortRating.Easy, Guid.NewGuid());
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var completed = await coordinator.FinishAsync();
        var exercise = Assert.Single(completed.Exercises);
        var editOperationId = Guid.NewGuid();
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var mutation = await new WorkoutHistoryCoordinator(
            repository, boundary, clock).EditSetAsync(
                completed.Id,
                exercise.Id,
                saved.Id,
                new HistorySetMeasurement(72.5m, null, 9),
                editOperationId);
        var operation = Assert.Single(
            await new OutboxRepository(database).PendingAsync(),
            item => item.OperationId == editOperationId);
        var payload = operation.DeserializePayload<EditSetOutboxPayload>();
        var editedSet = Assert.Single(Assert.Single(mutation.Workout.Exercises).Sets);
        var nextGraphSet = Assert.Single(
            Assert.Single(ToSyncWorkout(mutation.Workout).Exercises).Sets);

        Assert.Equal(72.5m, editedSet.WeightKg);
        Assert.Equal(9, editedSet.Reps);
        Assert.Equal(SetEffortRating.Easy, editedSet.Effort);
        Assert.Equal(SetEffortRating.Easy, nextGraphSet.Effort);
        Assert.DoesNotContain("effort", JsonSerializer.Serialize(payload),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Assisted_flow_describes_lower_assistance_as_harder()
    {
        var exerciseId = Guid.NewGuid();
        var clock = new MutableClock(
            new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero));
        var boundary = new AccountSessionBoundary();
        var database = new TrackZLocalDatabase(_path);
        var repository = new LocalWorkoutRepository(database);
        var coordinator = new ActiveWorkoutCoordinator(repository, boundary, clock);
        await coordinator.StartAsync([
            new WorkoutExerciseSelection(exerciseId, TrackingMode.Assisted)
        ]);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var saved = await coordinator.SaveSetAsync(
            exerciseId, new LocalSet(null, 30m, 12));
        var previous = PreviousSession(
            TrackingMode.Assisted, weightKg: null,
            assistedKg: 30m, effort: SetEffortRating.Easy);
        var raw = new MemoryWorkoutPreferenceStore();
        var preferences = new ExerciseGuidancePreferenceStore(raw);
        preferences.SetIncrementKg(exerciseId, 2.5m);
        var units = new WeightUnitPreference(raw);
        var prompt = new SetEffortPromptViewModel(
            coordinator, preferences, units, boundary, WorkoutResources.English);
        prompt.Initialize(
            new SetEffortPromptRequest(
                exerciseId, TrackingMode.Assisted, saved, previous, Guid.NewGuid()),
            _ => true);

        await prompt.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(HypertrophyGuidanceAction.Increase, prompt.Guidance!.Action);
        Assert.Equal(27.5m, prompt.Guidance.SuggestedAssistedKg);
        Assert.Null(prompt.Guidance.SuggestedWeightKg);
        Assert.Equal("Try 27.5 kg assistance next set", prompt.RecommendationTitle);
        Assert.Equal(prompt.Text.GuidanceLessAssistanceReason,
            prompt.RecommendationReason);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class MemoryWorkoutPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    private sealed class RecordingSyncApi(
        IReadOnlyList<OutboxOperation> expected,
        SyncWorkoutDto authoritative) : ISyncApi
    {
        private int _pushIndex;
        private bool _pulled;
        public List<(Guid Id, string Action, long? BaseVersion)> Received { get; } = [];

        public Task<SyncPushResponse> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default)
        {
            var dto = Assert.Single(request.Operations);
            var local = expected[_pushIndex++];
            Assert.Equal(local.OperationId, dto.OperationId);
            Assert.Equal(local.Type.ToString(), dto.Action);
            Assert.Equal(local.BaseVersion, dto.BaseVersion);
            Received.Add((dto.OperationId, dto.Action, dto.BaseVersion));
            return Task.FromResult(new SyncPushResponse([
                new SyncOperationResultDto(dto.OperationId, SyncOperationStatus.Applied,
                    dto.BaseVersion!.Value + 1, null)
            ]));
        }

        public Task<SyncPullResponse> PullAsync(
            string? cursor,
            CancellationToken cancellationToken = default)
        {
            if (_pulled) return Task.FromResult(new SyncPullResponse([], cursor, false));
            _pulled = true;
            return Task.FromResult(new SyncPullResponse([
                new SyncChangeDto(1, "Workout", authoritative.Id, authoritative.Version,
                    authoritative.DeletedAt is not null, authoritative.StartedAt, authoritative)
            ], "guidance-cursor-1", false));
        }
    }

    private static ExerciseHistorySessionDto PreviousSession(
        TrackingMode mode,
        decimal? weightKg,
        decimal? assistedKg,
        SetEffortRating effort)
    {
        var completedAt = new DateTimeOffset(
            2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var weightedVolumeKg = mode == TrackingMode.Weighted
            ? weightKg!.Value * 12
            : 0m;
        return new ExerciseHistorySessionDto(
            Guid.NewGuid(),
            completedAt,
            mode,
            weightedVolumeKg,
            [new WorkoutSetDto(
                Guid.NewGuid(), 0, weightKg, assistedKg, 12,
                completedAt, null, effort)]);
    }

    private static SetLoggerViewModel CreateLogger(
        ActiveWorkoutCoordinator coordinator,
        ExerciseHistorySessionDto previous,
        IAccountSessionBoundary boundary,
        OutboxRepository outbox,
        IWeightUnitPreference units) =>
        new(
            coordinator,
            new FixedHistorySource(previous),
            new ImmediateFeedback(),
            boundary,
            new OfflineConnectivity(),
            outbox,
            WorkoutResources.English,
            unitPreference: units);

    private static HypertrophyGuidanceResult GuidanceFrom(
        LocalWorkout workout,
        ExerciseHistorySessionDto previous,
        decimal incrementKg)
    {
        var exercise = Assert.Single(
            workout.Exercises, item => item.DeletedAt is null);
        var current = Assert.Single(
            exercise.Sets, item => item.DeletedAt is null);
        var saved = new HypertrophyGuidanceSet(
            current.Id,
            exercise.TrackingMode,
            current.WeightKg,
            current.AssistedKg,
            current.Reps,
            current.Effort,
            current.CompletedAt,
            current.Order);
        var prior = previous.Sets
            .OrderByDescending(item => item.CompletedAt)
            .ThenByDescending(item => item.Order)
            .Select(item => new HypertrophyGuidanceSet(
                item.Id,
                previous.TrackingMode,
                item.WeightKg,
                item.AssistedKg,
                item.Reps,
                item.Effort,
                item.CompletedAt,
                item.Order))
            .ToArray();
        return HypertrophyLoadGuidancePolicy.Evaluate(
            new HypertrophyGuidanceRequest(saved, prior, incrementKg));
    }

    private static SyncWorkoutDto ToSyncWorkout(LocalWorkout workout) =>
        new(
            workout.Id,
            (int)workout.Status,
            workout.StartedAt,
            workout.CompletedAt,
            workout.DeletedAt,
            workout.Version,
            workout.Exercises.Select(exercise => new SyncWorkoutExerciseDto(
                exercise.Id,
                exercise.ExerciseDefinitionId,
                (int)exercise.TrackingMode,
                exercise.Order,
                exercise.DeletedAt,
                exercise.Version,
                exercise.Sets.Select(set => new SyncSetDto(
                    set.Id,
                    set.Order,
                    set.WeightKg?.ToString(CultureInfo.InvariantCulture),
                    set.AssistedKg?.ToString(CultureInfo.InvariantCulture),
                    set.Reps,
                    set.CompletedAt,
                    set.UpdatedAt,
                    set.DeletedAt,
                    set.Version,
                    set.Effort)).ToArray())).ToArray());

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var databasePath in new[] { _path, _historyPath })
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
        return ValueTask.CompletedTask;
    }

    private sealed class FixedHistorySource(ExerciseHistorySessionDto previous)
        : IExerciseHistorySource
    {
        public Task<ExerciseHistorySessionDto?> GetMostRecentAsync(
            Guid exerciseId,
            bool refreshIfOnline,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ExerciseHistorySessionDto?>(previous);
        }
    }

    private sealed class ImmediateFeedback : ISetSavedFeedback
    {
        public Task SetSavedAsync(
            SetSavedPresentation presentation,
            SetSavedFeedbackSession session)
        {
            session.CancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged
        {
            add { }
            remove { }
        }
    }
}
