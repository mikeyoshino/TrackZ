# Momentum Home acceptance ledger

Date: 2026-08-22

Reference commit before Task 6: `25f8292fa4e799267482b2a90aed8376897e3ef6`

Product acceptance: **simulator acceptance pending**

This ledger maps every row in the approved Discussion Acceptance Matrix to production code and
behavior-sensitive automated evidence. The persistent comparison source is
`docs/design/momentum-home-reference.html`; production remains native MAUI XAML. Manual results are
intentionally not inferred from automated tests.

## Verification commands

Focused Momentum Home:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~LocalTrainDashboardSourceTests|FullyQualifiedName~TrainAgainWorkoutTests|FullyQualifiedName~MomentumHomePresentationTests|FullyQualifiedName~MauiCompositionTests" --verbosity minimal -m:1
```

Full Mobile regression:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --verbosity minimal -m:1
```

Core and native iOS XAML compile:

```bash
dotnet build src/TrackZ.Mobile.Core/TrackZ.Mobile.Core.csproj --no-restore -m:1 -v:minimal
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -v:minimal
```

Fresh Task 6 results are recorded after the acceptance matrix.

## Discussion acceptance matrix

In the “Initial RED” column, `T1`–`T5` refer to the preserved Task reports in this SDD directory.
Where a task report did not retain row-specific failing console output, this ledger says so rather
than inventing it.

| Approved requirement | Production files | Automated evidence (exact test names) | Initial RED evidence | Current verification | Manual evidence |
|---|---|---|---|---|---|
| No active workout shows Start hero | `TrainTodayViewModel.cs`; `TrainPage.xaml` | `TrainTodayViewModelTests.Hero_opens_picker_without_active_workout_and_active_workout_when_active`; `TrainTodayViewModelTests.Ready_home_presentation_uses_literal_localized_copy_and_formats`; `MomentumHomePresentationTests.Ready_home_inflates_the_approved_named_hierarchy_with_one_primary_action` | T3: `ITrainNavigator` compile failure; T4: `HeroActionButton` absent | PASS — focused 48/48 | pending — services intentionally stopped |
| Active workout shows Continue hero | `LocalTrainDashboardSource.cs`; `TrainTodayViewModel.cs`; `TrainPage.xaml` | `LocalTrainDashboardSourceTests.Source_returns_exact_active_progress_and_newest_repeatable_selection_order`; `TrainTodayViewModelTests.Active_home_presentation_uses_the_same_localized_hero_contract`; `MomentumHomePresentationTests.Active_reload_keeps_the_same_primary_button_and_hides_only_train_again` | T1: missing active snapshot facts at compile; T4: required presentation properties/page hierarchy absent | PASS — focused 48/48 | pending — services intentionally stopped |
| Continue opens Today’s workout list | `TrainTodayViewModel.cs`; `TrainNavigation.cs` | `TrainTodayViewModelTests.Hero_opens_picker_without_active_workout_and_active_workout_when_active` (asserts the active navigator branch); `MauiCompositionTests.Momentum_home_provider_keeps_one_page_and_view_model_while_loading_local_and_cached_state` (production navigator registration/lifetime) | T3: `ITrainNavigator` did not exist (`CS0246`) | PASS — focused 48/48 | pending — services intentionally stopped |
| Home remains useful beyond Resume | `TrainPage.xaml`; `TrainTodayViewModel.cs` | `MomentumHomePresentationTests.Ready_home_inflates_the_approved_named_hierarchy_with_one_primary_action`; `MomentumHomePresentationTests.Active_reload_keeps_the_same_primary_button_and_hides_only_train_again`; `NativeIosExperienceAcceptanceTests.Momentum_home_keeps_the_approved_named_hierarchy_one_primary_and_quiet_home_copy` | T4: real inflated page had no `HeroActionButton`; T4 resources/presentation API were missing | PASS — focused 48/48 and full 577/577 | pending — services intentionally stopped |
| Weekly goal, Streak, Level/XP are truthful | `TrainTodayViewModel.cs`; `TrainPage.xaml`; existing `ProgressSnapshotSource` | `TrainTodayViewModelTests.Local_state_commits_before_gated_progress_refresh_and_cached_values_survive_failure`; `TrainTodayViewModelTests.Ready_home_presentation_uses_literal_localized_copy_and_formats`; `MauiCompositionTests.Momentum_home_provider_keeps_one_page_and_view_model_while_loading_local_and_cached_state` | T2: authoritative Home fields did not exist (11 compiler errors) | PASS — focused 48/48 | pending — services intentionally stopped |
| No fabricated zero values | `TrainTodayViewModel.cs`; `TrainPage.xaml` visibility bindings | `TrainTodayViewModelTests.No_cache_offline_hides_motivation_instead_of_fabricating_zeroes`; `TrainTodayViewModelTests.Progress_cache_read_failure_keeps_local_workout_and_hides_unauthoritative_motivation`; `MomentumHomePresentationTests.No_authoritative_progress_hides_motivation_and_momentum_without_hiding_repeat` | T2: no-authority state API was absent; cache-read RED leaked workout error instead of quiet hidden state | PASS — focused 48/48 | pending — services intentionally stopped |
| Train again preserves exercises, modes, and order | `LocalTrainDashboardSource.cs`; `TrainTodayViewModel.cs`; existing `ActiveWorkoutCoordinator` | `LocalTrainDashboardSourceTests.Source_returns_exact_active_progress_and_newest_repeatable_selection_order`; `TrainAgainWorkoutTests.Train_again_recreates_exact_ordered_modes_without_sets_after_sqlite_restart` | T1: `Repeat`/selection contract missing; T3: `ITrainNavigator` compile RED before command existed | PASS — focused 48/48 | pending — services intentionally stopped |
| Train again never silently drops an unavailable exercise | `LocalTrainDashboardSource.cs` | `LocalTrainDashboardSourceTests.Source_refuses_partial_repeat_when_any_live_definition_is_missing_or_mode_changed`; `LocalTrainDashboardSourceTests.Source_skips_a_newer_inexact_workout_and_returns_the_next_exact_repeat`; `TrainAgainWorkoutTests.Reload_that_removes_repeat_makes_train_again_unavailable` | T1: repeat contract missing at compile; tests were introduced to reject the previous partial model | PASS — focused 48/48 | pending — services intentionally stopped |
| Repeat is idempotent under double tap/re-entry | `TrainTodayViewModel.cs`; existing `ActiveWorkoutCoordinator`; `MauiProgram.cs` singleton composition | `TrainAgainWorkoutTests.Train_again_recreates_exact_ordered_modes_without_sets_after_sqlite_restart` (two concurrent command calls, one active workout/outbox/navigation); `TrainAgainWorkoutTests.Account_reset_while_repeat_start_is_committing_prevents_stale_navigation`; `MauiCompositionTests.Momentum_home_provider_keeps_one_page_and_view_model_while_loading_local_and_cached_state` | T3: command/navigation contract compile RED; T5: transient view model failed `Assert.Same` | PASS — focused 48/48 | pending — services intentionally stopped |
| App never recommends a required exercise | `WorkoutStrings.resx`; `WorkoutStrings.th.resx`; `TrainPage.xaml` | `NativeIosExperienceAcceptanceTests.Momentum_home_keeps_the_approved_named_hierarchy_one_primary_and_quiet_home_copy` | No row-specific failing console was retained; T5 report records a mutation guard that fails if recommendation copy is added | PASS — full 577/577 | pending — services intentionally stopped |
| Recent momentum uses exact Last/Best | `TrainTodayViewModel.cs`; `HomeMomentumItem.cs`; `TrainPage.xaml` | `TrainTodayViewModelTests.Recent_momentum_uses_latest_time_then_descending_exercise_id`; `HomeMomentumItemTests.Formats_exact_weighted_last_and_best`; `HomeMomentumItemTests.Formats_exact_assisted_last_and_best`; `HomeMomentumItemTests.Formats_bodyweight_as_reps_without_fabricating_weight` | T2: `HomeMomentumItem` and recent-momentum properties were missing (compiler RED) | PASS — focused 48/48 and full 577/577 | pending — services intentionally stopped |
| kg/lb updates across Home | `HomeMomentumItem.cs`; shared `IWeightUnitPreference`; `TrainTodayViewModel.cs` | `HomeMomentumItemTests.Preference_change_updates_both_text_properties_without_replacing_item`; `HomeMomentumItemTests.Dispose_unsubscribes_from_the_shared_weight_preference`; `TrainTodayViewModelTests.Ready_home_presentation_uses_literal_localized_copy_and_formats`; `MomentumHomePresentationTests.Shared_unit_change_updates_the_existing_recent_momentum_row_in_pounds` | T2: unit-aware item/properties missing at compile | PASS — focused 48/48 and full 577/577 | pending — services intentionally stopped |
| Offline-first and cached-state retention | `TrainTodayViewModel.cs`; existing `ProgressSnapshotSource`/cache; `MauiProgram.cs` singleton | `TrainTodayViewModelTests.Local_state_commits_before_gated_progress_refresh_and_cached_values_survive_failure`; `TrainTodayViewModelTests.Known_offline_cache_read_never_shows_progress_loading_while_local_state_commits`; `TrainTodayViewModelTests.Going_offline_during_gated_refresh_hides_loading_notifies_state_and_cannot_restore_after_reset`; `TrainTodayViewModelTests.Account_reset_after_cached_progress_prevents_delayed_refresh_from_restoring_home_state`; `MauiCompositionTests.Momentum_home_provider_keeps_one_page_and_view_model_while_loading_local_and_cached_state` | T2 REDs: known-offline skeleton shown; online→offline transition was not observed; delayed reset fencing was exercised | PASS — focused 48/48 | pending — services intentionally stopped |
| Exactly one lime primary action | `TrainPage.xaml`; `TrackZControls.xaml` | `MomentumHomePresentationTests.Ready_home_inflates_the_approved_named_hierarchy_with_one_primary_action`; `MomentumHomePresentationTests.Active_reload_keeps_the_same_primary_button_and_hides_only_train_again`; `AppWideVisualConsistencyTests.Momentum_home_audit_rejects_duplicate_or_outside_hero_primary_actions`; `AppWideVisualConsistencyTests.Momentum_home_hero_states_keep_the_approved_ready_and_active_emphasis` | T4: `HeroActionButton` absent; reference-emphasis mutation failed before Ready/Active setters | PASS — focused 48/48 and full 577/577 | pending — services intentionally stopped |
| Native spacing, typography, and targets | `TrainPage.xaml`; `TrackZControls.xaml`; shared native styles | `AccessibilitySemanticsTests.Momentum_home_uses_localized_semantic_bindings_and_shared_44_point_targets`; `AppWideVisualConsistencyTests.Momentum_home_audit_rejects_metric_height_and_section_order_mutations`; `AppWideVisualConsistencyTests.Momentum_home_artwork_columns_reserve_the_semantic_artwork_width_and_gap`; `MomentumHomePresentationTests.Ready_home_inflates_the_approved_named_hierarchy_with_one_primary_action` | T4: approved native named hierarchy/geometry was absent; geometry mutation tests recorded RED | PASS — focused 48/48 and full 577/577 | pending — services intentionally stopped |
| English and Thai are complete | `WorkoutStrings.resx`; `WorkoutStrings.th.resx`; `WorkoutResources.cs`; `TrainTodayViewModel.cs` | `MomentumHomePresentationTests.Momentum_home_copy_is_complete_distinct_and_reuses_existing_primary_contracts`; `TrainTodayViewModelTests.Ready_home_presentation_uses_literal_localized_copy_and_formats`; `TrainTodayViewModelTests.Active_home_presentation_uses_the_same_localized_hero_contract`; `HomeMomentumItemTests.Resource_contract_has_distinct_english_and_thai_momentum_copy` | T4: 17 missing `WorkoutTextSet` properties caused compile RED; exact Thai separator test then failed | PASS — focused 48/48 and full 577/577 | pending — services intentionally stopped |
| XAML compiles for iOS | `TrainPage.xaml`; `TrackZControls.xaml`; `MauiProgram.cs` | native iOS `Compile` command below | T4: attempted unsupported targeted VisualState setter produced `MAUIG1001`; supported triggers replaced it | PASS — exit 0, no warning/error output | pending — services intentionally stopped |

## Approved HTML comparison and regression audit

The production hierarchy was opened and compared directly with all six interactive reference
states (Ready/Active × EN/TH × kg/lb):

- Header → state-aware hero → motivation → Ready-only Train again → Recent momentum is preserved.
- `HeroActionButton` is one native button; Ready inverts it inside the lime hero and Active restores
  lime action on the dark hero. No second lime button is present.
- Continue calls `OpenActiveWorkoutAsync`, whose MAUI adapter routes to `active-workout`, not an
  exercise detail page.
- Motivation visibility is bound to `HasAuthoritativeProgress`; it cannot render reset/default
  zero fields without an authoritative snapshot.
- Repeat is created only from a fully resolved cached definition graph, preserving mode/order; the
  command is serialized and calls the existing atomic coordinator.
- Recent momentum uses the newest `LastPerformedAt`, deterministic descending exercise-ID tie
  break, mode-specific Last/Best, canonical kg, and the shared live kg/lb preference.
- EN/TH strings are resource-backed, and device-local ISO week context is clock/timezone injected.
- The `ScrollView`, semantic styles, shared spacing/geometry resources, 44-point action minimums,
  and absence of fixed line heights preserve Dynamic Type reachability; no mandatory animation was
  added, so Reduce Motion is not bypassed.

Deliberate regression checks:

| Regression | Guard |
|---|---|
| Home collapses to only Resume | named hierarchy/order assertions require motivation, Train again, and Recent momentum |
| Two lime buttons | inflated real-page single primary assertion plus structural mutation audit |
| Continue opens an exercise | navigator branch test and literal `active-workout` adapter |
| Fake zero metrics | no-cache/cache-failure visibility tests and `HasAuthoritativeProgress` binding |
| Partial repeat | missing-definition/mode rejection and fallback-to-next-exact tests |
| Lost cached state | cache-before-refresh, refresh-failure, singleton lifecycle, restart/account-reset tests |
| kg/lb mismatch | exact weighted/assisted/bodyweight formatting and live preference event tests |
| English literals in Thai | literal EN/TH view-model and resource-contract tests |
| Stale account state | reset-during-load, reset-during-refresh, reset-during-repeat tests |

## Fresh Task 6 results

- Focused Momentum Home: **48 passed, 0 failed, 0 skipped** (4 seconds, exit 0).
- Full Mobile regression: **577 passed, 0 failed, 0 skipped** (8 seconds, exit 0).
- Mobile.Core build: exit 0, **0 warnings, 0 errors**.
- Native iOS XAML Compile: exit 0 with no product warning or error output.
- `git diff --check`: exit 0 with no output.
- `./scripts/trackz-dev status` before and after verification: Docker Desktop stopped; TrackZ
  containers stopped; TrackZ API stopped; iOS Simulator stopped.
- Read-only process audit: no TrackZ API, Docker Compose, `testhost`, Simulator.app, Java, or
  `aapt2` process remained. macOS CoreSimulator background helper services remained resident while
  the simulator device and app were stopped; they were not started or modified by this task.

## Manual simulator matrix

No stack or simulator was started for Task 6. Every manual matrix row remains
`pending — services intentionally stopped`. Screenshot paths, device/runtime, Dynamic Type,
Reduce Motion, offline/relaunch, rapid double-tap, seeded profile/history comparison, and EN/TH
observations must be recorded only after the user explicitly requests `./scripts/trackz-dev start`.

## Preserved unrelated worktree state

The following pre-existing user changes are outside Momentum Home Task 6 and are intentionally not
staged by this task: `.gitignore`, the existing media-origin/development-script hunks in
`docs/testing/native-ios-simulator-walkthrough.md`, `WorkoutViewModel.cs`,
`ExercisePickerPage.xaml`, three existing Mobile test files, untracked `scripts/`, and untracked
`tests/tooling/`.
