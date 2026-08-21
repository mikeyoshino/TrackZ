# Task 6 — Exact Development-Only Exercise Artwork Publication

## Status

Complete. The explicit `publish-exercise-catalog` command is gated to the exact `Development` environment before any database resolution, verifies all 96 private catalog objects byte-for-byte, and atomically reviews and publishes exactly 48 matching Draft system images. Normal deployment and redeployment remain Draft. No normal API startup, migration, seeding, deployment, or hosted-service path invokes publication.

## Files and behavior

- `ObjectStorageExerciseCatalogAssetDeployment` now shares deterministic rendition generation/matching between deployment and the read-only `VerifyExactAsync`. Verification performs exactly 96 `GetAsync("system/", exactKey, ...)` calls and no put/delete operation; length, `image/png` content type, complete stream length, and bytes must all match.
- `ExerciseCatalogPublicationService` validates reviewer/rights input, verifies storage before opening a transaction, then preflights all 48 exact definitions and images before mutating any entity. It accepts only all-clean-Draft or all-complete-Published state matrices. Draft publication uses one `TimeProvider` UTC instant, one save, and one commit. Matching Published reruns make no entity writes; conflicting, mixed, Reviewed, incomplete, future-created, missing, or structurally unexpected rows fail closed.
- `ExerciseCatalogPublicationCommand` accepts only the exact ordered seven-token contract and rejects missing, duplicate, reordered, extra, empty, null, invalid, zero-GUID, and out-of-range rights arguments. It resolves `IHostEnvironment` first and requires both `IsDevelopment()` and exact ordinal `Development` before resolving `AppDbContext`.
- `Program.cs` parses deploy/publication commands before builder creation, strips command arguments from host construction, executes at most one command after build, and returns before middleware/routes.
- Infrastructure DI registers the publication service as scoped.
- API tests cover the hostile parser matrix, exact rights boundary/normalization, exact environment gate before database access, and deploy/publication command separation.
- PostgreSQL-backed tests cover exact publication metadata, no-write object verification, missing/corrupt/wrong-content-type objects, 47 rows, unexpected version/keys/source/definition fields, Reviewed/mixed states, conflicting and exact reruns, timestamp preflight, deterministic rollback, and normal deployment/redeployment remaining Draft.

## RED evidence

Initial API behavior RED:

```text
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationCommandTests|FullyQualifiedName~ExerciseCatalogDeploymentCommandTests" --verbosity minimal -m:1
exit 1: CS0246, ExerciseCatalogPublicationCommand did not exist.
```

The first authoring attempt before that intended RED exposed invalid `TheoryData` initializer syntax (CS1003/CS1525); the test syntax was corrected without production code, then the intended missing-command RED above was recorded.

Initial infrastructure behavior RED:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests|FullyQualifiedName~ExerciseCatalogDeploymentTests" --verbosity minimal -m:1
exit 1: CS0246, ExerciseCatalogPublicationService did not exist.
```

The first infrastructure GREEN attempt then exposed a test-fixture authoring error (nonexistent tracking-mode enum member names, CS0117). Inspecting the real enum identified the cause; only the mutation fixture was corrected.

Self-review parser hardening RED:

```text
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationCommandTests.Publication_command_rejects_hostile_argument_shapes" --verbosity minimal -m:1
Failed: 1, Passed: 14, Total: 15
```

The null rights array element produced `NullReferenceException` instead of the required `ArgumentException` usage failure.

Self-review two-pass timestamp RED:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests.Publication_preflights_all_created_timestamps_before_mutating_tracked_drafts" --verbosity minimal -m:1
Failed: 1, Passed: 0, Total: 1
```

The domain threw `ArgumentOutOfRangeException` only after earlier tracked Drafts had begun mutation. The service now preflights every `CreatedAt` against the one publication instant before calling `Review` on any image.

## GREEN and regression evidence

First API GREEN:

```text
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationCommandTests|FullyQualifiedName~ExerciseCatalogDeploymentCommandTests" --verbosity minimal -m:1
Passed: 22, Failed: 0, Skipped: 0, Total: 22
```

First complete PostgreSQL matrix found one test-only counter assertion issue after all production behaviors ran:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests|FullyQualifiedName~ExerciseCatalogDeploymentTests" --verbosity minimal -m:1
Passed: 20, Failed: 1, Skipped: 0, Total: 21, Duration: 10m42s
```

The failing no-write assertion read the lifetime put count (the expected 96 setup uploads) instead of the already-recorded post-reset count. No production change was made. Corrected isolated GREEN:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests.Verification_reads_exact_96_private_objects_without_writes" --verbosity minimal -m:1
Passed: 1, Failed: 0, Total: 1, Duration: 29s
```

Strengthened deterministic rollback GREEN:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests.Trigger_failure_on_24th_publish_update_rolls_back_all_48_rows" --verbosity minimal -m:1
Passed: 1, Failed: 0, Total: 1, Duration: 28s
```

The assertion verified the exact injected PostgreSQL error text, disposed the publication context, and read 48 Draft rows from a fresh context.

First required infrastructure aggregate before self-review additions:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests|FullyQualifiedName~ExerciseCatalogDeploymentTests|FullyQualifiedName~ObjectStorageIntegrationTests" --verbosity minimal -m:1
Passed: 28, Failed: 0, Skipped: 0, Total: 28, Duration: 11m49s
```

Self-review parser GREEN:

```text
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationCommandTests.Publication_command_rejects_hostile_argument_shapes" --verbosity minimal -m:1
Passed: 15, Failed: 0, Total: 15
```

Self-review timestamp preflight GREEN:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests.Publication_preflights_all_created_timestamps_before_mutating_tracked_drafts" --verbosity minimal -m:1
Passed: 1, Failed: 0, Total: 1, Duration: 31s
```

Final fresh API command aggregate:

```text
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationCommandTests|FullyQualifiedName~ExerciseCatalogDeploymentCommandTests" --verbosity minimal -m:1
Passed: 23, Failed: 0, Skipped: 0, Total: 23
```

Final fresh infrastructure/PostgreSQL/MinIO aggregate:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests|FullyQualifiedName~ExerciseCatalogDeploymentTests|FullyQualifiedName~ObjectStorageIntegrationTests" --verbosity minimal -m:1
Passed: 29, Failed: 0, Skipped: 0, Total: 29, Duration: 11m48s
```

Final fresh server build:

```text
dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --verbosity minimal -m:1
Build succeeded. 0 Warning(s), 0 Error(s). Duration: 0.58s
```

An earlier build at the first GREEN boundary also succeeded with 0 warnings and 0 errors.

## Real infrastructure, rollback, idempotency, and security evidence

- Publication/deployment tests used `PostgreSqlFixture` with a real `postgres:17-alpine` Testcontainer and real EF migrations.
- The final 29-test run included seven existing `ObjectStorageIntegrationTests` against real MinIO/Testcontainers, preserving private prefix validation and lifecycle/security coverage.
- Successful verification observed exactly 96 expected private reads, all under `system/`, and post-reset zero puts/deletes. Missing object, one-byte mismatch, and wrong content type all failed before database publication and produced no storage writes.
- All-Draft publication produced exactly 48 complete Published rows with one reviewer, normalized rights reference, all approvals, readiness, and equal one-instant review/publication timestamps.
- Exact all-Published rerun retained every original timestamp. Reviewer or rights conflicts failed without changing existing metadata. Reviewed and mixed Draft/Published matrices failed without publishing additional rows.
- A PostgreSQL trigger counted deterministic `ReviewState=Published` updates and raised on update 24. `SaveChangesAsync` failed, the transaction rolled back, the publishing context was disposed, and a fresh context observed all 48 rows still Draft.
- A future-created final ordered image proves the second pass checks all timestamps before mutating any tracked Draft entity.
- Exact ordinal `Development` plus `IHostEnvironment.IsDevelopment()` is evaluated before scope/database resolution. Production, Staging, lowercase/uppercase variants, trailing space, and empty environment names fail without database registration.
- `rg` found the publication service only in its scoped registration, explicit command, implementation, and tests; deploy, seeding, migrations, hosted services, and normal startup do not invoke it.

## Self-review

- Re-read the Task 6 brief and checked the command, service, object verification, Program selection, DI, and both new/modified test files against every listed behavior.
- Confirmed two-pass preflight occurs before mutation, the all-Published branch returns without `SaveChangesAsync`, and Draft publication performs one save/commit with one UTC instant.
- Confirmed `VerifyExactAsync` shares the same rendition pipeline as deploy but contains no put/delete call; deployment retains conflict-first scanning, missing-only upload retry, final exact verification, and Draft-only seeding behavior.
- Confirmed parser list-pattern order/arity rejects duplicate/reordered/extra flags; `Guid.TryParseExact("D")`, nonzero reviewer, trimmed 1–512 rights, and null-safe handling are enforced.
- `git diff --check` passed before report creation; no unrelated user files were changed and no broad solution or MAUI build was run.

## Process/container audit and concerns

- During the final run, task-owned PIDs were `45719` (`dotnet test`), `45726` (`vstest`), and `45727` (`testhost`); the testhost was state `R` at approximately 101% CPU, demonstrating progress rather than a stall.
- After completion, a targeted `ps` audit found no Task 6 `dotnet test`, `vstest`, or `testhost` process (only the audit shell/`rg`).
- `docker ps` after completion showed only the pre-existing compose services `native-ios-experience-postgres-1` and `native-ios-experience-minio-1`, both healthy and running for about seven hours before this task run. They were preserved. Task-created Testcontainers were gone.
- Concern: the focused infrastructure command takes approximately 12 minutes because every hostile case deliberately regenerates and verifies all exact renditions. This is expected security work, not a hang. No implementation concern remains.

## Fix Round 1/5 — deterministic concurrent publication serialization

### Important finding and root cause

Independent review identified that the publication transaction used PostgreSQL's default isolation, read unversioned Draft entities, and issued unconditional EF updates. Two different commands could therefore pass the same 48-row lifecycle preflight and both commit; the later transaction silently overwrote the first reviewer, rights, and timestamps.

The deterministic real-PostgreSQL reproducer uses two separate `AppDbContext` instances and a shared post-preflight `TimeProvider` barrier. Before serialization, both commands reached the barrier only after observing all 48 Draft rows. Both were then released together. This was the intended RED:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests.Concurrent_" --verbosity minimal -m:1
Failed: 2, Passed: 0, Skipped: 0, Total: 2, Duration: 57s
```

The conflicting test showed both different reviewer/rights commands succeeded. The matching test showed both distinct `TimeProvider` instances were consumed, proving the second command performed an overwrite rather than an idempotent Published reread.

### Serialization design and GREEN

`ExerciseCatalogPublicationService` now acquires one fixed PostgreSQL transaction-scoped advisory lock immediately after `BeginTransactionAsync` and before loading any definitions/images or running lifecycle preflight. This follows existing repository advisory-lock practice. Object generation and `VerifyExactAsync` intentionally remain before the database transaction/lock, so two commands can safely perform the 192 total exact private reads concurrently without holding a database lock.

The concurrency test remains condition-driven after the fix: the first winner reaches the post-preflight barrier; the second is observed as an ungranted advisory waiter through real `pg_locks`; only then is the winner released. After its commit, the loser acquires the lock and reloads current Published rows. Exact conflicting reviewer/rights metadata produces the service's lifecycle conflict, while exact matching metadata returns idempotently without reading the loser's publication time or overwriting timestamps.

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests.Concurrent_" --verbosity minimal -m:1
Passed: 2, Failed: 0, Skipped: 0, Total: 2, Duration: 59s
```

The final rerun with an exact loser conflict-message assertion also passed 2/2 in 59 seconds. Both cases assert 48 consistent Published rows, exact winner metadata/timestamps, readiness/approvals, 192 concurrent object reads, and zero post-reset puts/deletes.

### Cancellation, rollback, and idempotency

The advisory lock is transaction-scoped, so disposal, rollback, and cancellation release it. Tests make this observable rather than inferred:

- the update-24 PostgreSQL trigger failure rolls all 48 rows back, disposes the failed context, then a fresh publication context acquires the lock and publishes all 48;
- cancellation is issued only after `pg_locks` observes the advisory lock granted, the cancelled transaction is disposed, and a fresh publication context then acquires the lock and publishes all 48;
- matching concurrent commands both return successfully, but only the winner consumes its distinct UTC instant and the loser preserves those exact timestamps;
- conflicting concurrent commands produce exactly one successful winner and one lifecycle-conflict loser, with no partial or mixed rows.

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests.Cancellation_releases_publication_lock|FullyQualifiedName~ExerciseCatalogPublicationTests.Trigger_failure_on_24th_publish_update" --verbosity minimal -m:1
Passed: 2, Failed: 0, Skipped: 0, Total: 2, Duration: 1m33s
```

### Exact Development gate test sensitivity

The environment theory now asserts the exact gate error text. A mutation run temporarily removed the ordinal exact-name clause while retaining `IsDevelopment()`. Lowercase and uppercase variants then reached missing `AppDbContext` resolution, and the strengthened assertion caught both false positives:

```text
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationCommandTests.Publication_command_rejects_every_non_exact_development_environment" --verbosity minimal -m:1
Failed: 2, Passed: 3, Skipped: 0, Total: 5
```

After restoring the exact ordinal gate, the same command passed 5/5. The full command/parser regression passed:

```text
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationCommandTests|FullyQualifiedName~ExerciseCatalogDeploymentCommandTests" --verbosity minimal -m:1
Passed: 23, Failed: 0, Skipped: 0, Total: 23
```

### Affected regression suite and build

The affected real-PostgreSQL matrix deliberately avoided duplicating Task 7's ledgered real-MinIO publication end-to-end setup. It included concurrent conflict/matching publication, cancellation/rollback retries, exact object/no-write and mismatch checks, Published idempotency/conflicts, and deployment remaining Draft:

```text
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~ExerciseCatalogPublicationTests.Concurrent_|FullyQualifiedName~ExerciseCatalogPublicationTests.Trigger_failure_on_24th|FullyQualifiedName~ExerciseCatalogPublicationTests.Cancellation_releases|FullyQualifiedName~ExerciseCatalogPublicationTests.Verification_reads_exact|FullyQualifiedName~ExerciseCatalogPublicationTests.Publication_fails_closed_when_one_object|FullyQualifiedName~ExerciseCatalogPublicationTests.Publication_fails_closed_for_one_byte|FullyQualifiedName~ExerciseCatalogPublicationTests.Published_rerun_rejects|FullyQualifiedName~ExerciseCatalogPublicationTests.Exact_published_rerun|FullyQualifiedName~ExerciseCatalogDeploymentTests" --verbosity minimal -m:1
Passed: 13, Failed: 0, Skipped: 0, Total: 13, Duration: 7m59s
```

```text
dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --verbosity minimal -m:1
Build succeeded. 0 Warning(s), 0 Error(s). Duration: 0.59s
```

`rg` reconfirmed that publication remains reachable only through its explicit command and scoped DI registration; no migration, startup, seeding, deployment, or hosted-service invocation was added. No migration or schema change was required.

Final Fix Round 1 process audit found no task-owned `dotnet test`, `vstest`, or `testhost` PID; only the audit shell/`rg` appeared. `docker ps` showed only the pre-existing healthy compose services `native-ios-experience-postgres-1` and `native-ios-experience-minio-1`, still at their seven-hour uptime. Task-created PostgreSQL Testcontainers were disposed, and the pre-existing services were preserved. `git diff --check` passed. Concern remains limited to the expected CPU/runtime cost of exact rendition regeneration; there is no open serialization or lifecycle concern.
