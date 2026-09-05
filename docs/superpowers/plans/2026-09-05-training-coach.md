# Training coach and home refresh implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the isolated home presentation task and review; integrate sequentially in this checkout.

**Goal:** Implement the approved home and four-step coaching mock, with optional exercise-level check-ins, conservative progression and useful weekly reporting.

**Architecture:** Keep existing workout save/sync unchanged. Add a local coaching journal in the workout SQLite database for assessments, accepted targets, warm-up labels and recovery answers. A deterministic policy reads real workout history and the journal; both home and progress consume the same report. Never present example data as user data. Journal entries are device-local and cleared with private workout data; this limitation must be visible in the app and handoff.

**Tech Stack:** .NET 10, MAUI, SQLite, existing account boundary, Thai/English localization.

**Spec:** User-approved mock images and plain-language flow in this conversation: home `exec-0f97e1d3-c64e-4583-9452-adc9e89c8b71.png`, flow `exec-f261191b-1374-424e-9dd8-b62f017a8ebe.png`, under `/Users/mikeyoshino/.codex/generated_images/01a06cdf-c9c0-7011-98ce-a5b790776311/`.

## Delivery status — 2026-09-05

Tasks1–4 implemented and independently reviewed with no remaining Critical/Important findings. Task5 automated verification:907 mobile tests pass (0 failures/skips); scoped diff checks clean. iOS build/install and real screenshot details are recorded in `.superpowers/sdd/2026-09-05-training-coach/progress.md`. The original checklists below preserve planning requirements; this status records execution.

Device-local journal/sync limitation remains intentional and disclosed in the UI. The full saved check-in flow is behavior-tested with isolated data, but not end-to-end exercised on the user's simulator history. Home/report/unsaved-editor screenshots were checked; exact pixel parity across devices is not asserted.

## Global constraints

- User explicitly approved working in the existing dirty TrackZ checkout on main. Preserve unrelated edits; no commits of pre-existing work, no other API/docker operations.
- Near black background, dark cards, lime primary action, centered TrackZ mark, Noto Sans Thai. Responsive and accessible layouts take precedence over impossible pixel equivalence at every text/device size.
- One optional check-in per workout exercise, after finishing that exercise; not after every set. Skipping never prevents logging or leaving.
- Pain and loss of control block progression. Form is self-reported, never certified by the app.
- Increase repetitions before load. No automatic changes to logged data or future inputs. An accepted target is displayed as a target only.
- Compare identical exercise definition, tracking mode, load type and number of working sets. No cross-machine comparisons; a custom definition represents a distinct machine.
- Warm-ups must be explicitly marked; unclassified legacy sets are labelled as logged sets, not asserted to be working sets.
- Volume check-in thresholds are product heuristics relative to personal history, never overtraining diagnoses or universal safe limits.
- Report load/repetition trends, days trained, and sets by primary body area; no muscle-growth percentages or form score. Explain indirect muscle work is not counted.

## Task 1: Home presentation

**Files:** `src/TrackZ.Mobile/Features/Train/TrainPage.xaml`, `.xaml.cs`, new `TrainingCoachHomeView.cs` in same folder if needed; `TrainTodayViewModel.cs` only for home copy properties (coordinate with controller).

**Interfaces:** Retain existing TrainTodayViewModel bindings and HeroActionCommand. Controller will supply a `TrainingCoachReport` and journal-driven card separately; create named insertion host `CoachHomeHost` between weekly section and latest workout. Weekly host `CoachWeekHost` replaces old numeric large card. Main page code should expose internal `SetCoachContent(View week, View advice)` if needed. Summary link routes to `//progress`; history link routes to `//history` after verifying actual shell route names. Never reuse TrainAgainCommand for a row labelled history.

- [ ] Inspect approved home image and existing tokens/navigation.
- [ ] Replace lime slab with dark hero, compact centered brand, ready/active states, single lime start button. Remove ambient contour backdrop on this page.
- [ ] Place week host, advice host, then compact latest-workout row (actual history metadata). Hide absent history. Keep real errors/retry/loading.
- [ ] Use existing fonts, body 14–16, headings 20–26, 18–20 horizontal gutters, 16 corner radii. Do not use fixed screen heights or mock numbers. Preserve shell tab bar.
- [ ] Verify MAUI compile and review layout source; controller verifies simulator. No exact-source assertion tests; update obsolete visual tests only where the redesign intentionally invalidates them.

## Task 2: Coaching policy and persistence

**Files:** new `src/TrackZ.Mobile.Core/Features/Coach/` models, policy, journal, source; `TrackZLocalDatabase.cs`; tests `Coach/TrainingCoachTests.cs`.

**Interfaces:** `CoachAssessment(Guid WorkoutId, Guid ExerciseId, DateTimeOffset At, int Effort, bool? Controlled, bool Pain, bool Accepted)`; `CoachJournal` persists assessments and warm-up labels as JSON in a SQLite singleton row (versioned schema, under same database private-clear). `TrainingCoachSource.LoadAsync` reads history + active + ExerciseCache + journal with cancellation. `TrainingCoachPolicy.Evaluate` is pure and consumes completed comparable sessions and self-reports.

- [ ] Tests first: missing/form-failed/pain/only-one-session never increases; two distinct comparable easy controlled sessions yield +1 rep; upper range offers smallest known increment only; stale evidence never increases; weekly local date deduplicates sessions; warm-ups excluded; partial current week is not projected; deletions invalidate evidence.
- [ ] Implement assessment/journal validation and SQLite transaction persistence with existing session fencing at callers. Reset on signout.
- [ ] Implement +1 repetition only after two qualifying distinct recent sessions. At configured upper range offer load change only with a known increment, otherwise explain keep current and ask smallest increment. Inconsistent, hard or absent reports yield no increase.
- [ ] Count weekly sets and days from actual local workouts, all six body areas including zero. Compare current weekly totals with preceding three complete active weeks; require baseline before warning. Expose reasons, report completeness and device-local notice.

## Task 3: Exercise-level flow

**Files:** new `src/TrackZ.Mobile/Features/Coach/ExerciseCheckInPage.cs`; `SetLoggerPage.xaml/.cs`, `SetLoggerViewModel.cs`; `MauiProgram.cs`.

- [ ] Add explicit finish-exercise action beside add-set footer (only when saved sets exist). Opens optional full-screen check-in without automatically marking workout complete.
- [ ] Steps: last set effort with skip/pain; control question only if easy; recommendation with reason and accepted/keep actions. Save choices per exercise/session and show no repeated mandatory prompts.
- [ ] Allow warm-up labels on saved sets and exclude them from advice/report. Journal is device-local; make this clear in explanatory text.
- [ ] Use account-generation cancellation for every journal write; saving failures retain choices and expose retry. Back/skip does not discard logged sets.

## Task 4: Shared home insights and weekly report

**Files:** new `src/TrackZ.Mobile/Features/Coach/CoachViews.cs`; TrainPage integration; ExerciseProgressPage integration and XAML; `MauiProgram.cs`.

- [ ] Load shared real report on appearance; no sample data. Home week circles and one most relevant advice/recovery card, advice explanation opens detail.
- [ ] Report: days/goal, working-set count with classification note, six body-area bars, same-load repetition trend, one next action. Keep detailed existing PR/history reachable but secondary.
- [ ] Recovery answer provides rest/keep guidance, supersedes increase while unresolved, deduplicated per body-area/week. No diagnosis or generic danger cap.
- [ ] Handle empty, offline/local-data notice, loading, error/retry and account reset without leaking old user data.

## Task 5: Verification and handoff

- [ ] Run focused policy/journal tests, then full mobile suite. Baseline is 885 passing.
- [ ] Build iOS simulator target, install only com.trackz.app on existing simulator, inspect screenshots of home/flow/report. Compare with mock and fix visual defects.
- [ ] Independent scoped review for safety, account boundaries, data accuracy, navigation and UI. Fix material findings.
- [ ] Record completed tasks, evidence, remaining gaps (including local journal sync limitation); never claim 100% parity without measured matching screenshots.
