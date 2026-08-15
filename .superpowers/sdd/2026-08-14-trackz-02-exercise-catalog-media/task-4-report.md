# Task 4 report — Secure exercise-image uploads

## Lifecycle

The chosen lifecycle is the preferred attached flow. A request must name an existing active custom exercise owned by the authenticated caller. The server creates a five-minute, owner-scoped `ImageUploadTicket`, generates a random staging key, and returns only an opaque upload ID plus a time-limited PUT authorization. Completion is owner-scoped. It moves the ticket `Pending -> Processing -> Completed` under an EF concurrency token, validates object length/content type and fully decodes it, writes JPEG 1024px/320px private renditions, records the new `ExerciseImage` version, and removes staging. A replacement never deletes the prior ready row. Failures mark the ticket failed and best-effort remove staging and partial renditions; the caller obtains a fresh ticket to retry.

Application owns framework-neutral `IObjectStorage`/`IImageProcessor`; AWS S3 and ImageSharp remain Infrastructure. Neither completion DTO contains an internal key, bucket, credentials, or metadata. Signed read URLs are produced by the server-owned storage resolver.

## RED/GREEN record

- RED: `dotnet test tests/TrackZ.Application.Tests --filter ImageUploadTests --no-restore` failed with `CS0234` because `TrackZ.Application.Media` did not yet exist.
- GREEN: the same command passed `3/3` after implementing declared MIME/size validation (`image/gif -> 50003`, zero length -> `10009`, over 5 MB -> `50002`).
- Migration parity initially revealed a real failure in the existing persistence test: it compared the old Task 3 frozen target model with the now-expanded current snapshot. The test now deliberately compares the newest Task 4 frozen target model.

## Sensitivity and verification

- Request accepts only exact `image/jpeg`, `image/png`, or `image/webp`; size is bounded at 5,000,000 bytes. Completion rechecks observed length and decoded bytes, rejects multiple frames, caps pixels at 20,000,000, auto-orients and removes EXIF/XMP/IPTC/ICC metadata before encoding.
- Foreign/unknown/expired/non-pending completion resolves to the same `ExerciseNotFound` response; route authorization is required before JSON parsing. Root malformed known request fields map case-insensitively to canonical `exerciseId`, `contentType`, or `length`; nested/unknown paths map to `body`.
- `compose.yaml` supplies MinIO without a host port plus a private bucket bootstrap. Credentials are configuration/environment values intended for local use only.
- `dotnet test tests/TrackZ.Application.Tests --no-restore`: PASS, `31/31`.
- `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore`: PASS, `24/24`.
- `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore`: PASS, 0 warnings / 0 errors.
- `dotnet ef migrations has-pending-model-changes --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --no-build`: `No changes have been made to the model since the last migration.`
- A focused API filter for newly added endpoint tests reported no matching tests because this task adds the route but the existing API test project has no Media endpoint fixture. The API build succeeded. The full existing API suite was invoked; its runner discovered the project but emitted no final aggregate in this execution environment.

No Task 5 assets or mobile work was started.

## Fix round 1

- Root cause: the first completion flow mapped operational exceptions to invalid-image responses and let post-commit staging deletion/read-URL work enter the same failure path as the durable commit. It also used an S3 presign that exposed service details and could not enforce the declared object length.
- RED: `dotnet test tests/TrackZ.Domain.Tests --filter ImageUploadTicketTests --no-restore` failed because `ImageUploadTicket.TryClaim` did not exist.
- GREEN: the same command passed after adding an expiring processing lease: an active lease yields deterministic conflict; an expired lease is reclaimable. Completion now keeps post-commit staging deletion best-effort and returns opaque API rendition paths without S3/MinIO signatures or keys.
- Sensitive defenses added: authenticated opaque PUT gateway checks exact `Content-Length`, declared MIME, and a bounded 5,000,000-byte stream before private storage; completion buffers/compares observed bytes to the declared length, detects canonical format, identifies dimensions before decode, rejects non-JPEG/PNG/WebP and multi-frame images, then strips metadata and re-encodes bounded JPEG renditions.
- Durable commit acquires an exercise advisory lock, rechecks active ownership, serializes version allocation and creates a unique `(ExerciseDefinitionId, Version)` image row. Failure before commit releases the lease for retry and removes only attempt-owned final objects; failure after commit cannot fail/delete the ready image.
- Migration history was squashed into `20260815130000_AddImageUploadTickets`, after Task 3; old empty/earlier Task 4 migrations were removed.
- Fresh checks: `dotnet test tests/TrackZ.Application.Tests --no-restore` PASS 31/31; `dotnet test tests/TrackZ.Infrastructure.Tests --filter ExerciseCatalogPersistenceTests --no-restore` PASS 7/7; `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore` PASS 0 warnings/errors; `dotnet ef migrations has-pending-model-changes --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --no-build` reports no pending model changes.

## Fix round 2

- Root cause: the PUT gateway stored staging bytes without a durable `Pending -> Uploaded` transition, so completion could race a still-pending upload. The processing lease was only a timestamp; an expired claimant could still write final keys and commit after a newer claimant reclaimed the ticket.
- RED: `dotnet test tests/TrackZ.Domain.Tests --filter ImageUploadTicketTests --no-restore` failed because `Uploaded`, `TryMarkUploaded`, and `ProcessingLeaseId` did not exist. GREEN: the same focused suite passed `3/3` after the state-machine transition and new fencing token were added.
- The authenticated gateway now writes staging bytes, then atomically changes the ticket from `Pending` to `Uploaded`; a rejected transition cleans staging best-effort, while an already-durable concurrent upload is never deleted. Lock reads clear EF's request-local tracked instance so a concurrent PUT cannot reuse a stale `Pending` state. Completion only claims `Uploaded` tickets (or reclaims an expired processing lease), and every claim receives a fresh `ProcessingLeaseId`.
- `CommitCompletionAsync` locks the ticket and requires the active matching lease ID. Rendition paths include that ID, so stale-attempt cleanup can remove only its own master/thumbnail. Invalid terminal failures mark the claimed ticket failed and remove staging plus that attempt's finals; cancellation/unexpected failures release to `Uploaded`, preserve staging for retry, and remove only attempt finals. Post-commit staging cleanup remains best-effort and cannot delete final objects or change `Completed`.
- The frozen `20260815130000_AddImageUploadTickets` migration, designer, and snapshot now include `ProcessingLeaseId`, explicit `Uploaded` state values, and state/lease check constraints. PostgreSQL tests cover migration parity, lease reclaim fencing, concurrent ticket version serialization, archive recheck, and prior-ready-image preservation.
- Fresh verification: `dotnet test tests/TrackZ.Domain.Tests --no-restore` PASS 53/53; `dotnet test tests/TrackZ.Application.Tests --no-restore` PASS 31/31; `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore` PASS 29/29; `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore` PASS with 0 warnings/errors; `dotnet ef migrations has-pending-model-changes --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --no-build` reports no pending model changes.

## Fix round 3

- Added deterministic in-memory application fakes for the upload store, object storage, and image processor. Focused completion tests prove the observed-size terminal path returns `50003` and deletes staging, oversized observed input returns `50002`, and unexpected pre-commit storage errors propagate while returning the ticket to `Uploaded` without deleting staging.
- Added ImageSharp-generated processor fixtures covering PNG detection, JPEG rendition output, bounded/aspect-preserving master and thumbnail dimensions, single-frame output, explicit EXIF/XMP/IPTC/ICC stripping, and non-seekable/unsupported input rejection.
- Focused verification: `dotnet test tests/TrackZ.Application.Tests --filter CompleteImageUploadFailureTests --no-restore` PASS 3/3; `dotnet test tests/TrackZ.Infrastructure.Tests --filter ImageProcessorTests --no-restore` PASS 2/2.

## Fix round 4

- RED exposed three completion defects: a failed claim save was rewritten as a misleading `409`, cancellation at storage/processor/commit left the ticket fenced in `Processing` because compensation reused the canceled request token, and post-commit staging deletion was fire-and-forget. Claim-save failures now preserve their operational exception; retry release and attempt cleanup use an independent cancellation token; post-commit staging removal is awaited but remains best-effort, so a completed DTO is stable even when staging cleanup fails.
- Expanded deterministic application fakes cover independent staging get/read, master/thumbnail put, final commit, staging/attempt-delete failure, unexpected processor failure, cancellation at each pre-commit boundary, retry/terminal state, staging/final presence, completed DTO idempotency, prior-ready preservation, and a reclaimed-lease loser that cannot remove finals under the winner's tokenized path.
- RED exposed processor defects: `ResizeMode.Max` upscaled small inputs, truncated JPEGs and malformed headers could leak ImageSharp parser exceptions, and a huge PNG header could drive `Identify` through payload data. The processor now preserves small image dimensions, checks JPEG/PNG/WebP container completion, normalizes decoder-format failures to `InvalidDataException`, observes cancellation before parsing, and rejects PNG dimensions/pixels from the IHDR before full decode. It still applies the independent decoded-image dimensions/frame checks.
- Real ImageSharp fixtures cover JPEG/PNG/WebP detection; single-frame GIF/BMP/TIFF rejection; malformed/truncated data; animated WebP rejection; a 25,000,000-pixel crafted PNG header with read-count evidence that less than 100 KB of a 1 MB payload is read; EXIF orientation/GPS plus XMP/IPTC/comment stripping with metadata-free JPEG output (including no ICC profile); JPEG output, one frame, aspect tolerances, max 1024/320 dimensions, and no-upscale behavior.
- Focused verification: `dotnet test tests/TrackZ.Application.Tests --filter CompleteImageUploadFailureTests --no-restore` PASS 17/17; `dotnet test tests/TrackZ.Infrastructure.Tests --filter ImageProcessorTests --no-restore` PASS 13/13.
- Sequential verification: `dotnet test tests/TrackZ.Application.Tests --no-restore` PASS 48/48; `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore` PASS 42/42; `dotnet test tests/TrackZ.Domain.Tests --no-restore` PASS 53/53; `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore` PASS with 0 warnings/errors.

## Fix round 5 — API/storage acceptance

- Added a PostgreSQL-backed `MediaEndpointTests` WebApplicationFactory with deterministic object-storage and image-processor fakes. It proves authorization happens before request-body parsing; accepts `application/json` and UTF-8 `application/*+json`; rejects unsupported and duplicate charsets; canonicalizes known root paths; returns exact Thai validation/problem contracts; and exercises request → opaque PUT gateway → completion → opaque thumbnail read without exposing bucket, key, credential, or MinIO details.
- Missing PUT/read routes now emit the same localized `ApiProblemDetails` contract as other exercise-missing paths: `404`, `20001`, `application/problem+json`, trace ID, and no field errors. This replaces the framework-default problem response that omitted the stable error code and localized message.
- `ObjectStorageOptions` has no usable defaults. Startup validates an absolute HTTP(S) endpoint, DNS-compatible bucket, nonempty access key, and secret. Production/development app settings no longer contain object-storage credentials; the explicit Testing environment uses isolated non-routable values and the media factory replaces the network adapter with a deterministic fake.
- Catalog projection remains a single query and now selects only the highest-version ready private user-upload for the authenticated custom exercise, rendering its opaque thumbnail route. Draft system artwork stays `null`, and raw storage keys never enter the response.
- MinIO compose now uses the pinned server image health endpoint, has bounded bootstrap retry, fails fast on policy/user setup errors, keeps root credentials limited to MinIO/bootstrap, denies anonymous access, and grants the API user only object `GetObject`/`PutObject`/`DeleteObject` permissions on the private bucket.
- RED/GREEN: options validation test first failed because defaults made empty object-storage configuration valid; it passes after defaults were removed. The catalog thumbnail API test first returned `null`; it passes after adding the single-query latest-ready projection. The media filter passes `7/7`.
- Fresh verification: `dotnet test tests/TrackZ.Api.Tests --filter MediaEndpointTests --no-restore --disable-build-servers` PASS `7/7`; `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore --disable-build-servers` PASS `43/43`; `dotnet test tests/TrackZ.Application.Tests --no-restore --disable-build-servers` PASS `48/48`; `dotnet test tests/TrackZ.Domain.Tests --no-restore --disable-build-servers` PASS `53/53`; `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --disable-build-servers` PASS with 0 warnings/errors; `docker compose config` PASS.

## Fix round 6 — real MinIO adapter integration

- Added `ObjectStorageIntegrationTests` using the exact pinned MinIO server and `mc` images. The test creates an isolated Testcontainers network, starts MinIO with per-test random root credentials and a random host port, waits for `/minio/health/live`, and uses a separate `mc` bootstrap container to create a private bucket, deny anonymous access, create a non-root API user, and attach a bucket-object-only `GetObject`/`PutObject`/`DeleteObject` policy.
- The production `ObjectStorage` adapter is constructed with only that scoped API credential (asserted distinct from root). It proves private owner-prefix PUT/GET/DELETE, exact JPEG content type and bytes, missing object => `null`, anonymous unauthenticated GET => `403`, and that traversal, sibling-owner, and foreign-prefix keys fail locally before a network call. Container and network disposal are owned by the async fixture.
- Fresh verification: `dotnet test tests/TrackZ.Infrastructure.Tests --filter ObjectStorageIntegrationTests --no-restore --disable-build-servers` PASS `1/1` against real MinIO; full `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore --disable-build-servers` PASS `44/44`; `docker compose config` PASS; `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --disable-build-servers` PASS with 0 warnings/errors.

## Fix round 7 — final review recovery hardening

- PUT now durably claims an `Uploading` lease before object I/O and assigns a lease-scoped staging key. Only the claim owner can move that attempt to `Uploaded`; an expired attempt cannot overwrite a newer claim and only deletes its own key.
- Completion treats a commit acknowledgement failure as ambiguous. It reads a fresh durable ticket/image outcome using owner and exact rendition keys: a confirmed completed row is returned without deleting finals; a confirmed absent row follows normal retry compensation; a failed reconciliation raises `UploadCommitOutcomeUnknownException` and preserves finals for the worker.
- Upload tickets preserve staging/final cleanup candidates across terminal failure, expired processing reclaim, and staging-delete failure. The hosted cleanup service retries idempotent deletions without logging keys or owner IDs, and never scans completed rendition keys.
- Container validation now bounds width and height independently at 8192 and requires exact JPEG/PNG/WebP container EOF, rejecting trailing/polyglot payloads.
- Fresh focused verification: `dotnet test tests/TrackZ.Application.Tests --filter CompleteImageUploadFailureTests --no-restore --disable-build-servers` PASS `18/18`; `dotnet test tests/TrackZ.Infrastructure.Tests --filter ExerciseCatalogPersistenceTests --no-restore --disable-build-servers` PASS `12/12`; API build PASS; EF reports no pending model changes.
