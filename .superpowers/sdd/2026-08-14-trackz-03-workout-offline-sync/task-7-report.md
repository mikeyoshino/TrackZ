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

## Fix Round 1

The independent Task 7 review reported one Critical and four Important findings. The fix round closes all five without adding a direct history route or a framework dependency to Application:

- `CompleteWorkout`, `EditSet`, `DeleteSet`, and `DeleteWorkout` now recompute owner-scoped `ExercisePerformance` rows inside the same PostgreSQL transaction as the aggregate, processed-operation replay record, and `SyncChange`. Per-owner/exercise advisory locks serialize concurrent workouts. LAST and PR follow weighted/bodyweight/assisted ranking rules, editing updates the row in place, and deletion falls back to earlier valid completed history or removes the projection.
- Exact local completed history now participates in `IExerciseHistorySource`. After a kill/recreation it competes with remote/cache data by completion chronology and wins ties, so a just-finished offline workout is immediately the exact previous session without a second crash-prone cache write.
- An exact no-op history edit returns the unchanged graph with `Guid.Empty` before allocating a timestamp, snapshot, outbox row, or version. The caller-provided ID remains unbound and a later real edit uses the correct predecessor version.
- Apply Local now moves the durable undo snapshot to the replacement operation in the same SQLite transaction. Restarted Undo restores the exact pre-edit graph and neutralizes the explicitly conflicted replacement ancestry; ambiguous/non-conflict ancestry still fails closed.
- Keep Server now also marks every archived operation in the resolved original/replacement chain as neutralized in the same transaction. Audit rows remain durable, but they cannot reappear as a permanent failure with a missing Undo snapshot or block a later history mutation.
- History status now consumes the composed connectivity source, observes offline/online transitions, and releases its subscription on page deactivation. Permanent completion/edit/delete failure remains visible, disables repeat edit/set-delete/workout-delete, and retains Undo/conflict actions. Existing EN/TH status text is used.
- Local workout and final-set tombstones now suppress a stale cached/remote copy of the same completed exercise session across process recreation; an older still-valid local or remote session remains eligible as the fallback.
- The named fake acceptance was demoted to a local persistence unit. The authenticated TestServer/PostgreSQL acceptance now drops responses after committed `CompleteWorkout` and `EditSet`, recreates SQLite/coordinators, replays the exact persisted operation IDs through `ProcessedClientOperation`, performs a real set tombstone, and pulls it into an independent second-device SQLite cache.

### Fix-round TDD evidence

1. Projection RED: the completion test failed with `Sequence contains no elements` for `ExercisePerformances`; the real PostgreSQL lifecycle now proves weighted/bodyweight/assisted LAST/PR, concurrent duplicate completion replay, edit recalculation, delete-set/delete-workout fallback, clearing, and owner isolation.
2. Local-history RED: the restart test could not construct a source with local history; it now restores exact set IDs, order, mode, kg, and reps and selects the newer local completion over an older cache row.
3. No-op RED: the coordinator returned the supplied operation ID and wrote intent; it now returns `Guid.Empty`, preserves all versions/timestamps/snapshot counts, and the later real successor synchronizes from the unchanged base version.
4. Apply-Local RED: restarted Undo failed with `The durable undo snapshot is missing`; it now transfers the snapshot and transactionally neutralizes both replacement and conflicted ancestor.
5. UI RED: connectivity/status tests first failed to compile without a real source; offline transitions, subscription ownership, rejected restart state, mutation disabling, and retained Undo now pass.
6. The real ambiguity test commits on the server and deliberately throws before mobile acknowledgement for both completion and edit. Recreated coordinators replay the same IDs, and the final PostgreSQL/second-cache assertions prove one aggregate, no duplicate sets/projections, exact history, and a propagated tombstone.
7. Keep-Server RED: a resolved conflict remained in the history status source as `Rejected`, and invoking Keep Server on a replacement that itself conflicted initially left its ancestor unneutralized. Resolution now walks both directions and neutralizes the original plus replacement while retaining both audit rows.
8. Local-tombstone RED: both a deleted completed workout and a deleted final set returned the stale cached latest session after SQLite recreation; both now return no invalidated session (or the next valid chronological candidate).

### Fix-round verification

- Domain workout/performance: PASS, 82/82.
- Application workout/sync: PASS, 7/7.
- Infrastructure workout/sync/migration parity: PASS, 26/26 against PostgreSQL.
- API workout/sync: PASS, 29/29 against PostgreSQL, including the real dual-SQLite ambiguity/tombstone acceptance.
- Mobile workout/sync/history: PASS, 127/127.
- Application/Domain/Mobile architecture: PASS, 6/6, 2/2, and 3/3.
- API and Mobile.Core builds: PASS, 0 warnings / 0 errors.
- iOS MAUI `Compile`: PASS, 0 warnings / 0 errors with XAML source generation.
- Android: exact external `XA5300` gate because no Android SDK directory is installed; no SDK/emulator was downloaded or started.

### Fix-round concerns

- A full `iossimulator-arm64` packaging attempt emitted the application DLL/XAML successfully, then remained silent in a post-compile tool step and was terminated after cancellation did not exit. The deterministic iOS `Compile` target is green, but this particular full packaging invocation is not counted as verified.
- Whole-solution `dotnet format --verify-no-changes` reports many whitespace diagnostics in unchanged pre-existing files and cannot load the absent Android restore graph. `git diff --check` is clean; unrelated formatting was deliberately not rewritten.

## Fix Round 2

The second independent review found a resolution race in a rebased conflict: after the replacement was marked sent but before any authoritative response was stored, Keep Server could discard that ambiguous replacement and install the older ancestor authority.

- Keep Server now inspects the complete ancestor/descendant replacement chain inside its existing SQLite transaction. A `Pending` operation with `SendStartedAt` is an unresolved server outcome, so resolution fails before changing the graph, outbox rows, or undo snapshot.
- Existing Undo and Apply Local barriers remain fail-closed for the same chain: Undo of the sent replacement refuses it, Undo of the ancestor sees the later intent, and another Apply Local cannot pass the live-replacement barrier.
- History projects unresolved sent intent as a dedicated `Reconciling` state before Conflict/Pending precedence. The EN/TH status remains visible even offline, uses a non-success native status color, and disables edit, set/workout delete, Undo, Keep Server, and Apply Local until an exact response is stored.
- A handler-confirmed pre-commit Retryable response clears `SendStartedAt` and retains the same operation/payload with bounded retry metadata, proving that the server did not apply it; conflict resolution can then safely proceed. Once `CommitAsync` starts, transient commit/disposal failures propagate as an ambiguous transport failure instead of being mislabeled Retryable. Applied/Conflict/Rejected responses retain their existing authoritative paths.
- A new authenticated TestServer/PostgreSQL acceptance creates a real server version-4 conflict, rebases locally, commits the replacement as server version 5, and drops the response before the mobile acknowledgement. Recreated SQLite/coordinators cannot apply stale version-4 authority or Undo; replay of the identical replacement operation returns the single processed result, pulls version 5, and only then purges the snapshot.

### Fix-round-2 TDD evidence

1. Core RED: the real-SQLite conflict-chain test expected Keep Server to throw, but no exception was raised and stale ancestor authority was installed. The passing test now proves the graph, both operation records, payloads, and transferred snapshot remain exact across restart.
2. UI RED: the localized history test failed to compile because `Reconciling` did not exist. EN/TH theory cases now prove state text, offline precedence, retained conflict visibility, and disabled commands.
3. Retryable coverage proves a sent-but-not-committed replacement remains ambiguous before its response, then clears only `SendStartedAt` after the exact Retryable result and can be safely resolved without losing the snapshot prematurely.
4. Real PostgreSQL acceptance proves the complementary committed case: server version 5 is observable while mobile still holds the sent replacement, stale resolution is blocked, and identical-ID replay plus pull reconciles exactly once.
5. Commit-acknowledgement RED expected the transient `TimeoutException` to escape after `CommitAsync` began, but the handler returned Retryable. The handler now distinguishes pre-commit failures from commit-started ambiguity; the focused test is green and the existing real-PostgreSQL pre-commit transient test remains green.
6. The full API regression exposed a noncanonical base64url alias whose decoded HMAC bytes were unchanged, allowing a textually tampered sync cursor. A focused infrastructure test reproduced the acceptance before canonical decode validation; it is green and the owner/tamper API test is now stable.

### Fix-round-2 verification

- Domain workout/performance: PASS, 82/82.
- Application workout/sync: PASS, 8/8.
- Infrastructure workout/sync/migration parity: PASS, 27/27 against PostgreSQL.
- API workout/sync: PASS, 30/30 against PostgreSQL.
- Mobile workout/sync/history: PASS, 130/130.
- Application/Domain/Mobile architecture: PASS, 6/6, 2/2, and 3/3.
- API and Mobile.Core builds: PASS, 0 warnings / 0 errors.
- iOS MAUI `Compile`: PASS, 0 warnings / 0 errors with XAML source generation.
- Android: exact external `XA5300` gate because no Android SDK directory is installed; no SDK/emulator was downloaded or started.

### Fix-round-2 concerns

- Android packaging remains unverified until an Android SDK is installed.
- The earlier full `iossimulator-arm64` post-compile packaging stall remains an environment/tooling concern; deterministic iOS XAML `Compile` is green and is the only iOS result claimed in this round.
