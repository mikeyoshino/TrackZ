using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class SetEffortPromptViewModelTests
{
    [Fact]
    public async Task Choosing_effort_records_it_before_showing_recommendation()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSet(70m, 12),
            previous: PreviousSet(70m, 12, SetEffortRating.Easy),
            incrementKg: 2.5m);
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);

        await sut.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(SetEffortPromptState.Recommendation, sut.State);
        Assert.Equal(HypertrophyGuidanceAction.Increase, sut.Guidance!.Action);
        Assert.Equal(72.5m, sut.Guidance.SuggestedWeightKg);
        var persisted = await fixture.ReloadSavedSetAsync();
        Assert.Equal(SetEffortRating.Productive, persisted.Effort);
    }

    [Fact]
    public async Task Skip_and_dismiss_create_no_effort_operation()
    {
        await using var fixture = await Fixture.CreateAsync(current: CurrentSet(70m, 10));
        var sut = fixture.CreateViewModel();
        var dismissed = 0;
        sut.DismissRequested += (_, _) => dismissed++;
        sut.Initialize(fixture.Request, _ => true);
        var before = fixture.Recorder.Calls.Count;

        sut.Skip();

        Assert.Equal(1, dismissed);
        Assert.Equal(before, fixture.Recorder.Calls.Count);
        Assert.Null((await fixture.ReloadSavedSetAsync()).Effort);
    }

    [Fact]
    public async Task Effort_write_failure_confirms_original_set_and_retry_reuses_operation_id()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSet(70m, 10), failFirstEffortWrite: true);
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);

        await sut.ChooseEffortAsync(SetEffortRating.Productive);
        Assert.Equal(SetEffortPromptState.SaveFailed, sut.State);
        Assert.True(sut.ConfirmsOriginalSetSaved);
        Assert.Null(sut.Guidance);

        await sut.RetryAsync();
        Assert.Equal(SetEffortPromptState.Recommendation, sut.State);
        Assert.All(fixture.Recorder.Calls,
            call => Assert.Equal(fixture.Request.EffortOperationId, call.OperationId));
    }

    [Fact]
    public async Task Workout_finished_while_sheet_is_open_has_no_impossible_retry()
    {
        await using var fixture = await Fixture.CreateAsync(current: CurrentSet(70m, 10));
        var sut = fixture.CreateViewModel();
        var dismissed = 0;
        sut.DismissRequested += (_, _) => dismissed++;
        sut.Initialize(fixture.Request, _ => true);
        fixture.Recorder.FinishWorkout();

        await sut.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(SetEffortPromptState.Unavailable, sut.State);
        Assert.True(sut.HasUnavailable);
        Assert.False(sut.HasSaveError);
        Assert.True(sut.ConfirmsOriginalSetSaved);
        Assert.Null(sut.Guidance);
        Assert.Single(fixture.Recorder.Calls);
        Assert.Equal(0, fixture.Recorder.SuccessfulWrites);
        await sut.RetryAsync();
        Assert.Single(fixture.Recorder.Calls);
        sut.NotNow();
        Assert.Equal(1, dismissed);
    }

    [Fact]
    public async Task Workout_finished_after_effort_commit_has_no_impossible_retry()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSet(70m, 10),
            finishAfterSuccessfulEffortWrite: true);
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);

        await sut.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(SetEffortPromptState.Unavailable, sut.State);
        Assert.True(sut.HasUnavailable);
        Assert.False(sut.HasSaveError);
        Assert.True(sut.ConfirmsOriginalSetSaved);
        Assert.Null(sut.Guidance);
        Assert.Equal(SetEffortRating.Productive, (await fixture.ReloadSavedSetAsync()).Effort);
        Assert.Equal(1, fixture.Recorder.SuccessfulWrites);
        Assert.Single(fixture.Recorder.Calls);
        await sut.RetryAsync();
        Assert.Single(fixture.Recorder.Calls);
    }

    [Fact]
    public async Task Target_removed_while_sheet_is_open_uses_neutral_unavailable_state()
    {
        await using var fixture = await Fixture.CreateAsync(current: CurrentSet(70m, 10));
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);
        fixture.Recorder.RemoveTarget();

        await sut.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(SetEffortPromptState.Unavailable, sut.State);
        Assert.Equal(
            "This set can no longer be rated. Your original set is saved.",
            sut.Text.EffortUnavailable);
        Assert.True(sut.ConfirmsOriginalSetSaved);
        Assert.Null(sut.Guidance);
        Assert.Equal(0, fixture.Recorder.SuccessfulWrites);
        Assert.Single(fixture.Recorder.Calls);
        await sut.RetryAsync();
        Assert.Single(fixture.Recorder.Calls);
    }

    [Fact]
    public async Task Missing_increment_is_collected_in_the_same_state_machine_then_recomputed()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSet(70m, 12),
            previous: PreviousSet(70m, 12, SetEffortRating.Easy));
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);
        await sut.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(SetEffortPromptState.NeedsIncrement, sut.State);
        Assert.Equal(["1.25 kg", "2.5 kg", "5 kg"],
            sut.IncrementOptions.Select(option => option.Label));
        sut.IncrementInput = "2.5";
        await sut.SaveIncrementAsync();

        Assert.Equal(SetEffortPromptState.Recommendation, sut.State);
        Assert.Equal(72.5m, sut.Guidance!.SuggestedWeightKg);
        Assert.Equal(2.5m, fixture.Preferences.GetIncrementKg(fixture.ExerciseId));
    }

    [Fact]
    public async Task Common_pound_options_convert_to_canonical_kg_and_saved_increment_can_be_edited()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSet(70m, 12),
            previous: PreviousSet(70m, 12, SetEffortRating.Easy),
            displayUnit: WeightDisplayUnit.Pounds);
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);
        await sut.ChooseEffortAsync(SetEffortRating.Productive);
        Assert.Equal(["2.50 lb", "5.00 lb", "10.00 lb"],
            sut.IncrementOptions.Select(option => option.Label));

        await sut.SelectIncrementAsync(5m);
        Assert.Equal(2.268m, fixture.Preferences.GetIncrementKg(fixture.ExerciseId));
        Assert.Equal(SetEffortPromptState.Recommendation, sut.State);

        sut.EditIncrement();
        Assert.Equal(SetEffortPromptState.NeedsIncrement, sut.State);
        Assert.Equal("5.00", sut.IncrementInput);
        Assert.Equal(1, fixture.Recorder.SuccessfulWrites);
    }

    [Fact]
    public async Task Use_action_invokes_draft_callback_only_after_explicit_tap()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSet(70m, 12),
            previous: PreviousSet(70m, 12, SetEffortRating.Easy),
            incrementKg: 2.5m);
        HypertrophyGuidanceResult? applied = null;
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, result => { applied = result; return true; });
        await sut.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Null(applied);
        sut.UseSuggestion();

        Assert.Same(sut.Guidance, applied);
    }

    [Theory]
    [InlineData(TrackingMode.Weighted, 10, SetEffortRating.Easy,
        HypertrophyGuidanceAction.IncreaseRepetitions, HypertrophyGuidanceReason.EasyWithinRange, "Try 11 reps")]
    [InlineData(TrackingMode.Weighted, 10, SetEffortRating.Productive,
        HypertrophyGuidanceAction.Keep, HypertrophyGuidanceReason.ProductiveWithinRange, "Keep 70 kg")]
    [InlineData(TrackingMode.Weighted, 7, SetEffortRating.Productive,
        HypertrophyGuidanceAction.Reduce, HypertrophyGuidanceReason.BelowRepRange, "Try 67.5 kg next set")]
    [InlineData(TrackingMode.Weighted, 10, SetEffortRating.TooHeavy,
        HypertrophyGuidanceAction.Reduce, HypertrophyGuidanceReason.TooHeavy, "Try 67.5 kg next set")]
    [InlineData(TrackingMode.Bodyweight, 12, SetEffortRating.Productive,
        HypertrophyGuidanceAction.None, HypertrophyGuidanceReason.BodyweightRangeCompleted,
        "You completed the 8–12 rep range with good form.")]
    [InlineData(TrackingMode.Assisted, 10, SetEffortRating.TooHeavy,
        HypertrophyGuidanceAction.Reduce, HypertrophyGuidanceReason.TooHeavy,
        "Try 32.5 kg assistance next set")]
    public async Task Typed_result_maps_to_plain_language(
        TrackingMode mode,
        int reps,
        SetEffortRating effort,
        HypertrophyGuidanceAction action,
        HypertrophyGuidanceReason reason,
        string title)
    {
        await using var fixture = await Fixture.CreateAsync(
            mode: mode,
            current: CurrentSetFor(mode, 70m, 30m, reps),
            incrementKg: 2.5m);
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);
        await sut.ChooseEffortAsync(effort);
        Assert.Equal(action, sut.Guidance!.Action);
        Assert.Equal(reason, sut.Guidance.Reason);
        Assert.Equal(title, sut.RecommendationTitle);
    }

    [Fact]
    public async Task Unsupported_higher_measurement_never_reverses_into_reduce_copy()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSet(SetMeasurement.MaximumKilograms, 12),
            previous: PreviousSet(SetMeasurement.MaximumKilograms, 12, SetEffortRating.Easy),
            incrementKg: SetMeasurement.MinimumKilograms);
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);

        await sut.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(HypertrophyGuidanceReason.InvalidSuggestedMeasurement,
            sut.Guidance!.Reason);
        Assert.Equal(sut.Text.GuidanceKeepCurrentLoad, sut.RecommendationTitle);
        Assert.Equal(sut.Text.GuidanceHigherLoadUnavailable, sut.RecommendationReason);
        Assert.False(sut.HasUseAction);
    }

    [Fact]
    public async Task Unsupported_lower_assistance_uses_assistance_specific_copy()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSetFor(
                TrackingMode.Assisted, 70m, SetMeasurement.MinimumKilograms, 12),
            previous: PreviousAssistedSet(
                SetMeasurement.MinimumKilograms, 12, SetEffortRating.Easy),
            incrementKg: SetMeasurement.MinimumKilograms);
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);

        await sut.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(HypertrophyGuidanceReason.InvalidSuggestedMeasurement,
            sut.Guidance!.Reason);
        Assert.Equal(sut.Text.GuidanceKeepCurrentAssistance, sut.RecommendationTitle);
        Assert.Equal(sut.Text.GuidanceLowerAssistanceUnavailable, sut.RecommendationReason);
        Assert.False(sut.HasUseAction);
    }

    [Fact]
    public async Task Unexpected_invalid_restored_shape_is_non_actionable_not_a_crash()
    {
        await using var fixture = await Fixture.CreateAsync(current: CurrentSet(0m, 10));
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);

        await sut.ChooseEffortAsync(SetEffortRating.Productive);

        Assert.Equal(SetEffortPromptState.Recommendation, sut.State);
        Assert.Equal(HypertrophyGuidanceReason.InvalidInput, sut.Guidance!.Reason);
        Assert.Equal(sut.Text.GuidanceNoSuggestion, sut.RecommendationTitle);
        Assert.False(sut.HasUseAction);
    }

    [Fact]
    public async Task Captured_previous_session_wins_over_later_history_refresh()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSet(70m, 12),
            previous: PreviousSet(67.5m, 12, SetEffortRating.Easy),
            incrementKg: 2.5m);
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);
        fixture.ReplaceBackgroundHistory(PreviousSet(70m, 12, SetEffortRating.Easy));
        await sut.ChooseEffortAsync(SetEffortRating.Productive);
        Assert.Equal(HypertrophyGuidanceAction.CollectMoreData, sut.Guidance!.Action);
    }

    [Fact]
    public async Task Account_reset_dismisses_and_cancels_without_creating_effort()
    {
        await using var fixture = await Fixture.CreateAsync(current: CurrentSet(70m, 10));
        var sut = fixture.CreateViewModel();
        var dismissed = 0;
        sut.DismissRequested += (_, _) => dismissed++;
        sut.Initialize(fixture.Request, _ => true);
        var before = fixture.Recorder.Calls.Count;
        await fixture.Boundary.ResetAsync(_ => Task.CompletedTask);
        Assert.Equal(1, dismissed);
        Assert.Equal(before, fixture.Recorder.Calls.Count);
    }

    [Fact]
    public async Task Invalid_increment_stays_in_the_same_sheet_without_writing_a_preference()
    {
        await using var fixture = await Fixture.CreateAsync(
            current: CurrentSet(70m, 12),
            previous: PreviousSet(70m, 12, SetEffortRating.Easy));
        var sut = fixture.CreateViewModel();
        sut.Initialize(fixture.Request, _ => true);
        await sut.ChooseEffortAsync(SetEffortRating.Productive);
        sut.IncrementInput = "0";
        await sut.SaveIncrementAsync();
        Assert.Equal(SetEffortPromptState.NeedsIncrement, sut.State);
        Assert.Equal(sut.Text.GuidanceIncrementInvalid, sut.IncrementValidationMessage);
        Assert.Null(fixture.Preferences.GetIncrementKg(fixture.ExerciseId));
    }

    private sealed record CurrentSeed(
        TrackingMode Mode,
        decimal? WeightKg,
        decimal? AssistedKg,
        int Reps);

    private static CurrentSeed CurrentSet(decimal weightKg, int reps) =>
        new(TrackingMode.Weighted, weightKg, null, reps);

    private static CurrentSeed CurrentSetFor(
        TrackingMode mode,
        decimal weightKg,
        decimal assistedKg,
        int reps) => mode switch
    {
        TrackingMode.Weighted => new(mode, weightKg, null, reps),
        TrackingMode.Assisted => new(mode, null, assistedKg, reps),
        TrackingMode.Bodyweight => new(mode, null, null, reps),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static WorkoutSetDto PreviousSet(
        decimal? weightKg,
        int reps,
        SetEffortRating? effort) =>
        new(Guid.NewGuid(), 0, weightKg, null, reps,
            new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero), null, effort);

    private static WorkoutSetDto PreviousAssistedSet(
        decimal assistanceKg,
        int reps,
        SetEffortRating? effort) =>
        new(Guid.NewGuid(), 0, null, assistanceKg, reps,
            new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero), null, effort);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            Guid exerciseId,
            AccountSessionBoundary boundary,
            FakeSetEffortRecorder recorder,
            IExerciseGuidancePreferenceStore preferences,
            IWeightUnitPreference unitPreference,
            SetEffortPromptRequest request)
        {
            ExerciseId = exerciseId;
            Boundary = boundary;
            Recorder = recorder;
            Preferences = preferences;
            UnitPreference = unitPreference;
            Request = request;
        }

        public Guid ExerciseId { get; }
        public AccountSessionBoundary Boundary { get; }
        public FakeSetEffortRecorder Recorder { get; }
        public IExerciseGuidancePreferenceStore Preferences { get; }
        public IWeightUnitPreference UnitPreference { get; }
        public SetEffortPromptRequest Request { get; }
        public ExerciseHistorySessionDto? BackgroundPreviousSession { get; private set; }

        public static Task<Fixture> CreateAsync(
            CurrentSeed current,
            WorkoutSetDto? previous = null,
            decimal? incrementKg = null,
            bool failFirstEffortWrite = false,
            bool finishAfterSuccessfulEffortWrite = false,
            WeightDisplayUnit displayUnit = WeightDisplayUnit.Kilograms,
            TrackingMode? mode = null)
        {
            var trackingMode = mode ?? current.Mode;
            Assert.Equal(trackingMode, current.Mode);
            var exerciseId = Guid.NewGuid();
            var workoutId = Guid.NewGuid();
            var workoutExerciseId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);
            var saved = new LocalSet(
                Guid.NewGuid(), workoutExerciseId, 0,
                current.WeightKg, current.AssistedKg, current.Reps,
                now, null, null, 1, 0, Guid.NewGuid(), null);
            var exercise = new LocalWorkoutExercise(
                workoutExerciseId, workoutId, exerciseId, trackingMode,
                0, null, 2, 1, [saved]);
            var workout = new LocalWorkout(
                workoutId, LocalWorkoutStatus.Active, now.AddMinutes(-5),
                null, null, 2, 1, [exercise]);
            var recorder = new FakeSetEffortRecorder(
                workout, failFirstEffortWrite, finishAfterSuccessfulEffortWrite);
            var raw = new MemoryWorkoutPreferenceStore();
            var preferences = new ExerciseGuidancePreferenceStore(raw);
            if (incrementKg is { } increment)
                preferences.SetIncrementKg(exerciseId, increment);
            var unitPreference = new WeightUnitPreference(raw);
            unitPreference.Set(displayUnit);
            var previousSession = previous is null ? null : new ExerciseHistorySessionDto(
                Guid.NewGuid(), now.AddDays(-1), trackingMode, 0m, [previous]);
            var request = new SetEffortPromptRequest(
                exerciseId, trackingMode, saved, previousSession, Guid.NewGuid());
            return Task.FromResult(new Fixture(
                exerciseId, new AccountSessionBoundary(), recorder,
                preferences, unitPreference, request));
        }

        public SetEffortPromptViewModel CreateViewModel() => new(
            Recorder, Preferences, UnitPreference, Boundary, WorkoutResources.English);

        public Task<LocalSet> ReloadSavedSetAsync() => Task.FromResult(
            Recorder.Workout.Exercises.Single().Sets.Single());

        public void ReplaceBackgroundHistory(WorkoutSetDto replacement) =>
            BackgroundPreviousSession = new ExerciseHistorySessionDto(
                Guid.NewGuid(), replacement.CompletedAt, Request.TrackingMode, 0m, [replacement]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record EffortRecorderCall(
        Guid ExerciseDefinitionId,
        Guid SetId,
        SetEffortRating Effort,
        Guid OperationId);

    private sealed class FakeSetEffortRecorder(
        LocalWorkout workout,
        bool failFirstWrite,
        bool finishAfterSuccessfulWrite) : ISetEffortRecorder
    {
        public LocalWorkout Workout { get; private set; } = workout;
        public List<EffortRecorderCall> Calls { get; } = [];
        public int SuccessfulWrites { get; private set; }

        public void FinishWorkout() => Workout = Workout with
        {
            Status = LocalWorkoutStatus.Completed,
            CompletedAt = Workout.StartedAt.AddHours(1),
            Version = Workout.Version + 1
        };

        public void RemoveTarget()
        {
            var exercise = Assert.Single(Workout.Exercises);
            Workout = Workout with
            {
                Exercises = [exercise with
                {
                    DeletedAt = Workout.StartedAt.AddMinutes(1),
                    Version = exercise.Version + 1
                }],
                Version = Workout.Version + 1
            };
        }

        public Task<LocalSet> RecordSetEffortAsync(
            Guid exerciseDefinitionId,
            Guid setId,
            SetEffortRating effort,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new(exerciseDefinitionId, setId, effort, operationId));
            if (Workout.Status != LocalWorkoutStatus.Active)
                throw new SetEffortRecordingUnavailableException();
            if (failFirstWrite && Calls.Count == 1)
                throw new IOException("Injected effort write failure.");
            var exercise = Workout.Exercises.SingleOrDefault(item =>
                item.DeletedAt is null
                && item.ExerciseDefinitionId == exerciseDefinitionId)
                ?? throw new SetEffortRecordingUnavailableException();
            var existing = exercise.Sets.SingleOrDefault(item =>
                item.DeletedAt is null && item.Id == setId)
                ?? throw new SetEffortRecordingUnavailableException();
            var updated = existing with
            {
                Effort = effort,
                UpdatedAt = existing.CompletedAt.AddMinutes(1),
                Version = existing.Version + 1
            };
            var updatedExercise = exercise with
            {
                Sets = exercise.Sets.Select(item => item.Id == setId ? updated : item).ToArray(),
                Version = exercise.Version + 1
            };
            Workout = Workout with
            {
                Exercises = Workout.Exercises
                    .Select(item => item.Id == exercise.Id ? updatedExercise : item).ToArray(),
                Version = Workout.Version + 1
            };
            SuccessfulWrites++;
            if (finishAfterSuccessfulWrite) FinishWorkout();
            return Task.FromResult(updated);
        }

        public Task<LocalWorkout?> RestoreActiveAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<LocalWorkout?>(
                Workout.Status == LocalWorkoutStatus.Active ? Workout : null);
        }
    }

    private sealed class MemoryWorkoutPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }
}
