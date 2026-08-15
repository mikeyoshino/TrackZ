# TrackZ Plan 2A: Media Lifecycle Hardening

> **For Codex:** Execute with `superpowers:subagent-driven-development`, TDD, one implementer and one reviewer per task, then fresh final review.

**Goal:** Close the two remaining Plan 2 upload races: accepted PUTs must remain successful after `Uploaded -> Processing -> Completed`, and staging objects created by late writers must have a production-enforced deletion bound.

**Architecture:** Keep HTTP/storage orchestration in API, persistence checks behind `IExerciseImageUploadStore`, PostgreSQL/S3 details in Infrastructure. Compare the exact immutable staging contract. Keep per-lease unique staging keys and enforce a private-bucket lifecycle rule scoped to `staging/` as the crash-safe cleanup fallback.

**Stack:** .NET 10, ASP.NET Core, EF Core/Npgsql/PostgreSQL 17, AWS S3 SDK/MinIO, xUnit/Testcontainers.

**Worktree:** `/Users/mikeyoshino/gitRepos/TrackZ/.worktrees/trackz-02-exercise-catalog-media`

**Base:** `d2f1ace1b5c36ccc5bb94ea9ce331117ef9fcb91` on `feat/trackz-02-exercise-catalog-media`.

**Thermal rule:** Run focused tests sequentially with `--maxcpucount:1`; no emulator; terminate only exact plan-owned PIDs.

---

## Task 1: Monotonic accepted-PUT reconciliation

**Files:**

- Modify `src/TrackZ.Application/Media/IExerciseImageUploadStore.cs`
- Modify `src/TrackZ.Infrastructure/Persistence/AppDbContext.cs`
- Modify `src/TrackZ.Api/Endpoints/MediaEndpoints.cs`
- Modify `tests/TrackZ.Infrastructure.Tests/Persistence/ExerciseCatalogPersistenceTests.cs`
- Modify `tests/TrackZ.Api.Tests/Media/MediaEndpointTests.cs`

### 1. RED: persistence successor matrix

Add real-PostgreSQL tests proving the exact accepted attempt is durable in `Uploaded`, `Processing`, and `Completed`. Add negative rows for a different owner, staging key, content type, length, and pre-acceptance `Pending`/`Uploading` state.

Rename the port method to the invariant:

```csharp
Task<bool> IsAcceptedUploadAttemptDurableAsync(
    Guid ticketId, Guid ownerId, string stagingObjectKey,
    string contentType, long length, CancellationToken cancellationToken);
```

The production query must require exact owner/key/content contract equality, cleared upload lease fields, and one of the three monotonic accepted states.

Run RED:

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Accepted_upload_attempt" --no-restore --maxcpucount:1
```

Expected: `Processing` and `Completed` fail under the current Uploaded-only query.

### 2. RED: API replay and ambiguous commit after advancement

Extend the existing ambiguous transition seam so `TryMarkUploadedAsync` commits, advances the same ticket to `Processing` and in a separate case `Completed`, then throws the simulated post-commit exception. Assert PUT returns 204, retains the exact staging object, and does not write a duplicate. Add replay cases in `Processing` and `Completed`; verify the stored object's exact length/content type before 204. A mismatched contract must not enter the idempotent path.

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "FullyQualifiedName~Content_put|FullyQualifiedName~Ambiguous_mark_uploaded" --no-restore --maxcpucount:1
```

Expected: current code false-fails or deletes the accepted object.

### 3. GREEN: implement exact monotonic behavior

In `UploadContentAsync`, treat only `Uploaded`, `Processing`, and `Completed` as accepted replay states. Verify the persisted staging object against the exact declared contract before returning 204 and never claim a second upload in this path.

In the ambiguous exception path:

- exact durable accepted attempt: return 204 and retain bytes;
- reconciliation query failure: throw `UploadCommitOutcomeUnknownException` and retain bytes;
- conclusive non-commit: delete only this request's lease-scoped key and rethrow the original exception.

Never normalize storage keys; compare the exact persisted key.

### 4. Verify and commit

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Accepted_upload_attempt" --no-restore --maxcpucount:1
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "FullyQualifiedName~Content_put|FullyQualifiedName~Ambiguous_mark_uploaded|FullyQualifiedName~Request_put_complete" --no-restore --maxcpucount:1
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --filter Media --no-restore --maxcpucount:1
git add src/TrackZ.Application/Media/IExerciseImageUploadStore.cs src/TrackZ.Infrastructure/Persistence/AppDbContext.cs src/TrackZ.Api/Endpoints/MediaEndpoints.cs tests/TrackZ.Infrastructure.Tests/Persistence/ExerciseCatalogPersistenceTests.cs tests/TrackZ.Api.Tests/Media/MediaEndpointTests.cs
git commit -m "fix: reconcile accepted upload successors"
```

---

## Task 2: Mandatory staging-object lifecycle fallback

**Files:**

- Modify `src/TrackZ.Infrastructure/Media/ObjectStorage.cs`
- Create `src/TrackZ.Infrastructure/Media/StagingObjectLifecycleService.cs`
- Modify `src/TrackZ.Infrastructure/DependencyInjection.cs`
- Modify `src/TrackZ.Api/Endpoints/MediaEndpoints.cs`
- Modify `src/TrackZ.Api/appsettings.json`
- Modify `src/TrackZ.Api/appsettings.Development.json`
- Modify `src/TrackZ.Api/appsettings.Testing.json`
- Modify `tests/TrackZ.Infrastructure.Tests/Media/ObjectStorageIntegrationTests.cs`
- Modify `tests/TrackZ.Api.Tests/Media/MediaEndpointTests.cs`
- Modify `docs/deployment/catalog-and-media.md`

### 1. RED: real MinIO lifecycle contract

Add `StagingExpirationDays` to `ObjectStorageOptions`, default 1 and valid only from 1 through 30. Add:

```csharp
public interface IStagingObjectLifecycle
{
    Task EnsureConfiguredAsync(CancellationToken cancellationToken);
}
```

Extend the MinIO test policy with only `s3:GetLifecycleConfiguration` and `s3:PutLifecycleConfiguration`. With real MinIO assert the TrackZ rule is enabled, prefix-scoped exactly to `staging/`, expires after the configured days, is idempotent, and fails closed on a conflicting TrackZ staging rule. Reassert objects remain private.

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ObjectStorageIntegrationTests" --no-restore --maxcpucount:1
```

Expected: compile/test failure because lifecycle support does not exist.

### 2. GREEN: fail-fast production initializer

Implement `StagingObjectLifecycleService : IHostedService`. Startup calls `EnsureConfiguredAsync`; authorization, connectivity, and conflicting-rule failures must fail API startup. Stop is a no-op. Register one `ObjectStorage` singleton as both `IObjectStorage` and `IStagingObjectLifecycle`, then register the hosted service.

Install or verify only the TrackZ expiration rule for `staging/`; preserve unrelated bucket rules and never change `private/` or `system/`. Do not log secrets, object keys, owner IDs, or SDK exception messages.

Set `StagingExpirationDays: 1` explicitly in API appsettings. Document the two lifecycle permissions and startup fail-closed behavior.

### 3. RED/GREEN: minimize the lease window and prove fallback scope

Buffer and validate the bounded HTTP request body before `TryClaimUploadAsync`, so the two-minute lease covers object PUT plus durable transition rather than client buffering. After buffering, use a fresh store decision so a concurrent accepted upload follows exact replay semantics.

Add a deterministic API test using task-completion sources: block an old `PutAsync`, let its lease expire/reclaim, release the old writer, and assert it can materialize only its unique key below `staging/{owner}/`; the newer attempt never references that key. The acceptance explicitly proves any otherwise unreachable key remains inside the mandatory lifecycle-managed prefix; it does not pretend S3 day-based expiry is immediate.

### 4. Verify and commit

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ObjectStorageIntegrationTests" --no-restore --maxcpucount:1
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "FullyQualifiedName~Content_put|FullyQualifiedName~late_writer" --no-restore --maxcpucount:1
dotnet test tests/TrackZ.Architecture.Tests/TrackZ.Architecture.Tests.csproj --no-restore --maxcpucount:1
dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --maxcpucount:1
git add src/TrackZ.Infrastructure/Media/ObjectStorage.cs src/TrackZ.Infrastructure/Media/StagingObjectLifecycleService.cs src/TrackZ.Infrastructure/DependencyInjection.cs src/TrackZ.Api/Endpoints/MediaEndpoints.cs src/TrackZ.Api/appsettings.json src/TrackZ.Api/appsettings.Development.json src/TrackZ.Api/appsettings.Testing.json tests/TrackZ.Infrastructure.Tests/Media/ObjectStorageIntegrationTests.cs tests/TrackZ.Api.Tests/Media/MediaEndpointTests.cs docs/deployment/catalog-and-media.md
git commit -m "fix: enforce staging object expiration"
```

---

## Final review and handoff

1. Request a fresh independent review of `d2f1ace..HEAD` against the two original race narratives.
2. Run `git diff --check d2f1ace..HEAD`, focused media suites sequentially, and a clean API build. Android is excluded because this plan changes no mobile code.
3. Audit plan-owned `dotnet`, `testhost`, Java, `aapt2`, Testcontainers, and MinIO processes.
4. Write `.superpowers/sdd/2026-08-15-trackz-02a-media-lifecycle-hardening/final-report.md` with exact commands, counts, commits, reviews, and audit.
5. Append the Plan 2 ledger; preserve its historical blocked lines and record the Plan 2A resolution.

Plan 2A is code-complete only when both Important findings are independently reviewed as addressed. The 48 exercise images remain Draft until the product owner approves anatomy, movement clarity, originality, and rights; that external publication gate is unchanged.
