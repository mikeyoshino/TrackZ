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
