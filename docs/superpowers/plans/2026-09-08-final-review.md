# Final implementation review

## Verdict

- **Spec:** Approved scope is satisfied on final code review. Coverage and all per-set findings below are resolved.
- **Quality:** Approved scope is sound on final code review. The parent-reported full mobile suite completed with 966 passing tests.

## Re-review resolution

The original findings below are retained as review history and are now **resolved**. The editor preserves nullable warmup/pain values unless their controls are changed; direct post-save guidance exists and suppresses warmups, unanswered effort, and pain; the inline safety warning is visible; account reset clears edit/metadata/guidance state; and domain coaching edits enforce deletion and monotonic-time rules.

### Resolved final finding

6. **Resolved: imperial-mode recommendations treated a one-pound UI step as one kilogram.** `PublishInlineGuidance` now converts the display step to canonical kilograms exactly once before policy evaluation (`src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs:1076-1079`). `Imperial_inline_guidance_converts_one_pound_increment_to_kilograms_once` covers the displayed result and is parent-reported passing.

## Original findings (resolved)

### High

1. **Editing only load/reps silently converts legacy unknown classification and pain to `false`.** `EditSet` projects nullable values into non-nullable switches with `row.IsWarmup ?? false` and `row.HasPain ?? false` (`src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs:996-1006`), then every edit sends `coachingMetadataSpecified: true` (`:868-872`). Thus opening an old set whose fields are null and changing only reps persists both fields as explicit false, contrary to “unknown historical sets remain unknown.” **Fix:** retain the original nullable values and track per-control interaction/dirty state. Preserve null for untouched switches; only send/overwrite a metadata field after explicit interaction (or use per-field presence flags). Add a UI/view-model regression that edits only reps on a fully-null legacy set and asserts all metadata remains null through the outbox payload.

2. **The inline effort score is saved but never drives a visible load recommendation.** After save, `SetLoggerViewModel` returns at the hard-coded `PostSetPromptEnabled = false` gate (`src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs:120`, `:923-927`). `HypertrophyLoadGuidancePolicy` understands `EffortScore`/pain, but it is only reached through the disabled legacy prompt flow. The implemented experience therefore lacks the approved conservative next-set recommendation. **Fix:** evaluate guidance directly after the explicit inline save using the saved set and prior comparable sets, render the result inline, and preserve the existing guarded apply-to-next-draft action. Warmups, unanswered effort, and pain must produce no progression recommendation. Add an end-to-end view-model test proving score 0–100 reaches visible guidance and pain/warmup suppress progression.

### Medium

3. **Pain capture lacks the required immediate safety guidance.** The inline editor renders only a “Pain during this set” switch (`src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml:72-75`). The localized stop-exercise warning exists, but is only used by the disabled legacy effort sheet (`src/TrackZ.Mobile/Features/Workout/SetEffortSheetPage.xaml:61`). **Fix:** show `EffortPainSafety` beside the inline pain control, preferably always visible or at minimum immediately when selected; ensure selecting pain suppresses recommendation as above.

4. **Account reset leaves the new draft metadata and edit identity in the scoped view model.** `ClearPrivateState` clears workout IDs and rows but not `_editingSetId`, `EffortScore`/`_effortValue`, `IsWarmup`, or `HasPain` (`src/TrackZ.Mobile.Core/Features/Workout/SetLoggerViewModel.cs:1557-1581`). The hidden editor reduces immediate exposure, but private state from the prior account remains resident and can complicate subsequent lifecycle behavior. **Fix:** reset all five fields (with property notifications) on account reset and deactivation; add an account-boundary regression while an edit is open.

### Low

5. **The public domain coaching edit lacks its own deletion and timestamp invariants.** `SetEntry.EditCoaching` validates score only, then can update a deleted set or move `UpdatedAt` backwards (`src/TrackZ.Domain/Workouts/SetEntry.cs:125-134`). Current sync happens to call measurement edit first, which enforces these rules, but the public `WorkoutSession.EditSetCoaching` API is unsafe independently. **Fix:** mirror `SetEntry.Edit`'s deleted and monotonic timestamp checks inside `EditCoaching`, and add focused domain tests.

## Verified strengths and remaining visual limitation

- New sync fields are optional/defaulted, and PostgreSQL/SQLite columns are nullable with 0–100 checks; old payloads preserve coaching data through `CoachingMetadataSpecified = false`.
- Active-set edits preserve set/creation operation IDs and create a distinct edit operation; the local/server two-step version increments align when both measurement and coaching change.
- Coverage uses reserved stable IDs when cache definitions are missing, displays primary/secondary counts separately, and prevents navigation before year 2000.
- No third-party assets were adopted. `MuscleBodyDiagram` remains an original smoothened schematic, not photorealistic, pixel-perfect to the mock, or anatomy-approved. Device/render/accessibility review remains required; no such claim should be made from build/tests alone.
- No tests were run as part of this read-only review; parent-owned full suite/iOS verification was still in progress.
