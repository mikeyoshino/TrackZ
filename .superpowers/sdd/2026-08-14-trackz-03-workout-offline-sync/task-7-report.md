# Task 7 Report: History Editing and Offline End-to-End Acceptance

## Status

Implemented Task 7 from base `5b862caf1f8284ff49abee6c6e6acbfa09a7a8af`. This closes Plan 3's workout/offline-sync vertical slice; no later-plan work was started.

## Delivered

- Extended the typed sync push path end to end for `CompleteWorkout`, `EditSet`, `DeleteSet`, and `DeleteWorkout`. Existing per-owner operation locking, immutable SHA-256 request fingerprints, `ProcessedClientOperation` replay, PostgreSQL transactions, aggregate updates, `SyncChange`, tombstones, and `60001` base-version conflicts remain the single server business path. No direct edit-history route or Application framework dependency was added.
- Added a durable local Finish action. Completion, history edits, set/workout deletes, their typed outbox rows, and pre-mutation undo snapshots commit in one SQLite transaction with stable identifiers and a global monotonic mutation/outbox timeline. Completion replay with the same operation ID returns the durable graph; conflicting reuse fails closed.
- Added schema v4 with `HistoryUndo`, `SendStartedAt`, and `NeutralizedAt`, including genuine v2/v3 upgrade coverage and account-reset cleanup. Pending, retryable, rejected, conflicted, and in-flight ambiguous intent survives restart. An undo is allowed only before an operation could have reached the server (or after an authoritative rejected/conflict response), restores the exact prior graph transactionally, and neutralizes the original intent. Applied snapshots remain until the matching authoritative pull version is installed; conflict Keep Server removes the chain snapshot, while Apply Local retains it through the replacement acknowledgement.
- Generalized Task 5 conflict rebasing to the four history operation types. The exact local payload and server authority remain durable; replacement operations use the selected server version and a mutation timestamp after both local and server watermarks. Existing conflict barriers and pull-collision protections remain in force.
- Added `WorkoutHistoryCoordinator` and `WorkoutHistoryViewModel` to `TrackZ.Mobile.Core`, plus native MAUI XAML/page and platform confirmation adapter. Weighted, assisted, and bodyweight inputs remain canonical kg/reps and mode-valid. The UI exposes localized EN/TH confirmations, Pending/Syncing/Conflicted/Permanent Failure/Synced state, durable Undo, Keep Server, and Apply Local. Deleted failures stay visible but cannot be mutated again; acknowledged tombstones disappear from a second cache.
- Added transient, page-owned history activation through real DI/Shell navigation. Deactivation removes the account event handler, clears bindings, cancels page work, and commands are fenced by account generation. Account reset during an open confirmation cannot mutate the next session. No asset, XP, level, streak, badge, PR, or other client-owned achievement truth was introduced.
- Added a combined acceptance test that logs multiple sets to SQLite offline, discards/recreates persistence and coordinators, finishes, reconnects the real mobile sync client to an authenticated in-process API backed by real PostgreSQL, syncs twice, and verifies one workout, two exact ordered sets, and four processed client operations.

## TDD evidence

1. The offline acceptance test first failed to compile without `FinishAsync` and history reads; it then passed with exact restart restoration and causal operations `[StartWorkout, SaveSet, SaveSet, CompleteWorkout]` / base versions `[0,1,2,3]`.
2. Real PostgreSQL edit-history tests first received `Rejected` for `CompleteWorkout`; the implemented handlers now pass completion/edit/delete lifecycle, replay, altered-payload rejection, owner isolation, malformed shapes, auth-first behavior, tombstones, and stale-base `60001` conflicts.
3. Completion undo first observed zero durable snapshots; it now snapshots before mutation and can restore the exact active graph before send. Completion replay first threw `No active workout exists`; the same stable operation now returns the durable completed graph without duplication.
4. History coordinator tests first failed to compile, then proved offline edit/delete, kill/restart, exact LIFO restore, semantic replay, canonical mode validation, permanent snapshot retention, ambiguity fail-closed behavior, and pull-acknowledgement cleanup.
5. Two-device conflict tests retain the original edit payload and authoritative server graph, then prove both Keep Server and Apply Local resolution without local-payload loss.
6. A second-cache tombstone test first returned two sets; acknowledged deleted sets are now omitted while rejected/pending locally deleted sets remain visible through their undo link.
7. History ViewModel tests first failed to compile. Subsequent RED cases caught the assisted editor binding to weight, absent conflict actions, repeat mutation of a rejected deleted workout, and an old-session delete crossing account reset during confirmation. Each now passes through real SQLite/nonvisual components.
8. Real MAUI composition initially could not resolve the history page. It now proves transient page/ViewModel ownership, singleton outbox identity, real conflict-resolution composition, and native XAML compilation.

## Verification

- `dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --filter Workouts --no-restore -m:1 -nr:false` — PASS, 68/68.
- `dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --filter Workout --no-restore -m:1 -nr:false` — PASS, 6/6.
- `dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter Workout --no-restore -m:1 -nr:false` — PASS, 19/19 against PostgreSQL, including migration parity.
- `dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "Workout|Sync" --no-restore -m:1 -nr:false` — PASS, 26/26 against PostgreSQL, including SQLite-to-real-API acceptance.
- `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter "Workout|Sync|History|OfflineWorkout" --no-restore -m:1 -nr:false` — PASS, 120/120 after all focused additions.
- Application/Domain/Mobile architecture filters — PASS, 6/6, 2/2, and 3/3.
- API and Mobile.Core builds — PASS, 0 warnings / 0 errors.
- iOS MAUI `Compile` target — PASS with XAML source generation.
- iOS `iossimulator-arm64` build — PASS with XAML source generation.
- Android build was attempted and stopped at the exact environment gate `XA5300`: no Android SDK directory is installed. No SDK or emulator was downloaded or started.

## Concerns

- Android packaging remains unverified until an Android SDK is installed. Platform-independent Core, native iOS XAML compilation, and the iOS simulator build are green.
- Two late focused invocations intermittently encountered the sandbox's VSTest TCP bind restriction (`SocketException (13)`) after compilation. The identical isolated test passed once in the default sandbox and the final architecture rerun passed 3/3 with the already-approved `dotnet test` execution outside that socket restriction. All larger sequential suites passed; no product code change was made for the runner environment.
