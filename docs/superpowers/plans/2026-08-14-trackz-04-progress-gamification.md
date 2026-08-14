# TrackZ Progress and Gamification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compute correct LAST/PR projections and motivate consistency with idempotent XP, levels, weekly streaks, badges, progress APIs, and native summary/profile screens.

**Architecture:** Completed workout changes emit internal application events inside the modular monolith. Deterministic calculators rebuild exercise performance and motivation projections from canonical history. XP uses an immutable ledger with unique source keys; mobile never awards authoritative progress.

**Tech Stack:** .NET 10, MediatR notifications, EF Core/Npgsql, PostgreSQL, .NET MAUI XAML/MVVM, xUnit.

## Global Constraints

- Weighted best: higher weight wins, then higher reps.
- Bodyweight best: higher reps wins.
- Assisted best: lower assistance wins, then higher reps.
- PR celebrations do not award XP.
- Completed workout awards 100 XP; each valid set awards 5 XP capped at 100 set XP per workout; weekly-goal completion awards 150 XP.
- Weekly goal is configurable from 1–7 workouts and uses the user's time zone.
- Missing a goal resets current streak but preserves best streak.
- History edits/deletes recalculate LAST/PR/XP/level/streak/badges and use compensating XP ledger entries.
- Server is authoritative; mobile offline values are visibly provisional.

---

### Task 1: Exercise Performance Calculator and Projection

**Files:**
- Create: `src/TrackZ.Domain/Progress/ExercisePerformance.cs`
- Create: `src/TrackZ.Domain/Progress/PerformanceCalculator.cs`
- Create: `src/TrackZ.Application/Progress/RebuildExercisePerformance/RebuildExercisePerformanceCommand.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/ExercisePerformanceConfiguration.cs`
- Test: `tests/TrackZ.Domain.Tests/Progress/PerformanceCalculatorTests.cs`

**Interfaces:**
- Consumes: completed sets grouped by exercise/tracking mode.
- Produces: `ExercisePerformanceSnapshot(LastPerformedAt, LastBestSet, AllTimeBest)` and persisted user/exercise projection.

- [ ] **Step 1: Write failing comparison tests**

```csharp
[Fact]
public void Weighted_tie_uses_more_reps()
{
    var best = PerformanceCalculator.Best(TrackingMode.Weighted,
        [Set(70m, null, 8), Set(70m, null, 10), Set(67.5m, null, 12)]);
    Assert.Equal((70m, 10), (best.WeightKg, best.Reps));
}

[Fact]
public void Assisted_lower_assistance_wins_then_more_reps()
{
    var best = PerformanceCalculator.Best(TrackingMode.Assisted,
        [Set(null, 30m, 10), Set(null, 25m, 8), Set(null, 25m, 9)]);
    Assert.Equal((25m, 9), (best.AssistedKg, best.Reps));
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter PerformanceCalculatorTests
```

- [ ] **Step 3: Implement deterministic calculator and rebuild command**

`LastBestSet` comes only from the most recent completed session. `AllTimeBest` scans all non-deleted completed sets. An empty history deletes the projection. Upsert projection by unique `(UserId, ExerciseId)`.

- [ ] **Step 4: Run calculator and persistence tests**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter Performance
dotnet test tests/TrackZ.Application.Tests --filter RebuildExercisePerformance
```

- [ ] **Step 5: Commit performance projections**

```bash
git add src/TrackZ.Domain/Progress src/TrackZ.Application/Progress src/TrackZ.Infrastructure/Persistence tests
git commit -m "feat: calculate exercise last and PR projections"
```

### Task 2: XP Ledger and Versioned Levels

**Files:**
- Create: `src/TrackZ.Domain/Gamification/XpLedgerEntry.cs`
- Create: `src/TrackZ.Domain/Gamification/UserProgress.cs`
- Create: `src/TrackZ.Domain/Gamification/LevelThreshold.cs`
- Create: `src/TrackZ.Domain/Gamification/XpRules.cs`
- Create: `src/TrackZ.Application/Gamification/ReconcileWorkoutXp/ReconcileWorkoutXpCommand.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/XpLedgerEntryConfiguration.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Seed/LevelThresholdSeeder.cs`
- Test: `tests/TrackZ.Domain.Tests/Gamification/XpRulesTests.cs`

**Interfaces:**
- Consumes: completed workout ID and current valid-set count.
- Produces: immutable ledger deltas keyed by `(UserId, Reason, SourceId)` and current total/level projection.

- [ ] **Step 1: Write failing XP/idempotency tests**

```csharp
[Theory]
[InlineData(0, 100)]
[InlineData(10, 150)]
[InlineData(30, 200)]
public void Workout_xp_is_100_plus_five_per_set_capped_at_100(int sets, int expected)
    => Assert.Equal(expected, XpRules.ForCompletedWorkout(sets));

[Fact]
public async Task Reconciling_same_workout_twice_does_not_duplicate_xp()
{
    await _handler.Handle(new ReconcileWorkoutXpCommand(_workoutId), default);
    await _handler.Handle(new ReconcileWorkoutXpCommand(_workoutId), default);
    Assert.Single(_db.XpLedgerEntries.Where(x => x.SourceId == _workoutId));
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter XpRulesTests
dotnet test tests/TrackZ.Application.Tests --filter ReconcileWorkoutXp
```

- [ ] **Step 3: Implement ledger, compensation, and levels**

Use reason keys `WorkoutCompleted`, `WorkoutSets`, `WeeklyGoal`, and `Correction`. Seed a versioned monotonic threshold table; determine level as the highest threshold not exceeding total XP. On edit/delete, append the delta needed to make the source's net XP equal the current rule result.

- [ ] **Step 4: Add migration and run tests**

```bash
dotnet ef migrations add AddGamification --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --output-dir Persistence/Migrations
dotnet test tests/TrackZ.Domain.Tests --filter Gamification
dotnet test tests/TrackZ.Application.Tests --filter Gamification
```

- [ ] **Step 5: Commit XP and levels**

```bash
git add src/TrackZ.Domain/Gamification src/TrackZ.Application/Gamification src/TrackZ.Infrastructure/Persistence tests
git commit -m "feat: add idempotent XP and levels"
```

### Task 3: Weekly Goal and Streak Evaluation

**Files:**
- Create: `src/TrackZ.Domain/Gamification/StreakState.cs`
- Create: `src/TrackZ.Domain/Gamification/WeeklyGoalCalculator.cs`
- Create: `src/TrackZ.Domain/Gamification/YearWeek.cs`
- Create: `src/TrackZ.Application/Gamification/EvaluateStreak/EvaluateStreakCommand.cs`
- Test: `tests/TrackZ.Domain.Tests/Gamification/WeeklyGoalCalculatorTests.cs`

**Interfaces:**
- Consumes: weekly goal `1..7`, user time-zone ID, and completed-workout instants.
- Produces: current/best streak and idempotent weekly-goal XP source keyed by local ISO week.

- [ ] **Step 1: Write failing time-zone boundary and reset tests**

```csharp
[Fact]
public void Workout_is_counted_in_users_local_week()
{
    var result = WeeklyGoalCalculator.Evaluate(goal: 2, timeZone: "Asia/Bangkok",
        completedAtUtc: [Utc("2026-08-09T18:30:00Z"), Utc("2026-08-14T10:00:00Z")]);
    Assert.True(result.CurrentWeekGoalMet);
}

[Fact]
public void Missed_week_resets_current_but_keeps_best()
{
    var state = new StreakState(current: 4, best: 6);
    state.ApplyWeek(goalMet: false, new YearWeek(2026, 33));
    Assert.Equal(0, state.CurrentWeeks);
    Assert.Equal(6, state.BestWeeks);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter WeeklyGoalCalculatorTests
```

- [ ] **Step 3: Implement calendar evaluation**

Convert UTC completion instants through the stored IANA time zone, group by ISO local week, and count distinct completed workout IDs. Re-evaluate affected weeks after edits/deletes. Award weekly XP once using source ID derived from user ID plus ISO year/week.

- [ ] **Step 4: Run streak tests**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter "WeeklyGoal|Streak"
dotnet test tests/TrackZ.Application.Tests --filter EvaluateStreak
```

- [ ] **Step 5: Commit streaks**

```bash
git add src/TrackZ.Domain/Gamification src/TrackZ.Application/Gamification tests
git commit -m "feat: evaluate weekly workout streaks"
```

### Task 4: Badge Definitions and Awards

**Files:**
- Create: `src/TrackZ.Domain/Gamification/BadgeDefinition.cs`
- Create: `src/TrackZ.Domain/Gamification/UserBadge.cs`
- Create: `src/TrackZ.Application/Gamification/EvaluateBadges/EvaluateBadgesCommand.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Seed/BadgeDefinitionSeeder.cs`
- Test: `tests/TrackZ.Application.Tests/Gamification/BadgeEvaluationTests.cs`

**Interfaces:**
- Consumes: completed-workout count, best streak, distinct exercise count, and PR count.
- Produces: idempotent earned/revoked badge projection and newly-earned badge IDs for summary animation.

- [ ] **Step 1: Write failing badge tests**

```csharp
[Fact]
public async Task Fourth_consecutive_goal_week_awards_four_week_streak_once()
{
    await SeedStreakAsync(4);
    var first = await _handler.Handle(new EvaluateBadgesCommand(_userId), default);
    var second = await _handler.Handle(new EvaluateBadgesCommand(_userId), default);
    Assert.Contains("streak-4", first.NewlyEarnedBadgeKeys);
    Assert.Empty(second.NewlyEarnedBadgeKeys);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Application.Tests --filter BadgeEvaluationTests
```

- [ ] **Step 3: Seed exact MVP badge families and evaluator**

Seed `first-workout`, `workouts-10`, `workouts-25`, `workouts-50`, `streak-4`, `streak-8`, `streak-12`, `exercises-10`, `exercises-25`, `first-pr`, and `prs-10`. Definitions contain localized resource keys, criteria version, and icon key. Reconciliation removes an invalid earned projection after history deletion but retains a separate audit event.

- [ ] **Step 4: Run badge tests**

```bash
dotnet test tests/TrackZ.Application.Tests --filter Badge
```

- [ ] **Step 5: Commit badges**

```bash
git add src/TrackZ.Domain/Gamification src/TrackZ.Application/Gamification src/TrackZ.Infrastructure/Persistence/Seed tests
git commit -m "feat: award consistency badges"
```

### Task 5: Progress and Gamification API Queries

**Files:**
- Create: `src/TrackZ.Contracts/Progress/ProgressSummaryDto.cs`
- Create: `src/TrackZ.Contracts/Gamification/GamificationProfileDto.cs`
- Create: `src/TrackZ.Application/Progress/GetSummary/GetProgressSummaryQuery.cs`
- Create: `src/TrackZ.Application/Gamification/GetProfile/GetGamificationProfileQuery.cs`
- Create: `src/TrackZ.Api/Endpoints/ProgressEndpoints.cs`
- Create: `src/TrackZ.Api/Endpoints/GamificationEndpoints.cs`
- Test: `tests/TrackZ.Api.Tests/Progress/ProgressEndpointTests.cs`

**Interfaces:**
- Consumes: projections created in Tasks 1–4.
- Produces: `/api/v1/progress/summary` and `/api/v1/gamification/profile`.

- [ ] **Step 1: Write failing endpoint contract test**

```csharp
[Fact]
public async Task Profile_returns_level_xp_streak_goal_and_badges()
{
    var response = await _client.GetFromJsonAsync<GamificationProfileDto>("/api/v1/gamification/profile");
    Assert.Equal(12, response!.Level);
    Assert.Equal(4, response.CurrentStreakWeeks);
    Assert.Contains(response.Badges, x => x.Key == "streak-4");
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Api.Tests --filter ProgressEndpointTests
```

- [ ] **Step 3: Implement user-scoped projection queries**

Return total/weekly volume, completed workouts, exercises progressing, current PR summaries, total XP, current level/threshold, weekly-goal progress, current/best streak, earned badges, and newly-earned summary items. Use DTO projections; never serialize EF entities.

- [ ] **Step 4: Run API tests**

```bash
dotnet test tests/TrackZ.Api.Tests --filter "Progress|Gamification"
```

- [ ] **Step 5: Commit progress APIs**

```bash
git add src/TrackZ.Contracts src/TrackZ.Application/Progress src/TrackZ.Application/Gamification src/TrackZ.Api/Endpoints tests
git commit -m "feat: expose progress and gamification"
```

### Task 6: MAUI Summary, Progress, and Profile Screens

**Files:**
- Create: `src/TrackZ.Mobile/Features/Summary/WorkoutSummaryPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Summary/WorkoutSummaryViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Progress/ExerciseProgressPage.xaml`
- Create: `src/TrackZ.Mobile/Features/Progress/ExerciseProgressViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Profile/ProfilePage.xaml`
- Create: `src/TrackZ.Mobile/Features/Profile/ProfileViewModel.cs`
- Create: `src/TrackZ.Mobile/Components/XpBar.xaml`
- Create: `src/TrackZ.Mobile/Components/BadgeTile.xaml`
- Test: `tests/TrackZ.Mobile.Tests/Gamification/GamificationViewModelTests.cs`

**Interfaces:**
- Consumes: progress/gamification DTOs and offline cached snapshots.
- Produces: workout summary, exercise chart/history, level/XP, weekly goal, streak, and badge collection UI.

- [ ] **Step 1: Write failing provisional/offline test**

```csharp
[Fact]
public async Task Offline_summary_marks_xp_as_pending_server_confirmation()
{
    _connectivity.IsOnline.Returns(false);
    await _sut.LoadAsync(_completedLocalWorkoutId);
    Assert.True(_sut.IsProgressProvisional);
    Assert.Contains("pending", _sut.SyncAccessibilityText, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter GamificationViewModelTests
```

- [ ] **Step 3: Implement screens and cache behavior**

Render cached server projections immediately. Summary displays local workout volume/sets instantly but labels XP/PR/badges provisional until sync response. Charts use canonical kg and convert axis/labels at presentation. Profile permits weekly goal `1..7` and shows current/best streak.

- [ ] **Step 4: Run tests and platform builds**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter "Summary|Progress|Gamification"
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios
```

- [ ] **Step 5: Commit the progress milestone**

```bash
git add src/TrackZ.Mobile/Features src/TrackZ.Mobile/Components tests/TrackZ.Mobile.Tests
git commit -m "feat: show progress and motivation"
```

### Task 7: Recalculation After History Changes

**Files:**
- Create: `src/TrackZ.Application/Progress/ReconcileUserProgress/ReconcileUserProgressCommand.cs`
- Create: `tests/TrackZ.Api.Tests/Progress/HistoryRecalculationTests.cs`

**Interfaces:**
- Consumes: edited/deleted workout ID and affected exercise/user IDs.
- Produces: one transactional reconciliation of exercise projections, XP compensation, levels, streaks, and badges.

- [ ] **Step 1: Write failing full-recalculation test**

```csharp
[Fact]
public async Task Deleting_PR_workout_recalculates_every_dependent_projection()
{
    await SeedPrWorkoutWithXpStreakAndBadgeAsync();
    await DeleteWorkoutThroughSyncAsync(_prWorkoutId);
    var state = await ReadProgressStateAsync();
    Assert.Equal(70m, state.AllTimeBest.WeightKg);
    Assert.Equal(state.ExpectedXpAfterDelete, state.TotalXp);
    Assert.False(state.Badges.Any(x => x.Key == "first-pr"));
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Api.Tests --filter HistoryRecalculationTests
```

- [ ] **Step 3: Implement ordered reconciliation**

In the same server transaction that accepts the history mutation, rebuild affected exercise performance, reconcile workout/weekly XP, recompute level/streak, and evaluate badges. Publish the new projection version into the sync change log.

- [ ] **Step 4: Run full progress suite**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter "Progress|Gamification"
dotnet test tests/TrackZ.Application.Tests --filter "Progress|Gamification"
dotnet test tests/TrackZ.Api.Tests --filter "Progress|Gamification|HistoryRecalculation"
```

- [ ] **Step 5: Commit reconciliation**

```bash
git add src/TrackZ.Application/Progress tests/TrackZ.Api.Tests
git commit -m "fix: reconcile progress after history changes"
```

Plan 4 is complete when projections follow the approved comparison rules, XP is idempotent and reversible, weekly streaks respect Asia/Bangkok and other user zones, badges reconcile after edits, and mobile clearly separates provisional from confirmed progress.
