# Hypertrophy Load Guidance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add optional, plain-language post-set effort capture and conservative 8–12-rep load guidance to Set Logger without ever auto-saving or auto-applying a recommendation.

**Architecture:** Persist nullable `SetEffortRating` on completed sets through a dedicated idempotent `RecordSetEffort` mutation, while keeping ordinary measurement edits backward-compatible and effort-preserving. Compute recommendations locally with a pure typed policy, keep per-exercise equipment increments in device-local account-cleared preferences, and present the flow in one native medium bottom sheet after the existing durable-save feedback finishes.

**Tech Stack:** .NET 10, C# 14, ASP.NET Core, MediatR, EF Core 10/PostgreSQL, Microsoft.Data.Sqlite, .NET MAUI XAML, RESX localization, xUnit/Testcontainers

**Spec:** `docs/superpowers/specs/2026-08-23-hypertrophy-load-guidance-design.md`

## Global Constraints

- User goal is hypertrophy; 8–12 reps is a practical product range, not a claim that other ranges cannot build muscle.
- User-facing effort choices are `Easy`, `Productive`, and `TooHeavy`; do not expose RIR, a numeric score, or “4+.”
- `SetEffortRating` wire/database values are stable: `Easy = 1`, `Productive = 2`, `TooHeavy = 3`; `null` means unknown/skipped/legacy.
- The original set and `SaveSet` outbox row must commit before the effort sheet appears; failure to record effort must never make the UI imply that the original set was lost.
- `RecordSetEffort` is the only v1 mutation allowed to set or replace effort. `EditSet` must preserve effort when old clients omit the new field.
- `RecordSetEffort` is accepted only while the workout is active; writing the same effort value with a new operation ID is a no-op with no version or outbox change.
- Mobile `OutboxOperationType.RecordSetEffort = 11`; SQLite schema moves from version 5 to 6 and preserves every existing row during upgrade.
- Recommendations are pure local derivations and are never persisted or returned by an API endpoint.
- Increase readiness requires the two most recent same-load comparable sets—including the newly rated set—to each have at least 12 reps and `Easy` or `Productive` effort.
- Weighted difficulty increases by adding one configured increment; assisted difficulty increases by subtracting one assistance increment; bodyweight guidance changes repetitions only.
- Missing/invalid increments produce a request inside the same sheet; never stack a second modal and never clamp an invalid suggested measurement.
- A suggestion is copied only into a transient next-set draft after explicit user action; it never saves or changes an existing set.
- Set Logger has no planned-set ceiling, so v1 has no persisted or unreachable “next workout” recommendation branch.
- Guidance uses “try” language, treats good form as primary, and tells a user with pain or form breakdown to stop that exercise rather than generating advice.
- English and Thai RESX keys must stay in parity; interactive targets are at least 44×44 points; screen-reader focus/announcements and reduced-motion behavior remain usable.
- Per-exercise increments are canonical kilograms, device-local, cleared on account reset, and not synchronized in v1.
- Use TDD, preserve null compatibility for old rows/payloads, and commit after every independently reviewable task.

---

## File Structure

**Create:**

- `src/TrackZ.Domain/Workouts/SetEffortRating.cs` — stable persisted effort enum.
- `src/TrackZ.Mobile.Core/Features/Workout/HypertrophyLoadGuidancePolicy.cs` — pure validation and recommendation rules; no I/O or localized strings.
- `src/TrackZ.Mobile.Core/Features/Workout/PreviousWorkoutReferenceSelector.cs` — deterministic 8–12-rep prior-session reference selection.
- `src/TrackZ.Mobile.Core/Features/Workout/ExerciseGuidancePreferenceStore.cs` — validated per-exercise canonical increment storage over the existing preference abstraction.
- `src/TrackZ.Mobile.Core/Features/Workout/ISetEffortRecorder.cs` — narrow testable boundary over local effort mutation/snapshot reads.
- `src/TrackZ.Mobile.Core/Features/Workout/SetEffortPromptRequest.cs` — immutable post-save handoff and event args.
- `src/TrackZ.Mobile.Core/Features/Workout/SetEffortPromptViewModel.cs` — bottom-sheet state machine and commands.
- `src/TrackZ.Mobile/Features/Workout/ISetEffortSheet.cs` — testable MAUI modal presentation seam.
- `src/TrackZ.Mobile/Features/Workout/SetEffortSheetPage.xaml` — native effort/recommendation sheet.
- `src/TrackZ.Mobile/Features/Workout/SetEffortSheetPage.xaml.cs` — presentation, dismissal, focus, and announcement lifetime.
- `tests/TrackZ.Mobile.Tests/Workout/HypertrophyLoadGuidancePolicyTests.cs` — complete rule matrix.
- `tests/TrackZ.Mobile.Tests/Workout/PreviousWorkoutReferenceSelectorTests.cs` — deterministic reference-card rules.
- `tests/TrackZ.Mobile.Tests/Workout/ExerciseGuidancePreferenceStoreTests.cs` — validation, unit conversion, corruption, and clear behavior.
- `tests/TrackZ.Mobile.Tests/Workout/SetEffortPromptViewModelTests.cs` — sheet state machine and no-auto-apply contract.
- `tests/TrackZ.Mobile.Tests/NativeIos/SetEffortSheetTests.cs` — presenter, dismissal, and native sheet composition.
- `tests/TrackZ.Mobile.Tests/Acceptance/HypertrophyGuidanceAcceptanceTests.cs` — offline/restart/sync/legacy/assisted end-to-end behavior.
- EF migration pair under `src/TrackZ.Infrastructure/Persistence/Migrations/` named `AddSetEffortRating` plus updated model snapshot.

**Modify:**

- Domain aggregate: `SetEntry.cs`, `WorkoutExercise.cs`, `WorkoutSession.cs`.
- Server persistence/read contracts: `SetEntryConfiguration.cs`, `AppDbContextModelSnapshot.cs`, `IWorkoutReadStore.cs`, `WorkoutDtoMapper.cs`, `AppDbContext.cs`, `WorkoutDetailDto.cs`, `SyncPullResponse.cs`.
- Server push sync: `PushSyncCommand.cs`, `PushSyncHandler.cs`.
- Mobile persistence: `LocalSet.cs`, `TrackZLocalDatabase.cs`, `LocalWorkoutRepository.cs`.
- Mobile mutation/sync/cache: `OutboxOperation.cs`, `ActiveWorkoutCoordinator.cs`, `SyncCoordinator.cs`, `ExerciseHistoryCache.cs`.
- Set Logger orchestration/UI: `SetLoggerViewModel.cs`, `SetLoggerPage.xaml`, `SetLoggerPage.xaml.cs`, `MauiProgram.cs`, `MauiExerciseServices.cs`.
- Native presentation motion: `MauiNativeSheetPresenter.cs` and its reduced-motion tests.
- Localization/reference: `WorkoutStrings.resx`, `WorkoutStrings.th.resx`, `WorkoutResources.cs`, `docs/design/track-sets-reference.html`.
- Existing domain, infrastructure, API, sync, coordinator, localization, accessibility, composition, and visual-contract tests named in the tasks below.

The recommendation policy never references MAUI, SQLite, EF, RESX, or network types. The sheet view model may coordinate local facts and presentation state, but only the coordinator mutates durable workout data and only the page owns modal/focus lifetime.

### Task 1: Stable effort enum and pure guidance policy

**Files:**

- Create: `src/TrackZ.Domain/Workouts/SetEffortRating.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Workout/HypertrophyLoadGuidancePolicy.cs`
- Create: `tests/TrackZ.Mobile.Tests/Workout/HypertrophyLoadGuidancePolicyTests.cs`

**Interfaces:**

- Consumes: `TrackingMode`, `SetMeasurement.MinimumKilograms`, `MaximumKilograms`, and `MaximumKilogramScale`.
- Produces: `SetEffortRating`; `HypertrophyGuidanceSet`; `HypertrophyGuidanceRequest`; `HypertrophyGuidanceResult`; `HypertrophyLoadGuidancePolicy.Evaluate(HypertrophyGuidanceRequest)`.

- [ ] **Step 1: Write the failing rule-matrix tests**

Create `HypertrophyLoadGuidancePolicyTests.cs` with these helpers and core cases:

```csharp
using System.Globalization;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class HypertrophyLoadGuidancePolicyTests
{
    private static readonly Guid SavedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(7, SetEffortRating.Easy, HypertrophyGuidanceAction.Reduce, null)]
    [InlineData(7, SetEffortRating.Productive, HypertrophyGuidanceAction.Reduce, null)]
    [InlineData(8, SetEffortRating.Easy, HypertrophyGuidanceAction.IncreaseRepetitions, 9)]
    [InlineData(11, SetEffortRating.Easy, HypertrophyGuidanceAction.IncreaseRepetitions, 12)]
    [InlineData(8, SetEffortRating.Productive, HypertrophyGuidanceAction.Keep, 8)]
    [InlineData(11, SetEffortRating.Productive, HypertrophyGuidanceAction.Keep, 11)]
    [InlineData(12, SetEffortRating.Easy, HypertrophyGuidanceAction.CollectMoreData, null)]
    [InlineData(13, SetEffortRating.Easy, HypertrophyGuidanceAction.CollectMoreData, null)]
    [InlineData(12, SetEffortRating.Productive, HypertrophyGuidanceAction.CollectMoreData, null)]
    [InlineData(13, SetEffortRating.Productive, HypertrophyGuidanceAction.CollectMoreData, null)]
    public void Weighted_matrix_is_stable(
        int reps,
        SetEffortRating effort,
        HypertrophyGuidanceAction expectedAction,
        int? expectedReps)
    {
        var result = Evaluate(Weighted(70m, reps, effort));

        Assert.Equal(expectedAction, result.Action);
        Assert.Equal(expectedReps, result.SuggestedReps);
    }

    [Fact]
    public void Missing_effort_returns_none()
    {
        var result = Evaluate(Weighted(70m, 12, null));

        Assert.Equal(HypertrophyGuidanceAction.None, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.MissingEffort, result.Reason);
    }

    [Fact]
    public void Two_most_recent_qualifying_same_load_sets_increase_weight()
    {
        var saved = Weighted(70m, 12, SetEffortRating.Productive);
        var prior = Weighted(70m, 13, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-3));

        var result = Evaluate(saved, [prior], incrementKg: 2.5m);

        Assert.Equal(HypertrophyGuidanceAction.Increase, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.TwoQualifyingSets, result.Reason);
        Assert.Equal(72.5m, result.SuggestedWeightKg);
        Assert.False(result.RequiresIncrement);
    }

    [Fact]
    public void Different_load_or_legacy_effort_cannot_complete_readiness()
    {
        var saved = Weighted(70m, 12, SetEffortRating.Productive);
        var differentLoad = Weighted(67.5m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-1));
        var legacy = Weighted(70m, 12, null, Guid.NewGuid(), Now.AddMinutes(-2));

        var result = Evaluate(saved, [differentLoad, legacy], incrementKg: 2.5m);

        Assert.Equal(HypertrophyGuidanceAction.CollectMoreData, result.Action);
        Assert.Equal(HypertrophyGuidanceReason.OneQualifyingSet, result.Reason);
    }

    [Fact]
    public void Missing_increment_keeps_the_action_but_returns_no_invented_number()
    {
        var result = Evaluate(
            Weighted(70m, 12, SetEffortRating.Productive),
            [Weighted(70m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-1))]);

        Assert.Equal(HypertrophyGuidanceAction.Increase, result.Action);
        Assert.True(result.RequiresIncrement);
        Assert.Null(result.SuggestedWeightKg);
        Assert.Equal(HypertrophyGuidanceReason.MissingIncrement, result.Reason);
    }

    [Fact]
    public void Assisted_difficulty_inverts_the_assistance_direction()
    {
        var saved = Assisted(30m, 12, SetEffortRating.Productive);
        var prior = Assisted(30m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-1));
        var increase = Evaluate(saved, [prior], 2.5m);
        var reduce = Evaluate(Assisted(30m, 8, SetEffortRating.TooHeavy), incrementKg: 2.5m);

        Assert.Equal(27.5m, increase.SuggestedAssistedKg);
        Assert.Equal(32.5m, reduce.SuggestedAssistedKg);
    }

    [Theory]
    [InlineData(8, SetEffortRating.Easy, HypertrophyGuidanceAction.IncreaseRepetitions, 9)]
    [InlineData(11, SetEffortRating.Productive, HypertrophyGuidanceAction.Keep, 11)]
    [InlineData(12, SetEffortRating.Productive, HypertrophyGuidanceAction.None, null)]
    [InlineData(8, SetEffortRating.TooHeavy, HypertrophyGuidanceAction.Reduce, null)]
    public void Bodyweight_never_invents_external_load(
        int reps,
        SetEffortRating effort,
        HypertrophyGuidanceAction action,
        int? suggestedReps)
    {
        var result = Evaluate(new HypertrophyGuidanceSet(
            SavedId, TrackingMode.Bodyweight, null, null, reps, effort, Now, 0));

        Assert.Equal(action, result.Action);
        Assert.Equal(suggestedReps, result.SuggestedReps);
        Assert.Null(result.SuggestedWeightKg);
        Assert.Null(result.SuggestedAssistedKg);
    }

    [Fact]
    public void Invalid_measurement_or_unrepresentable_result_returns_no_numeric_guidance()
    {
        var invalid = Evaluate(Weighted(null, 10, SetEffortRating.Productive));
        var underflow = Evaluate(Weighted(1m, 7, SetEffortRating.Productive), incrementKg: 2.5m);

        Assert.Equal(HypertrophyGuidanceAction.None, invalid.Action);
        Assert.Equal(HypertrophyGuidanceReason.InvalidInput, invalid.Reason);
        Assert.Equal(HypertrophyGuidanceAction.Reduce, underflow.Action);
        Assert.Null(underflow.SuggestedWeightKg);
        Assert.Equal(HypertrophyGuidanceReason.InvalidSuggestedMeasurement, underflow.Reason);
    }

    private static HypertrophyGuidanceResult Evaluate(
        HypertrophyGuidanceSet saved,
        IReadOnlyList<HypertrophyGuidanceSet>? prior = null,
        decimal? incrementKg = null) =>
        HypertrophyLoadGuidancePolicy.Evaluate(new(
            saved, prior ?? [], incrementKg));

    private static HypertrophyGuidanceSet Weighted(
        decimal? kilograms,
        int reps,
        SetEffortRating? effort,
        Guid? id = null,
        DateTimeOffset? completedAt = null) =>
        new(id ?? SavedId, TrackingMode.Weighted, kilograms, null, reps, effort,
            completedAt ?? Now, 0);

    private static HypertrophyGuidanceSet Assisted(
        decimal kilograms,
        int reps,
        SetEffortRating? effort,
        Guid? id = null,
        DateTimeOffset? completedAt = null) =>
        new(id ?? SavedId, TrackingMode.Assisted, null, kilograms, reps, effort,
            completedAt ?? Now, 0);

[Theory]
[InlineData(7)]
[InlineData(8)]
[InlineData(11)]
[InlineData(12)]
[InlineData(13)]
public void Too_heavy_always_reduces_weighted_difficulty(int reps)
{
    var result = Evaluate(Weighted(70m, reps, SetEffortRating.TooHeavy), incrementKg: 2.5m);
    Assert.Equal(HypertrophyGuidanceAction.Reduce, result.Action);
    Assert.Equal(HypertrophyGuidanceReason.TooHeavy, result.Reason);
    Assert.Equal(67.5m, result.SuggestedWeightKg);
}

[Theory]
[InlineData("0")]
[InlineData("-1")]
[InlineData("1.0001")]
public void Invalid_increment_requests_a_valid_equipment_increment(string text)
{
    var prior = Weighted(70m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-1));
    var result = Evaluate(
        Weighted(70m, 12, SetEffortRating.Productive),
        [prior],
        decimal.Parse(text, CultureInfo.InvariantCulture));
    Assert.Equal(HypertrophyGuidanceAction.Increase, result.Action);
    Assert.Equal(HypertrophyGuidanceReason.MissingIncrement, result.Reason);
    Assert.True(result.RequiresIncrement);
    Assert.Null(result.SuggestedWeightKg);
}

[Fact]
public void A_nonqualifying_second_most_recent_set_blocks_an_older_qualifying_set()
{
    var result = Evaluate(
        Weighted(70m, 13, SetEffortRating.Productive),
        [
            Weighted(70m, 11, SetEffortRating.Productive, Guid.NewGuid(), Now.AddMinutes(-1)),
            Weighted(70m, 12, SetEffortRating.Easy, Guid.NewGuid(), Now.AddMinutes(-2))
        ],
        2.5m);
    Assert.Equal(HypertrophyGuidanceAction.CollectMoreData, result.Action);
    Assert.Equal(HypertrophyGuidanceReason.OneQualifyingSet, result.Reason);
}

[Fact]
public void The_same_set_identity_cannot_be_counted_twice_for_readiness()
{
    var saved = Weighted(70m, 12, SetEffortRating.Productive);
    var result = Evaluate(saved, [saved], 2.5m);

    Assert.Equal(HypertrophyGuidanceAction.CollectMoreData, result.Action);
    Assert.Equal(HypertrophyGuidanceReason.OneQualifyingSet, result.Reason);
}

[Fact]
public void Maximum_weight_overflow_is_non_numeric_and_never_clamped()
{
    var maximum = SetMeasurement.MaximumKilograms;
    var result = Evaluate(
        Weighted(maximum, 13, SetEffortRating.Easy),
        [Weighted(maximum, 12, SetEffortRating.Productive, Guid.NewGuid(), Now.AddMinutes(-1))],
        .001m);
    Assert.Equal(HypertrophyGuidanceAction.Increase, result.Action);
    Assert.Equal(HypertrophyGuidanceReason.InvalidSuggestedMeasurement, result.Reason);
    Assert.Null(result.SuggestedWeightKg);
    Assert.False(result.RequiresIncrement);
}
}
```

- [ ] **Step 2: Run the policy test and confirm it fails**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~HypertrophyLoadGuidancePolicyTests" -m:1
```

Expected: FAIL to compile because the enum and policy types do not exist.

- [ ] **Step 3: Add the stable enum and policy contracts**

Create `SetEffortRating.cs`:

```csharp
namespace TrackZ.Domain.Workouts;

public enum SetEffortRating
{
    Easy = 1,
    Productive = 2,
    TooHeavy = 3
}
```

Create the policy file with these public types:

```csharp
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Mobile.Features.Workout;

public enum HypertrophyGuidanceAction
{
    None = 0,
    Increase = 1,
    Keep = 2,
    Reduce = 3,
    IncreaseRepetitions = 4,
    CollectMoreData = 5
}

public enum HypertrophyGuidanceReason
{
    None = 0,
    MissingEffort = 1,
    InvalidInput = 2,
    TooHeavy = 3,
    BelowRepRange = 4,
    EasyWithinRange = 5,
    ProductiveWithinRange = 6,
    OneQualifyingSet = 7,
    TwoQualifyingSets = 8,
    BodyweightRangeCompleted = 9,
    MissingIncrement = 10,
    InvalidSuggestedMeasurement = 11
}

public sealed record HypertrophyGuidanceSet(
    Guid SetId,
    TrackingMode TrackingMode,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    SetEffortRating? Effort,
    DateTimeOffset CompletedAt,
    int Order);

public sealed record HypertrophyGuidanceRequest(
    HypertrophyGuidanceSet SavedSet,
    IReadOnlyList<HypertrophyGuidanceSet> PriorCandidatesNewestFirst,
    decimal? IncrementKg);

public sealed record HypertrophyGuidanceResult(
    HypertrophyGuidanceAction Action,
    HypertrophyGuidanceReason Reason,
    decimal? SuggestedWeightKg = null,
    decimal? SuggestedAssistedKg = null,
    int? SuggestedReps = null,
    bool RequiresIncrement = false);
```

- [ ] **Step 4: Implement the ordered rules without I/O or prose**

Implement `HypertrophyLoadGuidancePolicy.Evaluate` with this exact decision structure:

```csharp
public static class HypertrophyLoadGuidancePolicy
{
    public static HypertrophyGuidanceResult Evaluate(HypertrophyGuidanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SavedSet);
        ArgumentNullException.ThrowIfNull(request.PriorCandidatesNewestFirst);
        var saved = request.SavedSet;
        if (!Valid(saved)) return Result(HypertrophyGuidanceAction.None, HypertrophyGuidanceReason.InvalidInput);
        if (saved.Effort is null) return Result(HypertrophyGuidanceAction.None, HypertrophyGuidanceReason.MissingEffort);
        if (saved.TrackingMode == TrackingMode.Bodyweight)
            return Bodyweight(saved);
        if (saved.Effort == SetEffortRating.TooHeavy)
            return ChangeLoad(saved, request.IncrementKg, HypertrophyGuidanceAction.Reduce, HypertrophyGuidanceReason.TooHeavy);
        if (saved.Reps < 8)
            return ChangeLoad(saved, request.IncrementKg, HypertrophyGuidanceAction.Reduce, HypertrophyGuidanceReason.BelowRepRange);
        if (saved.Reps <= 11 && saved.Effort == SetEffortRating.Easy)
            return Result(HypertrophyGuidanceAction.IncreaseRepetitions,
                HypertrophyGuidanceReason.EasyWithinRange, reps: saved.Reps + 1);
        if (saved.Reps <= 11 && saved.Effort == SetEffortRating.Productive)
            return Result(HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange, reps: saved.Reps);

        var recentAtSameLoad = new[] { saved }
            .Concat(request.PriorCandidatesNewestFirst)
            .Where(candidate => Valid(candidate) && SameLoad(saved, candidate))
            .DistinctBy(candidate => candidate.SetId)
            .Take(2)
            .ToArray();
        var ready = recentAtSameLoad.Length == 2 && recentAtSameLoad.All(QualifiesForIncrease);
        if (!ready)
            return Result(HypertrophyGuidanceAction.CollectMoreData,
                HypertrophyGuidanceReason.OneQualifyingSet);
        return ChangeLoad(saved, request.IncrementKg, HypertrophyGuidanceAction.Increase,
            HypertrophyGuidanceReason.TwoQualifyingSets);
    }

    private static HypertrophyGuidanceResult Bodyweight(HypertrophyGuidanceSet saved)
    {
        if (saved.Effort == SetEffortRating.TooHeavy)
            return Result(HypertrophyGuidanceAction.Reduce,
                HypertrophyGuidanceReason.TooHeavy,
                reps: saved.Reps > 8 ? Math.Min(12, saved.Reps - 1) : null);
        if (saved.Reps < 8)
            return Result(HypertrophyGuidanceAction.Reduce,
                HypertrophyGuidanceReason.BelowRepRange);
        if (saved.Reps <= 11 && saved.Effort == SetEffortRating.Easy)
            return Result(HypertrophyGuidanceAction.IncreaseRepetitions,
                HypertrophyGuidanceReason.EasyWithinRange, reps: saved.Reps + 1);
        if (saved.Reps <= 11)
            return Result(HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange, reps: saved.Reps);
        return Result(HypertrophyGuidanceAction.None,
            HypertrophyGuidanceReason.BodyweightRangeCompleted);
}
```

Complete the class with these exact helpers:

```csharp
    private static bool Valid(HypertrophyGuidanceSet set)
    {
        if (set.SetId == Guid.Empty
            || !Enum.IsDefined(set.TrackingMode)
            || set.Effort is { } effort && !Enum.IsDefined(effort)
            || set.Reps is < 1 or > 999
            || set.CompletedAt == default
            || set.Order < 0)
            return false;

        return set.TrackingMode switch
        {
            TrackingMode.Weighted => Representable(set.WeightKg)
                && set.AssistedKg is null,
            TrackingMode.Assisted => set.WeightKg is null
                && Representable(set.AssistedKg),
            TrackingMode.Bodyweight => set.WeightKg is null
                && set.AssistedKg is null,
            _ => false
        };
    }

    private static bool SameLoad(
        HypertrophyGuidanceSet left,
        HypertrophyGuidanceSet right) =>
        left.TrackingMode == right.TrackingMode
        && (left.TrackingMode switch
        {
            TrackingMode.Weighted => left.WeightKg == right.WeightKg,
            TrackingMode.Assisted => left.AssistedKg == right.AssistedKg,
            TrackingMode.Bodyweight => true,
            _ => false
        });

    private static bool QualifiesForIncrease(HypertrophyGuidanceSet set) =>
        set.Reps >= 12
        && set.Effort is SetEffortRating.Easy or SetEffortRating.Productive;

    private static HypertrophyGuidanceResult ChangeLoad(
        HypertrophyGuidanceSet saved,
        decimal? incrementKg,
        HypertrophyGuidanceAction action,
        HypertrophyGuidanceReason reason)
    {
        if (!ValidIncrement(incrementKg))
            return new(action, HypertrophyGuidanceReason.MissingIncrement,
                RequiresIncrement: true);

        var increment = incrementKg!.Value;
        var suggestion = (saved.TrackingMode, action) switch
        {
            (TrackingMode.Weighted, HypertrophyGuidanceAction.Increase) =>
                saved.WeightKg!.Value + increment,
            (TrackingMode.Weighted, HypertrophyGuidanceAction.Reduce) =>
                saved.WeightKg!.Value - increment,
            (TrackingMode.Assisted, HypertrophyGuidanceAction.Increase) =>
                saved.AssistedKg!.Value - increment,
            (TrackingMode.Assisted, HypertrophyGuidanceAction.Reduce) =>
                saved.AssistedKg!.Value + increment,
            _ => throw new InvalidOperationException("The load action is invalid for this mode.")
        };
        if (!Representable(suggestion))
            return new(action,
                HypertrophyGuidanceReason.InvalidSuggestedMeasurement);

        return saved.TrackingMode == TrackingMode.Weighted
            ? new(action, reason, SuggestedWeightKg: suggestion)
            : new(action, reason, SuggestedAssistedKg: suggestion);
    }

    private static HypertrophyGuidanceResult Result(
        HypertrophyGuidanceAction action,
        HypertrophyGuidanceReason reason,
        int? reps = null) =>
        new(action, reason, SuggestedReps: reps);

    private static bool ValidIncrement(decimal? value) =>
        value is { } increment
        && increment > 0m
        && increment <= SetMeasurement.MaximumKilograms
        && DecimalScale(increment) <= SetMeasurement.MaximumKilogramScale;

    private static bool Representable(decimal? value) =>
        value is { } kilograms
        && kilograms is >= SetMeasurement.MinimumKilograms
            and <= SetMeasurement.MaximumKilograms
        && DecimalScale(kilograms) <= SetMeasurement.MaximumKilogramScale;

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0xff;
}
```

`DistinctBy(SetId)` is part of the rule, not a defensive optimization: a malformed snapshot cannot turn one physical set into the two observations required for progression.

- [ ] **Step 5: Run the policy tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~HypertrophyLoadGuidancePolicyTests" -m:1
```

Expected: PASS with all matrix, ordering, assisted-direction, bodyweight, and measurement-boundary cases green.

- [ ] **Step 6: Commit the pure policy**

```bash
git add src/TrackZ.Domain/Workouts/SetEffortRating.cs src/TrackZ.Mobile.Core/Features/Workout/HypertrophyLoadGuidancePolicy.cs tests/TrackZ.Mobile.Tests/Workout/HypertrophyLoadGuidancePolicyTests.cs
git commit -m "feat: add hypertrophy load guidance policy"
```

### Task 2: Domain effort mutation and PostgreSQL persistence

**Files:**

- Modify: `src/TrackZ.Domain/Workouts/SetEntry.cs`
- Modify: `src/TrackZ.Domain/Workouts/WorkoutExercise.cs`
- Modify: `src/TrackZ.Domain/Workouts/WorkoutSession.cs`
- Modify: `src/TrackZ.Infrastructure/Persistence/Configurations/SetEntryConfiguration.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Migrations/*_AddSetEffortRating.cs` (timestamped by `dotnet ef` in Step 6)
- Create: `src/TrackZ.Infrastructure/Persistence/Migrations/*_AddSetEffortRating.Designer.cs` (same generated timestamp)
- Modify: `src/TrackZ.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `tests/TrackZ.Domain.Tests/Workouts/WorkoutSessionTests.cs`
- Modify: `tests/TrackZ.Domain.Tests/Workouts/SetEntryPersistenceShapeTests.cs`
- Modify: `tests/TrackZ.Infrastructure.Tests/Persistence/WorkoutPersistenceTests.cs`

**Interfaces:**

- Consumes: `SetEffortRating` from Task 1 and existing aggregate version/timestamp rules.
- Produces: nullable `SetEntry.Effort`; `WorkoutSession.RecordSetEffort(Guid, Guid, SetEffortRating, DateTimeOffset)`; nullable PostgreSQL `Effort` column constrained to 1–3.

- [ ] **Step 1: Add failing aggregate tests**

Add these facts to `WorkoutSessionTests`:

```csharp
[Fact]
public void Record_set_effort_changes_only_effort_and_versions_each_aggregate_once()
{
    var workout = StartWorkout();
    workout.AddExercise(_itemId, _exerciseId, TrackingMode.Weighted, 0);
    workout.CompleteSet(_itemId, _setId, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
    var beforeWorkout = workout.Version;
    var exercise = workout.Exercises.Single();
    var beforeExercise = exercise.Version;
    var set = exercise.Sets.Single();
    var beforeSet = set.Version;

    workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Productive, _startedAt.AddMinutes(2));

    Assert.Equal(SetEffortRating.Productive, set.Effort);
    Assert.Equal(70m, set.WeightKg);
    Assert.Equal(10, set.Reps);
    Assert.Equal(beforeSet + 1, set.Version);
    Assert.Equal(beforeExercise + 1, exercise.Version);
    Assert.Equal(beforeWorkout + 1, workout.Version);
}

[Fact]
public void Measurement_edit_preserves_recorded_effort()
{
    var workout = StartWorkout();
    workout.AddExercise(_itemId, _exerciseId, TrackingMode.Weighted, 0);
    workout.CompleteSet(_itemId, _setId, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
    workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Easy, _startedAt.AddMinutes(2));

    workout.EditSet(_itemId, _setId, new SetMeasurement(72.5m, null, 9), _startedAt.AddMinutes(3));

    var set = workout.Exercises.Single().Sets.Single();
    Assert.Equal(SetEffortRating.Easy, set.Effort);
    Assert.Equal(72.5m, set.WeightKg);
}

[Fact]
public void Record_set_effort_is_idempotent_for_same_value_and_rejects_invalid_identity_or_time()
{
    var workout = StartWorkout();
    workout.AddExercise(_itemId, _exerciseId, TrackingMode.Weighted, 0);
    workout.CompleteSet(_itemId, _setId, new SetMeasurement(70m, null, 10), _startedAt.AddMinutes(1));
    workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Productive, _startedAt.AddMinutes(2));
    var version = workout.Version;

    workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Productive, _startedAt.AddMinutes(3));
    workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Productive, _startedAt);

    Assert.Equal(version, workout.Version);
    Assert.Throws<ArgumentException>(() => workout.RecordSetEffort(_itemId, Guid.NewGuid(), SetEffortRating.Easy, _startedAt.AddMinutes(4)));
    Assert.Throws<ArgumentOutOfRangeException>(() => workout.RecordSetEffort(_itemId, _setId, (SetEffortRating)99, _startedAt.AddMinutes(4)));
    Assert.Throws<ArgumentException>(() => workout.RecordSetEffort(_itemId, _setId, SetEffortRating.Easy, _startedAt));

    workout.Complete(_startedAt.AddMinutes(5));
    Assert.Throws<InvalidOperationException>(() => workout.RecordSetEffort(
        _itemId, _setId, SetEffortRating.Easy, _startedAt.AddMinutes(6)));
}
```

In `SetEntryPersistenceShapeTests`, add:

```csharp
[Theory]
[InlineData(SetEffortRating.Easy, 1)]
[InlineData(SetEffortRating.Productive, 2)]
[InlineData(SetEffortRating.TooHeavy, 3)]
public void Effort_values_are_stable_and_new_sets_start_unrated(
    SetEffortRating rating,
    int persistedValue)
{
    Assert.Equal(persistedValue, (int)rating);

    var now = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);
    var workout = WorkoutSession.Start(Guid.NewGuid(), Guid.NewGuid(), now);
    var itemId = Guid.NewGuid();
    workout.AddExercise(itemId, Guid.NewGuid(), TrackingMode.Weighted, 0);
    workout.CompleteSet(
        itemId,
        Guid.NewGuid(),
        new SetMeasurement(70m, null, 10),
        now.AddMinutes(1));

    Assert.Null(workout.Exercises.Single().Sets.Single().Effort);
}
```

- [ ] **Step 2: Run domain tests and confirm failure**

Run:

```bash
dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutSessionTests|FullyQualifiedName~SetEntryPersistenceShapeTests" -m:1
```

Expected: FAIL because `SetEntry.Effort` and `WorkoutSession.RecordSetEffort` do not exist.

- [ ] **Step 3: Implement aggregate-only effort mutation**

Add to `SetEntry`:

```csharp
public SetEffortRating? Effort { get; private set; }

internal bool RecordEffort(SetEffortRating effort, DateTimeOffset recordedAt)
{
    if (IsDeleted) throw new InvalidOperationException("A deleted set cannot record effort.");
    if (!Enum.IsDefined(effort)) throw new ArgumentOutOfRangeException(nameof(effort));
    if (Effort == effort) return false;
    if (recordedAt < LastMutationAt)
        throw new ArgumentException("The effort timestamp cannot precede an earlier set mutation.", nameof(recordedAt));
    Effort = effort;
    UpdatedAt = recordedAt;
    Version++;
    return true;
}
```

Do not touch `Effort` inside `Create` or `Edit`; default creation remains `null` and measurement edits preserve the value.

Add to `WorkoutExercise`:

```csharp
internal bool RecordSetEffort(Guid setId, SetEffortRating effort, DateTimeOffset recordedAt)
{
    EnsureNotDeleted();
    var set = FindSet(setId);
    if (!set.RecordEffort(effort, recordedAt)) return false;
    Version++;
    return true;
}
```

Add to `WorkoutSession` next to `EditSet`:

```csharp
public void RecordSetEffort(
    Guid workoutExerciseId,
    Guid setId,
    SetEffortRating effort,
    DateTimeOffset recordedAt)
{
    EnsureActive();
    EnsureNotEmpty(workoutExerciseId, nameof(workoutExerciseId));
    EnsureNotEmpty(setId, nameof(setId));
    if (!Enum.IsDefined(effort)) throw new ArgumentOutOfRangeException(nameof(effort));
    var exercise = FindActiveExercise(workoutExerciseId);
    var normalized = NormalizeTimestamp(recordedAt, nameof(recordedAt));
    EnsureNotBeforeStart(normalized, nameof(recordedAt));
    EnsureNotBeforeCompletion(normalized, nameof(recordedAt));
    if (exercise.RecordSetEffort(setId, effort, normalized)) Version++;
}
```

- [ ] **Step 4: Run domain tests**

Run the Step 2 command again. Expected: PASS.

- [ ] **Step 5: Add EF mapping and migration regression test**

Add to `SetEntryConfiguration` inside the table configuration:

```csharp
table.HasCheckConstraint(
    "CK_set_entries_effort",
    "\"Effort\" IS NULL OR \"Effort\" IN (1, 2, 3)");
```

Map the property:

```csharp
builder.Property(set => set.Effort);
```

Add this fact to `WorkoutPersistenceTests`:

```csharp
[Fact]
public async Task Effort_round_trips_and_database_constraint_rejects_unknown_values()
{
    await using var database = await PostgreSqlFixture.StartAsync();
    var owner = User.Create($"effort-{Guid.NewGuid():N}@example.com", "hash");
    var definition = ExerciseDefinition.CreateSystem(
        "Effort Press", BodyPart.Chest, TrackingMode.Weighted);
    var now = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);
    var workout = WorkoutSession.Start(owner.Id, Guid.NewGuid(), now);
    var itemId = Guid.NewGuid();
    var setId = Guid.NewGuid();
    workout.AddExercise(itemId, definition.Id, TrackingMode.Weighted, 0);
    workout.CompleteSet(
        itemId, setId, new SetMeasurement(70m, null, 10), now.AddMinutes(1));
    await database.Db.Users.AddAsync(owner);
    await database.Db.Exercises.AddAsync(definition);
    await database.Db.WorkoutSessions.AddAsync(workout);
    await database.Db.SaveChangesAsync();
    Assert.Null(workout.Exercises.Single().Sets.Single().Effort);

    workout.RecordSetEffort(
        itemId, setId, SetEffortRating.Productive, now.AddMinutes(2));
    await database.Db.SaveChangesAsync();
    database.Db.ChangeTracker.Clear();

    var reloaded = await database.Db.SetEntries.AsNoTracking()
        .SingleAsync(set => set.Id == setId);
    Assert.Equal(SetEffortRating.Productive, reloaded.Effort);
    Assert.Equal(70m, reloaded.WeightKg);
    Assert.Equal(10, reloaded.Reps);

    var failure = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
        database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE set_entries SET \"Effort\" = {99} WHERE \"Id\" = {setId}"));
    Assert.Equal("CK_set_entries_effort", failure.ConstraintName);
}
```

Add a second fact that exercises the actual immediate-predecessor migration with a populated set. `20260820061814_AddBadges` is the current latest migration before `AddSetEffortRating`; keep this target exact so the test fails if the planned migration is reordered:

```csharp
[Fact]
public async Task Pre_effort_database_with_an_existing_set_migrates_to_null_effort()
{
    await using var database = await PostgreSqlFixture.StartAsync();
    var owner = User.Create($"effort-upgrade-{Guid.NewGuid():N}@example.com", "hash");
    var definition = ExerciseDefinition.CreateSystem(
        "Effort Upgrade Press", BodyPart.Chest, TrackingMode.Weighted);
    var now = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
    var workout = WorkoutSession.Start(owner.Id, Guid.NewGuid(), now);
    var itemId = Guid.NewGuid();
    var setId = Guid.NewGuid();
    workout.AddExercise(itemId, definition.Id, TrackingMode.Weighted, 0);
    workout.CompleteSet(
        itemId, setId, new SetMeasurement(65m, null, 11), now.AddMinutes(1));
    await database.Db.Users.AddAsync(owner);
    await database.Db.Exercises.AddAsync(definition);
    await database.Db.WorkoutSessions.AddAsync(workout);
    await database.Db.SaveChangesAsync();

    var migrator = database.Db.GetService<IMigrator>();
    await migrator.MigrateAsync("20260820061814_AddBadges");
    var columnsBefore = await database.Db.Database.SqlQueryRaw<int>(
        """
        SELECT COUNT(*)::int AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'set_entries'
          AND column_name = 'Effort'
        """).SingleAsync();
    Assert.Equal(0, columnsBefore);

    await migrator.MigrateAsync();
    database.Db.ChangeTracker.Clear();
    var restored = await database.Db.SetEntries.AsNoTracking()
        .SingleAsync(set => set.Id == setId);

    Assert.Null(restored.Effort);
    Assert.Equal(65m, restored.WeightKg);
    Assert.Equal(11, restored.Reps);
}
```

- [ ] **Step 6: Generate and inspect the migration**

Run:

```bash
dotnet ef migrations add AddSetEffortRating --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --output-dir Persistence/Migrations
```

Expected migration `Up`:

```csharp
migrationBuilder.AddColumn<int>(
    name: "Effort",
    table: "set_entries",
    type: "integer",
    nullable: true);
migrationBuilder.AddCheckConstraint(
    name: "CK_set_entries_effort",
    table: "set_entries",
    sql: "\"Effort\" IS NULL OR \"Effort\" IN (1, 2, 3)");
```

Expected `Down`: drop the check constraint, then drop `Effort`. Confirm the snapshot declares the nullable enum property and constraint. Do not hand-edit the timestamp prefix generated by EF.

- [ ] **Step 7: Run domain and persistence tests**

Run:

```bash
dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutSessionTests|FullyQualifiedName~SetEntryPersistenceShapeTests" -m:1
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutPersistenceTests" -m:1
```

Expected: PASS; existing rows remain nullable and the database rejects values outside 1–3.

- [ ] **Step 8: Commit domain persistence**

```bash
git add src/TrackZ.Domain/Workouts/SetEntry.cs src/TrackZ.Domain/Workouts/WorkoutExercise.cs src/TrackZ.Domain/Workouts/WorkoutSession.cs src/TrackZ.Infrastructure/Persistence/Configurations/SetEntryConfiguration.cs src/TrackZ.Infrastructure/Persistence/Migrations tests/TrackZ.Domain.Tests/Workouts/WorkoutSessionTests.cs tests/TrackZ.Domain.Tests/Workouts/SetEntryPersistenceShapeTests.cs tests/TrackZ.Infrastructure.Tests/Persistence/WorkoutPersistenceTests.cs
git commit -m "feat: persist per-set effort ratings"
```

### Task 3: Workout contracts, read projections, and dedicated server sync action

**Files:**

- Modify: `src/TrackZ.Contracts/Workouts/WorkoutDetailDto.cs`
- Modify: `src/TrackZ.Contracts/Sync/SyncPullResponse.cs`
- Modify: `src/TrackZ.Application/Workouts/IWorkoutReadStore.cs`
- Modify: `src/TrackZ.Application/Workouts/WorkoutDtoMapper.cs`
- Modify: `src/TrackZ.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `src/TrackZ.Application/Sync/Push/PushSyncCommand.cs`
- Modify: `src/TrackZ.Application/Sync/Push/PushSyncHandler.cs`
- Modify: `tests/TrackZ.Application.Tests/Workouts/WorkoutQueryTests.cs`
- Modify: `tests/TrackZ.Api.Tests/Sync/SyncPushTests.cs`
- Modify: `tests/TrackZ.Api.Tests/Sync/SyncPullTests.cs`
- Modify: `tests/TrackZ.Api.Tests/Workouts/EditHistoryTests.cs`
- Modify: `tests/TrackZ.Api.Tests/Workouts/WorkoutEndpointTests.cs`

**Interfaces:**

- Consumes: aggregate mutation and persistence from Task 2.
- Produces: optional effort in `WorkoutSetDto`/`SyncSetDto`; API action `RecordSetEffort`; internal `RecordSetEffortSyncCommand` and payload.

- [ ] **Step 1: Add failing compatibility and API tests**

Add `using TrackZ.Domain.Workouts;` to `SyncPushTests`, then add this workflow using its existing `Operation`, `StartOperation`, `SaveSetOperation`, `AssertAppliedAsync`, `PushDocumentAsync`, `AssertResult`, and database fixture helpers:

```csharp
[Fact]
public async Task Effort_operation_is_idempotent_validated_and_survives_legacy_edit()
{
    var authentication = await AuthenticateAsync();
    var exercise = ExerciseDefinition.CreateSystem(
        $"Effort Sync Press {Guid.NewGuid():N}",
        BodyPart.Chest,
        TrackingMode.Weighted);
    await SeedAsync(exercise);
    var ids = SyncIds.Create();
    var startedAt = Utc(10);
    var setId = Guid.NewGuid();
    await AssertAppliedAsync(
        authentication.Token,
        StartOperation(ids, exercise.Id, startedAt),
        1);
    await AssertAppliedAsync(
        authentication.Token,
        SaveSetOperation(
            ids, Guid.NewGuid(), setId, 1, 0, "70", 12,
            startedAt.AddMinutes(1)),
        2);

    var effortOperationId = Guid.NewGuid();
    var effort = Operation(effortOperationId, "RecordSetEffort", 2, new
    {
        workoutId = ids.WorkoutId,
        workoutExerciseId = ids.WorkoutExerciseId,
        setId,
        effort = (int)SetEffortRating.Productive,
        recordedAt = startedAt.AddMinutes(2)
    });
    var first = await PushDocumentAsync(authentication.Token, effort);
    var replay = await PushDocumentAsync(authentication.Token, effort);
    AssertResult(first, 0, "Applied", 3, null);
    Assert.Equal(Result(first, 0).GetRawText(), Result(replay, 0).GetRawText());

    var sameValueNewOperation = await PushDocumentAsync(
        authentication.Token,
        Operation(Guid.NewGuid(), "RecordSetEffort", 3, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId,
            effort = (int)SetEffortRating.Productive,
            recordedAt = startedAt
        }));
    AssertResult(sameValueNewOperation, 0, "Applied", 3, null);

    var invalid = await PushDocumentAsync(
        authentication.Token,
        Operation(Guid.NewGuid(), "RecordSetEffort", 3, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId,
            effort = 0,
            recordedAt = startedAt.AddMinutes(3)
        }),
        Operation(Guid.NewGuid(), "RecordSetEffort", 3, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId,
            effort = 4,
            recordedAt = startedAt.AddMinutes(3)
        }),
        Operation(Guid.NewGuid(), "RecordSetEffort", 3, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId = Guid.NewGuid(),
            effort = 1,
            recordedAt = startedAt.AddMinutes(3)
        }),
        Operation(Guid.NewGuid(), "RecordSetEffort", 3, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId,
            effort = 1,
            recordedAt = startedAt
        }));
    for (var index = 0; index < 4; index++)
        AssertResult(invalid, index, "Rejected", null, 10009);

    var legacyEdit = Operation(Guid.NewGuid(), "EditSet", 3, new
    {
        workoutId = ids.WorkoutId,
        workoutExerciseId = ids.WorkoutExerciseId,
        setId,
        weightKg = "72.5",
        assistedKg = (string?)null,
        reps = 9,
        updatedAt = startedAt.AddMinutes(3)
    });
    await AssertAppliedAsync(authentication.Token, legacyEdit, 4);

    await AssertAppliedAsync(
        authentication.Token,
        Operation(Guid.NewGuid(), "CompleteWorkout", 4, new
        {
            workoutId = ids.WorkoutId,
            completedAt = startedAt.AddMinutes(4)
        }),
        5);
    var afterCompletion = await PushDocumentAsync(
        authentication.Token,
        Operation(Guid.NewGuid(), "RecordSetEffort", 5, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId,
            effort = (int)SetEffortRating.Easy,
            recordedAt = startedAt.AddMinutes(5)
        }));
    AssertResult(afterCompletion, 0, "Rejected", null, 10009);

    await using var scope = _factory!.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var stored = await database.SetEntries.AsNoTracking()
        .SingleAsync(set => set.Id == setId);
    Assert.Equal(SetEffortRating.Productive, stored.Effort);
    Assert.Equal(72.5m, stored.WeightKg);
    Assert.Equal(9, stored.Reps);
}
```

In `SyncPullTests.Applied_pushes_emit_exactly_once_changes_and_cursor_replay_is_stable`, bind `setId` before `setOperation`, then push:

```csharp
var effortOperation = new
{
    operationId = Guid.NewGuid(),
    entityType = "Workout",
    action = "RecordSetEffort",
    baseVersion = 2,
    payload = new
    {
        workoutId,
        workoutExerciseId,
        setId,
        effort = (int)SetEffortRating.Productive,
        recordedAt = Utc(8).AddMinutes(2)
    }
};
await PushAsync(account.Token, effortOperation);
await PushAsync(account.Token, effortOperation);
```

Read one more cursor page and assert the save page is unrated while the effort page carries the observation without changing measurement:

```csharp
var third = await PullAsync(account.Token, second.NextCursor, 1);
var savedSet = Assert.Single(Assert.Single(second.Changes).Workout.Exercises[0].Sets);
var ratedSet = Assert.Single(Assert.Single(third.Changes).Workout.Exercises[0].Sets);
Assert.Null(savedSet.Effort);
Assert.Equal(SetEffortRating.Productive, ratedSet.Effort);
Assert.Equal(savedSet.WeightKg, ratedSet.WeightKg);
Assert.Equal(savedSet.Reps, ratedSet.Reps);
Assert.Equal(3, third.Changes[0].ServerVersion);
```

After that test's existing `database` variable is created, add:

```csharp
Assert.Equal(1, await database.SyncChanges.CountAsync(
    item => item.OperationId == effortOperation.operationId));
```

In `WorkoutQueryTests.Workout_detail_preserves_exact_exercise_and_set_order_for_all_modes`, construct the order-zero weighted row with `SetEffortRating.Productive`, then assert the mapped DTO keeps it:

```csharp
Exercise(TrackingMode.Weighted, 0,
    Set(1, 60m, null, 7),
    Set(0, 65m, null, 5, SetEffortRating.Productive))

Assert.Equal(SetEffortRating.Productive, detail.Exercises[0].Sets[0].Effort);
```

Extend only that test file's helper with a trailing optional effort and pass it to `WorkoutSetReadRow`:

```csharp
private static WorkoutSetReadRow Set(
    int order,
    decimal? weightKg,
    decimal? assistedKg,
    int reps,
    SetEffortRating? effort = null) =>
    new(Guid.NewGuid(), order, weightKg, assistedKg, reps,
        new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero),
        null, effort);
```

In `WorkoutEndpointTests`, add a rated variant of the existing completed-workout helper so effort is recorded before completion:

```csharp
private static WorkoutSession CompletedWorkoutWithRatedFirstSet(
    Guid ownerId,
    ExerciseDefinition definition,
    DateTimeOffset completedAt,
    params (decimal WeightKg, int Reps)[] sets)
{
    var workout = WorkoutSession.Start(
        ownerId, Guid.NewGuid(), completedAt.AddMinutes(-10));
    var itemId = Guid.NewGuid();
    workout.AddExercise(itemId, definition.Id, TrackingMode.Weighted, 0);
    for (var index = 0; index < sets.Length; index++)
    {
        var setId = Guid.NewGuid();
        var setCompletedAt = completedAt.AddMinutes(-sets.Length + index);
        workout.CompleteSet(
            itemId,
            setId,
            new SetMeasurement(sets[index].WeightKg, null, sets[index].Reps),
            setCompletedAt);
        if (index == 0)
            workout.RecordSetEffort(
                itemId,
                setId,
                SetEffortRating.Productive,
                setCompletedAt.AddTicks(1));
    }
    workout.Complete(completedAt);
    return workout;
}
```

Use that helper only in `Detail_returns_exact_order_and_foreign_or_unknown_are_indistinguishable`, then keep its existing weight assertion and add:

```csharp
Assert.Equal(
    (int)SetEffortRating.Productive,
    json!.RootElement.GetProperty("exercises")[0].GetProperty("sets")[0]
        .GetProperty("effort").GetInt32());
```

Add this compatibility fact to `WorkoutQueryTests`, plus `using System.Text.Json;`, `using TrackZ.Contracts.Sync;`, and `using TrackZ.Contracts.Workouts;`:

```csharp
[Fact]
public void Legacy_set_json_without_effort_deserializes_as_unrated()
{
    const string workoutSet = """
        {"id":"11111111-1111-1111-1111-111111111111","order":0,
         "weightKg":70,"assistedKg":null,"reps":10,
         "completedAt":"2026-08-22T09:00:00+00:00","updatedAt":null}
        """;
    const string syncSet = """
        {"id":"22222222-2222-2222-2222-222222222222","order":0,
         "weightKg":"70","assistedKg":null,"reps":10,
         "completedAt":"2026-08-22T09:00:00+00:00","updatedAt":null,
         "deletedAt":null,"version":1}
        """;
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    Assert.Null(JsonSerializer.Deserialize<WorkoutSetDto>(workoutSet, options)!.Effort);
    Assert.Null(JsonSerializer.Deserialize<SyncSetDto>(syncSet, options)!.Effort);
}
```

- [ ] **Step 2: Run focused server tests and confirm failure**

Run:

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SyncPushTests|FullyQualifiedName~SyncPullTests|FullyQualifiedName~EditHistoryTests|FullyQualifiedName~WorkoutEndpointTests" -m:1
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutQueryTests" -m:1
```

Expected: FAIL because DTO effort and `RecordSetEffort` dispatch do not exist.

- [ ] **Step 3: Extend additive DTOs and read rows**

Add trailing optional parameters so old JSON and most existing constructors remain compatible:

```csharp
public sealed record WorkoutSetDto(
    Guid Id,
    int Order,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    DateTimeOffset CompletedAt,
    DateTimeOffset? UpdatedAt,
    SetEffortRating? Effort = null);

public sealed record SyncSetDto(
    Guid Id,
    int Order,
    string? WeightKg,
    string? AssistedKg,
    int Reps,
    DateTimeOffset CompletedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeletedAt,
    long Version,
    SetEffortRating? Effort = null);
```

Add `using TrackZ.Domain.Workouts;` to `SyncPullResponse.cs`; `WorkoutDetailDto.cs` already imports that namespace.

Append `SetEffortRating? Effort = null` to `WorkoutSetReadRow`, pass `set.Effort` in `AppDbContext.LoadWorkoutReadSessionsAsync`, and pass the row value in `WorkoutDtoMapper.ToDto`. Add `set.Effort` to `PushSyncHandler.ToDto` when constructing `SyncSetDto`.

- [ ] **Step 4: Add the dedicated command and payload**

In `PushSyncCommand.cs`, add:

```csharp
internal sealed record RecordSetEffortSyncCommand(
    long BaseVersion,
    RecordSetEffortSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record RecordSetEffortSyncPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid SetId,
    int Effort,
    DateTimeOffset RecordedAt);
```

In `PushSyncHandler.DispatchAsync`, add the exact action branch and contract:

```csharp
"RecordSetEffort" => await sender.Send(new RecordSetEffortSyncCommand(
    baseVersion,
    operation.Payload.Deserialize<RecordSetEffortSyncPayload>(JsonOptions)
        ?? throw new JsonException()), cancellationToken),
```

```csharp
"RecordSetEffort" => HasProperties(
    payload, "workoutId", "workoutExerciseId", "setId", "effort", "recordedAt"),
```

Do not add this action to `RequiresPerformanceRecomputation`; effort does not change the persisted PR/volume projection.

- [ ] **Step 5: Implement the server handler with normal aggregate concurrency**

Add beside `EditSetSyncHandler`:

```csharp
internal sealed class RecordSetEffortSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser)
    : IRequestHandler<RecordSetEffortSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        RecordSetEffortSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty
            || payload.WorkoutExerciseId == Guid.Empty
            || payload.SetId == Guid.Empty
            || payload.RecordedAt == default
            || !Enum.IsDefined((SetEffortRating)payload.Effort))
            return SyncMutationResult.Rejected();

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null)
            return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion)
            return SyncMutationResult.Conflict(workout.Version);

        try
        {
            workout.RecordSetEffort(
                payload.WorkoutExerciseId,
                payload.SetId,
                (SetEffortRating)payload.Effort,
                payload.RecordedAt);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }
}
```

- [ ] **Step 6: Run server compatibility tests**

Run the Step 2 commands again. Expected: PASS, including duplicate delivery, pull/detail/history effort, invalid input rejection, and legacy edit preservation.

- [ ] **Step 7: Commit server contracts and sync**

```bash
git add src/TrackZ.Contracts/Workouts/WorkoutDetailDto.cs src/TrackZ.Contracts/Sync/SyncPullResponse.cs src/TrackZ.Application/Workouts/IWorkoutReadStore.cs src/TrackZ.Application/Workouts/WorkoutDtoMapper.cs src/TrackZ.Infrastructure/Persistence/AppDbContext.cs src/TrackZ.Application/Sync/Push/PushSyncCommand.cs src/TrackZ.Application/Sync/Push/PushSyncHandler.cs tests/TrackZ.Application.Tests/Workouts/WorkoutQueryTests.cs tests/TrackZ.Api.Tests/Sync/SyncPushTests.cs tests/TrackZ.Api.Tests/Sync/SyncPullTests.cs tests/TrackZ.Api.Tests/Workouts/EditHistoryTests.cs tests/TrackZ.Api.Tests/Workouts/WorkoutEndpointTests.cs
git commit -m "feat: sync set effort independently"
```

### Task 4: Mobile schema 6 and lossless local effort storage

**Files:**

- Modify: `src/TrackZ.Mobile.Core/Data/Models/LocalSet.cs`
- Modify: `src/TrackZ.Mobile.Core/Data/TrackZLocalDatabase.cs`
- Modify: `src/TrackZ.Mobile.Core/Data/LocalWorkoutRepository.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/ActiveWorkoutCoordinator.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Workout/ActiveWorkoutCoordinatorTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Sync/SyncCoordinatorTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/History/WorkoutHistoryCoordinatorTests.cs`

**Interfaces:**

- Consumes: `SetEffortRating` and optional server DTO effort from Tasks 1–3.
- Produces: trailing optional `LocalSet.Effort`; SQLite `LocalSet.Effort`; schema version 6 whose outbox accepts operation values 1–11; `SaveSetAsync` rejects pre-populated effort so only the dedicated Task 5 mutation can write it.

- [ ] **Step 1: Add failing mobile migration and round-trip tests**

Add to `ActiveWorkoutCoordinatorTests`:

```csharp
[Fact]
public async Task Schema_v5_upgrades_to_v6_with_null_effort_and_accepts_operation_type_11()
{
    var fixture = CreateFixture();
    await fixture.Coordinator.StartAsync([
        new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)
    ]);
    var saved = await fixture.Coordinator.SaveSetAsync(
        _exerciseId, new LocalSet(72.5m, null, 10));
    await DowngradeGuidanceSchemaToV5Async();

    var upgraded = new TrackZLocalDatabase(_databasePath);
    await upgraded.InitializeAsync();
    var repository = new LocalWorkoutRepository(upgraded);
    var restored = Assert.Single(Assert.Single(
        (await repository.GetActiveAsync(default))!.Exercises).Sets);

    Assert.Equal(saved.Id, restored.Id);
    Assert.Null(restored.Effort);
    await using var connection = await OpenRawAsync();
    await using var version = connection.CreateCommand();
    version.CommandText = "PRAGMA user_version;";
    Assert.Equal(6, Convert.ToInt32(await version.ExecuteScalarAsync()));
    await using var schema = connection.CreateCommand();
    schema.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='OutboxOperation';";
    Assert.Contains("BETWEEN 1 AND 11", (string)(await schema.ExecuteScalarAsync())!, StringComparison.Ordinal);
}

[Fact]
public async Task Save_set_rejects_prepopulated_effort_without_graph_or_outbox_change()
{
    var fixture = CreateFixture();
    await fixture.Coordinator.StartAsync([
        new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)
    ]);
    var pendingBefore = await fixture.Outbox.PendingAsync();
    var set = new LocalSet(
        Guid.NewGuid(), Guid.Empty, 0, 70m, null, 10,
        default, null, null, 0, 0, Guid.NewGuid(), SetEffortRating.Easy);

    await Assert.ThrowsAsync<ArgumentException>(() =>
        fixture.Coordinator.SaveSetAsync(_exerciseId, set));

    Assert.Equal(pendingBefore, await fixture.Outbox.PendingAsync());
    Assert.Empty(Assert.Single(
        (await fixture.Coordinator.RestoreActiveAsync())!.Exercises).Sets);
}
```

Implement `DowngradeGuidanceSchemaToV5Async` in the test fixture using one transaction which:

1. drops `IX_OutboxOperation_Pending`;
2. renames `HistoryUndo` to `HistoryUndoV6` and `OutboxOperation` to `OutboxOperationV6`;
3. recreates `OutboxOperation` with the current columns but `CHECK (OperationType BETWEEN 1 AND 10)`;
4. copies every column and recreates the pending index;
5. recreates/copies `HistoryUndo`, then drops `HistoryUndoV6` before `OutboxOperationV6`;
6. executes `ALTER TABLE LocalSet DROP COLUMN Effort;`;
7. sets `PRAGMA user_version = 5` and commits.

The downgrade helper's rebuilt table and copy must be exactly:

```sql
CREATE TABLE OutboxOperation (
    OperationId TEXT PRIMARY KEY NOT NULL,
    EntityId TEXT NOT NULL,
    OperationType INTEGER NOT NULL CHECK (OperationType BETWEEN 1 AND 10),
    Payload TEXT NOT NULL CHECK (length(Payload) > 0),
    BaseVersion INTEGER NOT NULL CHECK (BaseVersion >= 0),
    CreatedAt TEXT NOT NULL,
    State INTEGER NOT NULL CHECK (State IN (1, 2, 3, 4)),
    DeletedAt TEXT NULL,
    Version INTEGER NOT NULL CHECK (Version >= 1),
    ServerVersion INTEGER NULL CHECK (ServerVersion IS NULL OR ServerVersion >= 0),
    RetryCount INTEGER NOT NULL DEFAULT 0 CHECK (RetryCount >= 0),
    NextAttemptAt TEXT NULL,
    ServerPayload TEXT NULL CHECK (ServerPayload IS NULL OR json_valid(ServerPayload)),
    ReplacesOperationId TEXT NULL,
    SendStartedAt TEXT NULL,
    NeutralizedAt TEXT NULL,
    FailureCode INTEGER NULL,
    FOREIGN KEY (EntityId) REFERENCES LocalWorkout(Id) ON DELETE RESTRICT
);
INSERT INTO OutboxOperation
    (OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
     State, DeletedAt, Version, ServerVersion, RetryCount, NextAttemptAt,
     ServerPayload, ReplacesOperationId, SendStartedAt, NeutralizedAt, FailureCode)
SELECT OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
       State, DeletedAt, Version, ServerVersion, RetryCount, NextAttemptAt,
       ServerPayload, ReplacesOperationId, SendStartedAt, NeutralizedAt, FailureCode
FROM OutboxOperationV6;
CREATE INDEX IX_OutboxOperation_Pending
    ON OutboxOperation(State, CreatedAt, OperationId)
    WHERE State = 1 AND DeletedAt IS NULL;
CREATE TABLE HistoryUndo (
    OperationId TEXT PRIMARY KEY NOT NULL,
    SnapshotJson TEXT NOT NULL CHECK (json_valid(SnapshotJson)),
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (OperationId) REFERENCES OutboxOperation(OperationId) ON DELETE CASCADE
);
INSERT INTO HistoryUndo (OperationId, SnapshotJson, CreatedAt)
SELECT OperationId, SnapshotJson, CreatedAt FROM HistoryUndoV6;
```

Add `using TrackZ.Domain.Workouts;` and this repository fact to `WorkoutHistoryCoordinatorTests`; it exercises hydrate, upsert, JSON undo, and restore without passing effort through the public `SaveSet` mutation:

```csharp
[Fact]
public async Task Rated_set_round_trips_through_repository_edit_and_undo_snapshot()
{
    var repository = new LocalWorkoutRepository(Database());
    var workoutId = Guid.NewGuid();
    var workoutExerciseId = Guid.NewGuid();
    var exerciseDefinitionId = Guid.NewGuid();
    var setId = Guid.NewGuid();
    var saveOperationId = Guid.NewGuid();
    var editOperationId = Guid.NewGuid();
    var startedAt = new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero);
    var setCompletedAt = startedAt.AddMinutes(1);
    var completedAt = startedAt.AddMinutes(2);
    var editedAt = startedAt.AddMinutes(3);
    var rated = new LocalSet(
        setId, workoutExerciseId, 0, 70m, null, 10,
        setCompletedAt, setCompletedAt.AddTicks(1), null,
        2, 1, saveOperationId, SetEffortRating.Easy);
    var previous = new LocalWorkout(
        workoutId, LocalWorkoutStatus.Completed, startedAt, completedAt,
        null, 3, 0,
        [new LocalWorkoutExercise(
            workoutExerciseId, workoutId, exerciseDefinitionId,
            TrackingMode.Weighted, 0, null, 2, 0, [rated])]);
    var seedOperation = OutboxOperation.Create(
        saveOperationId,
        workoutId,
        OutboxOperationType.SaveSet,
        new SaveSetOutboxPayload(
            workoutId, workoutExerciseId, setId, 0,
            "70", null, 10, setCompletedAt),
        2,
        setCompletedAt);
    await repository.SaveWorkoutAndEnqueueAsync(previous, seedOperation);

    var editedSet = rated with
    {
        WeightKg = 72.5m,
        Reps = 9,
        UpdatedAt = editedAt,
        Version = rated.Version + 1
    };
    var edited = previous with
    {
        Version = previous.Version + 1,
        Exercises = [previous.Exercises[0] with
        {
            Version = previous.Exercises[0].Version + 1,
            Sets = [editedSet]
        }]
    };
    var editOperation = OutboxOperation.Create(
        editOperationId,
        workoutId,
        OutboxOperationType.EditSet,
        new EditSetOutboxPayload(
            workoutId, workoutExerciseId, setId,
            "72.5", null, 9, editedAt),
        previous.Version,
        editedAt);
    await repository.SaveHistoryMutationAndEnqueueAsync(
        previous, edited, editOperation);

    var reloaded = Assert.Single(await repository.GetHistoryAsync());
    var reloadedSet = Assert.Single(Assert.Single(reloaded.Exercises).Sets);
    Assert.Equal(SetEffortRating.Easy, reloadedSet.Effort);
    Assert.Equal(72.5m, reloadedSet.WeightKg);
    Assert.Equal(9, reloadedSet.Reps);

    var restored = await repository.UndoHistoryMutationAsync(
        editOperationId, editedAt.AddMinutes(1));
    var restoredSet = Assert.Single(Assert.Single(restored.Exercises).Sets);
    Assert.Equal(SetEffortRating.Easy, restoredSet.Effort);
    Assert.Equal(70m, restoredSet.WeightKg);
    Assert.Equal(10, restoredSet.Reps);
}
```

- [ ] **Step 2: Run focused mobile persistence tests and confirm failure**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~ActiveWorkoutCoordinatorTests|FullyQualifiedName~SyncCoordinatorTests|FullyQualifiedName~WorkoutHistoryCoordinatorTests" -m:1
```

Expected: FAIL because `LocalSet.Effort`, schema 6, and the SQLite column do not exist.

- [ ] **Step 3: Add optional effort to the local graph**

Change the primary constructor by adding a trailing optional parameter, preserving existing call sites:

```csharp
public sealed record LocalSet(
    Guid Id,
    Guid WorkoutExerciseId,
    int Order,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    DateTimeOffset CompletedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeletedAt,
    long Version,
    long BaseVersion,
    Guid OperationId,
    SetEffortRating? Effort = null)
```

Add `using TrackZ.Domain.Workouts;` to `LocalSet.cs`.

In `ActiveWorkoutCoordinator.SaveSetAsync`, reject callers that try to bypass the dedicated post-save mutation before capturing the session generation:

```csharp
if (set.Effort is not null)
    throw new ArgumentException(
        "A new set must be saved before effort is recorded.", nameof(set));
```

Pass `null` from the three-argument convenience constructor. In `LocalWorkoutRepository`, add `Effort` to all of these exact paths:

- active/history `SELECT` lists and every `new LocalSet` hydrator;
- `UpsertSetAsync` insert, update, parameters, and identity comparison;
- history undo `HistoryUndoSet`, snapshot creation, and restoration;
- graph validation (`Enum.IsDefined` when non-null);
- all measurement-edit/delete/reindex copies so `with`/SQL updates do not overwrite effort;
- full graph conversion and authoritative replacement.

Also add `using TrackZ.Domain.Workouts;` to `LocalWorkoutRepository.cs`. Make its JSON undo shape backward-compatible by appending the optional field:

```csharp
private sealed record HistoryUndoSet(
    Guid Id,
    Guid WorkoutExerciseId,
    int Order,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    DateTimeOffset CompletedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeletedAt,
    long Version,
    long BaseVersion,
    Guid OperationId,
    SetEffortRating? Effort = null);
```

Replace the inner set projections in both positional conversions with these exact forms so old snapshots deserialize as `null` while new snapshots round-trip the observation:

```csharp
exercise.Sets.Select(set => new HistoryUndoSet(
    set.Id,
    set.WorkoutExerciseId,
    set.Order,
    set.WeightKg,
    set.AssistedKg,
    set.Reps,
    set.CompletedAt,
    set.UpdatedAt,
    set.DeletedAt,
    set.Version,
    set.BaseVersion,
    set.OperationId,
    set.Effort)).ToArray()

exercise.Sets.Select(set => new LocalSet(
    set.Id,
    set.WorkoutExerciseId,
    set.Order,
    set.WeightKg,
    set.AssistedKg,
    set.Reps,
    set.CompletedAt,
    set.UpdatedAt,
    set.DeletedAt,
    set.Version,
    set.BaseVersion,
    set.OperationId,
    set.Effort)).ToArray()
```

Store effort as nullable integer and read it as:

```csharp
reader.IsDBNull(effortOrdinal)
    ? null
    : EnumValue<SetEffortRating>(reader, effortOrdinal)
```

- [ ] **Step 4: Upgrade the final schema and all legacy branches**

Set:

```csharp
public const int CurrentSchemaVersion = 6;
```

Accept source versions `0 or 1 or 2 or 3 or 4 or 5`. Change initialization routing so v1–v4 call:

```csharp
await BuildSyncStateUpgradeAsync(
    connection,
    addEffortColumn: version is 2 or 3 or 4,
    cancellationToken)
```

and v5 calls `BuildGuidanceUpgradeAsync`. The v1 branch rebuilds `LocalSet` itself, so it passes `false`; this prevents adding the same column twice. In the fresh schema and v1 rebuilt `LocalSet`, add:

```sql
Effort INTEGER NULL CHECK (Effort IS NULL OR Effort IN (1, 2, 3)),
```

Change every fresh/rebuilt outbox constraint to:

```sql
OperationType INTEGER NOT NULL CHECK (OperationType BETWEEN 1 AND 11),
```

Change every terminal pragma to `PRAGMA user_version = 6`.

Update `BuildSyncStateUpgradeAsync` to accept the `bool addEffortColumn` parameter. For source versions 2–4, conditionally append the effort-column `ALTER TABLE` and rebuild the outbox directly with the 1–11 constraint. For source version 1, include `Effort` in the rebuilt table and use that same final outbox constraint without a later `ALTER TABLE`.

Replace the v1 rebuild projection explicitly; the new column and value counts must stay equal:

```sql
INSERT INTO LocalSet
    (Id, OperationId, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
     Effort, CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion)
SELECT legacy.Id,
       COALESCE(
           (SELECT operation.OperationId
            FROM OutboxOperation AS operation
            WHERE operation.OperationType = 2
              AND CASE
                      WHEN json_valid(operation.Payload)
                      THEN json_extract(operation.Payload, '$.setId')
                  END = legacy.Id
            ORDER BY operation.CreatedAt, operation.OperationId
            LIMIT 1),
           legacy.Id),
       legacy.WorkoutExerciseId, legacy.SortOrder, legacy.WeightKg,
       legacy.AssistedKg, legacy.Reps, NULL, legacy.CompletedAt,
       legacy.UpdatedAt, legacy.DeletedAt, legacy.Version, legacy.BaseVersion
FROM LocalSetV1 AS legacy;
```

For source version 5, add a new `BuildGuidanceUpgradeAsync` whose SQL is:

```sql
ALTER TABLE LocalSet
    ADD COLUMN Effort INTEGER NULL
    CHECK (Effort IS NULL OR Effort IN (1, 2, 3));
DROP INDEX IF EXISTS IX_OutboxOperation_Pending;
ALTER TABLE HistoryUndo RENAME TO HistoryUndoV5;
ALTER TABLE OutboxOperation RENAME TO OutboxOperationV5;
```

Then recreate/copy the complete outbox and undo tables exactly as in `BuildSyncStateUpgradeAsync`, with `BETWEEN 1 AND 11`, recreate the pending index, drop `HistoryUndoV5` before `OutboxOperationV5`, and finish at version 6. Preserve `PRAGMA foreign_keys = ON`; do not disable constraints to make the migration pass.

- [ ] **Step 5: Run mobile persistence and migration tests**

Run the Step 2 command again. Expected: PASS. Also run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~Sync_state_schema|FullyQualifiedName~Genuine_v2|FullyQualifiedName~Genuine_v3|FullyQualifiedName~Genuine_v4|FullyQualifiedName~Schema_v1|FullyQualifiedName~Schema_v5" -m:1
```

Expected: PASS for every supported source version; v5 rows read with null effort and the final outbox accepts 11.

- [ ] **Step 6: Commit mobile persistence**

```bash
git add src/TrackZ.Mobile.Core/Data/Models/LocalSet.cs src/TrackZ.Mobile.Core/Data/TrackZLocalDatabase.cs src/TrackZ.Mobile.Core/Data/LocalWorkoutRepository.cs src/TrackZ.Mobile.Core/Features/Workout/ActiveWorkoutCoordinator.cs tests/TrackZ.Mobile.Tests/Workout/ActiveWorkoutCoordinatorTests.cs tests/TrackZ.Mobile.Tests/Sync/SyncCoordinatorTests.cs tests/TrackZ.Mobile.Tests/History/WorkoutHistoryCoordinatorTests.cs
git commit -m "feat: store set effort in mobile workout graphs"
```

### Task 5: Local effort operation, ordered outbox, pull, rebase, and history cache

**Files:**

- Modify: `src/TrackZ.Mobile.Core/Sync/OutboxOperation.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Workout/ISetEffortRecorder.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/ActiveWorkoutCoordinator.cs`
- Modify: `src/TrackZ.Mobile.Core/Sync/SyncCoordinator.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/ExerciseHistoryCache.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Workout/ActiveWorkoutCoordinatorTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Sync/SyncCoordinatorTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Acceptance/OfflineWorkoutAcceptanceTests.cs`

**Interfaces:**

- Consumes: schema-6 local graph and server action/DTOs.
- Produces: `OutboxOperationType.RecordSetEffort = 11`; `RecordSetEffortOutboxPayload`; `SetEffortRecordingUnavailableException`; `ISetEffortRecorder`; `ActiveWorkoutCoordinator.RecordSetEffortAsync(Guid, Guid, SetEffortRating, Guid, CancellationToken)`; lossless pull/rebase/cache behavior.

- [ ] **Step 1: Add failing coordinator causality tests**

Add to `ActiveWorkoutCoordinatorTests`:

```csharp
[Fact]
public async Task Record_effort_updates_only_saved_set_and_queues_after_save_set()
{
    var fixture = CreateFixture();
    await fixture.Coordinator.StartAsync([
        new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)
    ]);
    var saved = await fixture.Coordinator.SaveSetAsync(
        _exerciseId, new LocalSet(70m, null, 12));
    var effortOperationId = Guid.NewGuid();

    var rated = await fixture.Coordinator.RecordSetEffortAsync(
        _exerciseId,
        saved.Id,
        SetEffortRating.Productive,
        effortOperationId);

    Assert.Equal(SetEffortRating.Productive, rated.Effort);
    Assert.Equal(saved.WeightKg, rated.WeightKg);
    Assert.Equal(saved.Reps, rated.Reps);
    var pending = await fixture.Outbox.PendingAsync();
    var saveIndex = Array.FindIndex(pending.ToArray(), operation => operation.OperationId == saved.OperationId);
    var effortIndex = Array.FindIndex(pending.ToArray(), operation => operation.OperationId == effortOperationId);
    Assert.True(saveIndex >= 0 && effortIndex > saveIndex);
    var payload = pending[effortIndex].DeserializePayload<RecordSetEffortOutboxPayload>();
    Assert.Equal(saved.Id, payload.SetId);
    Assert.Equal((int)SetEffortRating.Productive, payload.Effort);
}

[Fact]
public async Task Record_effort_replay_with_same_operation_is_idempotent_and_mismatch_is_rejected()
{
    var fixture = CreateFixture();
    await fixture.Coordinator.StartAsync([
        new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)
    ]);
    var saved = await fixture.Coordinator.SaveSetAsync(
        _exerciseId, new LocalSet(70m, null, 12));
    var operationId = Guid.NewGuid();
    var first = await fixture.Coordinator.RecordSetEffortAsync(
        _exerciseId, saved.Id, SetEffortRating.Easy, operationId);
    var replay = await fixture.Coordinator.RecordSetEffortAsync(
        _exerciseId, saved.Id, SetEffortRating.Easy, operationId);

    Assert.Equal(first, replay);

    var graphBeforeNoOp = (await fixture.Coordinator.RestoreActiveAsync())!;
    var pendingBeforeNoOp = await fixture.Outbox.PendingAsync();
    var sameValueNewOperationId = Guid.NewGuid();
    var sameValue = await fixture.Coordinator.RecordSetEffortAsync(
        _exerciseId, saved.Id, SetEffortRating.Easy, sameValueNewOperationId);
    var graphAfterNoOp = (await fixture.Coordinator.RestoreActiveAsync())!;

    Assert.Equal(first, sameValue);
    Assert.Equal(graphBeforeNoOp.Version, graphAfterNoOp.Version);
    Assert.Equal(
        graphBeforeNoOp.Exercises.Single().Version,
        graphAfterNoOp.Exercises.Single().Version);
    Assert.Equal(pendingBeforeNoOp.Count, (await fixture.Outbox.PendingAsync()).Count);
    Assert.DoesNotContain(
        await fixture.Outbox.PendingAsync(),
        operation => operation.OperationId == sameValueNewOperationId);

    await Assert.ThrowsAsync<InvalidDataException>(() =>
        fixture.Coordinator.RecordSetEffortAsync(
            _exerciseId, saved.Id, SetEffortRating.TooHeavy, operationId));
}

[Fact]
public async Task Record_effort_after_workout_finish_is_non_retryable_and_queues_nothing()
{
    var fixture = CreateFixture();
    await fixture.Coordinator.StartAsync([
        new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)
    ]);
    var saved = await fixture.Coordinator.SaveSetAsync(
        _exerciseId, new LocalSet(70m, null, 12));
    await fixture.Coordinator.FinishAsync();
    var pendingBefore = await fixture.Outbox.PendingAsync();

    await Assert.ThrowsAsync<SetEffortRecordingUnavailableException>(() =>
        fixture.Coordinator.RecordSetEffortAsync(
            _exerciseId,
            saved.Id,
            SetEffortRating.Productive,
            Guid.NewGuid()));

    Assert.Equal(pendingBefore.Count, (await fixture.Outbox.PendingAsync()).Count);
}
```

Add these focused facts to `SyncCoordinatorTests` (also add `using TrackZ.Domain.Workouts;`). They deliberately use the existing `SyncContext`, `ServerGraph`, `Change`, and `ConflictResolution` helpers:

```csharp
[Fact]
public async Task Restart_keeps_save_before_effort_and_transient_failure_keeps_both_facts()
{
    await using var context = await SyncContext.CreateAsync();
    var active = await context.StartAsync();
    var coordinator = new ActiveWorkoutCoordinator(
        context.Workouts, context.Boundary, context.Clock);
    context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
    var saved = await coordinator.SaveSetAsync(
        active.Exercises[0].ExerciseDefinitionId,
        new LocalSet(70m, null, 12));
    context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(1);
    await coordinator.RecordSetEffortAsync(
        active.Exercises[0].ExerciseDefinitionId,
        saved.Id,
        SetEffortRating.Productive,
        Guid.NewGuid());

    var restartedDatabase = new TrackZLocalDatabase(context.Path);
    await restartedDatabase.InitializeAsync();
    var pending = await new OutboxRepository(restartedDatabase).PendingAsync();
    Assert.Equal(
        [OutboxOperationType.StartWorkout, OutboxOperationType.SaveSet,
            OutboxOperationType.RecordSetEffort],
        pending.Select(operation => operation.Type));

    context.Api.PushException = new HttpRequestException("offline");
    Assert.Equal(SyncRunStatus.Offline, await context.Coordinator.RunOnceAsync());
    var restored = Assert.Single(Assert.Single(
        (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
    Assert.Equal(SetEffortRating.Productive, restored.Effort);
    Assert.Equal(3, (await context.Outbox.PendingAsync()).Count);
}

[Fact]
public async Task Applied_push_and_pull_keep_effort_and_measurement_together()
{
    await using var context = await SyncContext.CreateAsync();
    var active = await context.StartAsync();
    var coordinator = new ActiveWorkoutCoordinator(
        context.Workouts, context.Boundary, context.Clock);
    var saved = await coordinator.SaveSetAsync(
        active.Exercises[0].ExerciseDefinitionId,
        new LocalSet(70m, null, 12));
    await coordinator.RecordSetEffortAsync(
        active.Exercises[0].ExerciseDefinitionId,
        saved.Id,
        SetEffortRating.Productive,
        Guid.NewGuid());
    var rated = (await context.Workouts.GetActiveAsync())!;
    var pending = await context.Outbox.PendingAsync();
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[1].OperationId, SyncOperationStatus.Applied, 2, null)]));
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[2].OperationId, SyncOperationStatus.Applied, 3, null)]));
    context.Api.PullResponses.Enqueue(new SyncPullResponse([
        Change(ServerGraph(rated, 3), 1)
    ], "effort-cursor", false));

    Assert.Equal(SyncRunStatus.Completed, await context.Coordinator.RunOnceAsync());
    var restored = Assert.Single(Assert.Single(
        (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
    Assert.Equal(SetEffortRating.Productive, restored.Effort);
    Assert.Equal(70m, restored.WeightKg);
    Assert.Equal(12, restored.Reps);
}

[Fact]
public async Task Permanent_effort_rejection_uses_server_effort_without_touching_measurement()
{
    await using var context = await SyncContext.CreateAsync();
    var active = await context.StartAsync();
    var coordinator = new ActiveWorkoutCoordinator(
        context.Workouts, context.Boundary, context.Clock);
    var saved = await coordinator.SaveSetAsync(
        active.Exercises[0].ExerciseDefinitionId,
        new LocalSet(70m, null, 12));
    await coordinator.RecordSetEffortAsync(
        active.Exercises[0].ExerciseDefinitionId,
        saved.Id,
        SetEffortRating.Productive,
        Guid.NewGuid());
    var local = (await context.Workouts.GetActiveAsync())!;
    var pending = await context.Outbox.PendingAsync();
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[1].OperationId, SyncOperationStatus.Applied, 2, null)]));
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[2].OperationId, SyncOperationStatus.Rejected, null,
            BusinessErrorCode.InvalidRequest)]));
    var authority = ServerGraph(local, 2);
    authority = authority with
    {
        Exercises = authority.Exercises.Select(exercise => exercise with
        {
            Version = 2,
            Sets = exercise.Sets.Select(set => set with
            {
                Effort = null,
                UpdatedAt = null,
                Version = 1
            }).ToArray()
        }).ToArray()
    };
    context.Api.PullResponses.Enqueue(new SyncPullResponse([
        Change(authority, 1)
    ], "rejected-effort-cursor", false));

    Assert.Equal(
        SyncRunStatus.PermanentFailure,
        await context.Coordinator.RunOnceAsync());
    var restored = Assert.Single(Assert.Single(
        (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
    Assert.Null(restored.Effort);
    Assert.Equal(70m, restored.WeightKg);
    Assert.Equal(12, restored.Reps);
}

[Fact]
public async Task Effort_conflict_rebase_changes_only_base_version_and_recorded_time()
{
    await using var context = await SyncContext.CreateAsync();
    var active = await context.StartAsync();
    var coordinator = new ActiveWorkoutCoordinator(
        context.Workouts, context.Boundary, context.Clock);
    var saved = await coordinator.SaveSetAsync(
        active.Exercises[0].ExerciseDefinitionId,
        new LocalSet(70m, null, 12));
    await coordinator.RecordSetEffortAsync(
        active.Exercises[0].ExerciseDefinitionId,
        saved.Id,
        SetEffortRating.Productive,
        Guid.NewGuid());
    var local = (await context.Workouts.GetActiveAsync())!;
    var pending = await context.Outbox.PendingAsync();
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[1].OperationId, SyncOperationStatus.Applied, 2, null)]));
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[2].OperationId, SyncOperationStatus.Conflict, 4,
            BusinessErrorCode.VersionConflict)]));
    var authority = ServerGraph(local, 4);
    authority = authority with
    {
        Exercises = authority.Exercises.Select(exercise => exercise with
        {
            Sets = exercise.Sets.Select(set => set with
            {
                Effort = SetEffortRating.Easy
            }).ToArray()
        }).ToArray()
    };
    context.Api.PullResponses.Enqueue(new SyncPullResponse([
        Change(authority, 1)
    ], "effort-conflict-cursor", false));
    await context.Coordinator.RunOnceAsync();
    var conflict = Assert.Single(await context.Outbox.ConflictedAsync());
    var original = conflict.DeserializePayload<RecordSetEffortOutboxPayload>();
    var pulled = Assert.Single(Assert.Single(
        (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
    Assert.Equal(SetEffortRating.Easy, pulled.Effort);

    var replacement = await new ConflictResolution(context.Coordinator)
        .ApplyLocalAgainstVersionAsync(conflict.OperationId, 4);
    var rebased = replacement.DeserializePayload<RecordSetEffortOutboxPayload>();
    Assert.Equal(conflict.OperationId, replacement.ReplacesOperationId);
    Assert.Equal(4, replacement.BaseVersion);
    Assert.True(rebased.RecordedAt > original.RecordedAt);
    Assert.Equal(original.WorkoutId, rebased.WorkoutId);
    Assert.Equal(original.WorkoutExerciseId, rebased.WorkoutExerciseId);
    Assert.Equal(original.SetId, rebased.SetId);
    Assert.Equal(original.Effort, rebased.Effort);
    Assert.Equal((int)SetEffortRating.Productive, rebased.Effort);
    var authorityAfterRebase = Assert.Single(Assert.Single(
        (await context.Workouts.GetActiveAsync())!.Exercises).Sets);
    Assert.Equal(SetEffortRating.Easy, authorityAfterRebase.Effort);
}

[Fact]
public async Task Effort_conflict_cannot_rebase_against_a_completed_workout()
{
    await using var context = await SyncContext.CreateAsync();
    var active = await context.StartAsync();
    var coordinator = new ActiveWorkoutCoordinator(
        context.Workouts, context.Boundary, context.Clock);
    var saved = await coordinator.SaveSetAsync(
        active.Exercises[0].ExerciseDefinitionId,
        new LocalSet(70m, null, 12));
    await coordinator.RecordSetEffortAsync(
        active.Exercises[0].ExerciseDefinitionId,
        saved.Id,
        SetEffortRating.Productive,
        Guid.NewGuid());
    var local = (await context.Workouts.GetActiveAsync())!;
    var pending = await context.Outbox.PendingAsync();
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[0].OperationId, SyncOperationStatus.Applied, 1, null)]));
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[1].OperationId, SyncOperationStatus.Applied, 2, null)]));
    context.Api.PushResponses.Enqueue(new SyncPushResponse([
        new(pending[2].OperationId, SyncOperationStatus.Conflict, 4,
            BusinessErrorCode.VersionConflict)]));
    var completedAuthority = ServerGraph(local, 4) with
    {
        Status = (int)WorkoutStatus.Completed,
        CompletedAt = context.Clock.UtcNow.AddMinutes(1)
    };
    context.Api.PullResponses.Enqueue(new SyncPullResponse([
        Change(completedAuthority, 1)
    ], "completed-effort-conflict-cursor", false));
    await context.Coordinator.RunOnceAsync();
    var conflict = Assert.Single(await context.Outbox.ConflictedAsync());

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        new ConflictResolution(context.Coordinator)
            .ApplyLocalAgainstVersionAsync(conflict.OperationId, 4));
}
```

Change `ServerGraph` to pass `set.Effort` as the final `SyncSetDto` argument. Add this cache fact to `OfflineWorkoutAcceptanceTests` and add `using System.Text.Json;`, `using Microsoft.Data.Sqlite;`, `using TrackZ.Contracts.Workouts;`, and `using TrackZ.Domain.Workouts;`:

```csharp
[Fact]
public async Task Exercise_history_cache_round_trips_effort_and_accepts_legacy_json()
{
    var path = Path.Combine(
        Path.GetTempPath(), $"trackz-effort-cache-{Guid.NewGuid():N}.db");
    try
    {
        var exerciseId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);
        var cache = new ExerciseHistoryCache(path);
        var session = new ExerciseHistorySessionDto(
            Guid.NewGuid(),
            now,
            TrackingMode.Weighted,
            700m,
            [new WorkoutSetDto(
                Guid.NewGuid(), 0, 70m, null, 10, now, null,
                SetEffortRating.Easy)]);
        await cache.ReplaceAsync(exerciseId, session);
        Assert.Equal(
            SetEffortRating.Easy,
            Assert.Single((await cache.GetMostRecentAsync(exerciseId))!.Sets).Effort);

        var legacy = JsonSerializer.Serialize(new
        {
            workoutId = session.WorkoutId,
            completedAt = session.CompletedAt,
            trackingMode = session.TrackingMode,
            weightedVolumeKg = session.WeightedVolumeKg,
            sets = session.Sets.Select(set => new
            {
                id = set.Id,
                order = set.Order,
                weightKg = set.WeightKg,
                assistedKg = set.AssistedKg,
                reps = set.Reps,
                completedAt = set.CompletedAt,
                updatedAt = set.UpdatedAt
            })
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var connection = new SqliteConnection(
            $"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PreviousExerciseSession
            SET Payload = $payload
            WHERE ExerciseDefinitionId = $id;
            """;
        command.Parameters.AddWithValue("$payload", legacy);
        command.Parameters.AddWithValue("$id", exerciseId.ToString("D"));
        await command.ExecuteNonQueryAsync();

        Assert.Null(
            Assert.Single((await cache.GetMostRecentAsync(exerciseId))!.Sets).Effort);
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var candidate = path + suffix;
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }
}
```

- [ ] **Step 2: Run focused mutation/sync tests and confirm failure**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~ActiveWorkoutCoordinatorTests|FullyQualifiedName~SyncCoordinatorTests|FullyQualifiedName~OfflineWorkoutAcceptanceTests" -m:1
```

Expected: FAIL because operation 11, coordinator mutation, and effort graph mapping are missing.

- [ ] **Step 3: Add the operation enum and exact payload**

Append without renumbering existing values:

```csharp
public enum OutboxOperationType
{
    StartWorkout = 1,
    SaveSet = 2,
    CompleteWorkout = 3,
    EditSet = 4,
    DeleteSet = 5,
    DeleteWorkout = 6,
    ReorderExercises = 7,
    AddExercise = 8,
    RemoveExercise = 9,
    DeleteWorkoutExercise = 10,
    RecordSetEffort = 11
}

public sealed record RecordSetEffortOutboxPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid SetId,
    int Effort,
    DateTimeOffset RecordedAt);
```

`SyncCoordinator.ToDto` already uses `operation.Type.ToString()`, so this serializes to the exact server action `RecordSetEffort` without a separate action map.

- [ ] **Step 4: Implement the atomic local mutation**

Create the narrow boundary:

```csharp
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public sealed class SetEffortRecordingUnavailableException()
    : InvalidOperationException(
        "The saved set is no longer available in an active workout.");

public interface ISetEffortRecorder
{
    Task<LocalSet> RecordSetEffortAsync(
        Guid exerciseDefinitionId,
        Guid setId,
        SetEffortRating effort,
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<LocalWorkout?> RestoreActiveAsync(
        CancellationToken cancellationToken = default);
}
```

Declare `ActiveWorkoutCoordinator : ISetEffortRecorder`, add `using TrackZ.Domain.Workouts;`, and implement the method under the existing mutation/session gates:

```csharp
public async Task<LocalSet> RecordSetEffortAsync(
    Guid exerciseDefinitionId,
    Guid setId,
    SetEffortRating effort,
    Guid operationId,
    CancellationToken cancellationToken = default)
{
    if (exerciseDefinitionId == Guid.Empty)
        throw new ArgumentException("Exercise ID cannot be empty.", nameof(exerciseDefinitionId));
    if (setId == Guid.Empty)
        throw new ArgumentException("Set ID cannot be empty.", nameof(setId));
    if (operationId == Guid.Empty)
        throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
    if (!Enum.IsDefined(effort))
        throw new ArgumentOutOfRangeException(nameof(effort));

    var generation = sessionBoundary.Capture();
    await _mutationGate.WaitAsync(cancellationToken);
    try
    {
        LocalSet? rated = null;
        var enqueued = false;
        var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
        {
            var active = await workouts.GetActiveAsync(token)
                ?? throw new SetEffortRecordingUnavailableException();
            var exercise = active.Exercises.SingleOrDefault(item =>
                item.DeletedAt is null
                && item.ExerciseDefinitionId == exerciseDefinitionId)
                ?? throw new SetEffortRecordingUnavailableException();
            var set = exercise.Sets.SingleOrDefault(item =>
                item.DeletedAt is null && item.Id == setId)
                ?? throw new SetEffortRecordingUnavailableException();

            if (await workouts.GetOperationAsync(operationId, token) is { } replay)
            {
                var replayPayload = replay.Type == OutboxOperationType.RecordSetEffort
                    ? replay.DeserializePayload<RecordSetEffortOutboxPayload>()
                    : null;
                if (replay.EntityId != active.Id
                    || replay.NeutralizedAt is not null
                    || replayPayload is null
                    || replayPayload.WorkoutId != active.Id
                    || replayPayload.WorkoutExerciseId != exercise.Id
                    || replayPayload.SetId != set.Id
                    || replayPayload.Effort != (int)effort
                    || set.Effort != effort)
                    throw new InvalidDataException(
                        "The effort operation is bound to another intent.");
                rated = set;
                return;
            }

            if (set.Effort == effort)
            {
                rated = set;
                return;
            }

            var recordedAt = await NextMutationAtAsync(active, token);
            var updatedSet = set with
            {
                Effort = effort,
                UpdatedAt = recordedAt,
                Version = set.Version + 1
            };
            rated = updatedSet;
            var updatedExercise = exercise with
            {
                Sets = exercise.Sets
                    .Select(item => item.Id == set.Id ? updatedSet : item)
                    .ToArray(),
                Version = exercise.Version + 1
            };
            var updatedWorkout = active with
            {
                Exercises = active.Exercises
                    .Select(item => item.Id == exercise.Id ? updatedExercise : item)
                    .ToArray(),
                Version = active.Version + 1
            };
            var payload = new RecordSetEffortOutboxPayload(
                active.Id,
                exercise.Id,
                set.Id,
                (int)effort,
                recordedAt);
            var operation = OutboxOperation.Create(
                operationId,
                active.Id,
                OutboxOperationType.RecordSetEffort,
                payload,
                active.Version,
                recordedAt);
            await workouts.SaveWorkoutAndEnqueueAsync(
                updatedWorkout, operation, token);
            enqueued = true;
        }, cancellationToken);

        EnsureCurrent(committed, generation, cancellationToken);
        if (enqueued) syncTrigger?.NotifyMutation();
        return rated ?? throw new InvalidOperationException(
            "The effort rating was not saved.");
    }
    finally
    {
        _mutationGate.Release();
    }
}
```

The set's original `OperationId` remains the `SaveSet` operation ID; the separate effort operation ID lives only in the outbox payload/row. This preserves the existing local-set identity contract.

- [ ] **Step 5: Carry effort through sync and cache paths**

Add `using TrackZ.Domain.Workouts;` to `SyncCoordinator.cs`, then in `SyncCoordinator`:

- add this conflict-rebase arm:

```csharp
OutboxOperationType.RecordSetEffort =>
    RebaseRecordSetEffort(original, server, mutationAt),
```

- add this exact helper:

```csharp
private static RecordSetEffortOutboxPayload RebaseRecordSetEffort(
    OutboxOperation original,
    SyncWorkoutDto server,
    DateTimeOffset mutationAt)
{
    var payload = original.DeserializePayload<RecordSetEffortOutboxPayload>();
    if (payload.WorkoutId != server.Id
        || server.Status != (int)WorkoutStatus.Active
        || server.DeletedAt is not null
        || !Enum.IsDefined((SetEffortRating)payload.Effort))
        throw new InvalidOperationException(
            "The server workout cannot accept this effort rating.");
    var exercise = RequiredActiveExercise(server, payload.WorkoutExerciseId);
    _ = exercise.Sets.SingleOrDefault(set =>
            set.Id == payload.SetId && set.DeletedAt is null)
        ?? throw new InvalidOperationException(
            "The server set can no longer record effort.");
    return payload with { RecordedAt = mutationAt };
}
```

- add `Effort` to authoritative `LocalSet` insert/update SQL and `ValidateGraph`;
- keep server effort authoritative on permanent rejection/conflict while preserving `WeightKg`, `AssistedKg`, and `Reps` from the same authoritative graph.

Replace the set SQL inside `ApplyGraphAsync` with:

```sql
INSERT INTO LocalSet
    (Id, OperationId, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg,
     Reps, Effort, CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion)
VALUES ($id, $id, $exerciseId, $order, $weight, $assisted,
        $reps, $effort, $completedAt, $updatedAt, $deletedAt, $version, $version)
ON CONFLICT(Id) DO UPDATE SET
    SortOrder = excluded.SortOrder, WeightKg = excluded.WeightKg,
    AssistedKg = excluded.AssistedKg, Reps = excluded.Reps,
    Effort = excluded.Effort,
    CompletedAt = excluded.CompletedAt, UpdatedAt = excluded.UpdatedAt,
    DeletedAt = excluded.DeletedAt, Version = excluded.Version,
    BaseVersion = excluded.BaseVersion;
```

Add this parameter beside `$reps`:

```csharp
("$effort", set.Effort is null ? null : (int)set.Effort.Value),
```

Add this term to the existing malformed-set condition in `ValidateGraph`:

```csharp
|| set.Effort is { } effort && !Enum.IsDefined(effort)
```

Add `using TrackZ.Domain.Workouts;` to `ExerciseHistoryCache`, then pass the optional observation in `MostRecentLocal`:

```csharp
new WorkoutSetDto(
    set.Id,
    set.Order,
    set.WeightKg,
    set.AssistedKg,
    set.Reps,
    set.CompletedAt,
    set.UpdatedAt,
    set.Effort)
```

and reject only defined-value violations:

```csharp
if (set.Effort is { } effort && !Enum.IsDefined(effort))
    throw new InvalidDataException("Cached set effort is invalid.");
```

Old web-default JSON omits the trailing constructor parameter and therefore deserializes it as `null`; do not write a cache migration or substitute a default enum value.

- [ ] **Step 6: Run local mutation, sync, cache, and offline tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~ActiveWorkoutCoordinatorTests|FullyQualifiedName~SyncCoordinatorTests|FullyQualifiedName~OfflineWorkoutAcceptanceTests|FullyQualifiedName~SetLoggerViewModelTests" -m:1
```

Expected: PASS. Pending order survives restart, duplicate delivery is harmless, pull/rebase/cache preserve effort, and all failure cases leave the original set intact.

- [ ] **Step 7: Commit mobile effort sync**

```bash
git add src/TrackZ.Mobile.Core/Sync/OutboxOperation.cs src/TrackZ.Mobile.Core/Features/Workout/ISetEffortRecorder.cs src/TrackZ.Mobile.Core/Features/Workout/ActiveWorkoutCoordinator.cs src/TrackZ.Mobile.Core/Sync/SyncCoordinator.cs src/TrackZ.Mobile.Core/Features/Workout/ExerciseHistoryCache.cs tests/TrackZ.Mobile.Tests/Workout/ActiveWorkoutCoordinatorTests.cs tests/TrackZ.Mobile.Tests/Sync/SyncCoordinatorTests.cs tests/TrackZ.Mobile.Tests/Acceptance/OfflineWorkoutAcceptanceTests.cs
git commit -m "feat: queue effort after durable set saves"
```

### Task 6: Device-local progression increments and previous-workout reference selection

**Files:**

- Create: `src/TrackZ.Mobile.Core/Features/Workout/ExerciseGuidancePreferenceStore.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Workout/PreviousWorkoutReferenceSelector.cs`
- Create: `tests/TrackZ.Mobile.Tests/Workout/ExerciseGuidancePreferenceStoreTests.cs`
- Create: `tests/TrackZ.Mobile.Tests/Workout/PreviousWorkoutReferenceSelectorTests.cs`
- Modify: `src/TrackZ.Mobile/Features/Exercises/Services/MauiExerciseServices.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs`

**Interfaces:**

- Consumes: existing `IWorkoutPreferenceStore`, `IWeightUnitPreference`, `WorkoutSetDto.Effort`, and account private-data cleanup.
- Produces: `IExerciseGuidancePreferenceStore`; `ExerciseGuidancePreferenceStore`; `WeightUnitConversion`; `PreviousWorkoutReference`; `PreviousWorkoutReferenceSelector.Select(ExerciseHistorySessionDto?)`.

- [ ] **Step 1: Write failing preference tests**

Create `ExerciseGuidancePreferenceStoreTests.cs`:

```csharp
using System.Globalization;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class ExerciseGuidancePreferenceStoreTests
{
    [Fact]
    public void Stores_each_increment_in_canonical_kilograms_and_survives_unit_changes()
    {
        var raw = new MemoryWorkoutPreferenceStore();
        var store = new ExerciseGuidancePreferenceStore(raw);
        var exerciseId = Guid.NewGuid();
        var canonical = WeightUnitConversion.ToKilograms(5m, WeightDisplayUnit.Pounds);

        store.SetIncrementKg(exerciseId, canonical);

        Assert.Equal(2.268m, store.GetIncrementKg(exerciseId));
        Assert.Equal(5.00m, WeightUnitConversion.FromKilograms(
            store.GetIncrementKg(exerciseId)!.Value, WeightDisplayUnit.Pounds));
        Assert.Equal(2.268m, WeightUnitConversion.FromKilograms(
            store.GetIncrementKg(exerciseId)!.Value, WeightDisplayUnit.Kilograms));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0001")]
    [InlineData("100000")]
    public void Rejects_invalid_canonical_increments(string value)
    {
        var store = new ExerciseGuidancePreferenceStore(new MemoryWorkoutPreferenceStore());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.SetIncrementKg(Guid.NewGuid(), decimal.Parse(value, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Invalid_json_is_discarded_and_account_clear_removes_all_exercise_values()
    {
        var raw = new MemoryWorkoutPreferenceStore();
        raw.Set(ExerciseGuidancePreferenceStore.PreferenceKey, "not-json");
        var store = new ExerciseGuidancePreferenceStore(raw);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.Null(store.GetIncrementKg(first));
        store.SetIncrementKg(first, 2.5m);
        store.SetIncrementKg(second, 1.25m);
        store.Clear();

        Assert.Null(store.GetIncrementKg(first));
        Assert.Null(store.GetIncrementKg(second));
        Assert.Equal("{}", raw.Get(ExerciseGuidancePreferenceStore.PreferenceKey));
    }

    private sealed class MemoryWorkoutPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }
}
```

- [ ] **Step 2: Write failing deterministic reference-selection tests**

Create `PreviousWorkoutReferenceSelectorTests.cs` with one session helper and these exact expectations:

```csharp
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class PreviousWorkoutReferenceSelectorTests
{
[Fact]
public void Weighted_selects_heaviest_eligible_set_then_reps_then_later_order()
{
    var selected = PreviousWorkoutReferenceSelector.Select(Session(
        TrackingMode.Weighted,
        Set(0, 80m, null, 7, SetEffortRating.Easy),
        Set(1, 70m, null, 10, SetEffortRating.Productive),
        Set(2, 70m, null, 12, SetEffortRating.Easy),
        Set(3, 75m, null, 8, SetEffortRating.TooHeavy),
        Set(4, 70m, null, 12, null)));

    Assert.NotNull(selected);
    Assert.Equal(70m, selected!.WeightKg);
    Assert.Equal(12, selected.Reps);
    Assert.Equal(4, selected.Order);
    Assert.False(selected.HasRatedEffort);
}

[Fact]
public void Assisted_selects_least_assistance_and_bodyweight_selects_most_reps()
{
    var assisted = PreviousWorkoutReferenceSelector.Select(Session(
        TrackingMode.Assisted,
        Set(0, null, 30m, 12, SetEffortRating.Productive),
        Set(1, null, 25m, 9, SetEffortRating.Easy)));
    var bodyweight = PreviousWorkoutReferenceSelector.Select(Session(
        TrackingMode.Bodyweight,
        Set(0, null, null, 9, SetEffortRating.Easy),
        Set(1, null, null, 12, SetEffortRating.Productive)));

    Assert.Equal(25m, assisted!.AssistedKg);
    Assert.Equal(12, bodyweight!.Reps);
}

[Fact]
public void Returns_null_when_no_active_set_is_in_range_or_all_are_too_heavy()
{
    var selected = PreviousWorkoutReferenceSelector.Select(Session(
        TrackingMode.Weighted,
        Set(0, 70m, null, 7, SetEffortRating.Easy),
        Set(1, 80m, null, 8, SetEffortRating.TooHeavy),
        Set(2, 60m, null, 13, null)));

    Assert.Null(selected);
}

private static readonly DateTimeOffset CompletedAt =
    new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

private static ExerciseHistorySessionDto Session(
    TrackingMode mode,
    params WorkoutSetDto[] sets) =>
    new(Guid.NewGuid(), CompletedAt, mode, 0m, sets);

private static WorkoutSetDto Set(
    int order,
    decimal? weightKg,
    decimal? assistedKg,
    int reps,
    SetEffortRating? effort) =>
    new(Guid.NewGuid(), order, weightKg, assistedKg, reps,
        CompletedAt.AddMinutes(order), null, effort);

[Fact]
public void Eight_and_twelve_are_inclusive_but_seven_and_thirteen_are_not()
{
    var selected = PreviousWorkoutReferenceSelector.Select(Session(
        TrackingMode.Weighted,
        Set(0, 90m, null, 7, SetEffortRating.Easy),
        Set(1, 70m, null, 8, SetEffortRating.Easy),
        Set(2, 65m, null, 12, SetEffortRating.Productive),
        Set(3, 100m, null, 13, SetEffortRating.Easy)));
    Assert.Equal(70m, selected!.WeightKg);
    Assert.Equal(8, selected.Reps);
}

[Fact]
public void Invalid_measurement_shape_is_never_selected()
{
    var selected = PreviousWorkoutReferenceSelector.Select(Session(
        TrackingMode.Weighted,
        Set(0, null, 25m, 10, SetEffortRating.Easy)));
    Assert.Null(selected);
}

[Fact]
public void Exact_tie_uses_later_set_order()
{
    var selected = PreviousWorkoutReferenceSelector.Select(Session(
        TrackingMode.Weighted,
        Set(0, 70m, null, 10, SetEffortRating.Easy),
        Set(1, 70m, null, 10, SetEffortRating.Productive)));
    Assert.Equal(1, selected!.Order);
}
}
```

- [ ] **Step 3: Run the focused tests and confirm failure**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseGuidancePreferenceStoreTests|FullyQualifiedName~PreviousWorkoutReferenceSelectorTests" -m:1
```

Expected: FAIL because neither service exists.

- [ ] **Step 4: Implement validated single-key preference storage**

Create these contracts and conversion behavior:

```csharp
using System.Globalization;
using System.Text.Json;
using TrackZ.Domain.Workouts;

namespace TrackZ.Mobile.Features.Workout;

public interface IExerciseGuidancePreferenceStore
{
    decimal? GetIncrementKg(Guid exerciseDefinitionId);
    void SetIncrementKg(Guid exerciseDefinitionId, decimal incrementKg);
    void Clear();
}

public static class WeightUnitConversion
{
    private const decimal PoundsPerKilogram = 2.204622621848775807m;

    public static decimal ToKilograms(decimal value, WeightDisplayUnit unit) =>
        unit == WeightDisplayUnit.Kilograms
            ? ValidateAndRound(value)
            : ValidateAndRound(value / PoundsPerKilogram);

    public static decimal FromKilograms(decimal kilograms, WeightDisplayUnit unit) =>
        unit == WeightDisplayUnit.Kilograms
            ? kilograms
            : decimal.Round(kilograms * PoundsPerKilogram, 2, MidpointRounding.AwayFromZero);

    private static decimal ValidateAndRound(decimal value)
    {
        if (value <= 0m) throw new ArgumentOutOfRangeException(nameof(value));
        return decimal.Round(value, SetMeasurement.MaximumKilogramScale, MidpointRounding.AwayFromZero);
    }
}

public sealed class ExerciseGuidancePreferenceStore(IWorkoutPreferenceStore store)
    : IExerciseGuidancePreferenceStore
{
    public const string PreferenceKey =
        "trackz_hypertrophy_progression_increments_v1";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public decimal? GetIncrementKg(Guid exerciseDefinitionId)
    {
        if (exerciseDefinitionId == Guid.Empty)
            throw new ArgumentException(
                "Exercise ID cannot be empty.", nameof(exerciseDefinitionId));
        var values = ReadValidOrClear();
        return values.TryGetValue(exerciseDefinitionId.ToString("D"), out var value)
            ? Parse(value)
            : null;
    }

    public void SetIncrementKg(Guid exerciseDefinitionId, decimal incrementKg)
    {
        if (exerciseDefinitionId == Guid.Empty)
            throw new ArgumentException(
                "Exercise ID cannot be empty.", nameof(exerciseDefinitionId));
        if (!Valid(incrementKg))
            throw new ArgumentOutOfRangeException(nameof(incrementKg));
        var values = ReadValidOrClear();
        values[exerciseDefinitionId.ToString("D")] =
            incrementKg.ToString("0.###", CultureInfo.InvariantCulture);
        store.Set(PreferenceKey, JsonSerializer.Serialize(values, JsonOptions));
    }

    public void Clear() => store.Set(PreferenceKey, "{}");

    private Dictionary<string, string> ReadValidOrClear()
    {
        try
        {
            var raw = store.Get(PreferenceKey);
            var values = string.IsNullOrWhiteSpace(raw)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, string>>(raw, JsonOptions)
                    ?? throw new JsonException("Increment preference is null.");
            foreach (var (key, value) in values)
            {
                if (!Guid.TryParseExact(key, "D", out var id)
                    || id == Guid.Empty
                    || !string.Equals(key, id.ToString("D"), StringComparison.Ordinal)
                    || Parse(value) is null)
                    throw new InvalidDataException("Increment preference is invalid.");
            }
            return values;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException
                or FormatException or OverflowException)
        {
            Clear();
            return [];
        }
    }

    private static decimal? Parse(string? value) =>
        decimal.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed)
        && Valid(parsed)
            ? parsed
            : null;

    private static bool Valid(decimal value) =>
        value > 0m
        && value <= SetMeasurement.MaximumKilograms
        && DecimalScale(value) <= SetMeasurement.MaximumKilogramScale;

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0xff;
}
```

Keep the contracts, converter, and store in this one file under the namespace shown above. The class must never call platform `Preferences.Clear()`; writing exactly `{}` leaves weight, language, and unrelated account preferences untouched.

- [ ] **Step 5: Implement the reference selector**

Create:

```csharp
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Mobile.Features.Workout;

public sealed record PreviousWorkoutReference(
    TrackingMode TrackingMode,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    int Order,
    bool HasRatedEffort);

public static class PreviousWorkoutReferenceSelector
{
    public static PreviousWorkoutReference? Select(ExerciseHistorySessionDto? session)
    {
        if (session is null || !Enum.IsDefined(session.TrackingMode)) return null;
        var eligible = session.Sets
            .Where(set => set.Reps is >= 8 and <= 12)
            .Where(set => set.Effort != SetEffortRating.TooHeavy)
            .Where(set => set.Effort is null || Enum.IsDefined(set.Effort.Value))
            .Where(set => ValidForMode(session.TrackingMode, set))
            .ToArray();
        var selected = session.TrackingMode switch
        {
            TrackingMode.Weighted => eligible
                .OrderByDescending(set => set.WeightKg)
                .ThenByDescending(set => set.Reps)
                .ThenByDescending(set => set.Order)
                .FirstOrDefault(),
            TrackingMode.Assisted => eligible
                .OrderBy(set => set.AssistedKg)
                .ThenByDescending(set => set.Reps)
                .ThenByDescending(set => set.Order)
                .FirstOrDefault(),
            TrackingMode.Bodyweight => eligible
                .OrderByDescending(set => set.Reps)
                .ThenByDescending(set => set.Order)
                .FirstOrDefault(),
            _ => null
        };
        return selected is null ? null : new(
            session.TrackingMode,
            selected.WeightKg,
            selected.AssistedKg,
            selected.Reps,
            selected.Order,
            selected.Effort is not null);
    }

    private static bool ValidForMode(TrackingMode mode, WorkoutSetDto set) =>
        set.Id != Guid.Empty
        && set.Order >= 0
        && set.CompletedAt != default
        && (mode switch
        {
            TrackingMode.Weighted => Representable(set.WeightKg)
                && set.AssistedKg is null,
            TrackingMode.Assisted => set.WeightKg is null
                && Representable(set.AssistedKg),
            TrackingMode.Bodyweight => set.WeightKg is null
                && set.AssistedKg is null,
            _ => false
        });

    private static bool Representable(decimal? value) =>
        value is { } kilograms
        && kilograms is >= SetMeasurement.MinimumKilograms
            and <= SetMeasurement.MaximumKilograms
        && ((decimal.GetBits(kilograms)[3] >> 16) & 0xff)
            <= SetMeasurement.MaximumKilogramScale;
}
```

- [ ] **Step 6: Register and clear account-local preferences**

In `MauiProgram.cs`, register:

```csharp
builder.Services.AddSingleton<IExerciseGuidancePreferenceStore, ExerciseGuidancePreferenceStore>();
```

Inject `IExerciseGuidancePreferenceStore guidance` into `MauiPrivateDataCleaner`. Add a fifth guarded cleanup block:

```csharp
try
{
    guidance.Clear();
}
catch (Exception exception)
{
    failures.Add(exception);
}
```

Extend `MauiCompositionTests` to resolve the service twice and assert singleton identity, then execute private-data cleanup and assert the value is gone while the weight-unit preference remains.

- [ ] **Step 7: Run preference, selector, and composition tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseGuidancePreferenceStoreTests|FullyQualifiedName~PreviousWorkoutReferenceSelectorTests|FullyQualifiedName~MauiCompositionTests" -m:1
```

Expected: PASS; invalid state self-discards, unit display round-trips, account cleanup is scoped, and reference selection follows every tie-break.

- [ ] **Step 8: Commit preference and reference logic**

```bash
git add src/TrackZ.Mobile.Core/Features/Workout/ExerciseGuidancePreferenceStore.cs src/TrackZ.Mobile.Core/Features/Workout/PreviousWorkoutReferenceSelector.cs src/TrackZ.Mobile/Features/Exercises/Services/MauiExerciseServices.cs src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/Workout/ExerciseGuidancePreferenceStoreTests.cs tests/TrackZ.Mobile.Tests/Workout/PreviousWorkoutReferenceSelectorTests.cs tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs
git commit -m "feat: add local increments and prior workout references"
```

### Task 7: Set Logger reference card, post-feedback event, and explicit draft application

**Files:**

- Create: `src/TrackZ.Mobile.Core/Features/Workout/SetEffortPromptRequest.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Modify: `src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml`
- Modify: `tests/TrackZ.Mobile.Tests/Workout/SetLoggerViewModelTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/TrackSetsVisualContractTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs`

**Interfaces:**

- Consumes: selector/policy result from Tasks 1 and 6 plus existing durable-save/feedback lifecycle.
- Produces: `SetEffortPromptRequest`; `SetLoggerViewModel.EffortPromptRequested`; reference-card properties; `TryApplyGuidanceToNextDraft(HypertrophyGuidanceResult)`.

- [ ] **Step 1: Add failing reference-card and save-lifecycle tests**

Add to `SetLoggerViewModelTests`:

```csharp
[Fact]
public async Task Load_exposes_selected_previous_workout_reference()
{
    var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
    var sut = fixture.CreateLogger(Previous(
        TrackingMode.Weighted,
        Set(0, 70m, null, 10, SetEffortRating.Productive),
        Set(1, 75m, null, 7, SetEffortRating.Easy)));

    await sut.LoadAsync(ExerciseId, "Bench Press");

    Assert.True(sut.HasPreviousReference);
    Assert.Equal("70 kg", sut.PreviousReferenceLoad);
    Assert.Equal("10 reps", sut.PreviousReferenceReps);
    Assert.Equal("Heaviest set in the 8–12 rep range", sut.PreviousReferenceReason);
}

[Fact]
public async Task Effort_prompt_is_raised_once_only_after_durable_save_and_feedback()
{
    var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
    var sequence = new List<string>();
    fixture.Feedback.OnSaved = () =>
    {
        sequence.Add("feedback");
        return Task.CompletedTask;
    };
    var sut = fixture.CreateLogger(null);
    await sut.LoadAsync(ExerciseId, "Bench Press");
    sut.BeginSetCommand.Execute(null);
    sut.WeightKg = 70m;
    sut.Reps = 10;
    SetEffortPromptRequest? request = null;
    sut.EffortPromptRequested += (_, eventArgs) =>
    {
        sequence.Add("prompt");
        request = eventArgs.Request;
    };

    await sut.SaveDraftSetCommand.ExecuteAsync();

    Assert.Equal(["feedback", "prompt"], sequence);
    Assert.NotNull(request);
    Assert.Equal(Assert.Single(sut.TodaySets).Id, request!.SavedSet.Id);
    Assert.NotNull(await fixture.Repository.GetOperationAsync(request.SavedSet.OperationId, default));
    Assert.NotEqual(Guid.Empty, request.EffortOperationId);
}

[Fact]
public async Task Failed_save_and_reload_never_raise_effort_prompt()
{
    var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
    await CreateSaveFailureTriggerAsync();
    var sut = fixture.CreateLogger(null);
    var count = 0;
    sut.EffortPromptRequested += (_, _) => count++;
    await sut.LoadAsync(ExerciseId, "Bench Press");
    sut.BeginSetCommand.Execute(null);
    sut.WeightKg = 70m;
    sut.Reps = 10;

    await sut.SaveDraftSetCommand.ExecuteAsync();
    await sut.LoadAsync(ExerciseId, "Bench Press");

    Assert.Equal(0, count);
}

[Fact]
public async Task Applying_guidance_changes_only_a_transient_next_draft()
{
    var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
    var sut = fixture.CreateLogger(null);
    await sut.LoadAsync(ExerciseId, "Bench Press");
    var outbox = new OutboxRepository(fixture.Database);
    var outboxBefore = (await outbox.PendingAsync()).Count;
    var result = new HypertrophyGuidanceResult(
        HypertrophyGuidanceAction.Increase,
        HypertrophyGuidanceReason.TwoQualifyingSets,
        SuggestedWeightKg: 72.5m,
        SuggestedReps: 12);

    var applied = sut.TryApplyGuidanceToNextDraft(result);

    Assert.True(applied);
    Assert.True(sut.HasDraftSet);
    Assert.Equal(72.5m, sut.WeightKg);
    Assert.Equal(12, sut.Reps);
    Assert.Equal(outboxBefore, (await outbox.PendingAsync()).Count);
    Assert.Empty(sut.TodaySets);
}
```

Add these exact companion facts:

```csharp
[Fact]
public async Task Legacy_reference_is_neutral_and_ineligible_history_hides_the_card()
{
    var legacyFixture = await CreateFixtureAsync(TrackingMode.Weighted);
    var legacy = legacyFixture.CreateLogger(Previous(
        TrackingMode.Weighted,
        Set(0, 70m, null, 10)));
    await legacy.LoadAsync(ExerciseId, "Bench Press");
    Assert.True(legacy.HasPreviousReference);
    Assert.Equal("Recorded in your previous workout", legacy.PreviousReferenceReason);

    var empty = legacyFixture.CreateLogger(Previous(
        TrackingMode.Weighted,
        Set(0, 90m, null, 7, SetEffortRating.Easy),
        Set(1, 80m, null, 10, SetEffortRating.TooHeavy)));
    await empty.LoadAsync(ExerciseId, "Bench Press");
    Assert.False(empty.HasPreviousReference);
    Assert.Equal(string.Empty, empty.PreviousReferenceLoad);
    Assert.Equal(string.Empty, empty.PreviousReferenceReps);
}

[Theory]
[InlineData(TrackingMode.Assisted, null, 25, "25 kg", "10 reps")]
[InlineData(TrackingMode.Bodyweight, null, null, "10 reps", "")]
public async Task Reference_measurement_respects_tracking_mode(
    TrackingMode mode,
    double? weight,
    double? assistance,
    string expectedLoad,
    string expectedReps)
{
    var fixture = await CreateFixtureAsync(mode);
    var sut = fixture.CreateLogger(Previous(
        mode,
        Set(0, Decimal(weight), Decimal(assistance), 10, SetEffortRating.Productive)));
    await sut.LoadAsync(ExerciseId, "Exercise");
    Assert.Equal(expectedLoad, sut.PreviousReferenceLoad);
    Assert.Equal(expectedReps, sut.PreviousReferenceReps);
}

[Fact]
public async Task Non_actionable_guidance_cannot_change_the_current_draft()
{
    var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
    var sut = fixture.CreateLogger(null);
    await sut.LoadAsync(ExerciseId, "Bench Press");
    var result = new HypertrophyGuidanceResult(
        HypertrophyGuidanceAction.CollectMoreData,
        HypertrophyGuidanceReason.OneQualifyingSet);
    Assert.False(sut.TryApplyGuidanceToNextDraft(result));
    Assert.False(sut.HasDraftSet);
}

[Fact]
public async Task Reset_during_feedback_suppresses_prompt_and_subscriber_failure_cannot_fail_save()
{
    var resetFixture = await CreateFixtureAsync(TrackingMode.Weighted);
    resetFixture.Feedback.Block = true;
    var resetSut = resetFixture.CreateLogger(null);
    await resetSut.LoadAsync(ExerciseId, "Bench Press");
    resetSut.BeginSetCommand.Execute(null);
    resetSut.WeightKg = 70m;
    resetSut.Reps = 10;
    var promptCount = 0;
    resetSut.EffortPromptRequested += (_, _) => promptCount++;
    var save = resetSut.SaveDraftSetCommand.ExecuteAsync();
    await resetFixture.Feedback.Entered.Task;
    var reset = resetFixture.Boundary.ResetAsync(resetFixture.Coordinator.ClearPrivateDataAsync);
    resetFixture.Feedback.Release.TrySetResult();
    await Task.WhenAll(save, reset);
    Assert.Equal(0, promptCount);

    var throwFixture = await CreateFixtureAsync(TrackingMode.Weighted);
    var throwSut = throwFixture.CreateLogger(null);
    await throwSut.LoadAsync(ExerciseId, "Bench Press");
    throwSut.BeginSetCommand.Execute(null);
    throwSut.WeightKg = 70m;
    throwSut.Reps = 10;
    throwSut.EffortPromptRequested += (_, _) => throw new InvalidOperationException("presentation failed");
    await throwSut.SaveDraftSetCommand.ExecuteAsync();
    Assert.Single(throwSut.TodaySets);
    Assert.Null(throwSut.ErrorMessage);
}

[Fact]
public async Task Remote_refresh_updates_future_prompt_snapshot()
{
    var fixture = await CreateFixtureAsync(TrackingMode.Weighted);
    var cached = Previous(
        TrackingMode.Weighted,
        Set(0, 67.5m, null, 12, SetEffortRating.Easy));
    var refreshed = cached with
    {
        WorkoutId = Guid.NewGuid(),
        CompletedAt = cached.CompletedAt.AddDays(1),
        Sets = [Set(0, 70m, null, 12, SetEffortRating.Productive)]
    };
    var history = new GatedRemoteHistory(cached, refreshed);
    var sut = new SetLoggerViewModel(
        fixture.Coordinator,
        history,
        fixture.Feedback,
        fixture.Boundary,
        new StubConnectivity(true),
        new OutboxRepository(fixture.Database),
        WorkoutResources.English);

    await sut.LoadAsync(ExerciseId, "Bench Press");
    Assert.Equal("67.5 kg", sut.PreviousReferenceLoad);
    await history.RemoteEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    history.ReleaseRemote.TrySetResult();
    await sut.HistoryRefreshCompletion.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal("70 kg", sut.PreviousReferenceLoad);

    SetEffortPromptRequest? request = null;
    sut.EffortPromptRequested += (_, eventArgs) => request = eventArgs.Request;
    sut.BeginSetCommand.Execute(null);
    sut.WeightKg = 70m;
    sut.Reps = 10;
    await sut.SaveDraftSetCommand.ExecuteAsync();

    Assert.NotNull(request);
    Assert.Same(refreshed, request!.PreviousSession);
}
```

Extend only the existing `Set` helper with a trailing optional effort. Keep `CreateFixtureAsync`, `Fixture.CreateLogger`, `GatedRemoteHistory`, `RecordingFeedback.Entered`, and `RecordingFeedback.Release` as they are; the tests deliberately exercise the real coordinator, repository, background-refresh gate, and commands already provided by the file.

```csharp
private static WorkoutSetDto Set(
    int order,
    decimal? weight,
    decimal? assisted,
    int reps,
    SetEffortRating? effort = null) =>
    new(Guid.NewGuid(), order, weight, assisted, reps,
        Now.AddDays(-2).AddMinutes(order), null, effort);
```

- [ ] **Step 2: Add failing XAML reference-card contract**

Add these assertions to `TrackSetsVisualContractTests.Native_page_matches_the_approved_action_first_track_sets_reference` after `children` is created:

```csharp
Assert.DoesNotContain(page.Descendants(), element => Name(element) == "PreviousBestLabel");
var reference = content.Elements().Single(element =>
    Name(element) == "PreviousWorkoutReferenceCard");
var referenceLoad = reference.Descendants().Single(element =>
    Name(element) == "PreviousWorkoutReferenceLoad");
var referenceReps = reference.Descendants().Single(element =>
    Name(element) == "PreviousWorkoutReferenceReps");
var referenceReason = reference.Descendants().Single(element =>
    Name(element) == "PreviousWorkoutReferenceReason");
Assert.Equal("{Binding HasPreviousReference}", reference.Attribute("IsVisible")?.Value);
Assert.Equal("{DynamicResource TrackZPerformanceNumberStyle}",
    referenceLoad.Attribute("Style")?.Value);
Assert.Equal("{Binding PreviousReferenceLoad}",
    referenceLoad.Attribute("Text")?.Value);
Assert.Equal("{DynamicResource TrackZSecondaryStyle}",
    referenceReps.Attribute("Style")?.Value);
Assert.Equal("{Binding PreviousReferenceReps}",
    referenceReps.Attribute("Text")?.Value);
Assert.Equal("{DynamicResource TrackZSecondaryStyle}",
    referenceReason.Attribute("Style")?.Value);
Assert.Equal("{Binding PreviousReferenceReason}",
    referenceReason.Attribute("Text")?.Value);
Assert.Equal(children.IndexOf(exercise) + 1, children.IndexOf(reference));
Assert.True(children.IndexOf(reference) < children.IndexOf(editor));
```

In `MauiCompositionTests.AssertInlineSetEditorTransitionAsync`, replace the `PreviousBestLabel` lookup/assertion with:

```csharp
var reference = page.FindByName<Border>("PreviousWorkoutReferenceCard");
Assert.True(content.Children.IndexOf(exercise) < content.Children.IndexOf(reference));
Assert.True(content.Children.IndexOf(reference) < content.Children.IndexOf(editor));
Assert.Equal(logger.HasPreviousReference, reference.IsVisible);
```

- [ ] **Step 3: Run focused tests and confirm failure**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~SetLoggerViewModelTests|FullyQualifiedName~TrackSetsVisualContractTests|FullyQualifiedName~MauiCompositionTests" -m:1
```

Expected: FAIL because reference state, event, draft application, and card do not exist.

- [ ] **Step 4: Add immutable prompt request types**

Create:

```csharp
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public sealed record SetEffortPromptRequest(
    Guid ExerciseDefinitionId,
    TrackingMode TrackingMode,
    LocalSet SavedSet,
    ExerciseHistorySessionDto? PreviousSession,
    Guid EffortOperationId);

public sealed class SetEffortPromptRequestedEventArgs(SetEffortPromptRequest request) : EventArgs
{
    public SetEffortPromptRequest Request { get; } =
        request ?? throw new ArgumentNullException(nameof(request));
}
```

- [ ] **Step 5: Add localized reference-card copy**

Add these RESX entries and matching `WorkoutTextSet` fields:

| Key | English | Thai |
|---|---|---|
| `PreviousWorkoutReference` | `Previous workout reference` | `ข้อมูลอ้างอิงจากครั้งก่อน` |
| `HeaviestSetInRepRange` | `Heaviest set in the 8–12 rep range` | `เซ็ตที่หนักที่สุดในช่วง 8–12 ครั้ง` |
| `LightestAssistanceInRepRange` | `Least assistance in the 8–12 rep range` | `แรงช่วยน้อยที่สุดในช่วง 8–12 ครั้ง` |
| `HighestRepsInRepRange` | `Most reps in the 8–12 rep range` | `จำนวนครั้งสูงสุดในช่วง 8–12 ครั้ง` |
| `LegacyWorkoutReference` | `Recorded in your previous workout` | `บันทึกไว้ในการฝึกครั้งก่อน` |

Add this fact to `LocalizationAuditTests` before changing runtime code:

```csharp
[Fact]
public void Previous_workout_reference_copy_is_localized_in_English_and_Thai()
{
    var english = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("en-US"));
    var thai = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

    Assert.Equal("Previous workout reference", english.PreviousWorkoutReference);
    Assert.Equal("Heaviest set in the 8–12 rep range", english.HeaviestSetInRepRange);
    Assert.Equal("ข้อมูลอ้างอิงจากครั้งก่อน", thai.PreviousWorkoutReference);
    Assert.Equal("เซ็ตที่หนักที่สุดในช่วง 8–12 ครั้ง", thai.HeaviestSetInRepRange);
}
```

- [ ] **Step 6: Populate reference state and raise the prompt after feedback**

Store the stable history snapshot loaded by `LoadAsync` in `_previousSession`. Call `PreviousWorkoutReferenceSelector.Select(previous)` and expose:

```csharp
public bool HasPreviousReference => _previousReference is not null;
public string PreviousReferenceTitle => _text.PreviousWorkoutReference;
public string PreviousReferenceLoad => _previousReference is null
    ? string.Empty
    : _previousReference.TrackingMode switch
    {
        TrackingMode.Weighted =>
            $"{MeasurementValue(_previousReference.WeightKg)} {WeightUnitLabel}",
        TrackingMode.Assisted =>
            $"{MeasurementValue(_previousReference.AssistedKg)} {WeightUnitLabel}",
        TrackingMode.Bodyweight => $"{_previousReference.Reps} {_text.Reps}",
        _ => string.Empty
    };
public string PreviousReferenceReps => _previousReference is
    { TrackingMode: TrackingMode.Weighted or TrackingMode.Assisted }
        ? $"{_previousReference.Reps} {_text.Reps}"
        : string.Empty;
public string PreviousReferenceReason => _previousReference switch
{
    null => string.Empty,
    { HasRatedEffort: false } => _text.LegacyWorkoutReference,
    { TrackingMode: TrackingMode.Weighted } => _text.HeaviestSetInRepRange,
    { TrackingMode: TrackingMode.Assisted } => _text.LightestAssistanceInRepRange,
    _ => _text.HighestRepsInRepRange
};
```

Use one helper for cached load and successful background refresh:

```csharp
private void ApplyPreviousSession(ExerciseHistorySessionDto? session)
{
    _previousSession = session;
    _previousReference = PreviousWorkoutReferenceSelector.Select(session);
    PublishPreviousReferenceState();
}

private void PublishPreviousReferenceState()
{
    OnPropertyChanged(nameof(HasPreviousReference));
    OnPropertyChanged(nameof(PreviousReferenceTitle));
    OnPropertyChanged(nameof(PreviousReferenceLoad));
    OnPropertyChanged(nameof(PreviousReferenceReps));
    OnPropertyChanged(nameof(PreviousReferenceReason));
}
```

Call `ApplyPreviousSession(previous)` in the guarded initial-load commit. In `RefreshHistoryInBackgroundAsync`, after all existing generation/workout/mode/newer-timestamp guards pass, replace `LastSets`, set `_lastHistoryCompletedAt`, then call `ApplyPreviousSession(refreshed)` before the existing presentation/context notifications. A request already raised keeps its immutable session record; only prompts raised after that guarded refresh capture the new `_previousSession`. Call `PublishPreviousReferenceState()` after display-unit changes. Clear both fields and publish their empty state in every existing private-state/session-reset path.

Declare:

```csharp
public event EventHandler<SetEffortPromptRequestedEventArgs>? EffortPromptRequested;
```

In `CompleteSetAsync`, after the awaited feedback block and generation checks, schedule existing best-effort sync as today, then invoke exactly once:

```csharp
var request = new SetEffortPromptRequest(
    _exerciseId,
    TrackingMode,
    saved,
    _previousSession,
    Guid.NewGuid());
try
{
    EffortPromptRequested?.Invoke(this, new(request));
}
catch
{
    // A presentation subscriber cannot invalidate the durable set save.
}
```

Do not invoke on validation failure, coordinator failure, cancellation, reset, load/restore, or navigation. Do not route this through `MauiSetSavedFeedback.Saved`, whose handlers are awaited while save remains busy.

- [ ] **Step 7: Implement explicit transient draft application**

Add:

```csharp
public bool TryApplyGuidanceToNextDraft(HypertrophyGuidanceResult result)
{
    ArgumentNullException.ThrowIfNull(result);
    if (_disposed || IsBusy || result.RequiresIncrement
        || (result.SuggestedWeightKg is null
            && result.SuggestedAssistedKg is null
            && result.SuggestedReps is null))
        return false;
    if (!HasDraftSet) BeginSet();
    if (!HasDraftSet) return false;
    if (TrackingMode == TrackingMode.Weighted && result.SuggestedWeightKg is { } weight)
        WeightKg = weight;
    if (TrackingMode == TrackingMode.Assisted && result.SuggestedAssistedKg is { } assistance)
        AssistedKg = assistance;
    if (result.SuggestedReps is { } reps)
        Reps = reps;
    return IsValidMeasurement();
}
```

This method must not call a coordinator, sync runner, feedback service, or save command. `BeginSet()` supplies the current/previous measurement when a keep-result omits a load; the typed result overrides only fields it actually suggests.

- [ ] **Step 8: Render the compact reference card**

Remove `PreviousBestLabel` from `ExerciseSummary`. Insert immediately after that grid:

```xml
<Border x:Name="PreviousWorkoutReferenceCard"
        IsVisible="{Binding HasPreviousReference}"
        Style="{DynamicResource TrackZCardStyle}">
    <VerticalStackLayout Spacing="{DynamicResource TrackZSpace4}">
        <Label CharacterSpacing="1.6"
               Style="{DynamicResource TrackZPerformanceMetadataStyle}"
               Text="{Binding PreviousReferenceTitle}" />
        <Label x:Name="PreviousWorkoutReferenceLoad"
               Style="{DynamicResource TrackZPerformanceNumberStyle}"
               Text="{Binding PreviousReferenceLoad}" />
        <Label x:Name="PreviousWorkoutReferenceReps"
               Style="{DynamicResource TrackZSecondaryStyle}"
               Text="{Binding PreviousReferenceReps}" />
        <Label x:Name="PreviousWorkoutReferenceReason"
               Style="{DynamicResource TrackZSecondaryStyle}"
               Text="{Binding PreviousReferenceReason}" />
    </VerticalStackLayout>
</Border>
```

- [ ] **Step 9: Run Set Logger, localization, and visual tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~SetLoggerViewModelTests|FullyQualifiedName~TrackSetsVisualContractTests|FullyQualifiedName~LocalizationAuditTests|FullyQualifiedName~LocalizedUiScopeTests|FullyQualifiedName~MauiCompositionTests" -m:1
```

Expected: PASS. Reference selection is stable, the event follows feedback and durable save, and only explicit application opens/changes a transient draft.

- [ ] **Step 10: Commit Set Logger orchestration**

```bash
git add src/TrackZ.Mobile.Core/Features/Workout/SetEffortPromptRequest.cs src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml tests/TrackZ.Mobile.Tests/Workout/SetLoggerViewModelTests.cs tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs tests/TrackZ.Mobile.Tests/NativeIos/TrackSetsVisualContractTests.cs tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs
git commit -m "feat: prepare set logger effort guidance flow"
```

### Task 8: Effort-sheet state machine and local recommendation presentation

**Files:**

- Create: `src/TrackZ.Mobile.Core/Features/Workout/SetEffortPromptViewModel.cs`
- Create: `tests/TrackZ.Mobile.Tests/Workout/SetEffortPromptViewModelTests.cs`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs`

**Interfaces:**

- Consumes: `SetEffortPromptRequest`, `ISetEffortRecorder`, policy, local increment store, stable prior-session snapshot, and explicit draft callback.
- Produces: `SetEffortPromptState`; `SetEffortPromptViewModel.Initialize(SetEffortPromptRequest, Func<HypertrophyGuidanceResult, bool>)`; effort/increment/retry/use/skip commands; typed recommendation presentation state.

- [ ] **Step 1: Write failing state-machine tests**

Create `SetEffortPromptViewModelTests.cs` covering these exact transitions:

```csharp
using System.Globalization;
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
    await using var fixture = await Fixture.CreateAsync(
        current: CurrentSet(70m, 10));
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
    Assert.Equal(1, fixture.Recorder.Calls.Count);
    Assert.Equal(0, fixture.Recorder.SuccessfulWrites);
    await sut.RetryAsync();
    Assert.Equal(1, fixture.Recorder.Calls.Count);
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
    Assert.Equal(SetEffortRating.Productive,
        (await fixture.ReloadSavedSetAsync()).Effort);
    Assert.Equal(1, fixture.Recorder.SuccessfulWrites);
    Assert.Single(fixture.Recorder.Calls);
    await sut.RetryAsync();
    Assert.Single(fixture.Recorder.Calls);
}

[Fact]
public async Task Target_removed_while_sheet_is_open_uses_neutral_unavailable_state()
{
    await using var fixture = await Fixture.CreateAsync(
        current: CurrentSet(70m, 10));
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
```

Add this presentation matrix and snapshot/reset facts:

```csharp
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
        previous: PreviousSet(
            SetMeasurement.MaximumKilograms, 12, SetEffortRating.Easy),
        incrementKg: SetMeasurement.MinimumKilograms);
    var sut = fixture.CreateViewModel();
    sut.Initialize(fixture.Request, _ => true);

    await sut.ChooseEffortAsync(SetEffortRating.Productive);

    Assert.Equal(HypertrophyGuidanceReason.InvalidSuggestedMeasurement,
        sut.Guidance!.Reason);
    Assert.Equal(sut.Text.GuidanceKeepCurrentLoad, sut.RecommendationTitle);
    Assert.Equal(sut.Text.GuidanceHigherLoadUnavailable,
        sut.RecommendationReason);
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
    Assert.Equal(sut.Text.GuidanceKeepCurrentAssistance,
        sut.RecommendationTitle);
    Assert.Equal(sut.Text.GuidanceLowerAssistanceUnavailable,
        sut.RecommendationReason);
    Assert.False(sut.HasUseAction);
}

[Fact]
public async Task Unexpected_invalid_restored_shape_is_non_actionable_not_a_crash()
{
    await using var fixture = await Fixture.CreateAsync(
        current: CurrentSet(0m, 10));
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
```

```csharp
[Fact]
public async Task Invalid_increment_stays_in_the_same_sheet_without_writing_a_preference()
{
    await using var incrementFixture = await Fixture.CreateAsync(
        current: CurrentSet(70m, 12),
        previous: PreviousSet(70m, 12, SetEffortRating.Easy));
    var increment = incrementFixture.CreateViewModel();
    increment.Initialize(incrementFixture.Request, _ => true);
    await increment.ChooseEffortAsync(SetEffortRating.Productive);
    increment.IncrementInput = "0";
    await increment.SaveIncrementAsync();
    Assert.Equal(SetEffortPromptState.NeedsIncrement, increment.State);
    Assert.Equal(increment.Text.GuidanceIncrementInvalid, increment.IncrementValidationMessage);
    Assert.Null(incrementFixture.Preferences.GetIncrementKg(incrementFixture.ExerciseId));
}
```

Use this complete in-memory fixture in the same test file:

```csharp
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
            exerciseId, trackingMode, saved, previousSession,
            Guid.NewGuid());
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
```

The fixture's `BackgroundPreviousSession` is deliberately not consumed by the view model, proving the immutable request snapshot remains authoritative.

In `MauiCompositionTests.Real_Maui_provider_activates_the_routed_logger_graph_with_correct_lifetimes`, add this failing lifetime/alias contract after resolving the two logger pages:

```csharp
var firstPrompt = app.Services.GetRequiredService<SetEffortPromptViewModel>();
var secondPrompt = app.Services.GetRequiredService<SetEffortPromptViewModel>();

Assert.NotSame(firstPrompt, secondPrompt);
Assert.Same(
    app.Services.GetRequiredService<ActiveWorkoutCoordinator>(),
    app.Services.GetRequiredService<ISetEffortRecorder>());
```

- [ ] **Step 2: Run the focused test and confirm failure**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~SetEffortPromptViewModelTests|FullyQualifiedName~MauiCompositionTests" -m:1
```

Expected: FAIL because the sheet view model does not exist.

- [ ] **Step 3: Define the state and public surface**

Create:

```csharp
public enum SetEffortPromptState
{
    Asking = 1,
    SavingEffort = 2,
    NeedsIncrement = 3,
    Recommendation = 4,
    SaveFailed = 5,
    Unavailable = 6
}

public sealed record ProgressionIncrementOption(decimal DisplayValue, string Label);
```

These declarations are repeated at the top of the complete file listing in Step 4 for copyability; create each type only once.

Construct `SetEffortPromptViewModel` with:

```csharp
public SetEffortPromptViewModel(
    ISetEffortRecorder recorder,
    IExerciseGuidancePreferenceStore preferences,
    IWeightUnitPreference unitPreference,
    IAccountSessionBoundary boundary,
    WorkoutTextSet text)
```

Expose `WorkoutTextSet Text`, `State`, `Guidance`, `IReadOnlyList<ProgressionIncrementOption> IncrementOptions`, `IncrementInput`, `IncrementValidationMessage`, `RecommendationTitle`, `RecommendationReason`, `UseActionText`, `HasUseAction`, `CanEditIncrement`, `ConfirmsOriginalSetSaved`, state-visibility booleans including `HasSaveError` and `HasUnavailable`, `IsBusy`, the three effort commands, `SelectIncrementCommand`, `EditIncrementCommand`, `SaveIncrementCommand`, `RetryCommand`, `UseSuggestionCommand`, `SkipCommand`, `NotNowCommand`, and `event EventHandler? DismissRequested`.

Expose these methods for focused tests and command wrappers:

```csharp
public void Initialize(
    SetEffortPromptRequest request,
    Func<HypertrophyGuidanceResult, bool> applyToDraft);
public Task ChooseEffortAsync(SetEffortRating effort);
public Task RetryAsync();
public Task SaveIncrementAsync();
public Task SelectIncrementAsync(decimal displayValue);
public void EditIncrement();
public void UseSuggestion();
public void Skip();
public void NotNow();
public void Deactivate();
```

`Initialize` resets all transient fields, captures the account generation, stores the immutable request/callback, subscribes once to `boundary.SessionReset`, starts at `Asking`, and never writes data. The reset handler calls `Deactivate()` and raises `DismissRequested` once. `Deactivate` unsubscribes, cancels the lifetime, and clears request/callback/cached policy facts without retrying or reopening anything.

- [ ] **Step 4: Add complete English and Thai effort/guidance copy**

Add every row below to both RESX files and as positional fields in `WorkoutTextSet`; append one matching `Value(key, culture)` call per listed key in exactly the same order:

| Key | English | Thai |
|---|---|---|
| `EffortSheetTitle` | `Set effort` | `ความรู้สึกของเซ็ต` |
| `EffortSetSaved` | `Set saved` | `บันทึกเซ็ตแล้ว` |
| `EffortQuestion` | `How did this set feel?` | `เซ็ตนี้รู้สึกเป็นอย่างไร?` |
| `EffortGoodFormHelper` | `Answer based on the reps you could do with the same good form.` | `ตอบจากจำนวนครั้งที่ยังทำต่อได้โดยรักษาฟอร์มเดิม` |
| `EffortEasyOption` | `Too easy — many reps left` | `เบาไป — ยังไหวอีกหลายครั้ง` |
| `EffortProductiveOption` | `About right — the final reps were hard, with good form` | `กำลังดี — ช่วงท้ายเริ่มหนัก แต่ฟอร์มยังดี` |
| `EffortTooHeavyOption` | `Too heavy — missed the range or form began to break` | `หนักเกินไป — ทำไม่ถึงเป้าหรือฟอร์มเริ่มเสีย` |
| `EffortSkip` | `Not sure · skip this time` | `ไม่แน่ใจ · ข้ามครั้งนี้` |
| `EffortPainSafety` | `If you feel pain or cannot keep good form, stop this exercise.` | `ถ้ารู้สึกเจ็บหรือรักษาฟอร์มไม่ได้ ให้หยุดท่านี้` |
| `EffortSaving` | `Saving effort…` | `กำลังบันทึกความรู้สึก…` |
| `EffortSaveFailed` | `Could not save how this set felt` | `บันทึกความรู้สึกของเซ็ตไม่สำเร็จ` |
| `EffortUnavailable` | `This set can no longer be rated. Your original set is saved.` | `ไม่สามารถบันทึกความรู้สึกของเซ็ตนี้ได้แล้ว แต่เซ็ตเดิมของคุณบันทึกไว้แล้ว` |
| `EffortOriginalSetSafe` | `Your original set is already saved.` | `เซ็ตเดิมบันทึกไว้แล้ว` |
| `Retry` | `Try again` | `ลองอีกครั้ง` |
| `NotNow` | `Not now` | `ไว้ก่อน` |
| `GuidanceIncrementTitle` | `What is the smallest load change available?` | `อุปกรณ์นี้ปรับน้ำหนักทีละเท่าไร?` |
| `GuidanceIncrementHelper` | `We’ll remember it for this exercise on this device.` | `เราจะจำค่านี้สำหรับท่านี้บนอุปกรณ์เครื่องนี้` |
| `GuidanceIncrementPlaceholder` | `For example, 2.5` | `เช่น 2.5` |
| `GuidanceSaveIncrement` | `Use this increment` | `ใช้ค่านี้` |
| `GuidanceEditIncrement` | `Change equipment increment` | `เปลี่ยนค่าน้ำหนักที่อุปกรณ์ปรับได้` |
| `GuidanceIncrementInvalid` | `Enter a positive increment supported by your equipment.` | `ใส่ค่าน้ำหนักที่อุปกรณ์ปรับเพิ่มหรือลดได้จริง` |
| `GuidanceAlmostReady` | `Almost ready to increase` | `ใกล้พร้อมเพิ่มน้ำหนักแล้ว` |
| `GuidanceNeedAnotherReason` | `Confirm with one more comparable set at this load.` | `ทำให้ได้อีกหนึ่งเซ็ตที่น้ำหนักเดิมเพื่อยืนยัน` |
| `GuidanceKeepLoadFormat` | `Keep {0} {1}` | `คงน้ำหนัก {0} {1}` |
| `GuidanceTryRepsFormat` | `Try {0} reps` | `ลอง {0} ครั้ง` |
| `GuidanceTryWeightNextSetFormat` | `Try {0} {1} next set` | `เซ็ตถัดไปลอง {0} {1}` |
| `GuidanceTryAssistanceNextSetFormat` | `Try {0} {1} assistance next set` | `เซ็ตถัดไปลองแรงช่วย {0} {1}` |
| `GuidanceTwoSetsReason` | `Two comparable sets reached at least 12 reps with good form.` | `สองเซ็ตที่เทียบกันได้ทำอย่างน้อย 12 ครั้งด้วยฟอร์มที่ดี` |
| `GuidanceKeepReason` | `This load is in a productive 8–12 rep range.` | `น้ำหนักนี้อยู่ในช่วง 8–12 ครั้งที่กำลังดี` |
| `GuidanceReduceReason` | `Use a little less difficulty so you can keep good form.` | `ลดความยากลงเล็กน้อยเพื่อรักษาฟอร์มที่ดี` |
| `GuidanceLessAssistanceReason` | `Less assistance makes the exercise harder.` | `แรงช่วยน้อยลงทำให้ท่านี้ยากขึ้น` |
| `GuidanceMoreAssistanceReason` | `More assistance makes it easier to keep good form.` | `แรงช่วยมากขึ้นช่วยให้รักษาฟอร์มได้ง่ายขึ้น` |
| `GuidanceBelowRangeReason` | `This set was below the 8-rep target.` | `เซ็ตนี้ทำได้น้อยกว่าเป้าหมาย 8 ครั้ง` |
| `GuidanceReduceDifficulty` | `Reduce the difficulty next set` | `ลดความยากในเซ็ตถัดไป` |
| `GuidanceBodyweightComplete` | `You completed the 8–12 rep range with good form.` | `คุณทำครบช่วง 8–12 ครั้งด้วยฟอร์มที่ดีแล้ว` |
| `GuidanceNoSuggestion` | `Keep this set as recorded` | `คงเซ็ตนี้ตามที่บันทึกไว้` |
| `GuidanceNoSuggestionReason` | `We could not calculate a reliable next-set suggestion from this set.` | `ยังคำนวณคำแนะนำสำหรับเซ็ตถัดไปจากเซ็ตนี้ได้ไม่มั่นใจ` |
| `GuidanceKeepCurrentLoad` | `Keep your current load` | `คงน้ำหนักปัจจุบันไว้` |
| `GuidanceHigherLoadUnavailable` | `A higher supported load could not be suggested.` | `ยังไม่สามารถแนะนำน้ำหนักที่สูงขึ้นและรองรับได้` |
| `GuidanceKeepCurrentAssistance` | `Keep your current assistance` | `คงแรงช่วยปัจจุบันไว้` |
| `GuidanceLowerAssistanceUnavailable` | `Less supported assistance could not be suggested.` | `ยังไม่สามารถแนะนำแรงช่วยที่น้อยลงและรองรับได้` |
| `GuidanceUseWeightFormat` | `Use {0} {1} for the next set` | `ใช้ {0} {1} ในเซ็ตถัดไป` |
| `GuidanceUseAssistanceFormat` | `Use {0} {1} assistance for the next set` | `ใช้แรงช่วย {0} {1} ในเซ็ตถัดไป` |
| `GuidanceUseRepsFormat` | `Use {0} reps for the next set` | `ใช้เป้าหมาย {0} ครั้งในเซ็ตถัดไป` |

Extend `LocalizationAuditTests` to assert key parity and exact Thai/English values for the three effort options, pain copy, the neutral unavailable message, “less assistance,” “more assistance,” the non-actionable fallback, the unsupported-higher-load message, and the unsupported-lower-assistance message. No user-facing fallback literal belongs in C# or XAML.

Use the following complete implementation for `SetEffortPromptViewModel.cs`. This is the reference implementation for Steps 5–7; keep the steps below as the behavioral checklist while copying this code, rather than inventing another state machine:

```csharp
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Workout;

public enum SetEffortPromptState
{
    Asking = 1,
    SavingEffort = 2,
    NeedsIncrement = 3,
    Recommendation = 4,
    SaveFailed = 5,
    Unavailable = 6
}

public sealed record ProgressionIncrementOption(decimal DisplayValue, string Label);

public sealed class SetEffortPromptViewModel : INotifyPropertyChanged
{
    private readonly ISetEffortRecorder _recorder;
    private readonly IExerciseGuidancePreferenceStore _preferences;
    private readonly IWeightUnitPreference _unitPreference;
    private readonly IAccountSessionBoundary _boundary;
    private readonly AsyncCommand _chooseEasyCommand;
    private readonly AsyncCommand _chooseProductiveCommand;
    private readonly AsyncCommand _chooseTooHeavyCommand;
    private readonly AsyncCommand _selectIncrementCommand;
    private readonly AsyncCommand _editIncrementCommand;
    private readonly AsyncCommand _saveIncrementCommand;
    private readonly AsyncCommand _retryCommand;
    private readonly AsyncCommand _useSuggestionCommand;
    private readonly AsyncCommand _skipCommand;
    private readonly AsyncCommand _notNowCommand;
    private SetEffortPromptRequest? _request;
    private Func<HypertrophyGuidanceResult, bool>? _applyToDraft;
    private CancellationTokenSource? _lifetime;
    private AccountSessionCancellationLease? _sessionLease;
    private AccountSessionGeneration _generation;
    private SetEffortRating? _selectedEffort;
    private HypertrophyGuidanceSet? _ratedSet;
    private IReadOnlyList<HypertrophyGuidanceSet> _priorCandidates = [];
    private IReadOnlyList<ProgressionIncrementOption> _incrementOptions = [];
    private SetEffortPromptState _state;
    private HypertrophyGuidanceResult? _guidance;
    private string _incrementInput = string.Empty;
    private string _incrementValidationMessage = string.Empty;
    private string _recommendationTitle = string.Empty;
    private string _recommendationReason = string.Empty;
    private string _useActionText = string.Empty;
    private bool _confirmsOriginalSetSaved;
    private bool _hasStoredIncrement;
    private bool _subscribed;
    private bool _dismissRaised;

    public SetEffortPromptViewModel(
        ISetEffortRecorder recorder,
        IExerciseGuidancePreferenceStore preferences,
        IWeightUnitPreference unitPreference,
        IAccountSessionBoundary boundary,
        WorkoutTextSet text)
    {
        _recorder = recorder;
        _preferences = preferences;
        _unitPreference = unitPreference;
        _boundary = boundary;
        Text = text;
        _chooseEasyCommand = new AsyncCommand(
            _ => ChooseEffortAsync(SetEffortRating.Easy), _ => IsAsking);
        _chooseProductiveCommand = new AsyncCommand(
            _ => ChooseEffortAsync(SetEffortRating.Productive), _ => IsAsking);
        _chooseTooHeavyCommand = new AsyncCommand(
            _ => ChooseEffortAsync(SetEffortRating.TooHeavy), _ => IsAsking);
        _selectIncrementCommand = new AsyncCommand(
            SelectIncrementFromCommandAsync, _ => NeedsIncrement);
        _editIncrementCommand = new AsyncCommand(
            _ => { EditIncrement(); return Task.CompletedTask; },
            _ => CanEditIncrement);
        _saveIncrementCommand = new AsyncCommand(
            _ => SaveIncrementAsync(), _ => NeedsIncrement);
        _retryCommand = new AsyncCommand(
            _ => RetryAsync(), _ => HasSaveError);
        _useSuggestionCommand = new AsyncCommand(
            _ => { UseSuggestion(); return Task.CompletedTask; },
            _ => HasUseAction);
        _skipCommand = new AsyncCommand(
            _ => { Skip(); return Task.CompletedTask; }, _ => IsAsking);
        _notNowCommand = new AsyncCommand(
            _ => { NotNow(); return Task.CompletedTask; });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DismissRequested;

    public WorkoutTextSet Text { get; }
    public SetEffortPromptState State => _state;
    public HypertrophyGuidanceResult? Guidance => _guidance;
    public IReadOnlyList<ProgressionIncrementOption> IncrementOptions =>
        _incrementOptions;
    public string IncrementUnitLabel => _unitPreference.Current == WeightDisplayUnit.Kilograms
        ? Text.Kilograms
        : Text.Pounds;
    public string IncrementInput
    {
        get => _incrementInput;
        set
        {
            if (!Set(ref _incrementInput, value ?? string.Empty)) return;
            if (_incrementValidationMessage.Length == 0) return;
            _incrementValidationMessage = string.Empty;
            OnPropertyChanged(nameof(IncrementValidationMessage));
            OnPropertyChanged(nameof(HasIncrementValidation));
        }
    }
    public string IncrementValidationMessage => _incrementValidationMessage;
    public bool HasIncrementValidation => _incrementValidationMessage.Length > 0;
    public string RecommendationTitle => _recommendationTitle;
    public string RecommendationReason => _recommendationReason;
    public string UseActionText => _useActionText;
    public bool IsAsking => State == SetEffortPromptState.Asking;
    public bool IsSavingEffort => State == SetEffortPromptState.SavingEffort;
    public bool NeedsIncrement => State == SetEffortPromptState.NeedsIncrement;
    public bool ShowsRecommendation => State == SetEffortPromptState.Recommendation;
    public bool HasSaveError => State == SetEffortPromptState.SaveFailed;
    public bool HasUnavailable => State == SetEffortPromptState.Unavailable;
    public bool IsBusy => IsSavingEffort;
    public bool ConfirmsOriginalSetSaved => _confirmsOriginalSetSaved;
    public bool HasUseAction => ShowsRecommendation
        && _applyToDraft is not null
        && Guidance is
        {
            SuggestedWeightKg: not null
        } or
        {
            SuggestedAssistedKg: not null
        } or
        {
            SuggestedReps: not null
        };
    public bool CanEditIncrement => ShowsRecommendation
        && _hasStoredIncrement
        && _request?.TrackingMode is TrackingMode.Weighted or TrackingMode.Assisted;

    public IAsyncCommand ChooseEasyCommand => _chooseEasyCommand;
    public IAsyncCommand ChooseProductiveCommand => _chooseProductiveCommand;
    public IAsyncCommand ChooseTooHeavyCommand => _chooseTooHeavyCommand;
    public IAsyncCommand SelectIncrementCommand => _selectIncrementCommand;
    public IAsyncCommand EditIncrementCommand => _editIncrementCommand;
    public IAsyncCommand SaveIncrementCommand => _saveIncrementCommand;
    public IAsyncCommand RetryCommand => _retryCommand;
    public IAsyncCommand UseSuggestionCommand => _useSuggestionCommand;
    public IAsyncCommand SkipCommand => _skipCommand;
    public IAsyncCommand NotNowCommand => _notNowCommand;

    public void Initialize(
        SetEffortPromptRequest request,
        Func<HypertrophyGuidanceResult, bool> applyToDraft)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(applyToDraft);
        Deactivate();
        _request = request;
        _applyToDraft = applyToDraft;
        _generation = _boundary.Capture();
        _lifetime = new CancellationTokenSource();
        _sessionLease = _boundary.CreateCancellationLease(
            _generation, _lifetime.Token);
        _selectedEffort = null;
        _ratedSet = null;
        _priorCandidates = [];
        _guidance = null;
        _confirmsOriginalSetSaved = false;
        _hasStoredIncrement = false;
        _incrementInput = string.Empty;
        _incrementValidationMessage = string.Empty;
        _recommendationTitle = string.Empty;
        _recommendationReason = string.Empty;
        _useActionText = string.Empty;
        _dismissRaised = false;
        if (!_subscribed)
        {
            _boundary.SessionReset += OnSessionReset;
            _unitPreference.Changed += OnUnitPreferenceChanged;
            _subscribed = true;
        }
        RefreshIncrementOptions();
        SetState(SetEffortPromptState.Asking);
        PublishAllPresentation();
    }

    public async Task ChooseEffortAsync(SetEffortRating effort)
    {
        if (!Enum.IsDefined(effort))
            throw new ArgumentOutOfRangeException(nameof(effort));
        if (_request is null)
            throw new InvalidOperationException("Initialize the effort prompt first.");
        if (!IsAsking) return;
        _selectedEffort = effort;
        await RecordAndEvaluateAsync();
    }

    public Task RetryAsync() => HasSaveError && _selectedEffort is not null
        ? RecordAndEvaluateAsync()
        : Task.CompletedTask;

    public async Task SelectIncrementAsync(decimal displayValue)
    {
        if (!NeedsIncrement) return;
        IncrementInput = FormatDisplayValue(displayValue);
        await SaveIncrementAsync();
    }

    public async Task SaveIncrementAsync()
    {
        if (!NeedsIncrement || _request is null || _ratedSet is null) return;
        if (!decimal.TryParse(
                IncrementInput,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var displayValue)
            || displayValue <= 0m)
        {
            ShowIncrementValidation();
            return;
        }

        try
        {
            var canonical = WeightUnitConversion.ToKilograms(
                displayValue, _unitPreference.Current);
            _preferences.SetIncrementKg(_request.ExerciseDefinitionId, canonical);
            _hasStoredIncrement = true;
            EvaluateCached(canonical);
        }
        catch (ArgumentOutOfRangeException)
        {
            ShowIncrementValidation();
        }
        catch (OverflowException)
        {
            ShowIncrementValidation();
        }

        await Task.CompletedTask;
    }

    public void EditIncrement()
    {
        if (!CanEditIncrement || _request is null) return;
        var canonical = _preferences.GetIncrementKg(_request.ExerciseDefinitionId);
        if (canonical is null) return;
        IncrementInput = FormatDisplayValue(WeightUnitConversion.FromKilograms(
            canonical.Value, _unitPreference.Current));
        SetState(SetEffortPromptState.NeedsIncrement);
    }

    public void UseSuggestion()
    {
        if (!HasUseAction || Guidance is null) return;
        var callback = Interlocked.Exchange(ref _applyToDraft, null);
        OnPropertyChanged(nameof(HasUseAction));
        RaiseCommandStates();
        if (callback is null) return;
        bool applied;
        try
        {
            applied = callback(Guidance);
        }
        catch
        {
            applied = false;
        }
        if (applied) RequestDismiss();
    }

    public void Skip() => RequestDismiss();
    public void NotNow() => RequestDismiss();

    public void Deactivate()
    {
        if (_subscribed)
        {
            _boundary.SessionReset -= OnSessionReset;
            _unitPreference.Changed -= OnUnitPreferenceChanged;
            _subscribed = false;
        }
        var lease = Interlocked.Exchange(ref _sessionLease, null);
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        try
        {
            try { lifetime?.Cancel(); }
            catch { }
            lease?.Dispose();
        }
        finally
        {
            lifetime?.Dispose();
        }
        _request = null;
        _applyToDraft = null;
        _selectedEffort = null;
        _ratedSet = null;
        _priorCandidates = [];
        _guidance = null;
    }

    private async Task SelectIncrementFromCommandAsync(object? parameter)
    {
        if (parameter is decimal displayValue)
            await SelectIncrementAsync(displayValue);
    }

    private async Task RecordAndEvaluateAsync()
    {
        var request = _request
            ?? throw new InvalidOperationException("Initialize the effort prompt first.");
        var effort = _selectedEffort
            ?? throw new InvalidOperationException("Choose an effort before saving.");
        var token = _sessionLease?.Token
            ?? new CancellationToken(canceled: true);
        SetGuidance(null);
        SetConfirmation(false);
        SetState(SetEffortPromptState.SavingEffort);

        try
        {
            await _recorder.RecordSetEffortAsync(
                request.ExerciseDefinitionId,
                request.SavedSet.Id,
                effort,
                request.EffortOperationId,
                token);
        }
        catch (SetEffortRecordingUnavailableException)
        {
            if (IsCurrent(request)) SetUnavailable();
            return;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            if (IsCurrent(request)) SetSaveFailed();
            return;
        }

        if (!IsCurrent(request) || token.IsCancellationRequested) return;

        LocalWorkout? active;
        try
        {
            active = await _recorder.RestoreActiveAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            if (IsCurrent(request)) SetSaveFailed();
            return;
        }

        if (!IsCurrent(request) || token.IsCancellationRequested) return;
        if (active is null)
        {
            SetUnavailable();
            return;
        }

        var exercise = active.Exercises.SingleOrDefault(candidate =>
            candidate.DeletedAt is null
            && candidate.Id == request.SavedSet.WorkoutExerciseId
            && candidate.ExerciseDefinitionId == request.ExerciseDefinitionId
            && candidate.TrackingMode == request.TrackingMode);
        var saved = exercise?.Sets.SingleOrDefault(candidate =>
            candidate.DeletedAt is null
            && candidate.Id == request.SavedSet.Id);
        if (exercise is null || saved is null)
        {
            SetUnavailable();
            return;
        }

        _ratedSet = FromLocal(exercise.TrackingMode, saved);
        var currentCandidates = exercise.Sets
            .Where(candidate => candidate.DeletedAt is null
                && candidate.Id != saved.Id
                && (candidate.CompletedAt < saved.CompletedAt
                    || candidate.CompletedAt == saved.CompletedAt
                    && candidate.Order < saved.Order))
            .OrderByDescending(candidate => candidate.CompletedAt)
            .ThenByDescending(candidate => candidate.Order)
            .Select(candidate => FromLocal(exercise.TrackingMode, candidate));
        var previousCandidates = request.PreviousSession is { } previous
            && previous.TrackingMode == request.TrackingMode
            ? previous.Sets
                .OrderByDescending(candidate => candidate.CompletedAt)
                .ThenByDescending(candidate => candidate.Order)
                .Select(candidate => FromHistory(previous.TrackingMode, candidate))
            : Enumerable.Empty<HypertrophyGuidanceSet>();
        _priorCandidates = currentCandidates.Concat(previousCandidates).ToArray();
        var increment = _preferences.GetIncrementKg(request.ExerciseDefinitionId);
        _hasStoredIncrement = increment is not null;
        EvaluateCached(increment);
    }

    private void EvaluateCached(decimal? incrementKg)
    {
        if (_ratedSet is null) return;
        var result = HypertrophyLoadGuidancePolicy.Evaluate(
            new HypertrophyGuidanceRequest(
                _ratedSet,
                _priorCandidates,
                incrementKg));
        SetGuidance(result);
        SetConfirmation(true);
        if (result.RequiresIncrement)
        {
            RefreshIncrementOptions();
            SetState(SetEffortPromptState.NeedsIncrement);
            return;
        }
        MapRecommendation(result);
        SetState(SetEffortPromptState.Recommendation);
    }

    private void MapRecommendation(HypertrophyGuidanceResult result)
    {
        var request = _request
            ?? throw new InvalidOperationException("The effort prompt is inactive.");
        var rated = _ratedSet
            ?? throw new InvalidOperationException("The rated set is unavailable.");
        var titleAndReason = (result.Action, result.Reason, request.TrackingMode) switch
        {
            (HypertrophyGuidanceAction.Increase,
                HypertrophyGuidanceReason.TwoQualifyingSets,
                TrackingMode.Weighted) when result.SuggestedWeightKg is { } weight =>
                (FormatMeasurement(Text.GuidanceTryWeightNextSetFormat, weight),
                    Text.GuidanceTwoSetsReason),
            (HypertrophyGuidanceAction.Increase,
                HypertrophyGuidanceReason.TwoQualifyingSets,
                TrackingMode.Assisted) when result.SuggestedAssistedKg is { } assistance =>
                (FormatMeasurement(Text.GuidanceTryAssistanceNextSetFormat, assistance),
                    Text.GuidanceLessAssistanceReason),
            (HypertrophyGuidanceAction.Reduce, _, TrackingMode.Weighted)
                when result.SuggestedWeightKg is { } weight =>
                (FormatMeasurement(Text.GuidanceTryWeightNextSetFormat, weight),
                    result.Reason == HypertrophyGuidanceReason.BelowRepRange
                        ? Text.GuidanceBelowRangeReason
                        : Text.GuidanceReduceReason),
            (HypertrophyGuidanceAction.Reduce, _, TrackingMode.Assisted)
                when result.SuggestedAssistedKg is { } assistance =>
                (FormatMeasurement(Text.GuidanceTryAssistanceNextSetFormat, assistance),
                    Text.GuidanceMoreAssistanceReason),
            (HypertrophyGuidanceAction.Reduce, _, TrackingMode.Bodyweight)
                when result.SuggestedReps is { } reps =>
                (string.Format(CultureInfo.CurrentCulture,
                    Text.GuidanceTryRepsFormat, reps),
                    Text.GuidanceReduceReason),
            (HypertrophyGuidanceAction.Reduce, _, _) =>
                (Text.GuidanceReduceDifficulty,
                    result.Reason == HypertrophyGuidanceReason.BelowRepRange
                        ? Text.GuidanceBelowRangeReason
                        : Text.GuidanceReduceReason),
            (HypertrophyGuidanceAction.IncreaseRepetitions,
                HypertrophyGuidanceReason.EasyWithinRange,
                _) when result.SuggestedReps is { } reps =>
                (string.Format(CultureInfo.CurrentCulture,
                    Text.GuidanceTryRepsFormat, reps),
                    Text.GuidanceKeepReason),
            (HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange,
                TrackingMode.Weighted) when rated.WeightKg is { } weight =>
                (FormatMeasurement(Text.GuidanceKeepLoadFormat, weight),
                    Text.GuidanceKeepReason),
            (HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange,
                TrackingMode.Assisted) when rated.AssistedKg is { } assistance =>
                (FormatMeasurement(Text.GuidanceKeepLoadFormat, assistance),
                    Text.GuidanceKeepReason),
            (HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange,
                TrackingMode.Bodyweight) when result.SuggestedReps is { } reps =>
                (string.Format(CultureInfo.CurrentCulture,
                    Text.GuidanceTryRepsFormat, reps),
                    Text.GuidanceKeepReason),
            (HypertrophyGuidanceAction.CollectMoreData,
                HypertrophyGuidanceReason.OneQualifyingSet,
                _) =>
                (Text.GuidanceAlmostReady, Text.GuidanceNeedAnotherReason),
            (HypertrophyGuidanceAction.None,
                HypertrophyGuidanceReason.BodyweightRangeCompleted,
                TrackingMode.Bodyweight) =>
                (Text.GuidanceBodyweightComplete, string.Empty),
            (HypertrophyGuidanceAction.Increase,
                HypertrophyGuidanceReason.InvalidSuggestedMeasurement,
                TrackingMode.Weighted) =>
                (Text.GuidanceKeepCurrentLoad,
                    Text.GuidanceHigherLoadUnavailable),
            (HypertrophyGuidanceAction.Increase,
                HypertrophyGuidanceReason.InvalidSuggestedMeasurement,
                TrackingMode.Assisted) =>
                (Text.GuidanceKeepCurrentAssistance,
                    Text.GuidanceLowerAssistanceUnavailable),
            (HypertrophyGuidanceAction.None,
                HypertrophyGuidanceReason.InvalidInput
                    or HypertrophyGuidanceReason.MissingEffort,
                _) =>
                (Text.GuidanceNoSuggestion, Text.GuidanceNoSuggestionReason),
            _ =>
                (Text.GuidanceNoSuggestion, Text.GuidanceNoSuggestionReason)
        };
        Set(ref _recommendationTitle, titleAndReason.Item1,
            nameof(RecommendationTitle));
        Set(ref _recommendationReason, titleAndReason.Item2,
            nameof(RecommendationReason));
        var useText = result switch
        {
            { SuggestedWeightKg: { } weight } =>
                FormatMeasurement(Text.GuidanceUseWeightFormat, weight),
            { SuggestedAssistedKg: { } assistance } =>
                FormatMeasurement(Text.GuidanceUseAssistanceFormat, assistance),
            { SuggestedReps: { } reps } => string.Format(
                CultureInfo.CurrentCulture, Text.GuidanceUseRepsFormat, reps),
            _ => string.Empty
        };
        Set(ref _useActionText, useText, nameof(UseActionText));
        OnPropertyChanged(nameof(HasUseAction));
        OnPropertyChanged(nameof(CanEditIncrement));
        RaiseCommandStates();
    }

    private void RefreshIncrementOptions()
    {
        var values = _unitPreference.Current == WeightDisplayUnit.Kilograms
            ? new[] { 1.25m, 2.5m, 5m }
            : new[] { 2.5m, 5m, 10m };
        _incrementOptions = values.Select(value =>
            new ProgressionIncrementOption(
                value,
                $"{FormatDisplayValue(value)} {IncrementUnitLabel}"))
            .ToArray();
        OnPropertyChanged(nameof(IncrementOptions));
        OnPropertyChanged(nameof(IncrementUnitLabel));
    }

    private string FormatMeasurement(string format, decimal kilograms)
    {
        var display = WeightUnitConversion.FromKilograms(
            kilograms, _unitPreference.Current);
        return string.Format(
            CultureInfo.CurrentCulture,
            format,
            FormatDisplayValue(display),
            IncrementUnitLabel);
    }

    private string FormatDisplayValue(decimal value) => value.ToString(
        _unitPreference.Current == WeightDisplayUnit.Kilograms ? "0.###" : "0.00",
        CultureInfo.CurrentCulture);

    private static HypertrophyGuidanceSet FromLocal(
        TrackingMode mode,
        LocalSet set) =>
        new(set.Id, mode, set.WeightKg, set.AssistedKg, set.Reps,
            set.Effort, set.CompletedAt, set.Order);

    private static HypertrophyGuidanceSet FromHistory(
        TrackingMode mode,
        WorkoutSetDto set) =>
        new(set.Id, mode, set.WeightKg, set.AssistedKg, set.Reps,
            set.Effort, set.CompletedAt, set.Order);

    private void ShowIncrementValidation()
    {
        Set(ref _incrementValidationMessage,
            Text.GuidanceIncrementInvalid,
            nameof(IncrementValidationMessage));
        OnPropertyChanged(nameof(HasIncrementValidation));
        SetState(SetEffortPromptState.NeedsIncrement);
    }

    private void SetSaveFailed()
    {
        SetGuidance(null);
        SetConfirmation(true);
        SetState(SetEffortPromptState.SaveFailed);
    }

    private void SetUnavailable()
    {
        _selectedEffort = null;
        _ratedSet = null;
        _priorCandidates = [];
        SetGuidance(null);
        SetConfirmation(true);
        SetState(SetEffortPromptState.Unavailable);
    }

    private void SetGuidance(HypertrophyGuidanceResult? value)
    {
        if (Equals(_guidance, value)) return;
        _guidance = value;
        OnPropertyChanged(nameof(Guidance));
        OnPropertyChanged(nameof(HasUseAction));
        RaiseCommandStates();
    }

    private void SetConfirmation(bool value)
    {
        if (_confirmsOriginalSetSaved == value) return;
        _confirmsOriginalSetSaved = value;
        OnPropertyChanged(nameof(ConfirmsOriginalSetSaved));
    }

    private void SetState(SetEffortPromptState value)
    {
        if (_state == value) return;
        _state = value;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsAsking));
        OnPropertyChanged(nameof(IsSavingEffort));
        OnPropertyChanged(nameof(NeedsIncrement));
        OnPropertyChanged(nameof(ShowsRecommendation));
        OnPropertyChanged(nameof(HasSaveError));
        OnPropertyChanged(nameof(HasUnavailable));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(HasUseAction));
        OnPropertyChanged(nameof(CanEditIncrement));
        RaiseCommandStates();
    }

    private void OnUnitPreferenceChanged(object? sender, EventArgs eventArgs)
    {
        RefreshIncrementOptions();
        if (_request is { } request
            && _preferences.GetIncrementKg(request.ExerciseDefinitionId) is { } canonical
            && NeedsIncrement)
            IncrementInput = FormatDisplayValue(
                WeightUnitConversion.FromKilograms(canonical, _unitPreference.Current));
        if (Guidance is { } guidance && ShowsRecommendation)
            MapRecommendation(guidance);
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        if (_request is null) return;
        Deactivate();
        RequestDismiss();
    }

    private bool IsCurrent(SetEffortPromptRequest request) =>
        ReferenceEquals(_request, request)
        && !_boundary.IsCancellationRequested(_generation);

    private void RequestDismiss()
    {
        if (_dismissRaised) return;
        _dismissRaised = true;
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseCommandStates()
    {
        _chooseEasyCommand.RaiseCanExecuteChanged();
        _chooseProductiveCommand.RaiseCanExecuteChanged();
        _chooseTooHeavyCommand.RaiseCanExecuteChanged();
        _selectIncrementCommand.RaiseCanExecuteChanged();
        _editIncrementCommand.RaiseCanExecuteChanged();
        _saveIncrementCommand.RaiseCanExecuteChanged();
        _retryCommand.RaiseCanExecuteChanged();
        _useSuggestionCommand.RaiseCanExecuteChanged();
        _skipCommand.RaiseCanExecuteChanged();
        _notNowCommand.RaiseCanExecuteChanged();
    }

    private void PublishAllPresentation()
    {
        OnPropertyChanged(nameof(Guidance));
        OnPropertyChanged(nameof(IncrementInput));
        OnPropertyChanged(nameof(IncrementValidationMessage));
        OnPropertyChanged(nameof(HasIncrementValidation));
        OnPropertyChanged(nameof(RecommendationTitle));
        OnPropertyChanged(nameof(RecommendationReason));
        OnPropertyChanged(nameof(UseActionText));
        OnPropertyChanged(nameof(ConfirmsOriginalSetSaved));
    }

    private bool Set<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

The implementation intentionally separates the durable write from restoration, keeps the request's operation ID through retry, makes `Unavailable` terminal, and consumes the explicit draft callback at most once. Do not collapse those phases during implementation.

- [ ] **Step 5: Record effort and construct a stable policy snapshot**

In `ChooseEffortAsync`:

1. reject undefined effort or missing initialization;
2. set `SavingEffort`, clear prior errors, and call `recorder.RecordSetEffortAsync` with the request's exact IDs and stable `EffortOperationId`;
3. call `recorder.RestoreActiveAsync` after commit and find the matching exercise/set;
4. create `HypertrophyGuidanceSet` for the rated saved set;
5. build `PriorCandidatesNewestFirst` from other active current-workout sets ordered by `CompletedAt`/`Order` descending, followed by only the captured previous session's sets ordered the same way;
6. map every set's tracking mode, canonical measurement, reps, effort, timestamp, and order without using display units;
7. cache the rated set/candidates for increment recomputation;
8. evaluate with the current per-exercise increment;
9. transition to `NeedsIncrement` when `RequiresIncrement`, otherwise `Recommendation`.

Catch `SetEffortRecordingUnavailableException` before the general write-failure handler. Clear the selected effort and all cached policy facts, set `Unavailable`, `ConfirmsOriginalSetSaved = true`, leave `Guidance = null`, and do not expose or execute Retry. This is the permanent path when the workout finishes or the target disappears while the sheet is open.

Treat a `null` active graph or a missing exercise/set from step 3 after `RecordSetEffortAsync` has already returned successfully the same way: the effort write is durable, so transition to `Unavailable`, retain the saved confirmation, and do not route through `SaveFailed` or call `RetryAsync`. Keep the write and post-write lookup in distinct control-flow phases so this completion race cannot be mistaken for a failed mutation.

On any other local write failure, set `SaveFailed`, `ConfirmsOriginalSetSaved = true`, retain selected effort and operation ID for retry, leave `Guidance = null`, and do not call the policy. `RetryAsync` runs only from `SaveFailed` and repeats the same operation ID. Cancellation/account reset clears state without showing a recommendation.

- [ ] **Step 6: Parse/store increments and recompute without another effort mutation**

`IncrementOptions` is `[1.25m, 2.5m, 5m]` in kilograms and `[2.5m, 5m, 10m]` in pounds, formatted with the active display unit. `SelectIncrementAsync(displayValue)` sets the input and executes the same parse/store/recompute path as the custom field. `SaveIncrementAsync` parses `IncrementInput` using `CultureInfo.CurrentCulture`, converts through `WeightUnitConversion.ToKilograms`, validates/stores it, and calls the policy again from cached rated/candidate facts. It must not call `RecordSetEffortAsync` a second time. Invalid/zero/negative/out-of-range input stays in `NeedsIncrement` with the localized validation message.

`CanEditIncrement` is true only for weighted/assisted recommendation state. `EditIncrement()` returns to `NeedsIncrement`, prefilled from the stored canonical value converted to the current display unit; saving a replacement recomputes guidance and never records effort again.

Format increment input/output using `0.###` for kilograms and `0.00` for pounds; changing `IWeightUnitPreference.Current` changes display only and never rewrites stored canonical kilograms.

- [ ] **Step 7: Map typed results to plain-language state**

Use one exhaustive switch on `(Guidance.Action, Guidance.Reason, request.TrackingMode)`:

- `Increase` weighted: format suggested weight and the two-qualifying-sets reason.
- `Increase` assisted: say “less assistance,” never “increase weight.”
- `Reduce` weighted: format lower weight when numeric, otherwise non-numeric reduce guidance.
- `Reduce` assisted: say “more assistance.”
- `IncreaseRepetitions`: keep current load and format `SuggestedReps`.
- `Keep`: format the rated set's current load/assistance and current rep target.
- `CollectMoreData`: show “almost ready” for `OneQualifyingSet`; no Use action.
- `None/BodyweightRangeCompleted`: confirm range completion; no external-load action.
- `Increase/InvalidSuggestedMeasurement`: for weighted sets keep the current load and explain that no supported higher load could be suggested; for assisted sets keep the current assistance and explain that no supported lower assistance could be suggested. Never reverse either message into “reduce,” and never call assistance “weight.”
- `None/InvalidInput` and `None/MissingEffort`: show the localized non-actionable fallback and no Use action; unexpected restored data must not crash the sheet.

`UseSuggestion()` calls the callback exactly once only when `HasUseAction`; dismiss only when the callback returns true. `Skip()` and `NotNow()` only raise `DismissRequested`. No state transition applies or persists a recommendation automatically.

- [ ] **Step 8: Register the transient view model and run tests**

Register:

```csharp
builder.Services.AddSingleton<ISetEffortRecorder>(services =>
    services.GetRequiredService<ActiveWorkoutCoordinator>());
builder.Services.AddTransient<SetEffortPromptViewModel>();
```

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~SetEffortPromptViewModelTests|FullyQualifiedName~HypertrophyLoadGuidancePolicyTests|FullyQualifiedName~LocalizationAuditTests|FullyQualifiedName~MauiCompositionTests" -m:1
```

Expected: PASS. Effort commits precede policy output, retry is stable, increment collection stays in one state machine, and draft application remains explicit.

- [ ] **Step 9: Commit the state machine**

```bash
git add src/TrackZ.Mobile.Core/Features/Workout/SetEffortPromptViewModel.cs src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/Workout/SetEffortPromptViewModelTests.cs tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs
git commit -m "feat: add post-set effort guidance state machine"
```

### Task 9: Native medium bottom sheet, page orchestration, accessibility, and localization audit

**Files:**

- Create: `src/TrackZ.Mobile/Features/Workout/ISetEffortSheet.cs`
- Create: `src/TrackZ.Mobile/Features/Workout/SetEffortSheetPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Workout/SetEffortSheetPage.xaml.cs`
- Create: `tests/TrackZ.Mobile.Tests/NativeIos/SetEffortSheetTests.cs`
- Modify: `src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/Presentation/MauiNativeSheetPresenter.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Workout/ReduceMotionTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/AccessibilitySemanticsTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/NativePresentationCompositionTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs`

**Interfaces:**

- Consumes: post-feedback event and sheet state machine from Tasks 7–8 plus existing `INativeSheetPresenter`/`NativeSheetDetent.Medium` and `IReduceMotionPreference`.
- Produces: `ISetEffortSheet.PresentAsync(SetEffortPromptRequest, Func<HypertrophyGuidanceResult, bool>, CancellationToken)`; native accessible modal with no stacked alerts; safe Set Logger subscription/cancellation lifetime.

- [ ] **Step 1: Add failing native page and orchestration tests**

Create `SetEffortSheetTests.cs` with the tests and recording presenter below:

```csharp
using Microsoft.Maui.Controls;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class SetEffortSheetTests
{
[Fact]
public async Task Present_uses_one_medium_sheet_and_dismiss_completes_once()
{
    await using var fixture = await SheetFixture.CreateAsync();
    var presenter = new RecordingSheetPresenter();
    var page = fixture.CreatePage(presenter);

    var pending = page.PresentAsync(
        fixture.Request,
        _ => true,
        CancellationToken.None);
    await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Same(page, presenter.Page);
    Assert.Equal(NativeSheetDetent.Medium, presenter.Detent);
    await page.DismissAsyncForTest();
    await pending;
    Assert.Equal(1, presenter.DismissCount);
}

[Fact]
public async Task Swipe_disappearance_finishes_without_reopening_or_recording_effort()
{
    await using var fixture = await SheetFixture.CreateAsync();
    var presenter = new RecordingSheetPresenter();
    var page = fixture.CreatePage(presenter);
    var before = fixture.Recorder.RecordCalls;
    var pending = page.PresentAsync(fixture.Request, _ => true);
    await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

    page.SimulateDisappearingForTest();
    await pending;

    Assert.Equal(before, fixture.Recorder.RecordCalls);
    Assert.Equal(1, presenter.ShowCount);
}

[Fact]
public async Task Owner_cancellation_dismisses_the_presented_native_sheet_once()
{
    await using var fixture = await SheetFixture.CreateAsync();
    var presenter = new RecordingSheetPresenter();
    var page = fixture.CreatePage(presenter);
    using var cancellation = new CancellationTokenSource();
    var pending = page.PresentAsync(
        fixture.Request, _ => true, cancellation.Token);
    await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    Assert.Equal(1, presenter.DismissCount);
    Assert.Equal(0, fixture.Recorder.RecordCalls);
}

[Fact]
public async Task Replacement_waits_for_cancelled_presentation_cleanup_then_opens()
{
    await using var fixture = await SheetFixture.CreateAsync();
    var presenter = new RecordingSheetPresenter();
    var page = fixture.CreatePage(presenter);
    using var firstOwner = new CancellationTokenSource();
    var first = page.PresentAsync(
        fixture.Request, _ => true, firstOwner.Token);
    await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

    var replacement = page.PresentAsync(
        fixture.Request with { EffortOperationId = Guid.NewGuid() },
        _ => true,
        CancellationToken.None);
    Assert.Equal(1, presenter.ShowCount);

    firstOwner.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    await presenter.SecondPresented.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal(2, presenter.ShowCount);

    await page.DismissAsyncForTest();
    await replacement;
    Assert.Equal(2, presenter.DismissCount);
}

[Fact]
public async Task Focus_and_runtime_announcements_follow_each_visible_state_once()
{
    await using var fixture = await SheetFixture.CreateAsync();
    var presenter = new RecordingSheetPresenter();
    var page = fixture.CreatePage(presenter);
    var pending = page.PresentAsync(fixture.Request, _ => true);
    await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

    page.SimulateAppearingForTest();
    page.SimulateAppearingForTest();
    Assert.Equal(2, page.FocusAttemptCountForTest);
    Assert.Equal(1, page.AnnouncementCountForTest);
    Assert.Equal(WorkoutResources.English.EffortSetSaved,
        page.LastAnnouncementForTest);

    page.AnnounceStateForTest(SetEffortPromptState.Asking);
    page.AnnounceStateForTest(SetEffortPromptState.SavingEffort);
    Assert.Equal(1, page.AnnouncementCountForTest);

    page.AnnounceStateForTest(SetEffortPromptState.NeedsIncrement);
    page.AnnounceStateForTest(SetEffortPromptState.NeedsIncrement);
    Assert.Equal(2, page.AnnouncementCountForTest);
    Assert.Equal(WorkoutResources.English.GuidanceIncrementTitle,
        page.LastAnnouncementForTest);

    page.AnnounceStateForTest(SetEffortPromptState.SaveFailed);
    page.AnnounceStateForTest(SetEffortPromptState.SaveFailed);
    Assert.Equal(3, page.AnnouncementCountForTest);
    Assert.Equal(WorkoutResources.English.EffortSaveFailed,
        page.LastAnnouncementForTest);

    page.AnnounceStateForTest(SetEffortPromptState.Unavailable);
    page.AnnounceStateForTest(SetEffortPromptState.Unavailable);
    Assert.Equal(4, page.AnnouncementCountForTest);
    Assert.Equal(WorkoutResources.English.EffortUnavailable,
        page.LastAnnouncementForTest);

    await page.ViewModelForTest.ChooseEffortAsync(SetEffortRating.Productive);
    page.AnnounceCurrentStateForTest();
    page.AnnounceCurrentStateForTest();
    Assert.Equal(SetEffortPromptState.Recommendation,
        page.ViewModelForTest.State);
    Assert.Equal(5, page.AnnouncementCountForTest);
    Assert.Equal("Keep 70 kg", page.LastAnnouncementForTest);

    await page.DismissAsyncForTest();
    await pending;
}

private sealed class RecordingSheetPresenter : INativeSheetPresenter
{
    public TaskCompletionSource Presented { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondPresented { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ContentPage? Page { get; private set; }
    public NativeSheetDetent? Detent { get; private set; }
    public int ShowCount { get; private set; }
    public int DismissCount { get; private set; }

    public Task ShowAsync(ContentPage page, NativeSheetDetent detent,
        CancellationToken cancellationToken = default)
    {
        Page = page;
        Detent = detent;
        ShowCount++;
        Presented.TrySetResult();
        if (ShowCount == 2) SecondPresented.TrySetResult();
        return Task.CompletedTask;
    }

    public Task DismissAsync(ContentPage page,
        CancellationToken cancellationToken = default)
    {
        Assert.Same(Page, page);
        DismissCount++;
        return Task.CompletedTask;
    }
}

private sealed class SheetFixture : IAsyncDisposable
{
    private SheetFixture(
        SetEffortPromptRequest request,
        SheetRecorder recorder,
        IExerciseGuidancePreferenceStore preferences,
        IWeightUnitPreference unitPreference,
        AccountSessionBoundary boundary)
    {
        Request = request;
        Recorder = recorder;
        Preferences = preferences;
        UnitPreference = unitPreference;
        Boundary = boundary;
    }

    public SetEffortPromptRequest Request { get; }
    public SheetRecorder Recorder { get; }
    public IExerciseGuidancePreferenceStore Preferences { get; }
    public IWeightUnitPreference UnitPreference { get; }
    public AccountSessionBoundary Boundary { get; }

    public static Task<SheetFixture> CreateAsync()
    {
        var workoutId = Guid.NewGuid();
        var exerciseDefinitionId = Guid.NewGuid();
        var workoutExerciseId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);
        var saved = new LocalSet(
            Guid.NewGuid(), workoutExerciseId, 0, 70m, null, 10,
            now, null, null, 1, 0, Guid.NewGuid(), null);
        var graph = new LocalWorkout(
            workoutId, LocalWorkoutStatus.Active, now.AddMinutes(-5),
            null, null, 2, 1,
            [new LocalWorkoutExercise(
                workoutExerciseId, workoutId, exerciseDefinitionId,
                TrackingMode.Weighted, 0, null, 2, 1, [saved])]);
        var raw = new SheetPreferenceStore();
        return Task.FromResult(new SheetFixture(
            new SetEffortPromptRequest(
                exerciseDefinitionId, TrackingMode.Weighted, saved,
                null, Guid.NewGuid()),
            new SheetRecorder(graph),
            new ExerciseGuidancePreferenceStore(raw),
            new WeightUnitPreference(raw),
            new AccountSessionBoundary()));
    }

    public SetEffortSheetPage CreatePage(INativeSheetPresenter presenter) => new(
        presenter,
        new SetEffortPromptViewModel(
            Recorder, Preferences, UnitPreference, Boundary, WorkoutResources.English));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

private sealed class SheetRecorder(LocalWorkout initialGraph) : ISetEffortRecorder
{
    private LocalWorkout _graph = initialGraph;
    public int RecordCalls { get; private set; }

    public Task<LocalSet> RecordSetEffortAsync(
        Guid exerciseDefinitionId,
        Guid setId,
        SetEffortRating effort,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecordCalls++;
        var exercise = _graph.Exercises.Single(item =>
            item.DeletedAt is null
            && item.ExerciseDefinitionId == exerciseDefinitionId);
        var saved = exercise.Sets.Single(item =>
            item.DeletedAt is null && item.Id == setId);
        var updated = saved with
        {
            Effort = effort,
            UpdatedAt = saved.CompletedAt.AddMinutes(1),
            Version = saved.Version + 1
        };
        var updatedExercise = exercise with
        {
            Sets = exercise.Sets
                .Select(item => item.Id == setId ? updated : item)
                .ToArray(),
            Version = exercise.Version + 1
        };
        _graph = _graph with
        {
            Exercises = _graph.Exercises
                .Select(item => item.Id == exercise.Id ? updatedExercise : item)
                .ToArray(),
            Version = _graph.Version + 1
        };
        return Task.FromResult(updated);
    }

    public Task<LocalWorkout?> RestoreActiveAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<LocalWorkout?>(_graph);
}

private sealed class SheetPreferenceStore : IWorkoutPreferenceStore
{
    private readonly Dictionary<string, string> _values = [];
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public void Set(string key, string value) => _values[key] = value;
}
}
```

Add this failing injected-motion fact to `ReduceMotionTests`; it uses that file's existing `FixedReduceMotionPreference`:

```csharp
[Theory]
[InlineData(false, true)]
[InlineData(true, false)]
public void Native_sheet_animation_policy_respects_reduce_motion(
    bool reduceMotion,
    bool expectedAnimationsEnabled)
{
    var presenter = new MauiNativeSheetPresenter(
        new FixedReduceMotionPreference(reduceMotion));

    Assert.Equal(expectedAnimationsEnabled, presenter.AnimationsEnabled);
}
```

Extend `MauiCompositionTests.AssertInlineSetEditorTransitionAsync` by creating `RecordingEffortSheet effortSheet`, passing it to `TestSetLoggerPage`, and adding these assertions immediately after the existing `await saving`:

```csharp
await effortSheet.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));
Assert.Equal(1, effortSheet.PresentCount);
Assert.Equal(Assert.Single(logger.TodaySets).Id, effortSheet.Request!.SavedSet.Id);
Assert.NotEqual(Guid.Empty, effortSheet.Request.EffortOperationId);
Assert.True(saving.IsCompletedSuccessfully);
Assert.False(effortSheet.Release.Task.IsCompleted);
```

The last two assertions prove the interactive sheet is not awaited by `CompleteSetAsync`. Add this fake to the same test class and release it during page deactivation:

```csharp
private sealed class RecordingEffortSheet : ISetEffortSheet
{
    public TaskCompletionSource Presented { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public SetEffortPromptRequest? Request { get; private set; }
    public int PresentCount { get; private set; }

    public async Task PresentAsync(
        SetEffortPromptRequest request,
        Func<HypertrophyGuidanceResult, bool> applyToDraft,
        CancellationToken cancellationToken = default)
    {
        Request = request;
        PresentCount++;
        Presented.TrySetResult();
        try
        {
            await Release.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Cancelled.TrySetResult();
            throw;
        }
    }
}
```

Update `TestSetLoggerPage` protected-constructor calls to accept this fake plus an `IAccountSessionBoundary`. Construct the page with a dedicated `AccountSessionBoundary pageBoundary`. After the non-awaited assertions above, prove the page links the modal to that account lifetime:

```csharp
await pageBoundary.ResetAsync(_ => Task.CompletedTask);
await effortSheet.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
Assert.False(effortSheet.Release.Task.IsCompleted);
page.Deactivate();
```

Failed-save/load/reset non-presentation remains covered by the Task 7 view-model tests; this page test covers subscription, non-awaited presentation, and account-lifetime cancellation.

- [ ] **Step 2: Add failing XAML/accessibility/composition contracts**

Extend the shipped-page arrays in `LocalizationAuditTests` and `AppWideVisualConsistencyTests` with `Features/Workout/SetEffortSheetPage.xaml`. In `AppWideVisualConsistencyTests`, change both exact shipped-page counts from `17` to `18` and add:

```csharp
// ExpectedPagePaddingOwners
["Features/Workout/SetEffortSheetPage.xaml"] =
    ["0=TrackZPageContentPadding"],

// ExpectedPrimaryActionCounts
["Features/Workout/SetEffortSheetPage.xaml"] = 2,
```

Replace the two-page ternary used by the complementary-primary audit with this explicit allowlist:

```csharp
private static readonly IReadOnlyDictionary<string, string[]>
    ExpectedComplementaryPrimaryVisibility =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Features/Workout/WorkoutPage.xaml"] =
                ["{Binding HasStarted}", "{Binding IsDraft}"],
            ["Features/Workout/SetLoggerPage.xaml"] =
                ["{Binding HasDraftSet}", "{Binding HasNoDraftSet}"],
            ["Features/Workout/SetEffortSheetPage.xaml"] =
                ["{Binding HasUseAction}", "{Binding NeedsIncrement}"]
        };
```

Use it in `AuditPage`:

```csharp
if (expectedPrimaryCount == 2 &&
    (!ExpectedComplementaryPrimaryVisibility.TryGetValue(
        relativePath, out var expectedVisibility)
     || !primaryActions.Select(action => action.Attribute("IsVisible")?.Value)
         .Order(StringComparer.Ordinal)
         .SequenceEqual(expectedVisibility.Order(StringComparer.Ordinal),
             StringComparer.Ordinal)))
    yield return "Complementary primary actions must remain mutually exclusive through their exact phase visibility bindings.";
```

Add a focused assertion in `AccessibilitySemanticsTests` so the sheet itself proves the mutually exclusive pair:

```csharp
Assert.Equal("{Binding NeedsIncrement}",
    document.Descendants().Single(element =>
        Name(element) == "GuidanceSaveIncrementAction")
        .Attribute("IsVisible")?.Value);
Assert.Equal("{Binding HasUseAction}",
    document.Descendants().Single(element =>
        Name(element) == "GuidanceUseAction")
        .Attribute("IsVisible")?.Value);
```

In `AccessibilitySemanticsTests`, parse the new XAML and assert:

```csharp
var heading = document.Descendants().Single(element => Name(element) == "EffortSheetHeading");
Assert.Equal("Level2", heading.Attribute("SemanticProperties.HeadingLevel")?.Value);
foreach (var (name, expectedStyle) in new[]
{
    ("EffortEasyAction", "TrackZSecondaryButtonStyle"),
    ("EffortProductiveAction", "TrackZSecondaryButtonStyle"),
    ("EffortTooHeavyAction", "TrackZSecondaryButtonStyle"),
    ("EffortSkipAction", "TrackZQuietButtonStyle"),
    ("EffortRetryAction", "TrackZSecondaryButtonStyle"),
    ("EffortFailureNotNowAction", "TrackZQuietButtonStyle"),
    ("EffortUnavailableCloseAction", "TrackZQuietButtonStyle"),
    ("GuidanceSaveIncrementAction", "TrackZPrimaryButtonStyle"),
    ("GuidanceUseAction", "TrackZPrimaryButtonStyle"),
    ("GuidanceEditIncrementAction", "TrackZQuietButtonStyle"),
    ("GuidanceNotNowAction", "TrackZSecondaryButtonStyle")
})
{
    var button = document.Descendants().Single(element => Name(element) == name);
    Assert.Equal($"{{DynamicResource {expectedStyle}}}", button.Attribute("Style")?.Value);
    Assert.NotNull(button.Attribute("SemanticProperties.Description"));
}

var incrementInput = document.Descendants().Single(element =>
    Name(element) == "GuidanceIncrementInput");
Assert.Equal("{DynamicResource TrackZFieldStyle}",
    incrementInput.Attribute("Style")?.Value);
Assert.Equal("{Binding Text.GuidanceIncrementTitle}",
    incrementInput.Attribute("SemanticProperties.Description")?.Value);
```

Resolve each named button style through the app resources and assert `MinimumHeightRequest >= 44` and `MinimumWidthRequest >= 44`. Resolve `TrackZFieldStyle` for `GuidanceIncrementInput` and assert its effective `MinimumHeightRequest >= 44`; assert all visible text uses `{Binding Text.*}` or view-model presentation bindings and that no numeric RIR/score labels exist.

Extend `NativePresentationCompositionTests` to resolve separate transient page/view-model instances and the existing singleton `INativeSheetPresenter`.

- [ ] **Step 3: Run native contracts and confirm failure**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~SetEffortSheetTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~AppWideVisualConsistencyTests|FullyQualifiedName~LocalizationAuditTests|FullyQualifiedName~ReduceMotionTests" -m:1
```

Expected: FAIL because the shipped sheet and its registrations do not exist.

- [ ] **Step 4: Define the presentation interface**

Create:

```csharp
namespace TrackZ.Mobile.Features.Workout;

public interface ISetEffortSheet
{
    Task PresentAsync(
        SetEffortPromptRequest request,
        Func<HypertrophyGuidanceResult, bool> applyToDraft,
        CancellationToken cancellationToken = default);
}
```

This interface belongs in the MAUI project because it consumes a modal page lifetime; Mobile Core remains independent of MAUI.

- [ ] **Step 5: Build the one-sheet XAML state layout**

Create the page with this exact root declaration; `x:Name="Page"` is required by the common-option template:

```xml
<ContentPage
    x:Class="TrackZ.Mobile.Features.Workout.SetEffortSheetPage"
    x:Name="Page"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:workout="clr-namespace:TrackZ.Mobile.Features.Workout;assembly=TrackZ.Mobile.Core"
    x:DataType="workout:SetEffortPromptViewModel"
    BackgroundColor="{DynamicResource TrackZSurface}"
    SafeAreaEdges="All"
    Shell.NavBarIsVisible="False">
```

Use this complete XAML body. It includes every mutually exclusive state, every command binding, both complementary primary actions, the increment field, and the accessibility descriptions; do not leave any state as a prose-only template:

```xml
<ContentPage
    x:Class="TrackZ.Mobile.Features.Workout.SetEffortSheetPage"
    x:Name="Page"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:workout="clr-namespace:TrackZ.Mobile.Features.Workout;assembly=TrackZ.Mobile.Core"
    x:DataType="workout:SetEffortPromptViewModel"
    BackgroundColor="{DynamicResource TrackZSurface}"
    SafeAreaEdges="All"
    Shell.NavBarIsVisible="False">
    <ScrollView>
        <VerticalStackLayout
            Padding="{DynamicResource TrackZPageContentPadding}"
            Spacing="{DynamicResource TrackZSpace16}">
            <Label
                x:Name="EffortSheetHeading"
                SemanticProperties.HeadingLevel="Level2"
                Style="{DynamicResource TrackZSectionTitleStyle}"
                Text="{Binding Text.EffortSheetTitle}" />
            <Label
                Style="{DynamicResource TrackZPerformanceNumberStyle}"
                Text="{Binding Text.EffortSetSaved}" />

            <VerticalStackLayout
                x:Name="EffortQuestionState"
                IsVisible="{Binding IsAsking}"
                Spacing="{DynamicResource TrackZSpace12}">
                <Label
                    Style="{DynamicResource TrackZNavigationTitleStyle}"
                    Text="{Binding Text.EffortQuestion}" />
                <Label
                    Style="{DynamicResource TrackZSecondaryStyle}"
                    Text="{Binding Text.EffortGoodFormHelper}" />
                <Button
                    x:Name="EffortEasyAction"
                    Command="{Binding ChooseEasyCommand}"
                    SemanticProperties.Description="{Binding Text.EffortEasyOption}"
                    Style="{DynamicResource TrackZSecondaryButtonStyle}"
                    Text="{Binding Text.EffortEasyOption}" />
                <Button
                    x:Name="EffortProductiveAction"
                    Command="{Binding ChooseProductiveCommand}"
                    SemanticProperties.Description="{Binding Text.EffortProductiveOption}"
                    Style="{DynamicResource TrackZSecondaryButtonStyle}"
                    Text="{Binding Text.EffortProductiveOption}" />
                <Button
                    x:Name="EffortTooHeavyAction"
                    Command="{Binding ChooseTooHeavyCommand}"
                    SemanticProperties.Description="{Binding Text.EffortTooHeavyOption}"
                    Style="{DynamicResource TrackZSecondaryButtonStyle}"
                    Text="{Binding Text.EffortTooHeavyOption}" />
                <Button
                    x:Name="EffortSkipAction"
                    Command="{Binding SkipCommand}"
                    SemanticProperties.Description="{Binding Text.EffortSkip}"
                    Style="{DynamicResource TrackZQuietButtonStyle}"
                    Text="{Binding Text.EffortSkip}" />
                <Label
                    Style="{DynamicResource TrackZSecondaryStyle}"
                    Text="{Binding Text.EffortPainSafety}" />
            </VerticalStackLayout>

            <VerticalStackLayout
                x:Name="EffortSavingState"
                IsVisible="{Binding IsSavingEffort}"
                Spacing="{DynamicResource TrackZSpace12}">
                <ActivityIndicator
                    IsRunning="{Binding IsSavingEffort}"
                    SemanticProperties.Description="{Binding Text.EffortSaving}" />
                <Label
                    Style="{DynamicResource TrackZSecondaryStyle}"
                    Text="{Binding Text.EffortSaving}" />
            </VerticalStackLayout>

            <VerticalStackLayout
                x:Name="EffortIncrementState"
                IsVisible="{Binding NeedsIncrement}"
                Spacing="{DynamicResource TrackZSpace12}">
                <Label
                    SemanticProperties.HeadingLevel="Level3"
                    Style="{DynamicResource TrackZNavigationTitleStyle}"
                    Text="{Binding Text.GuidanceIncrementTitle}" />
                <Label
                    Style="{DynamicResource TrackZSecondaryStyle}"
                    Text="{Binding Text.GuidanceIncrementHelper}" />
                <FlexLayout
                    x:Name="CommonIncrementOptions"
                    BindableLayout.ItemsSource="{Binding IncrementOptions}"
                    Direction="Row"
                    Wrap="Wrap">
                    <BindableLayout.ItemTemplate>
                        <DataTemplate x:DataType="workout:ProgressionIncrementOption">
                            <Button
                                Command="{Binding Source={x:Reference Page}, Path=BindingContext.SelectIncrementCommand}"
                                CommandParameter="{Binding DisplayValue}"
                                MinimumHeightRequest="44"
                                MinimumWidthRequest="44"
                                SemanticProperties.Description="{Binding Label}"
                                Style="{DynamicResource TrackZSecondaryButtonStyle}"
                                Text="{Binding Label}" />
                        </DataTemplate>
                    </BindableLayout.ItemTemplate>
                </FlexLayout>
                <Grid
                    ColumnDefinitions="*,Auto"
                    ColumnSpacing="{DynamicResource TrackZSpace8}">
                    <Entry
                        x:Name="GuidanceIncrementInput"
                        Grid.Column="0"
                        Keyboard="Numeric"
                        Placeholder="{Binding Text.GuidanceIncrementPlaceholder}"
                        SemanticProperties.Description="{Binding Text.GuidanceIncrementTitle}"
                        Style="{DynamicResource TrackZFieldStyle}"
                        Text="{Binding IncrementInput, Mode=TwoWay}" />
                    <Label
                        Grid.Column="1"
                        Style="{DynamicResource TrackZSecondaryStyle}"
                        Text="{Binding IncrementUnitLabel}"
                        VerticalOptions="Center" />
                </Grid>
                <Label
                    IsVisible="{Binding HasIncrementValidation}"
                    Style="{DynamicResource TrackZInlineErrorStyle}"
                    Text="{Binding IncrementValidationMessage}" />
                <Button
                    x:Name="GuidanceSaveIncrementAction"
                    Command="{Binding SaveIncrementCommand}"
                    IsVisible="{Binding NeedsIncrement}"
                    SemanticProperties.Description="{Binding Text.GuidanceSaveIncrement}"
                    Style="{DynamicResource TrackZPrimaryButtonStyle}"
                    Text="{Binding Text.GuidanceSaveIncrement}" />
            </VerticalStackLayout>

            <VerticalStackLayout
                x:Name="EffortRecommendationState"
                IsVisible="{Binding ShowsRecommendation}"
                Spacing="{DynamicResource TrackZSpace12}">
                <Label
                    SemanticProperties.HeadingLevel="Level3"
                    Style="{DynamicResource TrackZNavigationTitleStyle}"
                    Text="{Binding RecommendationTitle}" />
                <Label
                    Style="{DynamicResource TrackZSecondaryStyle}"
                    Text="{Binding RecommendationReason}" />
                <Button
                    x:Name="GuidanceUseAction"
                    Command="{Binding UseSuggestionCommand}"
                    IsVisible="{Binding HasUseAction}"
                    SemanticProperties.Description="{Binding UseActionText}"
                    Style="{DynamicResource TrackZPrimaryButtonStyle}"
                    Text="{Binding UseActionText}" />
                <Button
                    x:Name="GuidanceEditIncrementAction"
                    Command="{Binding EditIncrementCommand}"
                    IsVisible="{Binding CanEditIncrement}"
                    SemanticProperties.Description="{Binding Text.GuidanceEditIncrement}"
                    Style="{DynamicResource TrackZQuietButtonStyle}"
                    Text="{Binding Text.GuidanceEditIncrement}" />
                <Button
                    x:Name="GuidanceNotNowAction"
                    Command="{Binding NotNowCommand}"
                    SemanticProperties.Description="{Binding Text.NotNow}"
                    Style="{DynamicResource TrackZSecondaryButtonStyle}"
                    Text="{Binding Text.NotNow}" />
            </VerticalStackLayout>

            <VerticalStackLayout
                x:Name="EffortSaveFailedState"
                IsVisible="{Binding HasSaveError}"
                Spacing="{DynamicResource TrackZSpace12}">
                <Label
                    SemanticProperties.HeadingLevel="Level3"
                    Style="{DynamicResource TrackZNavigationTitleStyle}"
                    Text="{Binding Text.EffortSaveFailed}" />
                <Label
                    Style="{DynamicResource TrackZSecondaryStyle}"
                    Text="{Binding Text.EffortOriginalSetSafe}" />
                <Button
                    x:Name="EffortRetryAction"
                    Command="{Binding RetryCommand}"
                    SemanticProperties.Description="{Binding Text.Retry}"
                    Style="{DynamicResource TrackZSecondaryButtonStyle}"
                    Text="{Binding Text.Retry}" />
                <Button
                    x:Name="EffortFailureNotNowAction"
                    Command="{Binding NotNowCommand}"
                    SemanticProperties.Description="{Binding Text.NotNow}"
                    Style="{DynamicResource TrackZQuietButtonStyle}"
                    Text="{Binding Text.NotNow}" />
            </VerticalStackLayout>

            <VerticalStackLayout
                x:Name="EffortUnavailableState"
                IsVisible="{Binding HasUnavailable}"
                Spacing="{DynamicResource TrackZSpace12}">
                <Label
                    SemanticProperties.HeadingLevel="Level3"
                    Style="{DynamicResource TrackZNavigationTitleStyle}"
                    Text="{Binding Text.EffortUnavailable}" />
                <Button
                    x:Name="EffortUnavailableCloseAction"
                    Command="{Binding NotNowCommand}"
                    SemanticProperties.Description="{Binding Text.NotNow}"
                    Style="{DynamicResource TrackZQuietButtonStyle}"
                    Text="{Binding Text.NotNow}" />
            </VerticalStackLayout>
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

Every button binds its localized semantic description, uses a semantic TrackZ button style, and resolves to at least 44×44. Do not add an alert, popup, nested sheet, custom animation, score, RIR label, or hard-coded user-facing text.

- [ ] **Step 6: Implement modal lifetime, focus, and announcements**

First make native modal presentation honor the already-registered reduce-motion preference. Add `using TrackZ.Mobile.Features.Workout;` and inject it into the shared presenter:

```csharp
public sealed class MauiNativeSheetPresenter(
    IReduceMotionPreference reduceMotion) : INativeSheetPresenter
{
    internal bool AnimationsEnabled => !reduceMotion.IsEnabled;
```

Use `AnimationsEnabled` as the `animated` argument in both platform branches of `navigation.PushModalAsync(page, AnimationsEnabled)` and in `navigation.PopModalAsync(AnimationsEnabled)`. Read the property at each presentation/dismissal so changing either the TrackZ setting or OS setting takes effect without recreating the singleton. The sheet's state sections switch visibility without custom animation, so reduced motion preserves every state transition while avoiding the large native sheet movement.

Use this complete `SetEffortSheetPage.xaml.cs`; it supplies the constructor, fields, property-change routing, focus, one-time announcements, interlocked dismissal, cleanup, and test hooks:

```csharp
using System.ComponentModel;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.Workout;

public partial class SetEffortSheetPage : ContentPage, ISetEffortSheet
{
    private readonly INativeSheetPresenter _presenter;
    private readonly SetEffortPromptViewModel _viewModel;
    private readonly SemaphoreSlim _presentationGate = new(1, 1);
    private TaskCompletionSource? _dismissed;
    private int _dismissStarted;
    private bool _savedAnnouncementMade;
    private SetEffortPromptState? _announcedState;

    public SetEffortSheetPage(
        INativeSheetPresenter presenter,
        SetEffortPromptViewModel viewModel)
    {
        _presenter = presenter;
        _viewModel = viewModel;
        InitializeComponent();
    }

    internal string? LastAnnouncementForTest { get; private set; }
    internal int AnnouncementCountForTest { get; private set; }
    internal int FocusAttemptCountForTest { get; private set; }
    internal SetEffortPromptViewModel ViewModelForTest => _viewModel;

    public async Task PresentAsync(
        SetEffortPromptRequest request,
        Func<HypertrophyGuidanceResult, bool> applyToDraft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(applyToDraft);
        await _presentationGate.WaitAsync(cancellationToken);
        try
        {
            if (_dismissed is not null)
                throw new InvalidOperationException(
                    "The effort sheet presentation gate is inconsistent.");
            _dismissed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref _dismissStarted, 0);
            _savedAnnouncementMade = false;
            _announcedState = null;
            LastAnnouncementForTest = null;
            AnnouncementCountForTest = 0;
            FocusAttemptCountForTest = 0;
            try
            {
                _viewModel.Initialize(request, applyToDraft);
                _viewModel.DismissRequested += OnDismissRequested;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                BindingContext = _viewModel;
                await _presenter.ShowAsync(
                    this, NativeSheetDetent.Medium, cancellationToken);
                await _dismissed.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await DismissCoreAsync();
                }
                catch
                {
                    _dismissed?.TrySetResult();
                }
                throw;
            }
            finally
            {
                CleanupPresentation();
            }
        }
        finally
        {
            _presentationGate.Release();
        }
    }

    private async Task DismissCoreAsync()
    {
        if (Interlocked.Exchange(ref _dismissStarted, 1) != 0) return;
        try
        {
            await _presenter.DismissAsync(this, CancellationToken.None);
        }
        finally
        {
            _dismissed?.TrySetResult();
        }
    }

    private async void OnDismissRequested(object? sender, EventArgs eventArgs)
    {
        try
        {
            await DismissCoreAsync();
        }
        catch
        {
            _dismissed?.TrySetResult();
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not nameof(SetEffortPromptViewModel.State)
            and not null)
            return;
        DispatchBestEffort(AnnounceCurrentState);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        DispatchBestEffort(RunAppearingActions);
    }

    protected override void OnDisappearing()
    {
        Interlocked.Exchange(ref _dismissStarted, 1);
        _dismissed?.TrySetResult();
        base.OnDisappearing();
    }

    private void RunAppearingActions()
    {
        FocusAttemptCountForTest++;
        try { EffortSheetHeading.Focus(); }
        catch { }
        if (_savedAnnouncementMade) return;
        _savedAnnouncementMade = true;
        AnnounceBestEffort(_viewModel.Text.EffortSetSaved);
    }

    private void AnnounceCurrentState() => AnnounceState(_viewModel.State);

    private void AnnounceState(SetEffortPromptState state)
    {
        if (_announcedState == state) return;
        var announcement = state switch
        {
            SetEffortPromptState.NeedsIncrement =>
                _viewModel.Text.GuidanceIncrementTitle,
            SetEffortPromptState.Recommendation =>
                _viewModel.RecommendationTitle,
            SetEffortPromptState.SaveFailed =>
                _viewModel.Text.EffortSaveFailed,
            SetEffortPromptState.Unavailable =>
                _viewModel.Text.EffortUnavailable,
            _ => null
        };
        if (announcement is null) return;
        _announcedState = state;
        AnnounceBestEffort(announcement);
    }

    private void AnnounceBestEffort(string announcement)
    {
        LastAnnouncementForTest = announcement;
        AnnouncementCountForTest++;
        try { SemanticScreenReader.Default.Announce(announcement); }
        catch { }
    }

    private void DispatchBestEffort(Action action)
    {
        try { Dispatcher.Dispatch(action); }
        catch
        {
            try { action(); }
            catch { }
        }
    }

    private void CleanupPresentation()
    {
        _viewModel.DismissRequested -= OnDismissRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Deactivate();
        BindingContext = null;
        Interlocked.Exchange(ref _dismissed, null);
        Interlocked.Exchange(ref _dismissStarted, 0);
    }

    internal Task DismissAsyncForTest() => DismissCoreAsync();
    internal void SimulateAppearingForTest() => RunAppearingActions();
    internal void SimulateDisappearingForTest() => OnDisappearing();
    internal void AnnounceCurrentStateForTest() => AnnounceCurrentState();
    internal void AnnounceStateForTest(SetEffortPromptState state) =>
        AnnounceState(state);
}
```

`OnDisappearing` does not call the presenter again because an interactive swipe has already dismissed the native controller; `PresentAsync.finally` performs cleanup. Owner cancellation explicitly calls `DismissCoreAsync` before rethrowing cancellation, so deactivation cannot leave a native sheet orphaned. `_presentationGate` makes a replacement prompt wait for cancellation, native dismissal, event unsubscription, and view-model deactivation to finish before the same transient page is initialized again; it does not drop the second prompt. The test hooks call the production paths and contain no parallel dismissal, focus, or announcement behavior.

- [ ] **Step 7: Subscribe Set Logger through the non-awaited VM event**

Add `using TrackZ.Mobile.Identity;`, inject both `ISetEffortSheet effortSheet` and `IAccountSessionBoundary sessionBoundary` into the public `SetLoggerPage` constructor, and retain them in `_effortSheet`/`_sessionBoundary`. Preserve existing protected test constructors by chaining them to a private no-op sheet plus a new `AccountSessionBoundary`; add one protected overload that accepts both a fake sheet and explicit boundary:

```csharp
private sealed class NullSetEffortSheet : ISetEffortSheet
{
    public static NullSetEffortSheet Instance { get; } = new();
    public Task PresentAsync(
        SetEffortPromptRequest request,
        Func<HypertrophyGuidanceResult, bool> applyToDraft,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

Represent replacement, deactivation, and account reset with one idempotent lifetime object:

```csharp
private sealed class EffortSheetLifetime : IDisposable
{
    private CancellationTokenSource? _ownerCancellation;
    private AccountSessionCancellationLease? _sessionLease;

    public EffortSheetLifetime(IAccountSessionBoundary boundary)
    {
        _ownerCancellation = new CancellationTokenSource();
        _sessionLease = boundary.CreateCancellationLease(
            boundary.Capture(), _ownerCancellation.Token);
        Token = _sessionLease.Token;
    }

    public CancellationToken Token { get; }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _ownerCancellation, null);
        var lease = Interlocked.Exchange(ref _sessionLease, null);
        if (owner is null)
        {
            lease?.Dispose();
            return;
        }
        try
        {
            try { owner.Cancel(); }
            catch { /* Cancellation callbacks are best effort here. */ }
            lease?.Dispose();
        }
        finally
        {
            owner.Dispose();
        }
    }
}
```

Add a separate `_effortPromptSubscribed` flag and subscribe/unsubscribe `SetLoggerViewModel.EffortPromptRequested` alongside page appearance/disappearance. The event handler is:

```csharp
private async void OnEffortPromptRequested(
    object? sender,
    SetEffortPromptRequestedEventArgs eventArgs)
{
    if (_deactivated) return;
    var lifetime = new EffortSheetLifetime(_sessionBoundary);
    var previous = Interlocked.Exchange(ref _effortSheetLifetime, lifetime);
    previous?.Dispose();
    try
    {
        await _effortSheet.PresentAsync(
            eventArgs.Request,
            _viewModel.TryApplyGuidanceToNextDraft,
            lifetime.Token);
    }
    catch (OperationCanceledException) when (lifetime.Token.IsCancellationRequested) { }
    catch
    {
        // The set is already durable; modal presentation is best effort.
    }
    finally
    {
        if (ReferenceEquals(Interlocked.CompareExchange(
                ref _effortSheetLifetime, null, lifetime), lifetime))
            lifetime.Dispose();
    }
}
```

In `Deactivate`, cancel the owner explicitly before deactivating the view model:

```csharp
Interlocked.Exchange(ref _effortSheetLifetime, null)?.Dispose();
```

Do not cancel the sheet merely because the parent receives `OnDisappearing` while its modal is covering it. Replacement and `Deactivate` cancel the owner token; account reset cancels the linked `AccountSessionCancellationLease` immediately. Do not subscribe this flow to `MauiSetSavedFeedback.Saved`.

- [ ] **Step 8: Register transient sheet composition**

Add:

```csharp
builder.Services.AddTransient<SetEffortSheetPage>();
builder.Services.AddTransient<ISetEffortSheet>(services =>
    services.GetRequiredService<SetEffortSheetPage>());
```

Keep the existing protected `SetLoggerPage` signatures functional through `NullSetEffortSheet.Instance`; add the fake-accepting overload used by `TestSetLoggerPage`. Confirm `INativeSheetPresenter` remains singleton and both sheet page and sheet view model are transient.

- [ ] **Step 9: Run native, localization, accessibility, and page lifecycle tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~SetEffortSheetTests|FullyQualifiedName~SetLoggerViewModelTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~AppWideVisualConsistencyTests|FullyQualifiedName~LocalizationAuditTests|FullyQualifiedName~MauiCompositionTests|FullyQualifiedName~ReduceMotionTests" -m:1
```

Expected: PASS. The sheet presents once at medium detent, save completion is not blocked, swipe/skip/retry work, focus/semantics exist, and all copy is localized.

- [ ] **Step 10: Commit native effort sheet**

```bash
git add src/TrackZ.Mobile/Features/Workout/ISetEffortSheet.cs src/TrackZ.Mobile/Features/Workout/SetEffortSheetPage.xaml src/TrackZ.Mobile/Features/Workout/SetEffortSheetPage.xaml.cs src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml.cs src/TrackZ.Mobile/Presentation/MauiNativeSheetPresenter.cs src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/NativeIos/SetEffortSheetTests.cs tests/TrackZ.Mobile.Tests/Workout/ReduceMotionTests.cs tests/TrackZ.Mobile.Tests/Localization/LocalizationAuditTests.cs tests/TrackZ.Mobile.Tests/NativeIos/AccessibilitySemanticsTests.cs tests/TrackZ.Mobile.Tests/NativeIos/NativePresentationCompositionTests.cs tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs
git commit -m "feat: present effort guidance in a native sheet"
```

### Task 10: Persistent visual reference and end-to-end acceptance

**Files:**

- Create: `tests/TrackZ.Mobile.Tests/Acceptance/HypertrophyGuidanceAcceptanceTests.cs`
- Modify: `docs/design/track-sets-reference.html`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/TrackSetsVisualContractTests.cs`

**Interfaces:**

- Consumes: complete domain/server/mobile/UI slices from Tasks 1–9.
- Produces: restart/sync/legacy/assisted acceptance evidence and maintained Set Logger/sheet visual source of truth.

Create the acceptance file with this opening shell, keep the class open, then place every fact and nested helper from the steps below inside it. The helper block at the end of Step 2 supplies the class's final closing brace.

```csharp
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
```

- [ ] **Step 1: Add the offline save-rate-restart acceptance test**

Create a test class with a unique temp SQLite path, cleanup identical to `OfflineWorkoutPersistenceTests`, and this test:

```csharp
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
```

- [ ] **Step 2: Add progression, sync-recreation, legacy edit, and assisted acceptance tests**

Add these four facts:

```csharp
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
```

Define these nested acceptance helpers in the new file and use real production coordinators/stores around them:

```csharp
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
```

Add the remaining helpers below. They keep only external history, feedback, and connectivity at the test boundary; persistence, mutation, policy, preference, and sync behavior remain production code:

```csharp
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
        workout.Exercises.Where(item => item.DeletedAt is null));
    var current = Assert.Single(
        exercise.Sets.Where(item => item.DeletedAt is null));
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
```

Construct `TrackZLocalDatabase`, `LocalWorkoutRepository`, `OutboxRepository`, `AccountSessionBoundary`, `ActiveWorkoutCoordinator`, `ExerciseGuidancePreferenceStore`, and `SyncCoordinator` directly in each test. `ToSyncWorkout` maps every persisted set fact including `Effort`; no recommendation field is added to the graph.

- [ ] **Step 3: Run the completed acceptance slice**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~HypertrophyGuidanceAcceptanceTests|FullyQualifiedName~OfflineWorkoutPersistenceTests" -m:1
```

Expected: PASS. A failure identifies a concrete integration omission; correct it in the owning task and rerun this command before continuing.

- [ ] **Step 4: Add persistent reference contract assertions**

Extend `TrackSetsVisualContractTests` to assert `docs/design/track-sets-reference.html` contains exactly one of every required specimen token and none of the forbidden unavailable actions:

```csharp
foreach (var token in new[]
{
    "data-previous-reference=\"true\"",
    "Previous workout reference",
    "70 kg × 10 reps",
    "Heaviest set in the 8–12 rep range",
    "data-effort-sheet=\"asking\"",
    "How did this set feel?",
    "Too easy — many reps left",
    "About right — the final reps were hard, with good form",
    "Too heavy — missed the range or form began to break",
    "data-effort-sheet=\"recommendation\"",
    "Try 72.5 kg next set",
    "data-effort-sheet=\"needs-increment\"",
    "data-effort-sheet=\"unavailable\"",
    "This set can no longer be rated. Your original set is saved.",
    "data-effort-unavailable-action=\"dismiss\""
})
{
    Assert.Equal(1, Occurrences(html, token));
}
Assert.DoesNotContain("data-effort-unavailable-action=\"retry\"", html, StringComparison.Ordinal);
Assert.DoesNotContain("data-effort-unavailable-action=\"use\"", html, StringComparison.Ordinal);
Assert.DoesNotContain("RIR", html, StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain("4+", html, StringComparison.Ordinal);
```

Add this occurrence helper at test-class scope:

```csharp
private static int Occurrences(string source, string value)
{
    var count = 0;
    var start = 0;
    while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
    {
        count++;
        start += value.Length;
    }
    return count;
}
```

- [ ] **Step 5: Update the maintained Set Logger reference**

In `track-sets-reference.html`:

- insert the compact previous-workout reference directly below the exercise summary and before the inline editor;
- render `70 kg` in the card's primary-number style and `10 reps` in secondary text; keep `aria-label="70 kg × 10 reps"` so the complete measurement is announced once;
- remove the old `Last: 15 kg × 8` line from the exercise summary so previous-session context appears only in the reference card;
- add a medium-detent bottom-sheet reference in `asking` state with saved confirmation, three full plain-language effort actions, skip, and pain/form safety copy;
- add a second static state specimen marked `recommendation` with “Try 72.5 kg next set,” the two-set explanation, primary Use action, and Not now;
- add a missing-increment specimen marked `data-effort-sheet="needs-increment"` within that same sheet frame, not a nested modal;
- add a saved-but-unavailable specimen marked `data-effort-sheet="unavailable"` with “This set can no longer be rated. Your original set is saved.” and one quiet `data-effort-unavailable-action="dismiss"` control; include no retry, increment, recommendation, or Use control in that specimen, and reserve the scoped `retry`/`use` attributes as forbidden contract values;
- retain current Track Sets order, spacing tokens, Today/Last sections, safe area, and existing saved-pulse reference;
- use no RIR score, automatic-save implication, medical claim, or non-localized runtime source.

The HTML is a maintained visual contract, not runtime code; all static specimens may appear side by side while native runtime shows only one state at a time.

- [ ] **Step 6: Run visual and acceptance verification**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~HypertrophyGuidanceAcceptanceTests|FullyQualifiedName~OfflineWorkoutPersistenceTests|FullyQualifiedName~TrackSetsVisualContractTests|FullyQualifiedName~SetEffortSheetTests" -m:1
```

Expected: PASS.

- [ ] **Step 7: Commit acceptance and reference artifacts**

```bash
git add tests/TrackZ.Mobile.Tests/Acceptance/HypertrophyGuidanceAcceptanceTests.cs docs/design/track-sets-reference.html tests/TrackZ.Mobile.Tests/NativeIos/TrackSetsVisualContractTests.cs
git commit -m "test: cover hypertrophy guidance end to end"
```

### Task 11: Full regression, migration, and compatibility gate

**Files:**

- Verify only; modify the owning task's files if a regression exposes a defect.

**Interfaces:**

- Consumes: all completed tasks.
- Produces: evidence that domain, EF/API, mobile persistence/sync, localization, accessibility, and complete solution tests pass together.

- [ ] **Step 1: Run focused domain and infrastructure gates**

Run:

```bash
dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --no-restore -m:1
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutPersistenceTests" -m:1
```

Expected: PASS with zero failures.

- [ ] **Step 2: Run application and API compatibility gates**

Run:

```bash
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --no-restore -m:1
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SyncPushTests|FullyQualifiedName~SyncPullTests|FullyQualifiedName~EditHistoryTests|FullyQualifiedName~WorkoutEndpointTests" -m:1
```

Expected: PASS, including old JSON without effort and legacy `EditSet` preserving effort.

- [ ] **Step 3: Run the complete mobile gate**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore -m:1
```

Expected: PASS with zero failures, including schema 1–5 upgrades, operation 11 ordering, offline restart, sheet lifecycle, Thai/English parity, accessibility, and visual contracts.

- [ ] **Step 4: Run the complete solution**

Run:

```bash
dotnet test TrackZ.slnx --no-restore -m:1
```

Expected: PASS with zero failed projects/tests.

- [ ] **Step 5: Inspect migration and repository hygiene**

Run:

```bash
dotnet ef migrations list --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api
git diff --check
git status --short
```

Expected: `AddSetEffortRating` is the newest migration; `git diff --check` emits nothing; status contains only deliberate implementation files and no `.superpowers/brainstorm/`, build output, database, secret, or unrelated user file.

- [ ] **Step 6: Confirm the verification task made no new edits**

Run:

```bash
git status --short
```

Expected: clean after the Task 10 commit. If a gate required a correction, return to that owning task, amend only its listed files and tests, rerun that task's focused command plus Steps 1–4 here, and create its named commit there. Do not create an empty verification commit.
