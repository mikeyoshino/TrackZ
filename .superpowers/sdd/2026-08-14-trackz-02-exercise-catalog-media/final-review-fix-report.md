# TrackZ Plan 2 final-review fix report

Date: 2026-08-15

Base: `ee06c5f034c72d692e3a2ef9fc8a790486cf4442`

Scope: the seven Important final-review findings only, plus direct correctness/security hardening discovered while exercising those fixes. Plan 3 was not started. Task 5 source assets, manifest records, checklist, and human-review state remain unchanged and Draft.

## Result

All seven findings were reproduced with focused tests, corrected in one branch-wide wave, and verified sequentially. The ordinary API startup path still performs no catalog seeding. The new one-shot deployment command is the only production entry point that deploys the exact manifest artwork and then invokes the idempotent seeder. No migration or persisted server model change was needed.

## Finding 1 — idempotent content PUT and ambiguous commit

### RED / sensitivity

- `Accepted_content_put_can_be_replayed_after_a_lost_no_content_response` initially returned `404 NotFound` instead of the expected `204 NoContent` on the second PUT. During final self-review the test was strengthened by expiring the already-Uploaded ticket before the replay; it again failed exactly `Expected: NoContent, Actual: NotFound` until the endpoint ordering was corrected.
- `Ambiguous_mark_uploaded_commit_is_reconciled_without_deleting_the_accepted_object` injected a lost PostgreSQL commit acknowledgement after the durable transition. Before reconciliation the API returned `500` and compensation removed the accepted staging object instead of returning idempotent success.
- `Lost_content_ack_replays_the_same_reservation_without_poisoning_the_outbox` preserves the same upload ID across a simulated lost `204`, restart, replay, and completion; its assertions are sensitive to requesting a replacement prematurely, changing the upload ID, or marking the row user-action-required.

### GREEN / design

- An owned `Uploaded` ticket is now an idempotent `204` only when the request matches its declared content type/length and the exact durable staging key still contains an object with that contract. This check intentionally precedes reservation expiry for an already accepted attempt; expired non-accepted attempts retain the prior non-disclosing behavior.
- A `TryMarkUploadedAsync` exception triggers a fresh durable query matching ticket ID, owner ID, lease-scoped staging key, declared type/length, `Uploaded` state, and cleared upload lease. A proven commit returns `204`. A proven non-commit deletes only that attempt's key and rethrows. A failed reconciliation preserves the object and raises `UploadCommitOutcomeUnknownException`; indeterminate success is never compensated.
- The mobile outbox keeps the same reservation after an I/O/lost-response failure and therefore safely replays against the idempotent API.

## Finding 2 — reservation expiry and bounded completion reconciliation

### RED / sensitivity

- The first expiry test failed to compile because `ImageUploadReservation` and `PendingCustomExercise` did not carry `ExpiresAt`; after that contract was introduced, the bounded conflict test failed to compile until an injectable retry-delay boundary existed.
- `Missing_completion_reservation_is_replaced_once_without_losing_server_identity` initially propagated `ExerciseNotFound` instead of atomically returning the row to a reservable phase.
- The restart test simulates an upload outage, advances the persisted reservation by more than six minutes, reconstructs the service/cache, injects a replacement-reservation outage, and asserts the durable intermediate reset. It would fail if the old URI were reused or original/preview/server identity were discarded.

### GREEN / design

- Server `ExpiresAt` now flows through the mobile DTO and the SQLite outbox (`UploadExpiresAt`, including additive legacy-schema upgrade).
- An absent or expired reservation is atomically persisted as `DetailsSaved` with only `UploadId`, `UploadUri`, and `UploadExpiresAt` cleared. Original path/type, preview path, local ID, operation ID, and server exercise ID are retained.
- A server `ExerciseNotFound` during content/completion resets and replaces the reservation once. A second absence propagates, preventing an unbounded loop.
- Completion `VersionConflict` is classified retryable and retries the same upload ID at most three total attempts with injectable `100 ms` and `250 ms` delays. Exhaustion leaves the durable row at `ContentUploaded` for a later synchronization rather than poisoning or spinning.

## Finding 3 — legal custom same-name reseeding

### RED / sensitivity

- `Reseed_allows_an_owned_custom_exercise_with_the_same_name_as_a_system_definition` failed with `InvalidOperationException` from the name-conflict guard after seed → legal custom insert → reseed.

### GREEN / design

- The normalized-name conflict query now considers only system definitions (`OwnerId == null`). Stable manifest IDs, system ownership, archive/body-part/tracking-mode invariants, image version/keys/provenance, and lifecycle consistency remain strict.

## Finding 4 — production catalog deployment command

### RED / sensitivity

- The initial deployment acceptance did not compile because there was no production deployment adapter/service/command.
- The partial-outage acceptance injects failure on object PUT 17 and asserts zero database definitions/images, then retries and requires all 96 deterministic objects plus 48 Draft image rows.
- The conflict acceptance seeds a wrong object and requires failure before any PUT or database mutation.
- The API acceptance starts with a fresh migrated PostgreSQL database and explicitly asserts normal startup left `exercise_definitions` empty. It invokes `ExerciseCatalogDeploymentCommand.ExecuteAsync`, then an authenticated `GET /api/v1/exercises?pageSize=50` must return exactly 48 items, every `thumbnailUrl` null, 48 Draft image rows, and 96 private-storage objects.

### GREEN / design

- `TrackZ.Api deploy-exercise-catalog --manifest <path>` is an exact, fail-fast one-shot path. It builds DI, applies migrations, deploys, seeds, and exits without starting the normal web host.
- The concrete object-storage adapter loads and validates the schema-v1 48-row manifest, resolves only repository-relative asset paths, requires PNG, identifies dimensions/animation before decode, bounds dimensions/pixels/frames, strips metadata from generated renditions, and produces deterministic 1024-max master and 320-max thumbnail PNGs under stable `system/exercises/{id}/v1/...` keys.
- Every existing object is compared byte-for-byte and by content type before uploads begin. Missing objects are uploaded, then all 96 are re-read and verified. The adapter exposes deployment confirmation to the seeder only after that full verification in the same scoped run. Partial deployment leaves the database untouched and is retryable; a conflict fails closed.
- DI registrations bind the concrete adapter, deployment interface, seeder, and command service to one scope. Documentation at `docs/deployment/catalog-and-media.md` records invocation and required database/storage/JWT/media configuration. No normal-startup seed hook was added.

## Finding 5 — binding short-lived signed GET access

### RED / sensitivity

- The API happy-path acceptance originally tried to parse the old direct image bytes as authorization JSON and failed with JSON byte `0x08` as an invalid start; there was no signed capability contract.
- Mobile signed-media tests initially failed to compile because the cache lacked separate API/media clients, configured media origin, clock, and session boundary.
- The off-origin matrix covers network-path, foreign host, suffix-confused host, user-info, wrong port, wrong image ID, encoded traversal, and query-injection URLs and requires zero media-client calls.

### GREEN / security decisions

- Authenticated `/api/v1/media/exercise-images/{imageId}/{rendition}` first performs the existing owner/published authorization and non-disclosing lookup, then returns a no-store `SignedMediaAccessDto`. No object key, bucket, owner ID, or storage credential is serialized.
- The signer uses HMAC-SHA256 over a versioned payload binding image ID, rendition, and Unix expiry. Lifetime is configurable from 15–300 seconds (60 seconds in development/testing). The public signed route verifies signature/expiry, rechecks that custom media is still private/ready or system media is still fully Published, derives its internal owner prefix server-side, and streams with private/no-store headers. Tampering is `404`; a valid expired capability is `410`; authenticated reauthorization issues a fresh capability.
- Mobile accepts only a canonical authenticated API authorization route and an absolute signed URL with the exact configured scheme/IDN host/effective port, no user-info/fragment/backslash/percent confusion, the same image/rendition path, exactly ordered `expires`/base64url-signature query fields, matching DTO/query expiry, and a future lifetime no greater than five minutes.
- Production registers a distinct credential-free signed-media `HttpClient` with redirects disabled. It rejects any default bearer before sending and explicitly sends no Authorization header. `Gone`/`401`/`403` reacquires authorization once. Download size and image MIME are bounded; bytes are cached under a local hash with their actual JPEG/PNG/WebP extension and promoted only through the captured account generation.
- Acceptances cover valid signature, tampering, expiry/refresh, owner isolation, Draft-system hiding, Published-system access, auth-first routing, no-store, off-origin rejection, no bearer leakage, bounded reacquisition, and account-reset promotion fencing.

## Finding 6 — hostile mobile preview decode

### RED / sensitivity

- `Image_import_rejects_a_highly_compressed_oversized_dimension_before_preview_decode` initially completed without an exception for a `9000 x 1` compressed PNG.
- While tightening frame metadata, the valid JPEG/PNG/WebP fixtures exposed an over-strict intermediate check (`3` valid fixtures rejected because static `FrameMetadataCollection.Count` is zero); the check was corrected to reject only counts greater than one and retain the container/frame checks.

### GREEN / security decisions

- The importer retains the 5 MB byte/MIME gate, then detects format and scans the bounded staged container before ImageSharp identification or decode.
- PNG parsing requires exact signature/chunk bounds/IHDR/data/IEND and rejects APNG chunks. JPEG parsing obtains SOF dimensions and requires a complete marker/entropy stream ending at EOI. WebP parsing requires exact RIFF size, known bounded chunks, rejects `ANIM`/`ANMF` and the VP8X animation bit, parses VP8/VP8L/VP8X dimensions, and permits exactly one image payload.
- Conservative limits are 8192 per dimension, 20 million total pixels, and one frame. ImageSharp `Identify(MaxFrames = 2)` runs before `Load(MaxFrames = 2)`; full decode occurs only after header bounds pass. The original bytes remain untouched and only the local preview is resized.
- Fixtures cover valid JPEG/PNG/WebP, unsupported GIF, animated WebP, tiny compressed oversized PNG/WebP, a forged 5000×5000 PNG decompression bomb, and truncated malformed input.

## Finding 7 — repeated expired upload-claim cleanup

### RED / sensitivity

- The domain three-crash test expected the first expired reclaim to return `false` and preserve the abandoned key, but it returned `true` and allocated a replacement immediately.
- The real-PostgreSQL three-crash acceptance initially found no cleanup candidate because `TryClaimUploadAsync` rolled back the stateful rejection that scheduled the key.

### GREEN / transaction decisions

- An expired `Uploading` claim now atomically records its exact staging key for cleanup, clears the lease, returns to `Pending`, and rejects the reclaim. Any pending cleanup/cleanup claim blocks new upload claims.
- `TryClaimUploadAsync` detects that stateful rejection, saves and commits it under the row lock, then reports unavailable. Stateless rejections still roll back.
- After the worker claims and completes deletion, a new lease-scoped staging key may be allocated. The PostgreSQL/fake-storage acceptance repeats crash → blocked reclaim → durable candidate → exact delete three times, then proves the fourth claim is distinct and storage has no unreachable key.

## Final verification

Commands ran sequentially with `--no-restore -m:1 --disable-build-servers`; no emulator or full-solution test run was used.

- Domain upload state machine: `dotnet test tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj --filter FullyQualifiedName~ImageUploadTicketTests ...` — **7 passed**.
- Application completion/failure matrix: `dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --filter FullyQualifiedName~CompleteImageUpload ...` — **19 passed**.
- Seeder: `dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter FullyQualifiedName~ExerciseCatalogSeederTests ...` — **8 passed**.
- Deployment partial/conflict acceptance: same infrastructure project, `ExerciseCatalogDeploymentTests` — **2 passed**.
- Repeated cleanup plus migration/model parity: same infrastructure project, two exact method filters — **2 passed**.
- Manifest/assets: same infrastructure project, `ExerciseCatalogManifestTests` — **11 passed**.
- Infrastructure DI/options: same infrastructure project, `DependencyInjectionTests` — **5 passed**.
- Media API/PostgreSQL/signed-storage acceptance: `dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter FullyQualifiedName~MediaEndpointTests ...` — **12 passed**.
- Exact command parser plus command→GET-48 acceptance: same API project, two exact filters — **2 passed**.
- Mobile expiry/outbox/completion: `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter FullyQualifiedName~CustomExerciseViewModelTests ...` — **30 passed**.
- Mobile signed-media/picker/cache: same mobile project, `ExercisePickerViewModelTests` — **32 passed**.
- Hostile importer fixtures: same mobile project, `LocalExerciseImageImporterSecurityTests` — **8 passed**.
- Clean-architecture guard: same mobile project, `MobileCoreDependencyTests` — **1 passed**.
- Android production graph/XAML: `dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android -t:Compile --no-restore -m:1 ...` with the pinned local SDK/JDK — **Build succeeded, 0 warnings, 0 errors** in 4.17 seconds.
- `git diff --check` — clean.
- `docker ps --format ...` — no running containers; all focused PostgreSQL Testcontainers exited.
- `ps -axo pid=,stat=,command=` filtered for `dotnet|testhost|java|aapt2|testcontainers` — no matching process.

The commit revision is recorded in the repository history after this report is added.
