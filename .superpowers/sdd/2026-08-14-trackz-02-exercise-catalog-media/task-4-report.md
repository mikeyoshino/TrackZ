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
