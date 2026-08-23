# Insight-First Home Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a truthful, versioned training-insight system that compares similar performances, produces bounded weekly narratives, and supports Progress and per-exercise drill-down.

**Architecture:** Materialize completed per-session exercise performance and classify it through a pure domain service. Expose structured, user-scoped weekly/detail contracts through progress endpoints; cache them independently on Mobile; then render localized Home and Progress views without API prose or an opaque score.

**Tech Stack:** .NET 10, C#, MediatR, EF Core, PostgreSQL/Npgsql, ASP.NET Core minimal APIs, .NET MAUI XAML, System.Text.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-24-trackz-insight-first-home-design.md`

## Global Constraints

- Start only after Phase 1 is merged and accepted.
- Classifier rules version is exactly `1`.
- Compare the same exercise and tracking mode; weighted/assisted pairs require repetitions within ±2.
- Do not calculate or expose estimated 1RM, volume status, or a universal score.
- One lower comparison is `LowerToday`; `Watch` requires three consecutive pairwise lower comparisons.
- A gap greater than eight weeks is `Returned`.
- New records exclude the current session from their historical baseline.
- Weekly aggregation uses the profile time zone and Monday 00:00 boundaries.
- Home renders at most two highlights and one watch item.
- API contracts contain enums and measurements, never localized prose.
- Canonical weights remain kilograms; Mobile owns Thai/English and kg/lb rendering.
- Insight failure must not disable Start/Continue or Train again.
- Do not start Docker. PostgreSQL fixture tests remain an explicit environment gate and Phase 2 cannot be called complete without them.
- Do not install or launch the Simulator without an explicit user request.

## File Structure

### New files

- `src/TrackZ.Domain/Progress/TrainingInsightModels.cs` — classifier inputs/results and rules version.
- `src/TrackZ.Domain/Progress/TrainingInsightClassifier.cs` — pair selection, outcomes, records, streaks, and weekly ranking.
- `src/TrackZ.Domain/Progress/ExerciseSessionPerformance.cs` — durable session projection.
- `src/TrackZ.Contracts/Progress/TrainingInsightDtos.cs` — weekly/detail wire contracts.
- `src/TrackZ.Application/Progress/GetWeeklyInsights/GetWeeklyInsightsQuery.cs`
- `src/TrackZ.Application/Progress/GetExerciseInsight/GetExerciseInsightQuery.cs`
- `src/TrackZ.Infrastructure/Persistence/Configurations/ExerciseSessionPerformanceConfiguration.cs`
- `src/TrackZ.Infrastructure/Progress/TrainingInsightReadStore.cs`
- `src/TrackZ.Mobile.Core/Features/Insights/TrainingInsightResources.cs`
- `src/TrackZ.Mobile.Core/Resources/TrainingInsightStrings.resx`
- `src/TrackZ.Mobile.Core/Resources/TrainingInsightStrings.th.resx`
- `src/TrackZ.Mobile.Core/Features/Insights/TrainingInsightSource.cs`
- `src/TrackZ.Mobile.Core/Features/Insights/TrainingInsightPresentation.cs`
- `src/TrackZ.Mobile.Core/Features/Insights/ExerciseInsightDetailViewModel.cs`
- `src/TrackZ.Mobile/Features/Progress/ExerciseInsightDetailPage.xaml`
- `src/TrackZ.Mobile/Features/Progress/ExerciseInsightDetailPage.xaml.cs`

### Existing integration points

- `src/TrackZ.Infrastructure/Persistence/AppDbContext.cs`
- `src/TrackZ.Infrastructure/DependencyInjection.cs`
- `src/TrackZ.Api/Endpoints/ProgressEndpoints.cs`
- `src/TrackZ.Mobile/MauiProgram.cs`
- `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- `src/TrackZ.Mobile/Features/Train/TrainPage.xaml`
- `src/TrackZ.Mobile.Core/Features/Gamification/ProgressDashboardViewModel.cs`
- `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml`
- `src/TrackZ.Mobile/AppShell.xaml.cs`

---

### Task 1: Pure comparable-performance classifier

**Files:**
- Create: `src/TrackZ.Domain/Progress/TrainingInsightModels.cs`
- Create: `src/TrackZ.Domain/Progress/TrainingInsightClassifier.cs`
- Create: `tests/TrackZ.Domain.Tests/Progress/TrainingInsightClassifierTests.cs`

**Interfaces:**
- Consumes: ordered `TrainingSessionPerformance` values in canonical kilograms.
- Produces: `ExerciseTrainingInsight TrainingInsightClassifier.ClassifyExercise(IReadOnlyList<TrainingSessionPerformance> sessions)`.

- [ ] **Step 1: Write the failing outcome matrix**

Use this exact core theory data and add separate changed-mode, ±2 boundary, tie-breaker, eight-week, record, and three-decline tests:

```csharp
public static TheoryData<TrackingMode, TrainingSetPerformance, TrainingSetPerformance, TrainingInsightOutcome> Cases => new()
{
    { TrackingMode.Weighted, Set(1, 70m, null, 10), Set(2, 75m, null, 8), TrainingInsightOutcome.ImprovedLoad },
    { TrackingMode.Weighted, Set(1, 70m, null, 8), Set(2, 70m, null, 10), TrainingInsightOutcome.ImprovedReps },
    { TrackingMode.Weighted, Set(1, 70m, null, 9), Set(2, 70m, null, 8), TrainingInsightOutcome.Stable },
    { TrackingMode.Weighted, Set(1, 70m, null, 10), Set(2, 70m, null, 8), TrainingInsightOutcome.LowerToday },
    { TrackingMode.Weighted, Set(1, 70m, null, 8), Set(2, 67.5m, null, 11), TrainingInsightOutcome.Inconclusive },
    { TrackingMode.Assisted, Set(1, null, 20m, 10), Set(2, null, 15m, 8), TrainingInsightOutcome.ImprovedAssistance },
    { TrackingMode.Assisted, Set(1, null, 15m, 8), Set(2, null, 20m, 8), TrainingInsightOutcome.LowerToday },
    { TrackingMode.Bodyweight, Set(1, null, null, 8), Set(2, null, null, 10), TrainingInsightOutcome.ImprovedReps },
    { TrackingMode.Bodyweight, Set(1, null, null, 10), Set(2, null, null, 9), TrainingInsightOutcome.Stable },
    { TrackingMode.Bodyweight, Set(1, null, null, 10), Set(2, null, null, 8), TrainingInsightOutcome.LowerToday }
};

private static TrainingSetPerformance Set(int id, decimal? weightKg, decimal? assistedKg, int reps) =>
    new(Guid.Parse($"00000000-0000-0000-0000-{id:D12}"), 0, weightKg, assistedKg, reps);
```

- [ ] **Step 2: Run the test and verify RED**

```bash
dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~TrainingInsightClassifierTests --verbosity minimal
```

Expected: FAIL because the types do not exist.

- [ ] **Step 3: Add the exact domain contract**

```csharp
public static class TrainingInsightRules
{
    public const int Version = 1;
    public const int ComparableRepDifference = 2;
    public const int MeaningfulRepDifference = 2;
    public static readonly TimeSpan MaximumComparisonGap = TimeSpan.FromDays(56);
}

public enum TrainingInsightOutcome
{
    InsufficientComparison,
    ImprovedLoad,
    ImprovedAssistance,
    ImprovedReps,
    Stable,
    LowerToday,
    Watch,
    Returned,
    Inconclusive,
    NewRecord
}

public enum TrainingRecordKind
{
    None,
    HeaviestAtSimilarReps,
    MostRepsAtSameLoad,
    LeastAssistanceAtSimilarReps,
    MostRepsAtSameAssistance,
    MostBodyweightReps
}

public sealed record TrainingSetPerformance(Guid SetId, int Order, decimal? WeightKg, decimal? AssistedKg, int Reps);

public sealed record TrainingSessionPerformance(
    Guid WorkoutId,
    Guid ExerciseDefinitionId,
    BodyPart BodyPart,
    TrackingMode TrackingMode,
    DateTimeOffset CompletedAt,
    IReadOnlyList<TrainingSetPerformance> Sets);

public sealed record ExerciseTrainingInsight(
    Guid ExerciseDefinitionId,
    BodyPart BodyPart,
    TrackingMode TrackingMode,
    DateTimeOffset LatestCompletedAt,
    TrainingInsightOutcome Outcome,
    TrainingRecordKind RecordKind,
    TrainingSetPerformance? LatestComparableSet,
    TrainingSetPerformance? PreviousComparableSet,
    int ConsecutiveLowerComparisons,
    int RulesVersion);
```

- [ ] **Step 4: Implement validation, pair ordering, and precedence**

Validate IDs, enums, timestamps, positive reps, and mode-specific measurement shape. Generate all valid pairs and select with:

```csharp
var pair = candidates
    .OrderBy(item => Math.Abs(item.Current.Reps - item.Previous.Reps))
    .ThenByDescending(item => HardnessFloor(mode, item.Current, item.Previous))
    .ThenByDescending(item => Math.Min(item.Current.Reps, item.Previous.Reps))
    .ThenBy(item => item.Current.Order)
    .ThenBy(item => item.Previous.Order)
    .ThenBy(item => item.Current.SetId)
    .ThenBy(item => item.Previous.SetId)
    .FirstOrDefault();
```

Apply this result precedence: insufficient history → changed mode → returned gap → no pair → proven record → three lower comparisons → latest-pair outcome. Record proof reads `sessions[..^1]`; consecutive decline stops at the first non-lower adjacent pair.

- [ ] **Step 5: Verify GREEN and commit**

Run Step 2, then:

```bash
git add src/TrackZ.Domain/Progress/TrainingInsightModels.cs src/TrackZ.Domain/Progress/TrainingInsightClassifier.cs tests/TrackZ.Domain.Tests/Progress/TrainingInsightClassifierTests.cs
git commit -m "feat: classify comparable training performance"
```

---

### Task 2: Weekly aggregation

**Files:**
- Modify: `src/TrackZ.Domain/Progress/TrainingInsightModels.cs`
- Modify: `src/TrackZ.Domain/Progress/TrainingInsightClassifier.cs`
- Create: `tests/TrackZ.Domain.Tests/Progress/WeeklyTrainingInsightTests.cs`

**Interfaces:**
- Consumes: exercise histories, profile time zone, and current instant.
- Produces: `WeeklyTrainingInsight TrainingInsightClassifier.SummarizeWeek(IReadOnlyDictionary<Guid, IReadOnlyList<TrainingSessionPerformance>> histories, TimeZoneInfo timeZone, DateTimeOffset now)`.

- [ ] **Step 1: Write failing weekly tests**

```csharp
[Fact]
public void Summary_counts_only_comparable_exercises_and_limits_items()
{
    var result = Summarize(FiveImprovedThreeStableOneLowerTwoInsufficient());

    Assert.Equal(5, result.ImprovedCount);
    Assert.Equal(3, result.StableCount);
    Assert.Equal(1, result.LowerCount);
    Assert.Equal(2, result.InsufficientCount);
    Assert.Equal(9, result.ComparableExerciseCount);
    Assert.True(result.Highlights.Count <= 2);
    Assert.True(result.WatchItems.Count <= 1);
}

[Fact]
public void Muscle_group_needs_two_comparable_exercises_and_strict_improved_majority()
{
    Assert.Null(Summarize(OneImprovedChest()).LeadingBodyPart);
    Assert.Null(Summarize(OneImprovedOneStableChest()).LeadingBodyPart);
    Assert.Equal(BodyPart.Chest, Summarize(TwoImprovedOneStableChest()).LeadingBodyPart);
}
```

Add Monday-boundary, prior-session-from-earlier-week, headline precedence, and deterministic ranking tests.

Build each named fixture from a dictionary of fixed exercise IDs to explicit `TrainingSessionPerformance` arrays. Use `2026-08-17T00:00:00+07:00` as the Monday boundary and lexically ordered fixed GUIDs for tie tests; do not use random IDs or the system clock.

- [ ] **Step 2: Run tests and verify RED**

```bash
dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~WeeklyTrainingInsightTests --verbosity minimal
```

Expected: FAIL because weekly models do not exist.

- [ ] **Step 3: Add result types and aggregation**

```csharp
public enum WeeklyInsightHeadlineKind
{
    NoComparableExercises,
    ImprovedLargest,
    StableLargest,
    LowerLargest,
    WatchPresent
}

public sealed record WeeklyInsightHighlight(
    Guid? ExerciseDefinitionId,
    BodyPart? BodyPart,
    TrainingInsightOutcome Outcome,
    TrainingRecordKind RecordKind);

public sealed record WeeklyTrainingInsight(
    DateOnly WeekStart,
    DateOnly WeekEndExclusive,
    WeeklyInsightHeadlineKind HeadlineKind,
    int ImprovedCount,
    int StableCount,
    int LowerCount,
    int InsufficientCount,
    int ComparableExerciseCount,
    BodyPart? LeadingBodyPart,
    IReadOnlyList<WeeklyInsightHighlight> Highlights,
    IReadOnlyList<Guid> WatchItems,
    DateTimeOffset ComputedThrough,
    int RulesVersion);
```

Derive the half-open Monday-to-Monday range in the profile time zone. Rank new records, qualifying muscle groups, load/assistance gains, then rep gains. Use completion time and exercise ID for ties. Headline precedence is no-comparable → watch → improved strictly largest → stable tied/largest → lower.

- [ ] **Step 4: Verify GREEN and commit**

Run Step 2, then:

```bash
git add src/TrackZ.Domain/Progress/TrainingInsightModels.cs src/TrackZ.Domain/Progress/TrainingInsightClassifier.cs tests/TrackZ.Domain.Tests/Progress/WeeklyTrainingInsightTests.cs
git commit -m "feat: aggregate weekly training insights"
```

---

### Task 3: Durable session projection

**Files:**
- Create: `src/TrackZ.Domain/Progress/ExerciseSessionPerformance.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/ExerciseSessionPerformanceConfiguration.cs`
- Modify: `src/TrackZ.Infrastructure/Persistence/AppDbContext.cs`
- Create: `tests/TrackZ.Infrastructure.Tests/Progress/ExerciseSessionPerformanceProjectionTests.cs`
- Create: generated `AddExerciseSessionPerformance` migration and snapshot update.

**Interfaces:**
- Consumes: canonical completed workout/exercise/set rows during existing reconciliation.
- Produces: `ExerciseSessionPerformance` and ordered child sets kept consistent after completion/edit/delete/restore.

- [ ] **Step 1: Write failing entity and reconciliation tests**

Assert `ExerciseSessionPerformance.Create` rejects mixed measurement shapes and preserves set order using:

```csharp
var projection = ExerciseSessionPerformance.Create(
    userId,
    workoutId,
    workoutExerciseId,
    exerciseId,
    BodyPart.Chest,
    TrackingMode.Weighted,
    completedAt,
    [
        ExerciseSessionPerformanceSet.Create(set2Id, 1, 75m, null, 8),
        ExerciseSessionPerformanceSet.Create(set1Id, 0, 70m, null, 10)
    ],
    TrainingInsightRules.Version);

Assert.Equal([set1Id, set2Id], projection.Sets.OrderBy(item => item.Order).Select(item => item.SetEntryId));
```

Add a PostgreSQL test that reconciles a completed workout, edits/deletes/restores a set, and finally deletes the workout; assert the projection equals canonical live state after each step.

- [ ] **Step 2: Verify Domain RED and record the PostgreSQL gate**

```bash
dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ExerciseSessionPerformance --verbosity minimal
```

Expected: FAIL because the entity does not exist. Do not run Infrastructure tests while Docker is off.

- [ ] **Step 3: Add the entity and EF configuration**

Use `WorkoutExerciseId` as projection key and `SetEntryId` as child key. Store user/workout/exercise IDs, body part, mode, completion time, ordered live sets, and rules version. Configure table names `exercise_session_performances` and `exercise_session_performance_sets`, `(10,3)` decimal precision, mode/shape constraints, unique `(WorkoutExerciseId, Order)`, and cascade child deletion.

- [ ] **Step 4: Rebuild projections in reconciliation**

Keep `ReconcileUserProgressHandler` and `IUserProgressReconciliationStore` unchanged. Inside the existing `AppDbContext.ReconcileAsync`, rebuild session projections before its first `SaveChangesAsync`: lock `exercise-session-performance:{userId}:{workoutId}`, remove projections for non-live/non-completed exercises, and replace each affected live projection from canonical rows in the same reconciliation transaction.

- [ ] **Step 5: Generate and inspect migration/backfill**

```bash
dotnet ef migrations add AddExerciseSessionPerformance --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api
```

Add deterministic SQL backfill using explicit insert-select statements that list every target column and every source expression for live completed workout exercises and live ordered sets. Inspect Down order: child table first, then parent.

- [ ] **Step 6: Verify non-Docker GREEN and commit**

```bash
dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ExerciseSessionPerformance --verbosity minimal
dotnet build src/TrackZ.Infrastructure/TrackZ.Infrastructure.csproj --no-restore --verbosity minimal
```

Expected: PASS. Keep the PostgreSQL lifecycle test pending until explicitly authorized.

```bash
git add src/TrackZ.Domain/Progress/ExerciseSessionPerformance.cs src/TrackZ.Infrastructure/Persistence/Configurations/ExerciseSessionPerformanceConfiguration.cs src/TrackZ.Infrastructure/Persistence/AppDbContext.cs src/TrackZ.Infrastructure/Persistence/Migrations tests/TrackZ.Infrastructure.Tests/Progress/ExerciseSessionPerformanceProjectionTests.cs tests/TrackZ.Domain.Tests/Progress
git commit -m "feat: project per-session exercise performance"
```

---

### Task 4: Structured queries and API endpoints

**Files:**
- Create: `src/TrackZ.Contracts/Progress/TrainingInsightDtos.cs`
- Create: `src/TrackZ.Application/Progress/GetWeeklyInsights/GetWeeklyInsightsQuery.cs`
- Create: `src/TrackZ.Application/Progress/GetExerciseInsight/GetExerciseInsightQuery.cs`
- Create: `src/TrackZ.Infrastructure/Progress/TrainingInsightReadStore.cs`
- Modify: `src/TrackZ.Infrastructure/DependencyInjection.cs`
- Modify: `src/TrackZ.Api/Endpoints/ProgressEndpoints.cs`
- Modify: `tests/TrackZ.Api.Tests/Progress/ProgressEndpointTests.cs`
- Create: `tests/TrackZ.Infrastructure.Tests/Progress/TrainingInsightReadStoreTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: weekly and per-exercise authenticated endpoints.

- [ ] **Step 1: Write failing auth/serialization tests**

```csharp
Assert.Equal(HttpStatusCode.Unauthorized,
    (await _client.GetAsync("/api/v1/progress/insights/weekly")).StatusCode);
Assert.Equal(HttpStatusCode.Unauthorized,
    (await _client.GetAsync($"/api/v1/progress/insights/exercises/{exerciseId:D}")).StatusCode);

var weekly = await SendAuthorized<WeeklyInsightSummaryDto>("/api/v1/progress/insights/weekly");
var detail = await SendAuthorized<ExerciseInsightDetailDto>($"/api/v1/progress/insights/exercises/{exerciseId:D}");
Assert.Equal(1, weekly.RulesVersion);
Assert.Equal(exerciseId, detail.ExerciseId);
Assert.DoesNotContain("headlineText", JsonSerializer.Serialize(weekly), StringComparison.OrdinalIgnoreCase);
```

Add an authenticated 404 test for an exercise absent from that user's history.

- [ ] **Step 2: Run API tests and verify RED**

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter FullyQualifiedName~ProgressEndpointTests --verbosity minimal
```

Expected: FAIL because routes/contracts do not exist.

- [ ] **Step 3: Add contracts and focused read interface**

Create `WeeklyInsightSummaryDto`, `ExerciseComparisonInsightDto`, `ExerciseInsightDetailDto`, `ExerciseInsightSessionDto`, and `TrainingInsightSetDto`. Use contract enums mirroring domain values; all facts remain structured.

```csharp
public interface ITrainingInsightReadStore
{
    Task<WeeklyInsightSummaryDto> GetWeeklyAsync(Guid userId, CancellationToken cancellationToken);
    Task<ExerciseInsightDetailDto?> GetExerciseAsync(Guid userId, Guid exerciseId, CancellationToken cancellationToken);
}
```

Queries pass only `ICurrentUser.UserId`; reject an empty exercise ID before calling the store.

- [ ] **Step 4: Implement store and routes**

The store reads only the authenticated user's projection, groups sessions, calls the classifier, resolves exercise names, and maps DTOs. Map:

```csharp
GET /api/v1/progress/insights/weekly
GET /api/v1/progress/insights/exercises/{exerciseId:guid}
```

Both require authorization. The detail route returns 404 for null without revealing cross-account existence.

- [ ] **Step 5: Verify API GREEN and commit**

Run Step 2. Keep the PostgreSQL read-store ownership/boundary tests pending until authorized.

```bash
git add src/TrackZ.Contracts/Progress/TrainingInsightDtos.cs src/TrackZ.Application/Progress/GetWeeklyInsights src/TrackZ.Application/Progress/GetExerciseInsight src/TrackZ.Infrastructure/Progress/TrainingInsightReadStore.cs src/TrackZ.Infrastructure/DependencyInjection.cs src/TrackZ.Api/Endpoints/ProgressEndpoints.cs tests/TrackZ.Api.Tests/Progress/ProgressEndpointTests.cs tests/TrackZ.Infrastructure.Tests/Progress/TrainingInsightReadStoreTests.cs
git commit -m "feat: expose structured training insights"
```

---

### Task 5: Account-scoped Mobile source and freshness

**Files:**
- Create: `src/TrackZ.Mobile.Core/Features/Insights/TrainingInsightSource.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Modify: `src/TrackZ.Mobile/Features/Exercises/Services/MauiExerciseServices.cs`
- Create: `tests/TrackZ.Mobile.Tests/Insights/TrainingInsightSourceTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs`

**Interfaces:**
- Consumes: Task 4 endpoints, `ILocalWorkoutRepository`, and `IAccountSessionBoundary`.
- Produces: `ITrainingInsightSource`, `TrainingInsightSnapshot`, and freshness state.

- [ ] **Step 1: Write failing cache, freshness, and account tests**

```csharp
[Fact]
public async Task Newer_local_completion_marks_cached_insight_stale()
{
    var source = CreateSource(
        cached: Snapshot(computedThrough: At(10)),
        latestLocalCompletion: At(11));

    var result = await source.GetCachedAsync();

    Assert.Equal(TrainingInsightFreshness.NewerLocalCompletion, result!.Freshness);
}

[Fact]
public async Task Reset_during_refresh_prevents_cache_write()
{
    var fixture = GatedRefreshFixture.Create();
    var refresh = fixture.Source.RefreshAsync();
    await fixture.Api.Entered.Task;
    await fixture.Boundary.ResetAsync(_ => Task.CompletedTask);
    fixture.Api.Release(WeeklyDto());

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
    Assert.Null(await fixture.Cache.ReadAsync());
}
```

Also test atomic `.tmp` replacement, invalid JSON, clear, cached-offline use, detail 404/null, and cancellation.

Define the test fixtures in the same file with per-test temporary paths and fixed timestamps. `GatedRefreshFixture` exposes `Source`, `Api`, `Boundary`, and `Cache` exactly as used above; `CreateSource` accepts the cached DTO and latest local completion shown in the test.

- [ ] **Step 2: Run tests and verify RED**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainingInsightSourceTests|FullyQualifiedName~MauiCompositionTests" --verbosity minimal
```

Expected: FAIL because source/cache registrations do not exist.

- [ ] **Step 3: Add exact Mobile interfaces**

```csharp
public enum TrainingInsightFreshness
{
    Current = 1,
    NewerLocalCompletion = 2
}

public sealed record TrainingInsightSnapshot(
    WeeklyInsightSummaryDto Weekly,
    DateTimeOffset CachedAt,
    TrainingInsightFreshness Freshness);

public interface ITrainingInsightSource
{
    Task<TrainingInsightSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default);
    Task<TrainingInsightSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
    Task<ExerciseInsightDetailDto?> GetExerciseAsync(Guid exerciseId, CancellationToken cancellationToken = default);
}
```

Add `TrackZTrainingInsightApi`, `TrainingInsightCache`, and `TrainingInsightSource`. Follow `ProgressSnapshotCache` atomic write and account-generation patterns. Compare the newest live local completed-workout time with `ComputedThrough` before returning a snapshot.

- [ ] **Step 4: Register and clear the cache**

Register API/cache/source in `MauiProgram` with cache path `training-insights.json`. Extend the existing account-data clearing service to clear this cache beside `ProgressSnapshotCache`.

- [ ] **Step 5: Verify GREEN and commit**

Run Step 2, then:

```bash
git add src/TrackZ.Mobile.Core/Features/Insights/TrainingInsightSource.cs src/TrackZ.Mobile/MauiProgram.cs src/TrackZ.Mobile/Features/Exercises/Services/MauiExerciseServices.cs tests/TrackZ.Mobile.Tests/Insights/TrainingInsightSourceTests.cs tests/TrackZ.Mobile.Tests/Architecture/MauiCompositionTests.cs
git commit -m "feat: cache account training insights"
```

---

### Task 6: Localized Home narrative and factual fallback

**Files:**
- Create: `src/TrackZ.Mobile.Core/Features/Insights/TrainingInsightResources.cs`
- Create: `src/TrackZ.Mobile.Core/Resources/TrainingInsightStrings.resx`
- Create: `src/TrackZ.Mobile.Core/Resources/TrainingInsightStrings.th.resx`
- Create: `src/TrackZ.Mobile.Core/Features/Insights/TrainingInsightPresentation.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- Modify: `src/TrackZ.Mobile/Features/Train/TrainPage.xaml`
- Create: `tests/TrackZ.Mobile.Tests/Insights/TrainingInsightPresentationTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs`

**Interfaces:**
- Consumes: Task 5 snapshots and Phase 1 factual latest/best presentation.
- Produces: immutable `HomeWeeklyInsightPresentation`, `ShowWeeklyInsight`, `ShowFactualLatestPerformance`, and updating state.

- [ ] **Step 1: Write failing bilingual presentation tests**

```csharp
[Fact]
public void Home_presentation_limits_items_and_contains_no_icons()
{
    var result = TrainingInsightPresenter.PresentHome(
        SummaryWithThreeHighlightsAndTwoWatchItems(),
        TrainingInsightResources.Thai,
        WeightDisplayUnit.Kilograms);

    Assert.Equal(2, result.Highlights.Count);
    Assert.Single(result.WatchItems);
    Assert.DoesNotMatch("[📈💪🔥⚠️🏆↑↓→↗]", result.AllText);
}

[Fact]
public async Task Newer_local_completion_uses_factual_card_and_updating_copy()
{
    var viewModel = CreateHomeWithInsight(TrainingInsightFreshness.NewerLocalCompletion);
    await viewModel.LoadAsync();

    Assert.False(viewModel.ShowWeeklyInsight);
    Assert.True(viewModel.ShowFactualLatestPerformance);
    Assert.Equal("ภาพรวมกำลังอัปเดต", viewModel.InsightUpdatingText);
}
```

Add exact English/Thai cases for all headline kinds, outcomes, record kinds, count formats, no history, one session, no repeats, returned, changed mode, and inconclusive facts.

`SummaryWithThreeHighlightsAndTwoWatchItems` constructs a DTO with exactly three ordered highlights and two ordered watch entries, proving that the Home presenter bounds its output. `CreateHomeWithInsight` uses a fixed cached source and the existing fixed dashboard/profile test doubles.

- [ ] **Step 2: Run tests and verify RED**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainingInsightPresentationTests|FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~MomentumHomePresentationTests" --verbosity minimal
```

Expected: FAIL because resources/presenter/state do not exist.

- [ ] **Step 3: Add focused typed resources and pure presenter**

Create `TrainingInsightTextSet` and `TrainingInsightResources.ForCulture` with the existing RESX manager pattern. `TrainingInsightPresenter` accepts DTOs, text, and display unit and returns immutable UI records. It may format facts, but it must not classify measurements.

Use the exact Thai copy from the spec and equivalent English. Include `InsightUpdating`, `NoHistory`, `OneSession`, `NoRepeatedExerciseFormat`, `Returned`, `ChangedMode`, and `Inconclusive` keys.

- [ ] **Step 4: Load insight independently on Home**

Inject optional `ITrainingInsightSource` into `TrainTodayViewModel`. Load local dashboard first, profile second, cached insight third, online refresh fourth. Fence each apply with account generation and isolate insight exceptions.

Apply this exact state table:

```text
Current comparable insight: show weekly narrative; hide factual card.
NewerLocalCompletion: hide weekly judgment; show factual card and updating text.
No insight but current latest fact: show factual card.
No history: show supportive empty copy.
```

- [ ] **Step 5: Add the text-only weekly XAML card**

Bind headline, count sentence, at-most-two highlights, one watch item, and `ดูทั้งหมด`. Keep Phase 1 `LatestPerformanceSection` as fallback. Render lists from already-bounded presentation collections. Do not place emoji, decorative images, or status arrows in the section.

- [ ] **Step 6: Verify GREEN and commit**

Run Step 2, then:

```bash
git add src/TrackZ.Mobile.Core/Features/Insights/TrainingInsightResources.cs src/TrackZ.Mobile.Core/Resources/TrainingInsightStrings.resx src/TrackZ.Mobile.Core/Resources/TrainingInsightStrings.th.resx src/TrackZ.Mobile.Core/Features/Insights/TrainingInsightPresentation.cs src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs src/TrackZ.Mobile/Features/Train/TrainPage.xaml tests/TrackZ.Mobile.Tests/Insights/TrainingInsightPresentationTests.cs tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs
git commit -m "feat: tell weekly performance story on home"
```

---

### Task 7: Progress weekly list and Exercise Detail

**Files:**
- Modify: `src/TrackZ.Mobile.Core/Features/Gamification/ProgressDashboardViewModel.cs`
- Modify: `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml`
- Modify: `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml.cs`
- Create: `src/TrackZ.Mobile.Core/Features/Insights/ExerciseInsightDetailViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Progress/ExerciseInsightDetailPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Progress/ExerciseInsightDetailPage.xaml.cs`
- Modify: `src/TrackZ.Mobile/AppShell.xaml.cs`
- Modify: `src/TrackZ.Mobile/MauiProgram.cs`
- Create: `tests/TrackZ.Mobile.Tests/Insights/ExerciseInsightDetailViewModelTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Gamification/ProgressDashboardViewModelTests.cs`
- Create: `tests/TrackZ.Mobile.Tests/NativeIos/InsightProgressPresentationTests.cs`

**Interfaces:**
- Consumes: Tasks 5–6.
- Produces: Progress weekly narrative/list, route `exercise-insight-detail`, and evidence-focused detail page.

- [ ] **Step 1: Write failing Progress and detail tests**

Assert inflated Progress section order is title → weekly insight → exercise list → gamification → error. Add detail tests for newest-first sessions, set-order preservation, exact compared pair, kg/lb live change, invalid ID, cancellation, and 404/no-history copy.

`DetailDtoWithTwoSessions` uses fixed workout IDs and intentionally supplies each session's sets out of order. `CreateDetailViewModel` returns the production view model with a fixed insight source, mutable weight preference, and English text resources.

```csharp
[Fact]
public async Task Detail_preserves_session_and_set_order()
{
    var sut = CreateDetailViewModel(DetailDtoWithTwoSessions());
    await sut.LoadAsync(ExerciseId);

    Assert.Equal([NewerWorkoutId, OlderWorkoutId], sut.Sessions.Select(item => item.WorkoutId));
    Assert.Equal([0, 1, 2], sut.Sessions[0].Sets.Select(item => item.Order));
}
```

- [ ] **Step 2: Run tests and verify RED**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~ProgressDashboardViewModelTests|FullyQualifiedName~ExerciseInsightDetailViewModelTests|FullyQualifiedName~InsightProgressPresentationTests" --verbosity minimal
```

Expected: FAIL because weekly/detail presentation and route do not exist.

- [ ] **Step 3: Make Progress the shared Data-second destination**

Inject `ITrainingInsightSource` and `TrainingInsightTextSet` into `ProgressDashboardViewModel`. Populate weekly presentation and an exercise insight collection from DTOs. Move existing Level/XP and badges below those sections in XAML. Exercise rows execute an app-owned navigation command with exercise ID; no classification occurs in the view model.

- [ ] **Step 4: Add Exercise Detail**

```csharp
public sealed class ExerciseInsightDetailViewModel(
    ITrainingInsightSource source,
    IWeightUnitPreference units,
    TrainingInsightTextSet text)
{
    public ObservableCollection<ExerciseInsightSessionPresentation> Sessions { get; } = [];
    public Task LoadAsync(Guid exerciseId, CancellationToken cancellationToken = default);
}
```

The page implements `IQueryAttributable`, rejects missing/empty IDs, shows the compared pair before ordered sessions, and renders a native trend plus an accessible textual list. Register transient page/view model and route `exercise-insight-detail`. Preserve cancellation before Shell route mutation.

- [ ] **Step 5: Verify GREEN and commit**

Run Step 2, then:

```bash
git add src/TrackZ.Mobile.Core/Features/Gamification/ProgressDashboardViewModel.cs src/TrackZ.Mobile.Core/Features/Insights/ExerciseInsightDetailViewModel.cs src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml.cs src/TrackZ.Mobile/Features/Progress/ExerciseInsightDetailPage.xaml src/TrackZ.Mobile/Features/Progress/ExerciseInsightDetailPage.xaml.cs src/TrackZ.Mobile/AppShell.xaml.cs src/TrackZ.Mobile/MauiProgram.cs tests/TrackZ.Mobile.Tests/Insights/ExerciseInsightDetailViewModelTests.cs tests/TrackZ.Mobile.Tests/Gamification/ProgressDashboardViewModelTests.cs tests/TrackZ.Mobile.Tests/NativeIos/InsightProgressPresentationTests.cs
git commit -m "feat: add training insight drill-down"
```

---

### Task 8: End-to-end acceptance and guarded rollout

**Files:**
- Modify: `docs/testing/native-ios-simulator-walkthrough.md`
- Create: `docs/testing/insight-first-home-phase-2-report.md`
- Modify: affected architecture/localization/native consistency tests.

**Interfaces:**
- Consumes: Tasks 1–7.
- Produces: requirement-to-evidence report with explicit environment gates.

- [ ] **Step 1: Write the acceptance matrix**

Map every spec rule to named automated/manual evidence: outcomes, tie-breakers, record proof, Watch, Returned, weekly denominator, body-part eligibility, item limits, ownership, migration/backfill, account cache, pending-sync fallback, Home hierarchy, Progress/detail, localization, units, accessibility, and no-icons copy.

- [ ] **Step 2: Run every non-Docker suite**

```bash
dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --no-restore --verbosity minimal
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --no-restore --verbosity minimal
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --verbosity minimal
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --verbosity minimal
```

Expected: all commands PASS with zero failures.

- [ ] **Step 3: Enforce the PostgreSQL environment gate**

Do not start Docker. Ask for explicit authorization before running:

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseSessionPerformanceProjectionTests|FullyQualifiedName~TrainingInsightReadStoreTests" --verbosity minimal
```

Without authorization, mark Phase 2 `blocked from completion — PostgreSQL evidence pending`; do not claim merge readiness. If later authorized, use Docker only for the approved test window and stop it immediately afterward.

- [ ] **Step 4: Run audits and iOS build**

```bash
git diff --check
rg -n "[📈💪🔥⚠️🏆]|↑|↓|→|↗" src/TrackZ.Mobile/Features/Train src/TrackZ.Mobile/Features/Progress src/TrackZ.Mobile.Core/Features/Insights src/TrackZ.Mobile.Core/Resources/TrainingInsightStrings.resx src/TrackZ.Mobile.Core/Resources/TrainingInsightStrings.th.resx
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios -r iossimulator-arm64 --no-restore -m:1 --verbosity minimal
```

Expected: clean diff; no prohibited insight glyphs; iOS build succeeds with zero warnings/errors.

- [ ] **Step 5: Request independent review**

Use `superpowers:requesting-code-review` for the Phase 2 base/head range. Require explicit review of classifier truthfulness, account isolation, cache freshness, and native UX. Resolve every Critical/Important issue and rerun current evidence.

- [ ] **Step 6: Commit evidence and offer manual acceptance**

```bash
git add docs/testing/native-ios-simulator-walkthrough.md docs/testing/insight-first-home-phase-2-report.md tests
git commit -m "test: verify insight-first home phase two"
```

Report exact test counts and pending gates. Install/launch only after the user asks. Manual acceptance covers ready/active, no history, one history, no repeat, weighted/assisted/bodyweight, Watch, Returned, pending sync, offline, Thai/English, kg/lb, Dynamic Type, VoiceOver, and no decorative icons.
