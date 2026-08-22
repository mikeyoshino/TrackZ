# Hypertrophy Load Guidance Design

**Date:** 2026-08-23  
**Status:** Approved design; awaiting written-spec review  
**Scope:** TrackZ mobile Today Workout and Set Logger, offline persistence, workout sync, workout history contracts, and supporting API persistence

## Summary

TrackZ will keep the Today Workout queue visually compact while adding optional, explainable load guidance to Set Logger for users whose primary goal is muscle hypertrophy.

The Today Workout row shows only the exercise artwork, exercise name, a prominent set count, and the navigation chevron. Tracking mode, previous performance, and previous weight are removed from that row. Set Logger becomes the place where prior-session context and guidance live.

After a set has been durably saved, a native modal bottom sheet asks the user—in ordinary language—how the set felt while maintaining good form. TrackZ stores that observation separately from the set save, combines it with an 8–12 repetition progression policy, and may suggest increasing, keeping, or reducing the load. The user must explicitly accept any suggested value; TrackZ never changes a load or saves a set automatically.

## Product Decision and Prior-Spec Amendment

The original product designs describe target-weight guidance as a non-goal:

- `docs/superpowers/specs/2026-08-14-trackz-mobile-fitness-tracker-design.md`
- `docs/superpowers/specs/2026-08-20-trackz-native-ios-experience-design.md`

This design intentionally amends that decision for one narrow capability: optional, explainable hypertrophy load guidance based solely on the user's own recorded sets and self-reported effort. It does not turn TrackZ into a prescriptive training-plan service. Where those earlier documents prohibit all suggested weights, this document supersedes them only for the feature defined here.

## Evidence and Product Guardrails

The 8–12 repetition range is a practical product default, not a claim that hypertrophy occurs only in that range. The 2026 American College of Sports Medicine position stand found that resistance training improves strength and hypertrophy across many prescriptions, that heavier loads are especially relevant to maximal strength, and that no single prescription variable determines every adaptation. Research on proximity to failure supports considering effort alongside load, while also cautioning that the exact optimum is uncertain and momentary failure is not required.

Primary references:

- [ACSM Position Stand: Resistance Training Prescription for Muscle Function, Hypertrophy, and Physical Performance in Healthy Adults (2026)](https://pubmed.ncbi.nlm.nih.gov/41843416/)
- [ACSM: With Resistance Training, Load is Only Half the Story (2026)](https://acsm.org/active-voice-with-resistance-training-load-is-only-half-the-story/)
- [Exploring the Dose-Response Relationship Between Estimated Proximity to Failure, Strength Gain, and Muscle Hypertrophy (2024)](https://pubmed.ncbi.nlm.nih.gov/38970765/)
- [Feasibility and Usefulness of Repetitions-In-Reserve Scales for Selecting Exercise Intensity (2024)](https://pubmed.ncbi.nlm.nih.gov/38563729/)

The product therefore follows these guardrails:

- Guidance is phrased as “try,” not as a guaranteed or medically individualized prescription.
- User-facing copy does not expose RIR or a 0–4 score.
- Good form and the user's judgment remain primary.
- Near-maximal effort with intact form is not automatically treated as a failure.
- Pain is not interpreted as training effort. The UI tells the user to stop that exercise rather than generating guidance.
- Recommendations are advisory and require explicit user action.

## Goals

- Make the Today Workout queue easier to scan by emphasizing only each exercise's logged set count.
- Keep previous-load context out of the queue and place it in Set Logger.
- Give a hypertrophy-focused user a simple way to report set effort without learning training jargon.
- Apply a conservative double-progression policy that does not react to one exceptional set.
- Work offline and preserve effort observations across sync and device changes.
- Explain why a recommendation was made.
- Preserve all existing durable-save, conflict, localization, accessibility, and account-boundary guarantees.
- Leave a durable design record that can guide later changes to the recommendation policy.

## Non-goals

- Exercise selection, workout programming, set-volume programming, recovery scoring, or weekly periodization.
- A personalized medical, rehabilitation, or injury-management service.
- Automated form detection or pain diagnosis.
- 1RM estimation or strength-peaking recommendations.
- Automatically applying or saving a suggested measurement.
- Treating one set as sufficient evidence to progress a load.
- Synchronizing per-exercise equipment increments in v1.
- Adding external weight to bodyweight-only exercises in v1.

## Terminology

### Effort rating

`SetEffortRating` is a nullable per-set observation with three stable values:

- `Easy`: “เบาไป — ยังไหวอีกหลายครั้ง” / “Too easy — many reps left.”
- `Productive`: “กำลังดี — ช่วงท้ายเริ่มหนัก แต่ฟอร์มยังดี” / “About right — the final reps were hard, with good form.”
- `TooHeavy`: “หนักเกินไป — ทำไม่ถึงเป้าหรือฟอร์มเริ่มเสีย” / “Too heavy — missed the range or form began to break.”

`null` means that the user skipped, dismissed, did not understand, or has legacy data. It never means zero effort.

### Reference performance

Reference performance is descriptive prior-session context, not a recommendation.

For the most recent completed workout containing the exercise:

- Weighted: choose the highest weight among active sets with 8–12 repetitions. Break ties by higher repetitions, then later set order.
- Assisted: choose the lowest assistance among active sets with 8–12 repetitions. Break ties by higher repetitions, then later set order.
- Bodyweight: choose the highest repetition count within 8–12. Break ties by later set order.
- Exclude sets rated `TooHeavy` from new rated history.
- Legacy sets with no effort rating may be displayed as historical reference but cannot satisfy an increase-readiness rule.

If no set qualifies, Set Logger shows no reference card rather than inventing a value.

### Comparable sets

Two sets are comparable when they:

- belong to the same exercise definition and tracking mode;
- are active, completed sets;
- use the same canonical weight or assistance value for weighted or assisted exercises;
- come from the current workout or the immediately preceding completed workout;
- have a non-null effort rating when used to justify an increase.

The two most recent comparable sets are used for increase readiness.

### Progression increment

Each weighted or assisted exercise may have one account-scoped, device-local progression increment stored canonically in kilograms. The user chooses it once from common values in the active display unit or enters a custom positive value. Unit changes convert the stored value for display without changing its physical magnitude.

If no increment exists when an exact change is needed, the same bottom sheet asks for the equipment's increment before displaying a numeric suggestion. It does not open a second modal. The setting is editable and is cleared with the account's local data. It is not synced in v1; another device asks again.

## User Experience

### Today Workout row

`ActiveWorkoutExerciseRow` retains:

- exercise artwork or placeholder;
- exercise name;
- a high-contrast counter on the right with the numeric logged set count and localized “sets” label;
- the navigation chevron;
- existing tap, move, and remove interactions.

It removes visible tracking-mode metadata, previous-performance text, previous weight, the separator, and “sets logged” prose. Zero remains visible as `0 sets` so unfinished exercises are unambiguous. Accessibility describes the exercise name, set count, and available action but does not announce removed previous-weight information.

### Set Logger reference card

A compact reference card sits directly below the exercise summary and above the inline set editor. It uses copy equivalent to:

- “Previous workout reference”
- “70 kg × 10 reps”
- “Heaviest set in the 8–12 rep range”

The weight or assistance value is the primary visual element; repetitions are secondary context. Legacy, unrated history uses neutral reference wording and is never labeled as an optimized or recommended load.

### Post-save bottom sheet

The bottom sheet opens only after the set's local SQLite transaction and `SaveSet` outbox operation have completed successfully. The set is already safe before the user sees the prompt.

Initial state:

- confirms that the set was saved;
- asks “How did this set feel?”;
- explains that the answer should assume the same good form;
- presents large buttons for `Easy`, `Productive`, and `TooHeavy`;
- offers “Not sure · skip this time” and swipe-to-dismiss.

The sheet does not re-open automatically for an unrated set after navigation, restart, or dismissal. Skipping creates no effort operation.

After a selection is durably stored, the same sheet transitions to one of these states:

- a numeric recommendation and explanation;
- “keep this load” with the next repetition target;
- “almost ready to increase—confirm with one more comparable set”;
- “we need another comparable set”;
- a bodyweight-specific repetition recommendation;
- an effort-save error that explicitly confirms the original set is still safe.

There is no stacked alert or second modal. A numeric recommendation includes a primary action such as “Use 72.5 kg for the next set” and a secondary “Not now.” Accepting copies the value into the next transient draft only. If no next set is open, the copy says “Try … next time.”

### Safety copy

The sheet's helper copy distinguishes effort from pain. If the user is experiencing pain or cannot preserve form, the interface tells them to stop that exercise and does not generate a load recommendation. This is informational safety guidance, not diagnosis.

## Guidance Policy

`HypertrophyLoadGuidancePolicy` is a pure Mobile Core component. It consumes recorded facts and returns a typed result; it performs no I/O and emits no localized prose.

Inputs include:

- tracking mode;
- the newly saved set and effort rating;
- active sets from the current workout;
- comparable sets from the immediately preceding completed workout;
- the optional per-exercise progression increment;
- whether another set can be opened in the current workout.

Outputs include:

- action: `Increase`, `Keep`, `Reduce`, `IncreaseRepetitions`, `CollectMoreData`, or `None`;
- optional suggested canonical weight, assistance, or repetition count;
- destination: `NextSet` or `NextWorkout`;
- a stable reason code used by localized presentation;
- whether an increment must be requested before an exact value can be shown.

### Decision order

Rules are evaluated in this order:

1. Missing or skipped effort returns `None`.
2. `TooHeavy` returns `Reduce` for weighted or assisted modes and a lower repetition target for bodyweight mode.
3. Fewer than 8 repetitions returns `Reduce`, regardless of `Easy` or `Productive`, because the set fell below this product's hypertrophy range.
4. `Easy` with 8–11 repetitions returns `IncreaseRepetitions`, keeping the load and targeting one additional repetition, capped at 12.
5. `Productive` with 8–11 repetitions returns `Keep`.
6. `Easy` or `Productive` with at least 12 repetitions checks increase readiness:
   - one qualifying set returns `CollectMoreData` with “almost ready to increase” copy;
   - two most recent comparable sets at that load that each reached at least 12 repetitions and were rated `Easy` or `Productive` return `Increase`.
7. Invalid or internally inconsistent data returns `None` and is never converted into a numeric recommendation.

The policy does not interpret `Productive` as “too heavy” merely because the final repetitions were difficult. Good form is the dividing line represented by the user's selected category.

### Tracking-mode behavior

- Weighted `Increase`: add one configured increment. Weighted `Reduce`: subtract one increment without falling below the domain minimum.
- Assisted `Increase` in training difficulty: subtract one assistance increment. Assisted `Reduce` in training difficulty: add one assistance increment. User-facing copy always says “less assistance” or “more assistance,” not the ambiguous “increase weight.”
- Bodyweight: adjust the repetition target within 8–12. At the top of the range, v1 may report that the range has been completed but does not prescribe a different exercise or added external weight.

All exact values are validated through the existing measurement bounds and canonical decimal precision. If a configured increment would produce an invalid value, the result is non-numeric guidance rather than a clamped value that changes the intended increment.

## Architecture

### Domain and contracts

- Add nullable `SetEffortRating` to server `SetEntry` and mobile `LocalSet`.
- Add a database check constraint limiting non-null values to the defined enum.
- Include optional effort in workout detail, exercise history, and sync pull set DTOs.
- Preserve `null` for all existing rows and payloads.
- Do not add recommendation fields to domain entities or DTOs.

Effort is an observation about a completed set. It is not part of `SetMeasurement`, because weight, assistance, and repetitions must be durably saved before the post-save prompt appears.

### Separate effort mutation

Add an idempotent `RecordSetEffort` local/outbox/server operation that changes only the effort rating of an existing active set and advances normal aggregate versions. It has its own stable operation ID and carries the expected workout, exercise, set, and base-version identities.

This separate mutation is required for compatibility. If effort were added as a nullable field to ordinary `EditSet`, an older client that omitted the property could deserialize as `null` and accidentally erase a newer rating. `EditSet` therefore preserves existing effort, and only `RecordSetEffort` may set or replace it in v1.

The local coordinator creates `RecordSetEffort` only after the preceding `SaveSet` is durable. Outbox ordering and base versions enforce `SaveSet → RecordSetEffort` causality. Duplicate delivery is idempotent by operation ID.

### Local recommendation component

The recommendation policy lives in `TrackZ.Mobile.Core`, adjacent to workout presentation logic but separate from `SetLoggerViewModel`. The view model supplies history and converts the typed result into localized display state and commands.

Recommendations are never persisted. The application recomputes them from facts so later policy improvements do not rewrite workout history. The API stores and returns set effort but exposes no recommendation endpoint.

### Local progression preference

Add an account-scoped local `ExerciseGuidancePreference` store keyed by exercise definition ID. It persists one optional canonical-kilogram increment and clears on account reset. It does not enter workout outbox graphs or server synchronization in v1.

## Data Flow

1. The user saves weight/assistance and repetitions through the existing inline editor.
2. `ActiveWorkoutCoordinator.SaveSetAsync` commits the `LocalSet` and `SaveSet` outbox operation atomically.
3. Existing saved feedback runs, and Set Logger presents the effort bottom sheet.
4. Skip or dismissal ends the flow without another mutation.
5. Selecting an effort rating calls the dedicated local effort coordinator.
6. The coordinator atomically updates `LocalSet.Effort`, increments versions, and queues `RecordSetEffort` after `SaveSet`.
7. After the effort commit succeeds, the view model invokes `HypertrophyLoadGuidancePolicy` using current and previous comparable sets.
8. If an exact load change needs a missing increment, the same sheet collects and stores the per-exercise local preference.
9. The sheet displays the typed recommendation and its reason.
10. “Use this value” populates a new transient draft. It does not save another set.
11. Sync pushes the two ordered operations and later pulls full graphs containing optional effort.

## Failure, Offline, and Conflict Behavior

- Set save fails: preserve the editor and show the existing save error; do not open the effort sheet.
- Effort local write fails: keep the set visible, state that the set was saved, offer effort retry or dismissal, and emit no recommendation.
- Offline: save both operations locally, calculate guidance locally, and sync later.
- Effort sync fails transiently: retain pending state and local guidance; retry through the normal sync lifecycle.
- Permanent effort rejection: preserve the set, surface a non-destructive sync notice, and reconcile to authoritative server effort without altering weight or repetitions.
- Concurrent effort changes: normal aggregate version/conflict handling applies. Full-graph replacement and undo snapshots must preserve effort.
- Legacy client edits: ordinary measurement edits preserve effort because only the dedicated operation may change it.
- Missing history or stale background refresh: compute from the stable local snapshot captured for the sheet. Never rewrite an already-open draft when refreshed history arrives.
- Invalid progression preference: discard the invalid local preference and request the increment again when needed.
- Account reset: clear pending presentation state, local progression preferences, and all account-bound guidance data through the existing session boundary.

## Localization and Accessibility

- Add every user-facing string to both `WorkoutStrings.resx` and `WorkoutStrings.th.resx`; keep key parity and avoid literal XAML copy.
- The Today row's set counter exposes one localized semantic phrase, not separate confusing number and unit announcements.
- Bottom-sheet options have at least 44×44-point hit targets and distinct localized semantic descriptions.
- On presentation, move accessibility focus to the sheet heading and announce the durable-save confirmation once.
- After effort selection, announce the changed recommendation state without reopening focus on the background page.
- Dismiss, skip, retry, “use value,” and “not now” remain reachable by assistive technology.
- Color reinforces but never solely communicates the effort choice or recommendation state.
- Reduced-motion mode avoids large sheet/recommendation animations while preserving state transitions.

## Testing Strategy

### Pure policy tests

- Matrix coverage for 7, 8, 11, 12, and 13 repetitions across every effort rating.
- One versus two comparable-set increase readiness.
- Same-load requirement and non-comparable tracking-mode/load cases.
- Weighted, assisted-inverted, and bodyweight behavior.
- Missing increment, invalid increment, unit conversion, precision, and measurement boundaries.
- Stable reason codes and `NextSet` versus `NextWorkout` destination.
- Legacy null-effort exclusion from increase readiness.

### Domain and persistence tests

- Nullable effort validation and database check constraints.
- Existing server and mobile databases migrate with effort set to `null`.
- Local hydrate, upsert, undo, conflict replacement, deletion, and graph validation preserve effort.
- `EditSet` cannot erase effort.
- `RecordSetEffort` is idempotent and rejects wrong aggregate/set identity.
- Account reset clears local guidance preferences.

### Sync and compatibility tests

- Ordered offline `SaveSet → RecordSetEffort` push after restart.
- Pull and cached exercise history retain effort.
- Duplicate effort delivery is harmless.
- Transient and permanent failures preserve the original set.
- Conflict rebase preserves the authoritative effort and all measurement fields.
- Old payloads without effort remain valid and cannot clear a stored effort through measurement edits.

### View-model and UI tests

- Today Workout visual contract asserts the prominent set counter and absence of tracking mode, previous-performance, and previous-weight labels.
- Reference-card selection and legacy neutral presentation.
- Bottom sheet appears exactly once and only after durable save.
- Skip, swipe dismissal, effort retry, collect-more-data, keep, increase, and reduce states.
- Missing-increment selection occurs inside the same sheet without modal stacking.
- Accepting a suggestion changes only the next transient draft.
- Save failure never opens the effort prompt.
- Thai/English localization parity, accessibility focus/announcements, 44-point targets, and reduced-motion behavior.

### Acceptance tests

- Save a set offline, rate it, receive guidance, restart, and retain both the set and pending effort operation.
- Complete two comparable qualifying sets, receive an increase recommendation, accept it, and verify that only the next draft changes.
- Sync to the server, recreate the mobile process, load history, and recompute the same typed recommendation from stored facts.
- Use an older measurement-edit payload after effort exists and verify that effort survives.
- Exercise the complete assisted flow and confirm that lower assistance is described as greater training difficulty.

## Persistent Visual References

Implementation updates both reference documents and their native visual contracts:

- `docs/design/todays-workout-reference.html`
- `docs/design/track-sets-reference.html`

The ignored Visual Companion exploration under `.superpowers/brainstorm/` is not a source of truth. This specification and the persistent reference documents are the maintained artifacts.
