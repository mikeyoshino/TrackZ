# Task 6 Report: MAUI Catalog Cache and Exercise Picker

## Scope and Base

- Base commit: `928d97dce7f5a61e1a3075e0de8ff4a0863ff7cf`
- Implemented only Plan 2 Task 6.
- Task 5 artwork and review state were not modified.
- Plan 3 was not started.

## TDD Evidence

The current `test-driven-development` skill and its `writing-good-tests.md` reference were read before production changes. Tests exercise real SQLite persistence and interface-level network/platform boundaries; they do not start MAUI platform APIs on the net10 test host.

### RED

1. Initial offline picker test:

   ```sh
   dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter ExercisePickerViewModelTests --no-restore -v:minimal
   ```

   Failed with missing `TrackZ.Mobile.Features.Exercises`, `ExerciseCache`, `ExercisePickerViewModel`, connectivity, clock, and API contracts.

2. Durable retry server ID:

   ```sh
   dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter Failed_upload_persists_server_id --no-restore -v:minimal
   ```

   Failed because retry created two server exercises (`Expected: 1`, `Actual: 2`).

3. Card performance text failed to compile because `LastDisplay` and `PersonalRecordDisplay` did not exist.

4. HTTP paging and stable problem-code tests failed to compile because `TrackZExerciseApiClient` did not exist.

5. Original/preview import test failed to compile because `LocalExerciseImageImporter` did not exist.

6. Safe background network failure and reconnect synchronization tests failed because transport failures faulted background tasks and there was no stable synchronization error state.

7. Online upload interruption failed with an uncaught `IOException` instead of creating durable pending work.

8. Pending thumbnail merge test failed because an online replacement changed `IsPendingSync` from `true` to `false`.

9. Bearer authorization and create-time connection-loss tests failed because the token provider/handler did not exist and transport failure was not queued.

10. Protected-thumbnail refresh test failed because `IExerciseThumbnailCache` did not exist.

11. Image validation tests failed because mismatched PNG bytes declared as JPEG were accepted and an oversized file reached ImageSharp decoding.

12. Online edit test failed because the cached private thumbnail was replaced with `null`.

13. Same-origin thumbnail security test failed because an absolute external URL reached the HTTP handler instead of being rejected before a bearer-bearing request.

Each failure was observed before its production implementation.

### GREEN

Focused cycles were rerun after each minimal implementation. The final required command was:

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter Exercises --no-restore -v:minimal
```

Result: **22 passed, 0 failed, 0 skipped** in 175 ms.

Architecture guard:

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter MobileCoreDependencyTests --no-restore -v:minimal
```

Result: **1 passed, 0 failed**. Mobile.Core retains its Contracts-only project boundary and no MAUI project dependency.

Additional static checks:

```sh
xmllint --noout src/TrackZ.Mobile/Components/ExercisePerformanceCard.xaml src/TrackZ.Mobile/Features/Exercises/ExercisePickerPage.xaml src/TrackZ.Mobile/Features/Exercises/CustomExercisePage.xaml src/TrackZ.Mobile/AppShell.xaml
git diff --check
```

Result: both exited 0.

## Files and Interfaces

### Testable mobile core

- `src/TrackZ.Mobile.Core/Features/Exercises/ExerciseServices.cs`
  - Connectivity, clock, UI dispatcher, catalog/custom/media APIs, file store, thumbnail cache, and draft contracts.
- `src/TrackZ.Mobile.Core/Features/Exercises/RelayCommand.cs`
- `src/TrackZ.Mobile.Core/Features/Exercises/Services/BearerTokenHandler.cs`
- `src/TrackZ.Mobile.Core/Features/Exercises/Services/TrackZExerciseApiClient.cs`
- `src/TrackZ.Mobile.Core/Features/Exercises/Services/AuthenticatedExerciseThumbnailCache.cs`
- `src/TrackZ.Mobile.Core/Features/Exercises/Services/LocalExerciseImageImporter.cs`

The requested cache/view-model/service source files remain at their Task 6 MAUI paths and are linked into Mobile.Core for deterministic net10 testing. The MAUI project excludes duplicate compilation of those linked files.

### Requested MAUI paths

- `src/TrackZ.Mobile/Features/Exercises/Models/CachedExercise.cs`
- `src/TrackZ.Mobile/Features/Exercises/Data/ExerciseCache.cs`
- `src/TrackZ.Mobile/Features/Exercises/ExercisePickerViewModel.cs`
- `src/TrackZ.Mobile/Features/Exercises/ExercisePickerPage.xaml` and code-behind
- `src/TrackZ.Mobile/Components/ExercisePerformanceCard.xaml` and code-behind
- `src/TrackZ.Mobile/Features/Exercises/CustomExercisePage.xaml` and code-behind
- `src/TrackZ.Mobile/Features/Exercises/CustomExerciseViewModel.cs`
- `src/TrackZ.Mobile/Features/Exercises/Services/CustomExerciseImageService.cs`
- `src/TrackZ.Mobile/Features/Exercises/Services/MauiExerciseServices.cs`

### Tests

- `tests/TrackZ.Mobile.Tests/Exercises/ExercisePickerViewModelTests.cs`
- `tests/TrackZ.Mobile.Tests/Exercises/CustomExerciseViewModelTests.cs`

## Cache, Picker, and Media Behavior

- Real file-backed SQLite rows are keyed by exercise ID and store `LastSyncedAt`, performance projections, thumbnail URI, custom state, and pending-sync state.
- The durable SQLite outbox stores local/server exercise IDs, draft fields, original image path/content type, library selection, and operation timestamp.
- Picker load reads SQLite first and returns immediately. When online, a background refresh traverses every opaque cursor, downloads authenticated same-origin private thumbnails into the app cache, rejects absolute thumbnail URLs before a bearer-bearing request, and replaces non-pending rows transactionally.
- Pending rows win catalog merge conflicts, so a refresh cannot erase the local preview or `Pending Sync` label.
- Body-part and normalized name search operate locally. Selection is keyed by exercise ID and survives filtering and refresh. The page has explicit All and Done actions and exposes the selected ID collection.
- The native XAML card displays cached image, exercise name, `LAST`, `PR`, pending label, checkbox, and custom-edit action. It contains no HTTP logic.
- The picker reloads on each appearance, so returning from create/edit shows current SQLite state.
- Custom create/edit validates name, body part, tracking mode, and mutually exclusive image selection. Stable server problem codes flow through `MobileApiException`.
- A selected JPEG/PNG/WebP is detected by bytes, checked against the declared content type and 5 MB server limit, copied unchanged into durable app storage, and separately resized to a maximum 512 px JPEG preview.
- Offline saves immediately write a pending cached card and durable outbox row. The preview path is displayed; the original path is retained for upload.
- Reconnect synchronization creates or updates the server exercise, persists its server ID before media continuation, then performs request/content/complete using a fresh stream over the unchanged original. Success atomically replaces the pending row with the server ID and private thumbnail route.
- Transport loss during online create is converted into durable pending intent. Transport loss after acknowledged creation stores the known server ID so retry updates rather than creates a duplicate.
- API and protected-thumbnail requests use a bearer handler backed by the MAUI secure-storage key `trackz_access_token`.

## Android Build Verification

Required command used explicit local toolchains:

```sh
JAVA_HOME=/Users/mikeyoshino/Library/Developer/TrackZ/jdk \
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android \
  -p:AndroidSdkDirectory=/Users/mikeyoshino/Library/Developer/TrackZ/android-sdk \
  -p:JavaSdkDirectory=/Users/mikeyoshino/Library/Developer/TrackZ/jdk -v:minimal
```

The first sandboxed restore was blocked by DNS for the native SQLite provider. The approved network retry restored successfully and reached MAUI XAML/C# compilation, where it reported four actionable errors (one missing namespace and three nullable event signatures). Those errors were fixed.

The post-fix `--no-restore` build then remained silent for 7:58 with the exact build process in sleeping state and no Java, aapt2, or testhost child. PID `72753` alone was terminated. A narrower `-t:Compile` retry with the same SDK/JDK also remained silent for approximately three minutes with no child compiler/package process; PID `72856` alone was terminated. A final process check confirmed no dotnet, testhost, Java, or aapt2 process remained.

Therefore the Android packaging/build gate is **environment-stalled, not reported as passing**. Net10 production-core compilation, 22 exercise tests, the architecture guard, XAML XML validation, and whitespace validation pass.

## Self-Review and External Concerns

- A read-only senior review was run. Its Task 6-local findings produced bearer injection, authenticated local thumbnail caching, safe create-time transport queuing, pending preview persistence, picker reload/All/Done actions, custom edit routing/hydration, and stronger import validation.
- The existing server custom endpoint currently rejects every non-null `LibraryImageId`, and Task 5 assets remain Draft. The view model and HTTP contract support mutually exclusive library selection, but the page cannot offer a published-library gallery until a backend read/assignment contract and approved assets exist.
- The client persists intent and acknowledged phase identifiers where the existing APIs expose them. True exactly-once recovery from a process crash or lost acknowledgement between server commit and client persistence requires a server-enforced idempotency key/reconciliation endpoint, which the prior API contract does not expose.
- Secure bearer injection expects the foundation identity flow to store the access token under `trackz_access_token`; this repository currently has no MAUI sign-in/token producer to integration-test in Task 6.
- Managed original/preview file lifecycle cleanup after successful synchronization remains a follow-up concern; correctness favors retaining the durable original until server completion.

## Controller Review Fix Round 1 (2026-08-15)

This section supersedes the earlier statements that Android compilation was stalled and that server idempotency, published-library selection, and a production token producer were unavailable. The controller explicitly brought those load-bearing prerequisites into Task 6 scope. The `receiving-code-review`, `test-driven-development`, and `writing-good-tests` instructions were reread before changes. All local commands below ran sequentially; no emulator or full solution suite was used.

### RED evidence

1. Same-origin media security:
   - New route fixtures for `//evil.example`, encoded slash, backslash, absolute URI, scheme/host/port mismatch, invalid GUID, and non-thumbnail rendition initially failed to compile because `BearerTokenHandler` had no configured-origin constructor.
   - The previous implementation allowed network-path references to reach `HttpClient`, where a bearer token could follow the resolved authority.
2. Successful-response decoding:
   - `Malformed_successful_catalog_json_is_normalized_to_stable_internal_error` leaked `JsonException`.
   - Three `{}` success fixtures for create, upload reservation, and completion were accepted instead of producing `InternalServerError` (4 failures total).
3. Metadata/media isolation:
   - New failed-thumbnail and bounded-concurrency tests exposed the missing cache update API and the sequential fail-all refresh path.
   - The reconnect test expected a local cached file but received the raw protected route.
4. Durable recovery/offline edits:
   - Phase tests failed to compile because the outbox had no operation kind, phase, upload ID, or upload URI.
   - After the first implementation, the offline edit test found the local card ID incorrectly stored as `ServerExerciseId`; the nullable coalescing bug was then fixed.
5. Server idempotency and library artwork:
   - The idempotent replay application test failed to compile because neither `ClientOperationId` nor the store lookup existed.
   - Published-library acceptance tests failed because `ExerciseDefinition` had no `LibraryImageId` relationship and handlers rejected every identifier.
   - Migration parity failed against the prior `AddImageUploadTickets` target after the model changed.
6. Native library selector and token integration:
   - The offline library selector test failed to compile because cached library rows and view-model selection/loading APIs did not exist.
   - The identity integration test failed to compile because no shared mobile token store or login/refresh/logout client existed.
7. Android XAML:
   - The first post-review compile completed rather than stalling and reported two `MAUIG1001` errors at the library item bindings because the template inherited the page view-model type. Adding `x:DataType="data:CachedLibraryImage"` resolved both.

### GREEN evidence

- Same-origin/bearer security:

  ```sh
  dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
    --filter "Thumbnail_cache|Bearer_handler|Api_http_handler" --no-restore -v:minimal
  ```

  Result: **12 passed, 0 failed**.

- Malformed 2xx JSON/shape normalization:

  ```sh
  dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
    --filter "Malformed_successful|Missing_required_success_shape" --no-restore -v:minimal
  ```

  Result: **4 passed, 0 failed**.

- Metadata-first thumbnails, failure isolation, bounded fill, and uploaded-thumbnail localization:

  ```sh
  dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
    --filter "Failed_thumbnail|bounded_parallelism|Reconnect_creates|Online_refresh_caches" --no-restore -v:minimal
  ```

  Result: **4 passed, 0 failed**.

- Durable phase recovery and offline identity coalescing:

  ```sh
  dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
    --filter "Offline_edit_coalesces|Lost_completion_response|Failed_upload_persists|Connection_loss_during_online" --no-restore -v:minimal
  ```

  Result: **5 passed, 0 failed**. Lost completion retries the same upload ID with one reservation and one content upload; an edited pending create remains a create with a nullable server identity and preserved preview.

- Final mobile regression, including token integration, SQLite migration, picker, outbox, and library selector:

  ```sh
  dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore -v:minimal
  ```

  Result: **43 passed, 0 failed, 0 skipped** in 398 ms.

- Application handler coverage:

  ```sh
  dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj \
    --filter "FullyQualifiedName~CustomExerciseTests" --no-restore -v:minimal
  ```

  Result: **18 passed, 0 failed**. This covers same-user replay, published system artwork, Draft/private/missing non-disclosure, and update assignment without partial mutation.

- Real API/PostgreSQL acceptance (each exact case ran separately):
  - `Custom_create_is_idempotent_and_accepts_only_published_library_artwork`: **1 passed**.
  - `Published_system_thumbnail_is_readable_but_draft_and_foreign_private_images_are_hidden`: **1 passed**.
  - `Create_custom_exercise_returns_created_and_is_visible_only_to_its_owner`: **1 passed**.
  - `Custom_requests_validate_canonical_fields_and_are_authorization_first_for_malformed_bodies`: **1 passed**.
  - `Concurrent_custom_creates_return_one_created_and_one_localized_duplicate_problem`: **1 passed**.
  - `List_never_serializes_image_object_keys_or_thumbnail_urls`: **1 passed** after updating the assertion to allow only opaque media routes for ready private/published images.

- Migration/model parity:

  ```sh
  dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj \
    --filter "FullyQualifiedName~Custom_exercise_migration_designer_contains_the_complete_target_model|FullyQualifiedName~Custom_exercise_migration_target_matches_current_snapshot_relational_metadata|FullyQualifiedName~Catalog_migration_matches_the_current_model" \
    --no-restore -v:minimal
  ```

  Result: **3 passed, 0 failed** against `20260815102739_AddCustomExerciseSyncIdentityAndLibraryImage`.

- Clean-architecture guards:

  ```sh
  dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj \
    --filter "FullyQualifiedName~Architecture" --no-restore -v:minimal
  ```

  Result: **4 passed, 0 failed**. The mobile architecture guard is also included in the 43-test mobile result. Mobile.Core contains no MAUI/platform dependency.

- Android XAML/C# compile with the required local toolchains:

  ```sh
  env ANDROID_HOME=/Users/mikeyoshino/Library/Developer/TrackZ/android-sdk \
      ANDROID_SDK_ROOT=/Users/mikeyoshino/Library/Developer/TrackZ/android-sdk \
      JAVA_HOME=/Users/mikeyoshino/Library/Developer/TrackZ/jdk \
      PATH=/Users/mikeyoshino/Library/Developer/TrackZ/jdk/bin:/usr/local/share/dotnet:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin \
    /usr/local/share/dotnet/dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj \
      -f net10.0-android -t:Compile --no-restore -m:1 -v:minimal \
      -p:AndroidSdkDirectory=/Users/mikeyoshino/Library/Developer/TrackZ/android-sdk \
      -p:JavaSdkDirectory=/Users/mikeyoshino/Library/Developer/TrackZ/jdk
  ```

  Result: **Build succeeded, 0 warnings, 0 errors** in 3.40 seconds. No full packaging build or emulator was run.

### Resulting contracts and behavior

- `BearerTokenHandler` adds authorization only when scheme, IDN host, and effective port exactly match the configured API origin. `AuthenticatedExerciseThumbnailCache` accepts only canonical single-slash `/api/v1/media/exercise-images/{D-guid}/thumbnail` routes before issuing a request.
- All catalog/create/reservation/completion 2xx bodies are shape-validated; malformed JSON, unsupported JSON, null bodies, empty IDs, and invalid media routes become stable `MobileApiException(InternalServerError)` values.
- Catalog metadata replaces SQLite transactionally before media work. Thumbnail fills use `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 4`; each image failure is isolated. Protected upload completion routes are converted to local cached paths before outbox deletion.
- The SQLite outbox now persists local and nullable server identities, stable operation ID, create/update kind, details/upload phase, upload ID/URI, original path/content type, preview path, and library ID. Intent is persisted before detail I/O and every acknowledged phase is persisted before the next network boundary.
- `CreateCustomExerciseRequest.OperationId` flows from the durable client operation through API/application/domain/persistence. A user-scoped filtered unique index on `(OwnerId, ClientOperationId)` plus replay lookup returns the same exercise ID after lost acknowledgements or restarts.
- `ExerciseDefinition.LibraryImageId` is an explicit restricted foreign key to `ExerciseImage`. Create/update accept only complete Published public system artwork. Draft, private, and missing IDs share the same stable invalid-request response. Catalog rows expose only a stable library image ID and opaque media route; authorized media reads allow published system artwork while preserving private ownership and non-disclosure.
- The native MAUI custom page includes a typed `CollectionView` selector. Published library metadata and authenticated previews are cached in SQLite/local storage; offline selection queues the stable image ID and preview. Metadata edits preserve both library identity and local thumbnail.
- `MobileTokenStore` is the single producer/provider for `MobileTokenKeys.AccessToken`, `RefreshToken`, and `SessionId`. `TrackZIdentityApiClient` stores login output, rotates it on refresh, and clears it on logout. `SecureMobileTokenStorage` is the sole MAUI secure-storage adapter; the bearer provider resolves the same store.

### Remaining concerns

- The two reviewer minors were explicitly deferred by the controller: overlapping picker refresh cancellation and cleanup of managed original/preview files.
- Task 5 artwork/review state remains unchanged and Draft, so the selector remains empty in production until humans publish approved system images. Tests create published fixtures only; no Task 5 asset was modified.

## Final Reviewer Gate

The mandatory read-only final review found two Critical and three Important cross-boundary issues. All were verified and corrected before commit:

- Login/account switching and logout now clear cached catalog rows, pending outbox rows, cached library metadata, and thumbnail files under the same synchronization lock used by save/reconnect. The token store also persists the authenticated `sub`; a different or missing prior user scope is cleared before new login tokens are committed. Logout clears tokens even if local cleanup fails.
- A create response is followed by an idempotent update reconciliation before media work. If an acknowledged create is edited while pending, the operation becomes an update. If the create acknowledgement was lost, replay resolves the same ID and the update applies the newest payload.
- Pending-detail edit phases retain a prior upload reservation/content phase when the same original is preserved, so completion continues with the same upload ID. Selecting a genuinely new original deliberately clears the old reservation and starts a new upload.
- `SaveAsync`, reconnect synchronization, and private-data cleanup share one semaphore; a race cannot reserve/upload the same pending operation twice or switch its bearer identity mid-phase.
- A selected library image explicitly takes display precedence over an older private upload. The real API acceptance fixture now gives the custom exercise both and verifies the selected published image route wins.
- Legacy SQLite rows with a known server ID normalize to `Update/PendingDetails`, ensuring metadata is applied instead of skipped.

Focused final-review command:

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
  --filter "IdentityTokenIntegrationTests|Editing_partially_synced|Online_save_and_reconnect|Account_cleanup|Lost_completion_response|Failed_upload_persists|Offline_edit_coalesces" \
  --no-restore -v:minimal
```

Result: **7 passed, 0 failed**.

Final mobile regression:

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore -v:minimal
```

Result: **46 passed, 0 failed, 0 skipped** in 565 ms.

The expanded `Custom_create_is_idempotent_and_accepts_only_published_library_artwork` PostgreSQL acceptance test passed with the prior-upload precedence fixture. The post-review Android `Compile` rerun also passed with **0 warnings and 0 errors** in 4.06 seconds.

Fresh pre-commit verification after the last safety edits:

- Mobile project: **46/46 passed** in 553 ms.
- Application custom exercise, media failure, and architecture filter: **41/41 passed** in 61 ms.
- Infrastructure migration/model parity: **3/3 passed** in 6 seconds.
- API idempotency/library assignment, published-media authorization, and validation acceptance filter: **3/3 passed** in 7 seconds.
- Android `net10.0-android` `Compile`: **0 warnings, 0 errors** in 0.73 seconds.

## Controller Review Fix Round 2 (2026-08-15)

The `receiving-code-review`, `test-driven-development`, `writing-good-tests`, and `systematic-debugging` instructions were reread before changes. Each finding was verified against the Round 1 implementation. Tests and builds ran one at a time; no emulator or full solution suite was used. The two explicitly deferred minors were not reopened.

### RED evidence

1. Shared account/session boundary:
   - `AccountSessionRaceTests` initially failed compilation because `AccountSessionBoundary` and boundary-aware view-model/service constructors did not exist. This represented the actual gap: picker/library operations had no common generation with login/logout cleanup.
   - After the boundary API was introduced, the two initial deterministic cases passed. Two additional delayed-response cases then exercised library and custom-create work that ignored cancellation until after logout/new-account state was committed.
   - A final file-level race fixture initially failed compilation because the authenticated thumbnail cache had no shared-boundary constructor. It delays an old response until after reset and proves that a stale temporary download is never promoted into the account cache.
2. Library metadata-first refresh:
   - `Metadata_is_usable_before_bounded_failure_isolated_thumbnail_fills_finish` failed with `Expected: 6, Actual: 0` while the first thumbnail was deliberately blocked. The prior sequential loop did not publish or persist metadata until all media calls completed.
3. Stable identity failures:
   - The new login/refresh/logout fixtures initially failed compilation because `TrackZIdentityApiClient` had no shared session-boundary dependency. The prior client mapped every non-success response to `InternalServerError` without decoding `ApiProblemDetails`.
4. Poison outbox:
   - `OutboxFailureTests` initially failed compilation because no failed/user-action queue API existed.
   - The subsequent retry test failed with different operation IDs, proving that an edited failed create would create a second local operation instead of reactivating the durable original.
5. Migration chronology:
   - The three chronology/target tests failed because `20260815143000_AddCustomExerciseSyncIdentityAndLibraryImage` was absent and the custom migration still sorted at `10:27`, before the existing `13:00` upload-ticket migration. The old test had incorrectly required that early historical target to equal the final snapshot.

### GREEN behavior

- `AccountSessionBoundary` is a platform-free singleton generation/gate. Catalog, library, thumbnail, custom-save, reconnect, refresh-token, and cache mutations capture a generation and must commit through the gate. Reset cancels the old generation before clearing SQLite/outbox/library/thumbnail state and synchronously resets live picker/custom collections. Delayed old catalog, library, and custom-create responses cannot mutate the new session.
- Login and logout perform cache/token transitions inside the same boundary reset; refresh-token output is generation checked. The custom sync lock never nests around account cleanup, avoiding the Round 1 lock-order race/deadlock. Per-generation cancellation covers network/media work; the gate covers durable or collection mutation.
- Published library metadata is transactionally replaced and made selectable before preview work. Preview fills use `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 4`, independently persist successful local paths, isolate an individual media failure, and discard stale-generation updates.
- `MobileApiException.IsRetryable` distinguishes stable business 4xx responses from transport/server retry conditions. Online definitive rejection restores the previous coalesced intent/card (or removes a new intent) before surfacing the stable error. Reconnect marks a poison operation `UserActionRequired` with durable code/message, continues later rows, excludes failed rows from automatic retries, and reactivates the same operation after a user edit.
- Identity login/refresh/logout decode canonical web-cased `ApiProblemDetails` and retain code, localized message, and field errors. Malformed/unrecognized responses alone normalize to `InternalServerError` / `The server returned an invalid response.` Logout always clears local data and shared secure-token keys even when the authorization-first response is a business problem.
- The incorrectly early migration was replaced with `20260815143000_AddCustomExerciseSyncIdentityAndLibraryImage`, after `20260815130000_AddImageUploadTickets`. The `13:00` designer remains historical and has neither future custom field; the new target equals the current snapshot. Tests inspect both Up and Down column operations.

### GREEN commands and counts

Focused RED→GREEN cycles:

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
  --filter FullyQualifiedName~AccountSessionRaceTests --no-restore -v:minimal
```

Result: **4 passed, 0 failed**. This includes delayed old catalog/library/custom responses, new-account row survival, stale LAST/PR reset, and catalog/outbox/library/thumbnail cleanup.

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
  --filter FullyQualifiedName~LibraryMetadataRefreshTests --no-restore -v:minimal
```

Result: **1 passed, 0 failed**; metadata is observable while fills are blocked, maximum concurrency is exactly four, and one failed image retains a null preview without discarding other metadata/previews.

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
  --filter FullyQualifiedName~OutboxFailureTests --no-restore -v:minimal
```

Result: **3 passed, 0 failed**; online rollback, queue-order continuation, stable failed diagnostics, automatic-retry exclusion, and user-edit reactivation are covered.

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
  --filter FullyQualifiedName~IdentityTokenIntegrationTests --no-restore -v:minimal
```

Result: **5 passed, 0 failed**; real shared token keys plus exact login, refresh, logout, field-error, and malformed-response behavior are covered.

Final sequential verification:

- Mobile project: **59 passed, 0 failed, 0 skipped** in 213 ms. This includes the stale-thumbnail promotion boundary test.
- Application custom exercise, media failure, and architecture filter: **41 passed, 0 failed**.
- API idempotency/library assignment, published-media authorization, and canonical validation filter: **3 passed, 0 failed**.
- Infrastructure current-model parity, final-target parity, migration chronology, and Up/Down sensitivity filter: **4 passed, 0 failed**.
- Android graph/XAML compile with explicit local SDK/JDK:

  ```sh
  env ANDROID_HOME=/Users/mikeyoshino/Library/Developer/TrackZ/android-sdk \
      ANDROID_SDK_ROOT=/Users/mikeyoshino/Library/Developer/TrackZ/android-sdk \
      JAVA_HOME=/Users/mikeyoshino/Library/Developer/TrackZ/jdk \
      PATH=/Users/mikeyoshino/Library/Developer/TrackZ/jdk/bin:/usr/local/share/dotnet:$PATH \
    dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android \
      -t:Compile --no-restore -m:1 -v:minimal
  ```

  Result after the final shared-thumbnail-boundary change: **Build succeeded, 0 warnings, 0 errors** in 3.50 seconds.

### Round 2 concerns

- Only the controller-deferred refresh-cancellation and managed original/preview cleanup minors remain.
- Task 5 artwork and review state remain untouched and Draft; no Plan 3 work was started.

### Final Round 2 review follow-up

A read-only review after the first Round 2 GREEN pass found five additional transition/phase defects within the new diff. They were verified and fixed with further RED→GREEN cycles:

- Three delayed identity-transition tests initially failed: old refresh threw no cancellation and replaced the new token, old logout cleared the new account (`Actual: null` user ID), and old login threw no cancellation and resurrected tokens after reset. Login/logout now use generation-conditional `TryResetAsync`; refresh captures before reading and reads its token under the session gate. All three tests pass.
- `Online_rejection_after_server_create_preserves_acknowledged_phase_for_user_action` initially found no failed row because unconditional rollback deleted the intent. The server ID is now durably persisted immediately after create and before reconciliation update; online rejection rolls back only before remote acknowledgement, otherwise retaining the exact server ID/phase as `UserActionRequired`.
- `Deterministic_missing_original_is_failed_without_blocking_later_valid_row` initially escaped with `FileNotFoundException` before reaching the later row. Stable local corruption/missing-file/invalid-state failures now receive `InternalServerError` plus `The pending exercise requires attention.`, become user-action rows, and allow later independent work to continue. Cancellation and generic transport I/O remain retryable/pending.
- The stale catalog/library error test initially observed `InvalidRequest` after session reset, and the full-reset test retained `Old Draft`. Error/loading/finally state now commits through the captured generation. The custom draft reset clears name, body part, tracking mode, IDs, media, selection, and validation errors. Custom synchronization diagnostics are committed under the same guard.
- `Edit_hydration_reads_and_applies_custom_row_through_session_boundary` initially failed compilation because edit hydration existed only in MAUI page code. `CustomExerciseViewModel.LoadForEditAsync` now owns the gated SQLite read and application; the page no longer reads the cache directly.
- The stale-thumbnail file test separately verified that an old response cannot be promoted after reset. `AuthenticatedExerciseThumbnailCache` downloads concurrently to a temporary file and promotes it only inside a generation-checked commit; runtime DI provides the shared boundary.

Focused follow-up results:

- Identity transition races: **3 passed, 0 failed**.
- Phase-aware online poison plus deterministic local queue continuation: **2 passed, 0 failed**.
- Stale response error state plus complete draft reset: **2 passed, 0 failed**.
- Boundary-owned edit hydration: **1 passed, 0 failed**.
- Stale thumbnail promotion: **1 passed, 0 failed**.

The mobile regression after all follow-up changes is **67 passed, 0 failed, 0 skipped** in 214 ms. This supersedes the earlier Round 2 mobile count.

A second narrow review found three remaining entry-point races. Additional RED tests proved each before production changes:

- Logout initially serialized the newly committed session ID (`bbbbbbbb-...`) instead of the old session ID (`aaaaaaaa-...`) when account change occurred during secure-storage read. Session ID read now occurs inside the captured-generation gate, the HTTP request uses the generation cancellation token, and conditional reset cannot revoke or clear the new account.
- `Save_queued_before_reset_cannot_persist_old_draft_in_new_session` initially threw no cancellation for the queued save. `CustomExerciseImageService.SaveAsync` now captures synchronously before waiting on its serialization lock; the view model passes its own captured generation through, so an old draft cannot acquire a new-account generation.
- The delayed dispatcher test initially left `IsRefreshing == true` after reset, and the canceled view-model save escaped as `TaskCanceledException`. Refresh-start dispatch, validation, save success, and business error mutation now commit only under the captured generation; reset cancellation is a discarded `false` save result with no stale error.

Focused GREEN results: logout session snapshot **1/1**, pre-lock queued save **1/1**, and stale picker/save UI state **2/2**. The final read-only re-review reported no unresolved Critical or Important finding in these fixes (its account-session/identity filter passed **18/18**).

Fresh final verification after those changes:

- Mobile project: **71 passed, 0 failed, 0 skipped** in 219 ms.
- Android `net10.0-android` `Compile`: **Build succeeded, 0 warnings, 0 errors** in 3.47 seconds.

## Controller Review Fix Round 3 (2026-08-15)

The `receiving-code-review`, `test-driven-development`, `writing-good-tests`, and `systematic-debugging` instructions were reread before changing code. The open finding was verified in `CustomExercisePage`: it awaited the native `FilePicker`, copied the returned stream into cache, imported directly into durable files, and called `SelectLocalImage` without capturing or checking the shared account generation.

### RED evidence

The new deterministic `LocalExerciseImageSessionRaceTests` were written first. The focused test command failed at compile time with nine `CS0246` errors because the required platform-free `ILocalExerciseImagePicker`, `LocalExerciseImageSelection`, and `LocalExerciseImageSelectionCoordinator` did not exist. That was the expected missing production boundary: no component captured a generation before the picker and atomically joined import with UI mutation.

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
  --filter FullyQualifiedName~LocalExerciseImageSessionRaceTests \
  --no-restore -v:minimal
```

After the production API was introduced, the first compile caught one test-fixture property typo (`OriginalImagePath` instead of the real `PendingCustomExercise.LocalImagePath`); correcting that test-only name produced the behavior run below.

### GREEN behavior

- `LocalExerciseImageSelectionCoordinator` captures `AccountSessionGeneration` immediately before invoking the platform-free picker interface. It deliberately holds no account lock while the native picker UI is open.
- On picker return, stream opening, bounded copy, image decode/preview generation, durable file promotion, managed-file registration, and ViewModel mutation execute through the same generation-checked account boundary. A reset request cancels the in-flight generation before waiting for the mutation gate, so a delayed copy/preview or dispatcher cannot repopulate an old account.
- `LocalExerciseImageImporter` now copies at most 5 MB into `.staging/session-{generation}/...`, validates the decoded format, generates the preview there, and only then promotes the original/preview pair. Every exception or cancellation removes staging and any partially promoted durable pair.
- The coordinator retains only the imported paths it created and deletes them on session reset. This is narrowly scoped to the account-race finding: no general managed-file garbage collection was added. SQLite/outbox cleanup remains owned by the existing account reset.
- `MauiLocalExerciseImagePicker` is the only layer using MAUI `FilePicker`; Core contracts and orchestration contain no platform types. `CustomExercisePage` now delegates to the coordinator and DI supplies the singleton coordinator, picker adapter, shared boundary, importer, and dispatcher.
- The three races are deterministic: reset while the picker is open never opens the returned stream; reset during a deliberately blocked stream copy cancels and waits for cleanup; reset at a deliberately blocked dispatcher cancels before `SelectLocalImage`. All assert empty draft paths, catalog/outbox, and destination files. The happy case proves original/preview files exist and the offline outbox stores those exact paths.
- Lock ordering has one direction: selection enters only the account gate after picker return and does not acquire the custom synchronization lock. Reset signals generation cancellation before waiting for the gate. Save/reconnect continue to capture their generation before their own serialization lock, so selection, save, and logout cannot form a lock cycle.

### GREEN commands and counts

Focused race/import tests:

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
  --filter FullyQualifiedName~LocalExerciseImageSessionRaceTests \
  --no-restore -v:minimal
```

Result: **4 passed, 0 failed, 0 skipped** in 137 ms.

Existing account/session and token-transition regression:

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
  --filter "FullyQualifiedName~AccountSessionRaceTests|FullyQualifiedName~IdentityTokenIntegrationTests" \
  --no-restore -v:minimal
```

Result: **18 passed, 0 failed, 0 skipped** in 67 ms.

Full Mobile regression (the only project-level suite run):

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore -v:minimal
```

Result: **75 passed, 0 failed, 0 skipped** in 171 ms.

Architecture guard:

```sh
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj \
  --filter FullyQualifiedName~MobileCoreDependencyTests \
  --no-restore -v:minimal
```

Result: **1 passed, 0 failed, 0 skipped** in 4 ms.

Single Android graph/XAML compile with explicit local SDK/JDK:

```sh
env ANDROID_HOME=/Users/mikeyoshino/Library/Developer/TrackZ/android-sdk \
    ANDROID_SDK_ROOT=/Users/mikeyoshino/Library/Developer/TrackZ/android-sdk \
    JAVA_HOME=/Users/mikeyoshino/Library/Developer/TrackZ/jdk \
    PATH=/Users/mikeyoshino/Library/Developer/TrackZ/jdk/bin:/usr/local/share/dotnet:/usr/bin:/bin \
  dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj \
    -f net10.0-android -t:Compile --no-restore -m:1 -v:minimal \
    -p:AndroidSdkDirectory=/Users/mikeyoshino/Library/Developer/TrackZ/android-sdk \
    -p:JavaSdkDirectory=/Users/mikeyoshino/Library/Developer/TrackZ/jdk
```

Result: **Build succeeded, 0 warnings, 0 errors** in 3.44 seconds.

No emulator, packaging build, full solution suite, parallel local test process, or additional Android attempt was run. A final `pgrep` found no `dotnet`, `testhost`, `java`, or `aapt2` process. Task 5 assets/review state and Plan 3 remain untouched.

### Round 3 concerns

- The controller-deferred general managed original/preview cleanup remains deferred. Round 3 deletes only files registered by an in-flight/current-session native selection when that account session resets.
