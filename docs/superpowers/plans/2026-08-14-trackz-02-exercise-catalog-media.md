# TrackZ Exercise Catalog and Media Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a tested 48-exercise system catalog, user-owned custom exercises, secure image upload/storage metadata, offline catalog cache, and an Exercise Picker that displays LAST and PR.

**Architecture:** Exercise metadata and media records are server-owned feature slices. Binary images live in object storage; PostgreSQL stores keys, review state, and rights metadata. MAUI caches catalog/projection DTOs in SQLite and renders native XAML cards.

**Tech Stack:** .NET 10, MediatR, EF Core/Npgsql, ASP.NET Core, S3-compatible object storage abstraction, .NET MAUI XAML, SQLite, xUnit.

## Global Constraints

- One standard exercise has one newly created, dedicated anatomy illustration; do not crop or republish the user's reference poster.
- System assets use grayscale anatomy, muted-red target muscle, correct equipment/movement, and no embedded text.
- AI output remains Draft until human review records anatomy, movement, and rights approval.
- Support `Chest=1`, `Back=2`, `Shoulders=3`, `Arms=4`, `Legs=5`, `Core=6`.
- Support `Weighted=1`, `Bodyweight=2`, `Assisted=3`; never reorder published enum values.
- Custom images are private, EXIF-stripped, validated, thumbnail-rendered, and accessed through signed URLs.
- Exercise Picker must return and cache `lastPerformedAt`, `lastBestSet`, and `allTimeBest` without N+1 requests.

---

### Task 1: Exercise Domain and Wire Contracts

**Files:**
- Create: `src/TrackZ.Domain/Exercises/BodyPart.cs`
- Create: `src/TrackZ.Domain/Exercises/TrackingMode.cs`
- Create: `src/TrackZ.Domain/Exercises/ExerciseDefinition.cs`
- Create: `src/TrackZ.Domain/Exercises/ExerciseImage.cs`
- Create: `src/TrackZ.Domain/Exercises/ExerciseImageReviewState.cs`
- Create: `src/TrackZ.Contracts/Exercises/ExerciseSummaryDto.cs`
- Create: `src/TrackZ.Contracts/Exercises/PerformanceSetDto.cs`
- Test: `tests/TrackZ.Domain.Tests/Exercises/ExerciseDefinitionTests.cs`

**Interfaces:**
- Consumes: Domain base conventions from Plan 1.
- Produces: `ExerciseDefinition.CreateSystem`, `ExerciseDefinition.CreateCustom`, and immutable exercise-summary contracts.

- [ ] **Step 1: Write failing ownership and enum-stability tests**

```csharp
[Fact]
public void Custom_exercise_requires_owner()
{
    Assert.Throws<ArgumentException>(() =>
        ExerciseDefinition.CreateCustom(Guid.Empty, "My Press", BodyPart.Chest, TrackingMode.Weighted));
}

[Fact]
public void Published_enum_values_are_stable()
{
    Assert.Equal(1, (int)BodyPart.Chest);
    Assert.Equal(6, (int)BodyPart.Core);
    Assert.Equal(3, (int)TrackingMode.Assisted);
}
```

- [ ] **Step 2: Run the tests and verify missing types**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter Exercises
```

Expected: compile failure for missing exercise types.

- [ ] **Step 3: Implement the aggregate and contracts**

```csharp
public enum BodyPart { Chest = 1, Back = 2, Shoulders = 3, Arms = 4, Legs = 5, Core = 6 }
public enum TrackingMode { Weighted = 1, Bodyweight = 2, Assisted = 3 }
public enum ExerciseImageReviewState { Draft = 1, Reviewed = 2, Published = 3 }

public sealed record PerformanceSetDto(decimal? WeightKg, decimal? AssistedKg, int Reps);
public sealed record ExerciseSummaryDto(
    Guid Id, string Name, BodyPart BodyPart, TrackingMode TrackingMode,
    string? ThumbnailUrl, DateTimeOffset? LastPerformedAt,
    PerformanceSetDto? LastBestSet, PerformanceSetDto? AllTimeBest, bool IsCustom);
```

Normalize custom names, enforce non-empty owner IDs, and prohibit changing `TrackingMode` after set history exists.

- [ ] **Step 4: Run domain tests**

```bash
dotnet test tests/TrackZ.Domain.Tests --filter Exercises
```

Expected: PASS.

- [ ] **Step 5: Commit the exercise model**

```bash
git add src/TrackZ.Domain/Exercises src/TrackZ.Contracts/Exercises tests/TrackZ.Domain.Tests
git commit -m "feat: model exercise catalog"
```

### Task 2: Catalog Persistence and List Query

**Files:**
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/ExerciseDefinitionConfiguration.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/ExerciseImageConfiguration.cs`
- Create: `src/TrackZ.Application/Exercises/ListExercises/ListExercisesQuery.cs`
- Create: `src/TrackZ.Application/Exercises/ListExercises/ListExercisesHandler.cs`
- Create: `src/TrackZ.Contracts/Common/CursorPage.cs`
- Create: `src/TrackZ.Api/Endpoints/ExerciseEndpoints.cs`
- Test: `tests/TrackZ.Application.Tests/Exercises/ListExercisesHandlerTests.cs`
- Test: `tests/TrackZ.Api.Tests/Exercises/ListExercisesEndpointTests.cs`

**Interfaces:**
- Consumes: `ExerciseSummaryDto`, authenticated user ID, and `IAppDbContext`.
- Produces: `ListExercisesQuery(BodyPart? BodyPart, string? Search, string? Cursor, int PageSize)` and cursor-paged results.

- [ ] **Step 1: Write a failing single-query projection test**

```csharp
[Fact]
public async Task List_returns_catalog_with_user_specific_last_and_pr()
{
    await SeedCatalogAndPerformanceAsync();
    var page = await _handler.Handle(new ListExercisesQuery(BodyPart.Chest, null, null, 20), default);
    var press = Assert.Single(page.Items, x => x.Name == "Incline Barbell Bench Press");
    Assert.Equal(70m, press.LastBestSet!.WeightKg);
    Assert.Equal(75m, press.AllTimeBest!.WeightKg);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Application.Tests --filter ListExercisesHandlerTests
```

Expected: missing query/handler and persistence mappings.

- [ ] **Step 3: Implement EF mappings and projected query**

Use one server query joining visible system/custom exercises to the current user's `ExercisePerformance` projection. Search normalized names, order by name then ID, cap `PageSize` at 50, and encode the last ordering tuple in an opaque cursor.

```csharp
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record ListExercisesQuery(
    BodyPart? BodyPart, string? Search, string? Cursor, int PageSize = 30)
    : IRequest<CursorPage<ExerciseSummaryDto>>;
```

- [ ] **Step 4: Add migration and run API/application tests**

```bash
dotnet ef migrations add AddExerciseCatalog --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --output-dir Persistence/Migrations
dotnet test tests/TrackZ.Application.Tests --filter ListExercises
dotnet test tests/TrackZ.Api.Tests --filter ListExercises
```

Expected: filtering, search, ownership, projection, and pagination pass.

- [ ] **Step 5: Commit catalog query**

```bash
git add src/TrackZ.Infrastructure src/TrackZ.Application/Exercises src/TrackZ.Api/Endpoints tests
git commit -m "feat: query exercise catalog with performance"
```

### Task 3: Custom Exercise CRUD

**Files:**
- Create: `src/TrackZ.Application/Exercises/CreateCustom/CreateCustomExerciseCommand.cs`
- Create: `src/TrackZ.Application/Exercises/CreateCustom/CreateCustomExerciseHandler.cs`
- Create: `src/TrackZ.Application/Exercises/UpdateCustom/UpdateCustomExerciseCommand.cs`
- Create: `src/TrackZ.Application/Exercises/DeleteCustom/DeleteCustomExerciseCommand.cs`
- Modify: `src/TrackZ.Api/Endpoints/ExerciseEndpoints.cs`
- Test: `tests/TrackZ.Application.Tests/Exercises/CustomExerciseTests.cs`

**Interfaces:**
- Consumes: current user ID and optional approved library-image ID/uploaded-image key.
- Produces: create/update/archive commands and user-scoped endpoints.

- [ ] **Step 1: Write failing ownership and archive tests**

```csharp
[Fact]
public async Task User_cannot_update_another_users_custom_exercise()
{
    var command = new UpdateCustomExerciseCommand(_otherUsersExerciseId, "Renamed", BodyPart.Back, null);
    var error = await Assert.ThrowsAsync<BusinessException>(() => _handler.Handle(command, default));
    Assert.Equal(BusinessErrorCode.ExerciseNotFound, error.Code);
}

[Fact]
public async Task Delete_archives_exercise_without_deleting_history()
{
    await _delete.Handle(new DeleteCustomExerciseCommand(_exerciseId), default);
    Assert.True((await _db.Exercises.FindAsync(_exerciseId))!.IsArchived);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Application.Tests --filter CustomExerciseTests
```

- [ ] **Step 3: Implement commands and endpoints**

Validate name length `1..100`, require a published system image or an uploaded image owned by the user, and scope lookup by owner so unauthorized IDs return `ExerciseNotFound = 20001`.

```csharp
public sealed record CreateCustomExerciseCommand(
    string Name, BodyPart BodyPart, TrackingMode TrackingMode,
    Guid? LibraryImageId, string? UploadedImageKey) : IRequest<Guid>;
```

- [ ] **Step 4: Run feature tests**

```bash
dotnet test tests/TrackZ.Application.Tests --filter CustomExercise
dotnet test tests/TrackZ.Api.Tests --filter CustomExercise
```

- [ ] **Step 5: Commit custom exercises**

```bash
git add src/TrackZ.Application/Exercises src/TrackZ.Api/Endpoints tests
git commit -m "feat: manage custom exercises"
```

### Task 4: Secure Media Upload and Renditions

**Files:**
- Create: `src/TrackZ.Application/Common/Interfaces/IObjectStorage.cs`
- Create: `src/TrackZ.Application/Common/Interfaces/IImageProcessor.cs`
- Create: `src/TrackZ.Application/Media/RequestUpload/RequestImageUploadCommand.cs`
- Create: `src/TrackZ.Application/Media/CompleteUpload/CompleteImageUploadCommand.cs`
- Create: `src/TrackZ.Infrastructure/Media/ObjectStorage.cs`
- Create: `src/TrackZ.Infrastructure/Media/ImageProcessor.cs`
- Modify: `compose.yaml`
- Create: `src/TrackZ.Api/Endpoints/MediaEndpoints.cs`
- Test: `tests/TrackZ.Application.Tests/Media/ImageUploadTests.cs`

**Interfaces:**
- Consumes: authenticated user ID and object-storage abstraction.
- Produces: short-lived upload request, validated completion, stripped master, thumbnail, and private metadata row.

- [ ] **Step 1: Write failing validation tests**

```csharp
[Theory]
[InlineData("image/gif", 1024, 50003)]
[InlineData("image/jpeg", 6_000_001, 50002)]
public async Task Invalid_upload_is_rejected(string contentType, long bytes, int expectedCode)
{
    var ex = await Assert.ThrowsAsync<BusinessException>(() =>
        _handler.Handle(new RequestImageUploadCommand(contentType, bytes), default));
    Assert.Equal(expectedCode, (int)ex.Code);
}
```

Reserve `50003 = ImageTypeNotSupported`; assert its value in error-contract tests.

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Application.Tests --filter ImageUploadTests
```

- [ ] **Step 3: Implement two-phase upload**

Accept only JPEG/PNG/WebP up to 5,000,000 bytes. Generate an owner-scoped random object key, issue a five-minute upload authorization, then on completion verify server-observed size/MIME, strip EXIF, render 320 px and 1024 px variants, and persist keys. Never trust a client-provided final URL. Add a private MinIO service/bucket to `compose.yaml` for local S3-compatible integration tests; expose it only to the local Docker network and use the S3 SDK through `IObjectStorage`.

```csharp
public sealed record UploadRequestDto(Guid UploadId, Uri UploadUri, DateTimeOffset ExpiresAt);
public sealed record CompleteImageUploadCommand(Guid UploadId) : IRequest<ExerciseImageDto>;
```

- [ ] **Step 4: Run media tests**

```bash
dotnet test tests/TrackZ.Application.Tests --filter Media
dotnet test tests/TrackZ.Api.Tests --filter Media
```

Expected: invalid type/size/ownership fail with stable codes; valid image produces private rendition keys.

- [ ] **Step 5: Commit media handling**

```bash
git add src/TrackZ.Application/Media src/TrackZ.Application/Common/Interfaces src/TrackZ.Infrastructure/Media src/TrackZ.Api/Endpoints tests
git commit -m "feat: add secure exercise image uploads"
```

### Task 5: Initial 48-Exercise Manifest and Reviewed Assets

**Files:**
- Create: `assets/exercises/catalog.json`
- Create: `assets/exercises/prompt-template.md`
- Create: `assets/exercises/review-checklist.md`
- Create: `src/TrackZ.Infrastructure/Persistence/Seed/ExerciseCatalogSeeder.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Seed/ExerciseManifest.cs`
- Test: `tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogManifestTests.cs`

**Interfaces:**
- Consumes: approved exercise-image style and image generation/review workflow.
- Produces: 48 stable catalog IDs, 48 asset briefs/files, and idempotent database seeding.

- [ ] **Step 1: Write a failing manifest test**

```csharp
[Fact]
public void Catalog_contains_48_unique_exercises_across_all_body_parts()
{
    var items = ExerciseManifest.Load("assets/exercises/catalog.json");
    Assert.Equal(48, items.Count);
    Assert.Equal(48, items.Select(x => x.Id).Distinct().Count());
    Assert.All(Enum.GetValues<BodyPart>(), part => Assert.Equal(8, items.Count(x => x.BodyPart == part)));
}
```

- [ ] **Step 2: Run and verify missing manifest failure**

```bash
dotnet test tests/TrackZ.Infrastructure.Tests --filter ExerciseCatalogManifestTests
```

- [ ] **Step 3: Create the exact catalog manifest**

Use deterministic IDs and these 48 names, eight per body part:

```text
Chest: Barbell Bench Press; Incline Barbell Bench Press; Dumbbell Bench Press; Incline Dumbbell Press; Chest Press Machine; Cable Fly; Pec Deck Fly; Decline Push-Up
Back: Lat Pulldown; Pull-Up; Assisted Pull-Up; Seated Cable Row; Chest-Supported Row; Barbell Row; One-Arm Dumbbell Row; Straight-Arm Pulldown
Shoulders: Overhead Press; Dumbbell Shoulder Press; Machine Shoulder Press; Lateral Raise; Cable Lateral Raise; Rear Delt Fly; Face Pull; Upright Row
Arms: Barbell Curl; Dumbbell Curl; Hammer Curl; Preacher Curl; Triceps Pushdown; Overhead Triceps Extension; Skull Crusher; Close-Grip Bench Press
Legs: Back Squat; Front Squat; Leg Press; Romanian Deadlift; Leg Extension; Seated Leg Curl; Bulgarian Split Squat; Standing Calf Raise
Core: Cable Crunch; Hanging Knee Raise; Hanging Leg Raise; Ab Wheel Rollout; Weighted Sit-Up; Decline Sit-Up; Reverse Crunch; Pallof Press
```

Assign `TrackingMode` explicitly in JSON; `Pull-Up`, `Decline Push-Up`, `Hanging Knee Raise`, `Hanging Leg Raise`, `Ab Wheel Rollout`, `Decline Sit-Up`, and `Reverse Crunch` are Bodyweight; `Assisted Pull-Up` is Assisted; all others are Weighted.

- [ ] **Step 4: Generate and review dedicated assets**

For each manifest row, use the approved scientific-educational prompt template to create one original image. Save the master at `assets/exercises/images/<stable-slug>.png`. Create a contact sheet, have the product owner/anatomy reviewer record pass/fail for equipment, grip, joints, movement path, target muscle, originality, and rights, then set only passed records to `Reviewed`. Failed records remain Draft and are regenerated with one targeted correction.

- [ ] **Step 5: Seed idempotently and commit approved catalog work**

```bash
dotnet test tests/TrackZ.Infrastructure.Tests --filter ExerciseCatalogManifestTests
git add assets/exercises src/TrackZ.Infrastructure/Persistence/Seed tests/TrackZ.Infrastructure.Tests
git commit -m "feat: seed reviewed exercise catalog"
```

### Task 6: MAUI Catalog Cache and Exercise Picker

**Files:**
- Create: `src/TrackZ.Mobile/Features/Exercises/Models/CachedExercise.cs`
- Create: `src/TrackZ.Mobile/Features/Exercises/Data/ExerciseCache.cs`
- Create: `src/TrackZ.Mobile/Features/Exercises/ExercisePickerViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Exercises/ExercisePickerPage.xaml`
- Create: `src/TrackZ.Mobile/Components/ExercisePerformanceCard.xaml`
- Create: `src/TrackZ.Mobile/Features/Exercises/CustomExercisePage.xaml`
- Create: `src/TrackZ.Mobile/Features/Exercises/CustomExerciseViewModel.cs`
- Create: `src/TrackZ.Mobile/Features/Exercises/Services/CustomExerciseImageService.cs`
- Test: `tests/TrackZ.Mobile.Tests/Exercises/ExercisePickerViewModelTests.cs`
- Test: `tests/TrackZ.Mobile.Tests/Exercises/CustomExerciseViewModelTests.cs`

**Interfaces:**
- Consumes: `ExerciseSummaryDto` API response and SQLite cache.
- Produces: body-part/search filtering, multi-selection, cached LAST/PR cards, selected exercise IDs, and custom exercise create/edit with library or uploaded image.

- [ ] **Step 1: Write a failing offline-cache ViewModel test**

```csharp
[Fact]
public async Task Offline_picker_displays_cached_last_and_pr_and_allows_multi_select()
{
    _connectivity.IsOnline.Returns(false);
    await _cache.SeedAsync(ExerciseSamples.ChestPressWithPerformance);
    await _sut.LoadAsync(BodyPart.Chest);
    _sut.ToggleSelectionCommand.Execute(_sut.Exercises[0]);
    Assert.Equal(70m, _sut.Exercises[0].LastBestSet!.WeightKg);
    Assert.Equal(75m, _sut.Exercises[0].AllTimeBest!.WeightKg);
    Assert.Single(_sut.SelectedExerciseIds);
}
```

- [ ] **Step 2: Run and verify failure**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter ExercisePickerViewModelTests
```

- [ ] **Step 3: Implement cache, ViewModel, and native XAML card**

Use SQLite rows keyed by exercise ID plus a `LastSyncedAt` timestamp. Load cache immediately, refresh in the background when online, replace rows transactionally, and expose observable selection state. The card displays image, name, `LAST`, `PR`, and a checkbox; it contains no HTTP logic.

`CustomExerciseViewModel` validates name/body part/tracking mode, permits either a published library image or a local JPEG/PNG/WebP, strips no data locally beyond resizing the upload preview, and sends the original through the two-phase server upload. If offline, save the custom exercise and local image URI as pending outbox work; upload and replace it with the private server key when connectivity returns. Until then, display the local thumbnail with a Pending Sync label.

- [ ] **Step 4: Run tests and Android build**

```bash
dotnet test tests/TrackZ.Mobile.Tests --filter Exercises
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android
```

- [ ] **Step 5: Commit the catalog milestone**

```bash
git add src/TrackZ.Mobile tests/TrackZ.Mobile.Tests
git commit -m "feat: add offline exercise picker"
```

Plan 2 is complete when the API returns the 48-item visible catalog with user projections, custom CRUD/media security tests pass, reviewed assets are seeded, and the native Exercise Picker works from cached data offline.
