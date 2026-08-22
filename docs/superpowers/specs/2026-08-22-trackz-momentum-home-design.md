# TrackZ Momentum Home Design

Date: 2026-08-22  
Status: Approved

## Purpose

Replace the current Train landing page, which is primarily an active-workout resume card and a recent-workout list, with a native iOS-oriented Momentum Home. The new Home must help experienced lifters begin or continue training quickly while showing enough weekly progress to motivate them. It must not prescribe a workout or imply that TrackZ is a coaching application.

The approved visual direction is **Momentum Home**: one state-aware primary action, a quiet motivation strip, one user-controlled repeat shortcut, and one exact recent-performance card.

The persistent visual source of truth is `docs/design/momentum-home-reference.html`. It is standalone and interactive: reviewers can switch ready/active state, kg/lb, and English/Thai without the temporary Virtual Companion URL. Production remains native MAUI XAML; the HTML is a comparison reference, not an implementation runtime.

## Product Principles

1. **Training remains the primary action.** Start or Continue is the only lime primary action on the page.
2. **The user stays in control.** TrackZ recalls what the user did; it does not tell them what they should train.
3. **Game mechanics support rather than dominate.** Weekly goal, streak, and Level/XP are visible, while badges remain on Progress.
4. **Every number is authoritative or cached authoritative data.** Home never fabricates zeros or a personal record.
5. **Offline is normal.** Local workout actions remain available and cached progress remains visible without a technical offline banner.
6. **The page follows the shared native design system.** It uses shared typography, spacing, color, touch-target, motion, localization, and kg/lb resources.

## Scope

### In scope

- Redesign `TrainPage` as Momentum Home.
- Provide distinct no-active-workout and active-workout hero states.
- Show weekly completion, current weekly streak, and Level/XP from the existing gamification profile.
- Provide one “Train again” shortcut based on the newest repeatable completed workout.
- Show one recent performance with exact Last and Best values.
- Support shared kg/lb preference changes without reloading the application.
- Preserve offline-first workout creation and synchronization.
- Add English and Thai user-facing copy and accessibility descriptions.
- Add a requirement-to-evidence acceptance matrix and implementation report.

### Out of scope

- Exercise recommendations, generated workout plans, or coaching advice.
- New badge rules, XP rules, level thresholds, or streak calculations.
- A new Home-specific backend endpoint.
- Changes to the Progress, History, Profile, exercise picker, or set logging information architecture.
- Showing all badges or a dense analytics dashboard on Home.
- Starting services or the simulator automatically after implementation. They remain stopped until the user asks to test.

## Screen Hierarchy

Momentum Home is a vertically scrolling native page with the existing bottom tab bar.

### 1. Context header

- Small contextual line: localized current day and week context.
- Large state-aware headline:
  - No active workout: “Ready when you are.”
  - Active workout: “You’re in motion.”
- Text uses shared page-title and secondary semantic styles. It must not rely on fixed device dimensions.

### 2. State-aware hero

The hero occupies the first content position and contains the only lime primary action.

No active workout:

- Eyebrow: “Start training”.
- Title: “Choose today’s workout”.
- Supporting copy explicitly says the user chooses the body area and exercises.
- Action: “Start workout”. It opens the existing body-area selection flow.

Active workout:

- Eyebrow: “Workout in progress”.
- Title: localized body-area summary for the active workout.
- Progress: live exercises with at least one live set / total live exercises, plus logged set count. An exercise is not called “completed” because the workout model has no exercise-completion state. It must not display a fake elapsed time.
- Action: “Continue workout”. It opens the existing Today’s workout list (`active-workout`), never an individual exercise.

The no-active and active heroes are mutually exclusive. Starting, continuing, and repeating must never create multiple lime primary actions.

### 3. Motivation strip

Three compact, equal-height metric cards appear under the hero:

1. Weekly completed workouts / user-defined weekly goal.
2. Current streak in weeks.
3. Current Level, Total XP, and a progress indicator toward the next level.

The strip uses an authoritative cached `GamificationProfileDto`. When no cached or refreshed authoritative profile exists, numeric cards are not rendered. A short native skeleton may occupy the strip only while an initial online refresh is in progress; it must disappear when the request completes or when the app knows it is offline. Zero is shown only when zero came from an authoritative profile.

### 4. Train again

When there is no active workout and at least one repeatable completed workout exists, Home displays one “Train again” row:

- Localized body-area summary.
- Completion date.
- Exercise and set counts.
- Cached thumbnail from the first ordered exercise when available; otherwise the shared neutral artwork fallback.

Tapping the row creates a new workout with the same live exercises, tracking modes, and exercise order. The new workout receives new workout, workout-exercise, and outbox operation IDs. No previous sets are copied. The action then opens Today’s workout.

The shortcut is hidden while a workout is active. It is also hidden when the source workout cannot be repeated exactly, for example when an exercise definition is missing. TrackZ must not silently remove missing exercises and call the result a repeat.

Double taps, navigation re-entry, account reset, and app restart must not create two active workouts or duplicate StartWorkout operations.

### 5. Recent momentum

Home shows at most one exact exercise-performance row from the existing progress summary. It selects the most recently performed item by `LastPerformedAt`, with exercise ID as a deterministic tie-breaker.

The row contains:

- Exercise name.
- Exact Last value.
- Exact Best value.
- Tracking-mode-aware labels for weighted, assisted, and bodyweight exercises.
- The shared current kg/lb preference. Canonical stored and API values remain kilograms.

The copy is “Recent momentum”, not “New PR”, because the existing contract does not prove that a record was newly achieved this week. The section is hidden when no authoritative performance exists.

## Component and Data Design

### Train dashboard source

`ITrainDashboardSource` remains the local source for workout state. `TrainDashboardSnapshot` is expanded to contain:

- Active-workout presentation facts, including ordered body areas, live exercise count, and logged set count.
- One optional repeat template for the newest repeatable completed workout.

The repeat template contains the source workout ID, completion time, ordered body areas, exercise count, set count, thumbnail path, and ordered `WorkoutExerciseSelection` values. It is an internal mobile contract and does not alter API contracts.

`LocalTrainDashboardSource` reads the workout graph and exercise cache, preserves active ordering, and marks a completed workout repeatable only when every live exercise resolves to a cached definition with the same tracking mode.

### Progress source

`TrainTodayViewModel` consumes the existing `IProgressSnapshotSource`; it does not depend on `ProgressDashboardViewModel` or duplicate HTTP logic.

The view model applies:

- `ProgressSnapshot.Profile` for weekly goal, completed workouts, streak, Level, XP, and level bounds.
- `ProgressSnapshot.Summary.PersonalRecords` for the recent-momentum item.

Home maintains a presentation-only momentum state. It does not mutate gamification rules.

### Repeat action

The repeat command calls the existing `ActiveWorkoutCoordinator.StartAsync` with the ordered selections from the repeat template. The coordinator remains the single authority for account-generation fencing, one-active-workout enforcement, SQLite transactionality, outbox creation, stable IDs, and sync notification.

The command is serialized and disabled while running. On success, navigation opens `active-workout`. On a definite failure, it leaves the existing local state unchanged and shows localized actionable copy. An account reset cancels the command and prevents stale navigation.

### Navigation boundary

Home navigation is moved behind an app-owned navigation interface rather than asserted through `Shell.Current` in view-model tests. The interface supports:

- Open body-area picker.
- Open Today’s workout.

The MAUI implementation continues to use the existing routes. The view model owns command eligibility and state; the navigation adapter owns Shell mechanics.

## Loading and Error Behavior

Home loads in two independent stages under the account session boundary:

1. Read the local workout dashboard and render Start/Continue and Train again.
2. Read the cached progress snapshot, then refresh it if online.

The two stages must not make local training wait for the network.

- Cached progress remains visible if refresh fails.
- A progress refresh failure does not replace valid values with zero and does not show a developer-style network message.
- No cached progress while offline hides the motivation and momentum sections.
- A local workout database failure disables mutation actions and shows a localized retry state because safe one-active-workout behavior cannot be guaranteed.
- Authentication expiration is handled by the existing auth gate and account session reset.
- All asynchronous commits verify the captured account generation before changing UI state or navigating.

## Localization, Units, and Accessibility

- All new copy is added to English and Thai resource sets.
- Body-area, exercise-count, set-count, Level, XP, Last, and Best text use localized format strings.
- Last and Best subscribe to the existing shared `IWeightUnitPreference`; kg keeps supported three-decimal precision and lb uses the established stable two-decimal display contract.
- Primary and row actions receive action-specific semantic descriptions.
- Every interactive target is at least 44 points.
- Dynamic Type must not overlap the hero action, metric labels, repeat row, or bottom tab bar.
- The page honors the existing Reduce Motion preference and does not add mandatory decorative animation.

## Verification Strategy

Implementation follows strict RED → GREEN TDD. Every acceptance item below must name the production mutation it catches. Tests exercise real view models, SQLite repositories, and inflated MAUI XAML where practical rather than checking source text alone.

### Discussion Acceptance Matrix

| Approved requirement | Automated evidence | Manual evidence |
|---|---|---|
| No active workout shows Start hero | View-model state test and real MAUI composition test | Simulator screenshot |
| Active workout shows Continue hero | State matrix test | Simulator screenshot |
| Continue opens Today’s workout list | Navigation adapter integration test | Tap walkthrough |
| Home remains useful beyond Resume | Inflated page hierarchy test for motivation, Train again, and Recent momentum | Both-state screenshots |
| Weekly goal, Streak, Level/XP are truthful | Cached/refreshed/no-authority matrix with literal values | Compare seeded profile |
| No fabricated zero values | No-cache and refresh-failure tests | Offline walkthrough |
| Train again preserves exercises, modes, and order | Real SQLite start/restart test | Repeat a seeded workout |
| Train again never silently drops an unavailable exercise | Missing-definition rejection test | Unavailable-data state |
| Repeat is idempotent under double tap/re-entry | Concurrent command and outbox-count tests | Rapid double-tap walkthrough |
| App never recommends a required exercise | Exact navigation/copy behavior test | Product-copy review |
| Recent momentum uses exact Last/Best | Weighted, assisted, and bodyweight table tests | Compare History values |
| kg/lb updates across Home | Shared preference change/restart tests, including decimal precision | Toggle in Profile |
| Offline-first and cached-state retention | Cache success → refresh failure, restart, and account-reset tests | Airplane-mode walkthrough |
| Exactly one lime primary action | Inflated MAUI page/state audit | Visual screenshot review |
| Native spacing, typography, and targets | Real MAUI geometry/accessibility audit | iOS simulator review |
| English and Thai are complete | Culture-specific binding tests | Change device language |
| XAML compiles for iOS | `net10.0-ios` XAML Compile | App launch after user requests start |

The implementation report must quote the final test commands and counts, map each matrix row to a test name or manual observation, record the simulator screenshots, and list any platform/environment gate. Passing tests without this traceability table is not sufficient for completion.

## Completion Gate

The implementation is code-verified only when:

1. Every matrix row has current automated evidence.
2. Focused tests demonstrate their initial RED and final GREEN results.
3. The full Mobile test suite passes.
4. Mobile.Core and native iOS XAML Compile succeed with zero product warnings/errors.
5. `git diff --check` is clean.
6. A self-review compares the implementation to this document and the approved Momentum Home mockup.
7. The implementation report says “simulator acceptance pending” rather than “complete” while services remain stopped.

Product acceptance occurs only after the user explicitly asks to start the stack, the simulator walkthrough supplies every manual-evidence item in the matrix, and the user confirms the result. Services remain stopped until that explicit request.
