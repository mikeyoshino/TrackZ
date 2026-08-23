# TrackZ Insight-First Home Design

Date: 2026-08-24
Status: Approved
Replaces the Home-specific presentation decisions in `2026-08-22-trackz-momentum-home-design.md` where the two documents conflict.

## Purpose

Redesign TrackZ Home so a user can begin or continue training immediately and understand progress without interpreting raw sets, repetitions, volume, XP, or unlabeled ratios.

The approved direction is **Action-first, Insight-second, Data-third**:

1. Home provides the fastest path into training.
2. Home explains progress in plain language.
3. Progress contains the detailed evidence and drill-down.

The UI uses no decorative icons, emoji, or symbolic status arrows. Meaning comes from explicit copy, hierarchy, and supporting facts. Color is never the only status signal.

## Product Position

TrackZ reports **training performance**, not muscle growth. Weight and repetition history can support a performance comparison, but it cannot prove that a user's muscle mass increased.

The insight system must prefer `ยังสรุปไม่ได้` over a confident but unsupported conclusion. It must not use total volume, an estimated 1RM, or an opaque score as the primary user-facing truth.

Resistance-training research also does not justify presenting 8–12 repetitions as the only hypertrophy-producing range. TrackZ therefore compares sufficiently similar performances rather than declaring one repetition range universally correct:

- https://pubmed.ncbi.nlm.nih.gov/41843416/
- https://pubmed.ncbi.nlm.nih.gov/33433148/

## Current-State Findings

The current Home motivation strip is technically correct but difficult to understand on first view:

- `1/3` means one completed workout out of a weekly goal of three.
- `ต่อเนื่องรายสัปดาห์` is the count of consecutive weeks in which the weekly goal was met.
- Level/XP is gamification and does not answer whether training performance changed.

The current progress contract cannot support the proposed weekly narrative:

- `ProgressSummaryDto` contains the latest best set and all-time best set per exercise, not the immediately previous session or a multi-session trend.
- `ExercisesProgressing` is currently populated with the number of exercise performance records. It is not a count of exercises that improved.
- Weekly volume covers weighted sets only and cannot be used as a universal weighted, assisted, and bodyweight progress measure.
- The existing performance projection ranks weighted sets by load first, so a heavy low-repetition set can outrank a hypertrophy-oriented set without proving better comparable performance.

Phase 1 must therefore remain factual. Phase 2 introduces the data and rules needed for truthful interpretation.

## Approved Information Architecture

Home remains a vertically scrolling native page with the existing tab bar.

### 1. Context

Show one quiet contextual line and one state-aware headline:

- `วันนี้ · สัปดาห์ที่ 35`
- No active workout: `พร้อมเมื่อคุณพร้อม`
- Active workout: `กำลังไปได้ดี`.

### 2. One primary action

Home has exactly one visually primary action:

- No active workout: `เริ่มออกกำลังกาย`
- Active workout: `ออกกำลังกายต่อ`

Home must not label this action `เพิ่มท่า`; selecting exercises is a later step inside workout creation.

### 3. Train again

When no workout is active and a completed workout is safely repeatable, show `ฝึกแบบครั้งก่อน` directly below the primary action.

The row uses `เปิด` rather than a chevron or decorative icon as its affordance. The existing exact-repeat and idempotency rules remain unchanged.

The row is hidden while a workout is active.

### 4. Weekly goal and streak

Replace the three metric cards with one plain-language block:

- `สัปดาห์นี้ฝึกแล้ว 1 จากเป้าหมาย 3 ครั้ง`
- `ทำถึงเป้า 4 สัปดาห์ติด`
- One supporting progress bar.

The block renders only authoritative or cached-authoritative values. It never fabricates `0/0` while data is unavailable.

Level/XP leaves Home and remains available in the secondary gamification section on Progress.

### 5. Performance

Phase 1 shows one factual recent exercise:

```text
ผลงานท่าล่าสุด
Bench Press
ครั้งล่าสุด     75 กก. × 8 ครั้ง
สถิติสูงสุด     80 กก. × 8 ครั้ง
ดูข้อมูลทั้งหมด
```

Phase 2 replaces this with a weekly narrative:

```text
ภาพรวมผลงาน                         ดูทั้งหมด
ผลงานดีขึ้น
สัปดาห์นี้คุณทำได้ดีขึ้นในหลายท่า
5 ท่าดีขึ้น · 3 ท่าใกล้เคียงเดิม · 1 ท่าวันนี้ทำได้น้อยลง

จุดเด่น
อกพัฒนาชัดที่สุด
Incline Bench Press เพิ่มจาก 30 เป็น 32.5 กก.

ยังพัฒนาต่อเนื่อง
หลังทำได้ดีขึ้น
Seated Row ใช้น้ำหนักเดิม แต่ทำได้เพิ่ม 3 ครั้ง

ควรจับตา
Shoulder Press
ทำได้น้อยลง 3 ครั้งติดต่อกันที่เปรียบเทียบกันได้
```

Home limits the narrative to:

- One headline.
- One status-count sentence.
- At most two highlights.
- At most one watch item.
- One `ดูทั้งหมด` action.

## Phase 1: Comprehension Using Existing Data

Phase 1 is intentionally presentation-only. It does not invent a comparison engine.

### Changes

- Preserve Start/Continue as the single primary action.
- Keep `ฝึกแบบครั้งก่อน` immediately below the hero.
- Replace `1/3`, weekly streak, and Level/XP cards with the weekly goal sentence and streak sentence.
- Remove Level/XP from Home.
- Rename and reformat Recent Momentum as factual latest exercise information.
- Use exact latest and current all-time-best fields from the existing contract.
- Keep shared kg/lb behavior.
- Change the Home-to-Progress action to `ดูข้อมูลทั้งหมด`.

### Explicit non-goals

Phase 1 must not display:

- `ดีขึ้น`, `คงที่`, or `ลดลง`.
- A count such as `ดีขึ้น 6 จาก 9 ท่า`.
- A muscle-group conclusion.
- A claim that a record was newly achieved this week.
- A volume-percentage interpretation.

## Phase 2: Training Insight Domain

### Comparison unit

An insight compares a completed session for one exercise with the immediately previous comparable completed session for the same exercise.

A comparison requires:

- The same exercise definition.
- The same `TrackingMode`.
- Valid, non-deleted sets from completed, non-deleted workouts.
- A representative set pair whose repetitions differ by no more than two.
- No gap longer than eight weeks between the two sessions.

Canonical calculation uses kilograms. Unit conversion is presentation-only.

### Representative pair selection

For weighted and assisted exercises, create every current/previous set pair with an absolute repetition difference of at most two. Choose deterministically by:

1. Smallest repetition difference.
2. Hardest comparable performance:
   - Weighted: highest lower-of-the-two loads.
   - Assisted: lowest higher-of-the-two assistance values.
3. Highest lower-of-the-two repetition counts.
4. Stable set order and set ID tie-breakers.

For bodyweight exercises, compare the highest valid repetition set in each session.

If no pair qualifies, the result is `InsufficientComparison`; the classifier must not fall back to estimated 1RM or volume.

### Per-comparison outcomes

All thresholds below are product comparison rules, not claims that smaller changes are physiologically meaningless.

#### Weighted

- `ImprovedLoad`: current load is greater and current repetitions are no more than two below previous repetitions.
- `ImprovedReps`: load is equal and current repetitions are at least two greater.
- `Stable`: load is equal and repetitions differ by at most one.
- `LowerToday`: current load is lower and repetitions are no more than one greater, or load is equal and repetitions are at least two lower.
- `Inconclusive`: load and repetitions move in opposite directions outside these rules.

#### Assisted

Lower assistance is harder and therefore reverses the load direction:

- `ImprovedAssistance`: current assistance is lower and current repetitions are no more than two below previous repetitions.
- `ImprovedReps`: assistance is equal and current repetitions are at least two greater.
- `Stable`: assistance is equal and repetitions differ by at most one.
- `LowerToday`: current assistance is higher and repetitions are no more than one greater, or assistance is equal and repetitions are at least two lower.
- `Inconclusive`: assistance and repetitions move in opposite directions outside these rules.

#### Bodyweight

- `ImprovedReps`: current repetitions are at least two greater.
- `Stable`: repetitions differ by at most one.
- `LowerToday`: current repetitions are at least two lower.

### Public presentation statuses

The UI maps domain outcomes to explicit text:

- `NewRecord` → `สถิติใหม่`, with the specific record fact.
- Any improved outcome → `ดีขึ้น`, with `เพิ่มน้ำหนัก`, `ใช้แรงช่วยน้อยลง`, or `ทำได้มากขึ้น`.
- `Stable` → `ใกล้เคียงเดิม`.
- One clear lower outcome → `วันนี้ทำได้น้อยลง`.
- Three consecutive clear lower comparisons → `ควรจับตา`.
- No valid comparison → `ยังเปรียบเทียบไม่ได้`.
- A return after more than eight weeks → `กลับมาเล่นท่านี้อีกครั้ง`.

`ควรจับตา` requires three consecutive pairwise `LowerToday` outcomes, which means four comparable sessions. An improved, stable, inconclusive, tracking-mode change, or greater-than-eight-week gap breaks the sequence.

### New-record proof

A `NewRecord` must be proven against all earlier valid sessions while excluding the current session from the baseline.

Supported record facts are:

- Weighted: a current load greater than every earlier valid set within two repetitions, or at least two more repetitions than every earlier valid set at the same load.
- Assisted: current assistance lower than every earlier valid set within two repetitions, or at least two more repetitions than every earlier valid set at the same assistance.
- Bodyweight: most repetitions.

The two-repetition improvement threshold applies to repetition records. When the historical data does not prove one of these facts, the UI must not use `สถิติใหม่`.

## Weekly Aggregation

The weekly summary includes exercises whose latest completed session is inside the user's current local calendar week. The week begins Monday at 00:00 in the profile time zone and ends at the next Monday at 00:00, matching the weekly-goal boundary. Each exercise compares with its own immediately previous comparable session, even when that session occurred in an earlier calendar week.

The aggregate counts:

- `NewRecord` and improved outcomes as improved.
- `Stable` as near the same.
- `LowerToday` and `Watch` as lower today.
- `Inconclusive`, `InsufficientComparison`, changed-mode, and returned-after-gap items as insufficient; they are not included in the comparable denominator.

### Muscle-group summary

A body-part statement such as `อกพัฒนาชัดที่สุด` requires:

- At least two comparable exercises for that body part in the current week.
- More than half of those exercises classified as new-record or improved.

Choose the leading body part by improved proportion, then improved count, then stable enum order. If no body part meets the rule, use an exercise-specific highlight.

### Highlight ranking

Select at most two highlights in this order:

1. Proven new records.
2. A qualifying muscle-group summary.
3. Improved load or assistance.
4. Improved repetitions.

Use completion time and exercise ID as deterministic tie-breakers.

Select at most one watch item, preferring the longest current lower streak, then the most recently performed exercise.

### Weekly headline

Choose one deterministic neutral headline with this precedence:

1. No comparable exercises: use the matching cold/sparse-state copy below.
2. A watch item exists: `มีบางท่าที่ควรติดตามต่อ`.
3. Improved is strictly the largest comparable group: `สัปดาห์นี้คุณทำได้ดีขึ้นในหลายท่า`.
4. Stable is tied for or is the largest group: `สัปดาห์นี้ผลงานส่วนใหญ่ใกล้เคียงเดิม`.
5. Otherwise lower is largest: `สัปดาห์นี้มีบางวันที่คุณทำได้น้อยลง`.

New-record copy appears in a highlight and does not override these count-based headline rules.

## Cold, Sparse, and Ambiguous States

- No completed sessions: `เริ่มบันทึกการฝึก แล้วเราจะช่วยติดตามผลงาน`.
- One session for an exercise: show the latest fact and `ฝึกท่านี้อีกครั้งเพื่อดูการเปลี่ยนแปลง`.
- No repeated exercise this week: `สัปดาห์นี้คุณลองท่าใหม่ X ท่า ยังไม่มีท่าที่เล่นซ้ำให้เทียบ`.
- Return after more than eight weeks: `กลับมาเล่นท่านี้อีกครั้ง`; exclude it from the trend denominator.
- Changed tracking mode: `รูปแบบการบันทึกเปลี่ยนไป จึงยังเปรียบเทียบไม่ได้`.
- Mixed load/repetition direction: describe the two exact performances and display `ยังสรุปไม่ได้`.
- No workout this week: show a neutral weekly-goal state, never a negative performance judgment.

## Backend and Domain Architecture

### Historical projection

Add a durable per-session performance projection rather than expanding the existing last/all-time-only projection beyond its purpose.

`ExerciseSessionPerformance` contains:

- User ID.
- Workout session ID and completion time.
- Workout exercise ID and exercise definition ID.
- Body part and tracking mode at calculation time.
- Ordered valid performance sets.
- Projection rules version.

Workout completion, later set correction, deletion, restoration, and sync reconciliation rebuild the affected session projection and downstream current insight snapshot. Rebuild behavior follows the existing exercise-performance reconciliation pattern.

### Pure classifier

`TrainingInsightClassifier` is a pure, versioned domain service. It accepts ordered session projections and returns machine-readable results. It owns:

- Comparable-pair selection.
- Per-exercise outcome classification.
- Consecutive-lower detection.
- New-record proof.
- Weekly counts and ranking.
- Muscle-group eligibility.

It does not produce Thai or English prose.

### Contracts

Introduce these contracts:

```text
ExerciseComparisonInsightDto
  ExerciseId, ExerciseName, BodyPart, TrackingMode
  LatestSession, PreviousComparableSession
  Status, Reason
  ConsecutiveLowerComparisons
  IsNewRecord, RecordKind
  RulesVersion

WeeklyInsightSummaryDto
  LocalWeekStart, LocalWeekEnd
  ImprovedCount, StableCount, LowerCount, InsufficientCount
  ComparableExerciseCount
  Highlights[0..2], WatchItems[0..1]
  ComputedThrough, RulesVersion

ExerciseInsightDetailDto
  ExerciseId, ExerciseName, BodyPart, TrackingMode
  CurrentComparison
  Sessions[] ordered newest first
  RulesVersion

ExerciseInsightSessionDto
  WorkoutId, CompletedAt
  Sets[] in recorded order
```

Measurements remain structured decimal/integer values. The API does not return localized narrative strings.

### API

Expose this progress-domain endpoint:

```text
GET /api/v1/progress/insights/weekly
```

This is not a Home-specific endpoint. Home and Progress consume the same insight contract.

Exercise Detail uses the same user-scoped read model through:

```text
GET /api/v1/progress/insights/exercises/{exerciseId}
```

The endpoint returns `404` when the authenticated user has no live completed history for the exercise. It never reveals whether another account has history for that identifier.

### Mobile source and cache

Add an isolated `ITrainingInsightSource` and account-scoped `TrainingInsightCache` rather than coupling insight availability to Start/Continue or the existing gamification snapshot.

Loading order is:

1. Load local workout state and render Start/Continue and Train again.
2. Load cached weekly-goal/profile data.
3. Load cached insight.
4. Refresh insight when online.

An insight error cannot disable local workout actions.

## Freshness, Offline, and Pending Sync

Cached authoritative insight remains visible offline when no newer local completed workout exists.

When a local completed workout is newer than `ComputedThrough`:

- Do not use the older weekly conclusion to judge the new workout.
- Show factual latest-exercise data from the local workout history.
- Show `ภาพรวมกำลังอัปเดต` in place of the stale conclusion.
- After sync and insight refresh, replace it with the authoritative narrative.

This state uses plain product language. It does not display a technical offline or queue banner.

Account reset clears the account-scoped insight cache and prevents a prior account's results from committing to the current UI.

## Progress and Drill-Down

The existing Progress tab becomes the Data-second destination rather than adding a duplicate Weekly Insight page.

Its approved hierarchy is:

1. Weekly insight narrative.
2. Exercise status list.
3. Per-exercise drill-down.
4. Level/XP and badges as secondary sections.

### Phase 1 navigation

`ดูข้อมูลทั้งหมด` from Home switches to Progress and focuses the most recently performed exercise row. It does not imply that a full historical chart already exists.

### Phase 2 navigation

`ดูทั้งหมด` opens the Weekly Insight section on Progress. Selecting an exercise opens Exercise Detail.

Exercise Detail shows:

- Chronological load and repetition trend.
- The exact latest/previous pair used by the classifier.
- Full set history.
- The machine-readable reason rendered in plain language.
- Why a result is comparable or inconclusive.

The detail view does not expose an opaque score.

## Visual and Copy Rules

- No emoji.
- No decorative icons.
- No arrow glyphs as status labels.
- Text labels remain understandable without color.
- Lime is reserved for the primary action and restrained emphasis.
- Home keeps one primary action.
- Thai is authored as primary product copy, with equivalent English resources.
- Status copy describes facts and avoids blame.
- A one-session decline never becomes an overall negative weekly headline.

## Accessibility

- Every interactive target is at least 44 points.
- Dynamic Type can wrap without overlapping actions or the tab bar.
- VoiceOver receives status plus the supporting fact, not a color name.
- Progress semantics read the full weekly-goal sentence instead of `1/3`.
- Status order and meaning do not depend on visual position alone.
- kg/lb changes update Home, Progress, and Exercise Detail from the shared preference.

## Error Handling

- A local workout database failure that prevents safe one-active-workout verification disables mutation and shows the existing actionable retry state.
- Weekly-goal/profile failure hides unavailable values rather than manufacturing zero.
- Insight failure retains a trustworthy cache when it is not stale relative to local workouts.
- Stale insight after a local completion becomes the neutral updating state.
- Navigation failure leaves Home usable and reports one localized retry action.
- Cancellation, account generation changes, and page disposal prevent stale state commits.

## Verification Strategy

Implementation follows RED → GREEN TDD separately for each phase.

### Phase 1 automated evidence

- Ready and active Home states retain one primary action.
- Train again is directly below the hero and hidden during an active workout.
- Home contains no Level/XP or unlabeled weekly ratio card.
- Weekly goal and streak render exact Thai and English sentences.
- No-authority state does not render fabricated zeros.
- Latest weighted, assisted, and bodyweight facts are correctly labeled.
- Shared kg/lb changes update the existing Home instance.
- Home-to-Progress navigation focuses the expected exercise.
- XAML contains no decorative emoji or status-arrow copy.

### Phase 2 domain evidence

- Deterministic representative-pair selection, including tie-breakers.
- Weighted improved-load, improved-reps, stable, lower, and inconclusive cases.
- Assisted inverse-direction equivalents.
- Bodyweight repetition thresholds.
- Repetition differences greater than two are not forced into a comparison.
- Tracking-mode changes are excluded.
- An eight-week-or-less gap remains eligible and a greater gap becomes returned.
- A watch item requires exactly three consecutive lower comparisons and resets correctly.
- New-record detection excludes the current result from its historical baseline.
- Weekly denominator excludes insufficient items.
- Muscle-group conclusions require two exercises and a strict improved majority.
- Highlight and watch limits and deterministic ordering.

### Phase 2 integration evidence

- Session projections rebuild after completion, edit, delete, restore, and sync reconciliation.
- API returns structured facts and a rules version, not localized prose.
- Cache is isolated by account.
- Offline cache remains visible when fresh relative to local history.
- A newer pending local completion suppresses stale judgment and shows the updating state.
- Home and Progress use the same source and classification results.
- Thai/English and kg/lb render from the same canonical DTO.

### Manual iOS evidence

- Ready and active Home.
- Phase 1 factual card and Phase 2 narrative.
- No-history, one-history, no-repeat, long-gap, and pending-sync states.
- Weighted, assisted, and bodyweight examples.
- Thai and English.
- kg and lb.
- Dynamic Type and VoiceOver.
- Offline launch and refresh recovery.
- No decorative icons or symbolic status arrows.

## Rollout

Phase 1 ships independently and provides immediate comprehension improvements without claiming new intelligence.

Phase 2 ships only after the historical projection, classifier, API, cache freshness, drill-down, and acceptance matrix are complete. A partially implemented Phase 2 must not expose weekly status copy from the existing `ExercisesProgressing` or volume fields.

## Out of Scope

- Prescribing the user's workout.
- Claiming direct muscle-size change.
- Nutrition, sleep, pain, or recovery diagnosis.
- Estimated 1RM as the user-facing truth.
- A universal performance score.
- Comparing changed tracking modes.
- Treating one lower session as failure.
- Replacing detailed workout history with generated prose.

## Approval Record

The user approved:

- Action-first Home.
- Two-phase delivery.
- Level/XP removal from Home.
- Plain-language weekly goal and streak.
- A factual latest/best card in Phase 1.
- Train again immediately below the primary action.
- Transparent comparable-performance rules rather than a score.
- Watch status after three consecutive declines.
- Supportive one-session state.
- Current-week exercises compared with each exercise's latest prior session.
- Muscle-group conclusions only with at least two exercises and an improved majority.
- Direction A narrative layout.
- No icons.
- Progress as the shared Weekly Insight and drill-down destination.

The approved visual exploration is retained under the ignored `.superpowers/brainstorm/` workspace and is not a production dependency.
