# Expanded Exercise Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand TrackZ from 48 to 90 system exercises, with exactly 15 exercises in each existing category and complete project-owned artwork, deployment, publication, API, and mobile-cache coverage.

**Architecture:** `assets/exercises/catalog.json` remains the source of truth. The existing 48 stable definitions and published media are preserved, 42 new definitions and media records are added idempotently, and publication is upgraded to accept an existing-published plus new-draft catalog without rewriting prior review metadata. The existing paged API and mobile aggregation flow stay intact and gain 90-item regression coverage.

**Tech Stack:** .NET 10, C#, EF Core/Npgsql, ASP.NET Core, ImageSharp, S3-compatible object storage, .NET MAUI, SQLite, xUnit, project-owned PNG assets.

**Spec:** `docs/superpowers/specs/2026-09-05-expanded-exercise-catalog-design.md`

## Global Constraints

- Keep `BodyPart` values `Chest=1`, `Back=2`, `Shoulders=3`, `Arms=4`, `Legs=5`, and `Core=6` unchanged.
- Keep `TrackingMode` values `Weighted=1`, `Bodyweight=2`, and `Assisted=3` unchanged.
- Preserve the current 48 exercise UUIDs, names, categories, modes, artwork, storage objects, and publication metadata.
- Add exactly seven exercises per category, for 90 system exercises and 15 per category.
- Use unique project-owned 1024 × 1024 RGBA PNG artwork for every new exercise.
- Do not delete or modify custom exercises, workout history, other APIs, other databases, or unrelated containers.
- Keep manifest schema `trackz.exercise-catalog` version `1`.
- Keep new artwork `Draft` until explicit review and publication.
- Use TDD for code changes and inspect every generated image before accepting it.
- Do not deploy until all targeted tests pass.

## Stable New Catalog Entries

These UUIDs are assigned once and become immutable.

| Body part | UUID | Exercise | Mode | Slug |
| --- | --- | --- | --- | --- |
| Chest | `6fee6f1f-edcb-471e-8ab9-49855f3aa4b6` | Push-Up | Bodyweight | `push-up` |
| Chest | `f42f83b8-c38a-489b-808c-01f0ad3dfb45` | Chest Dip | Bodyweight | `chest-dip` |
| Chest | `b7952b0f-7427-49ac-8464-6144a4dcceb3` | Decline Barbell Bench Press | Weighted | `decline-barbell-bench-press` |
| Chest | `2d2a2222-3149-4510-a823-008aad084be8` | Smith Machine Bench Press | Weighted | `smith-machine-bench-press` |
| Chest | `b1b38714-7d84-4044-a318-e0507ecc2a9b` | Dumbbell Fly | Weighted | `dumbbell-fly` |
| Chest | `dc85a3f4-6737-4c91-9746-822c6335fc69` | Low-to-High Cable Fly | Weighted | `low-to-high-cable-fly` |
| Chest | `107d60c7-5436-4fa4-bd46-d282c8f9a3ef` | High-to-Low Cable Fly | Weighted | `high-to-low-cable-fly` |
| Back | `bac8064d-c02c-4989-8ed6-61f1a0264cc2` | Conventional Deadlift | Weighted | `conventional-deadlift` |
| Back | `3c629c78-4993-41a0-8489-13496c803109` | T-Bar Row | Weighted | `t-bar-row` |
| Back | `997da94a-ead9-455f-a33e-401cf4c58c65` | Inverted Row | Bodyweight | `inverted-row` |
| Back | `21d3b334-30a3-4ff7-bd65-2b209c1e1021` | Neutral-Grip Lat Pulldown | Weighted | `neutral-grip-lat-pulldown` |
| Back | `086b42f9-7055-4c88-a96a-f30aeaab2ec3` | Wide-Grip Lat Pulldown | Weighted | `wide-grip-lat-pulldown` |
| Back | `40046edc-3551-4d86-9b48-5240a1798e01` | Single-Arm Cable Row | Weighted | `single-arm-cable-row` |
| Back | `ad9c1476-16c1-4577-8eb8-f611c068cb0a` | Machine High Row | Weighted | `machine-high-row` |
| Shoulders | `1f6193b6-c9b2-49d5-855c-b200be184005` | Arnold Press | Weighted | `arnold-press` |
| Shoulders | `9a1467c5-6d4b-48ab-ae55-0f55d656388f` | Dumbbell Front Raise | Weighted | `dumbbell-front-raise` |
| Shoulders | `66452088-fada-4505-8efe-8e1f40697244` | Cable Front Raise | Weighted | `cable-front-raise` |
| Shoulders | `f5e26454-3182-4b22-93e0-ebf9eddb1197` | Bent-Over Reverse Fly | Weighted | `bent-over-reverse-fly` |
| Shoulders | `7778206a-d9fd-47b1-86ff-54f9dbfb956e` | Reverse Pec Deck | Weighted | `reverse-pec-deck` |
| Shoulders | `ad64162d-cea5-4634-9a4f-56516c35a8f5` | Landmine Press | Weighted | `landmine-press` |
| Shoulders | `bb7d4225-6d07-48fa-ab6a-7086e9ca98c1` | Dumbbell Shrug | Weighted | `dumbbell-shrug` |
| Arms | `f47534bf-95b5-4b2d-88e6-57c82ab21dc2` | EZ-Bar Curl | Weighted | `ez-bar-curl` |
| Arms | `1ed6c327-00bf-4246-8d63-44174a07886d` | Incline Dumbbell Curl | Weighted | `incline-dumbbell-curl` |
| Arms | `29eac18e-b075-4695-ad07-217377dc49f3` | Cable Curl | Weighted | `cable-curl` |
| Arms | `96b626cf-45fc-4d23-8f97-150c9c21626d` | Concentration Curl | Weighted | `concentration-curl` |
| Arms | `5129fab6-7a4f-4942-bf87-336a9af33331` | Bench Dip | Bodyweight | `bench-dip` |
| Arms | `71528cbf-4420-41f4-953d-ea042e6f21ad` | Triceps Dip | Bodyweight | `triceps-dip` |
| Arms | `f31fdd3c-78f9-41e4-ba2c-fa893fb2870f` | Single-Arm Cable Pushdown | Weighted | `single-arm-cable-pushdown` |
| Legs | `b3127b81-a08c-4310-862a-063398f96457` | Goblet Squat | Weighted | `goblet-squat` |
| Legs | `66ba962e-bd16-4b50-a7de-d6675a956603` | Hack Squat | Weighted | `hack-squat` |
| Legs | `470f531a-7530-483a-b3fb-6c2c8fbc34ae` | Sumo Deadlift | Weighted | `sumo-deadlift` |
| Legs | `0e798fcf-5477-445c-a014-cc8956a4f839` | Walking Lunge | Weighted | `walking-lunge` |
| Legs | `1edfb74a-5cdb-4994-a3d8-e6f21f930123` | Hip Thrust | Weighted | `hip-thrust` |
| Legs | `15f2871e-b705-4865-9950-8e0fef86b97a` | Lying Leg Curl | Weighted | `lying-leg-curl` |
| Legs | `fb06d513-49ed-4bd7-80c0-e77a6942859f` | Seated Calf Raise | Weighted | `seated-calf-raise` |
| Core | `21af3244-02ae-4b55-95de-38691b470ba0` | Plank | Bodyweight | `plank` |
| Core | `c283aac1-6aa3-499e-b654-f7e6d989c821` | Side Plank | Bodyweight | `side-plank` |
| Core | `44a13be4-4567-4571-a023-3dbf206e36fa` | Dead Bug | Bodyweight | `dead-bug` |
| Core | `c5f1b776-f16a-4c88-96aa-ec1f4728980b` | Bird Dog | Bodyweight | `bird-dog` |
| Core | `86ddc383-11df-4a33-bff2-0b9eadd21c86` | Russian Twist | Weighted | `russian-twist` |
| Core | `09faa319-d24b-4df4-a53b-024ade496a61` | Bicycle Crunch | Bodyweight | `bicycle-crunch` |
| Core | `a7eabd25-cb93-4aa3-9134-f59f7022ba6b` | Mountain Climber | Bodyweight | `mountain-climber` |

---

### Task 1: Expand the Manifest Contract and Stable Catalog

**Files:**
- Modify: `tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogManifestTests.cs`
- Modify: `src/TrackZ.Infrastructure/Persistence/Seed/ExerciseManifest.cs`
- Modify: `assets/exercises/catalog.json`

**Interfaces:**
- Consumes: `ExerciseManifest.Load(string)` and the current immutable 48-entry snapshot.
- Produces: `ExerciseManifest.SystemExerciseCount = 90`, `ExerciseManifest.ExercisesPerBodyPart = 15`, and the 90-entry manifest consumed by every later task.

- [ ] **Step 1: Write the failing 90-item contract test**

```csharp
[Fact]
public void Catalog_contains_90_unique_exercises_with_15_in_every_body_part()
{
    var items = ExerciseManifest.Load(CatalogPath);
    Assert.Equal(90, items.Count);
    Assert.Equal(90, items.Select(item => item.Id).Distinct().Count());
    Assert.All(Enum.GetValues<BodyPart>(), part =>
        Assert.Equal(15, items.Count(item => item.BodyPart == part)));
}
```

Add a separate assertion that the first 48 entries still equal the existing names, IDs, categories, and modes before extending the snapshots.

- [ ] **Step 2: Run the focused test and verify failure**

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter FullyQualifiedName~ExerciseCatalogManifestTests.Catalog_contains_90
```

Expected: FAIL because `ExerciseManifest.Load` requires exactly 48 entries.

- [ ] **Step 3: Add the 42 manifest rows**

Insert each category's seven rows immediately after its existing eight. Use the exact stable table values and this exact object shape:

```json
{ "id": "6fee6f1f-edcb-471e-8ab9-49855f3aa4b6", "name": "Push-Up", "bodyPart": "Chest", "trackingMode": "Bodyweight", "slug": "push-up", "imagePath": "assets/exercises/images/push-up.png", "reviewState": "Draft", "sourceReference": "ai-generated-project-owned-draft" }
```

For each remaining entry, substitute only the table's ID, name, category, mode, slug, and the matching image path formed from that row's exact slug.

- [ ] **Step 4: Replace magic counts and expand canonical validation**

Add:

```csharp
public const int SystemExerciseCount = 90;
public const int ExercisesPerBodyPart = 15;
```

Use the constants in `Validate`; add all 42 exact name-to-`BodyPart` pairs to `CanonicalBodyParts`; keep schema and version unchanged.

- [ ] **Step 5: Extend immutable test snapshots**

Extend `ExpectedNames`, `ExpectedIds`, and `ExpectedBodyParts` in manifest order. Extend `BodyweightNames` with:

```csharp
"Push-Up", "Chest Dip", "Inverted Row", "Bench Dip", "Triceps Dip",
"Plank", "Side Plank", "Dead Bug", "Bird Dog", "Bicycle Crunch", "Mountain Climber"
```

Assert `Assisted Pull-Up` remains the only Assisted entry and every other non-bodyweight entry is Weighted.

- [ ] **Step 6: Run non-asset manifest tests**

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ExerciseCatalogManifestTests&Category!=Asset"
```

Expected: PASS. The PNG test remains deferred until Tasks 2–7.

- [ ] **Step 7: Commit manifest metadata**

```bash
git add assets/exercises/catalog.json src/TrackZ.Infrastructure/Persistence/Seed/ExerciseManifest.cs tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogManifestTests.cs
git commit -m "feat: expand system exercise manifest to 90"
```

---

### Task 2: Add Chest Artwork and Review Briefs

**Files:**
- Modify: `assets/exercises/prompt-template.md`
- Modify: `assets/exercises/review-checklist.md`
- Create: `assets/exercises/images/push-up.png`, `chest-dip.png`, `decline-barbell-bench-press.png`, `smith-machine-bench-press.png`, `dumbbell-fly.png`, `low-to-high-cable-fly.png`, `high-to-low-cable-fly.png`

**Interfaces:**
- Consumes: the shared artwork invariant and seven Chest manifest rows.
- Produces: seven distinct 1024 × 1024 RGBA Chest PNGs and matching prompt/review rows.

- [ ] **Step 1: Add exact prompt and blank review rows**

Specify: Push-Up (floor, shoulder-width hands, chest descends then presses, pectoralis major); Chest Dip (parallel bars, forward torso, elbows bend then extend, lower pectoralis); Decline Barbell Bench Press (decline bench, bar to lower chest, pectoralis major); Smith Machine Bench Press (flat bench under guided bar, vertical press, pectoralis major); Dumbbell Fly (flat bench, soft-elbow hugging arc, pectoralis major); Low-to-High Cable Fly (low dual cables, upward/inward arc, upper pectoralis); High-to-Low Cable Fly (high dual cables, downward/inward arc, lower pectoralis). Give each row a movement-specific failure. Add checklist rows with every approval field blank.

- [ ] **Step 2: Generate and inspect every Chest image**

Use the `imagegen` skill once per exercise with the shared invariant and exact row. Inspect original detail. Reject incorrect equipment, extra anatomy, unsafe positions, text, missing highlight, or cropping. Save accepted 1024 × 1024 RGBA PNGs at the exact manifest paths.

- [ ] **Step 3: Commit Chest assets**

```bash
git add assets/exercises/prompt-template.md assets/exercises/review-checklist.md assets/exercises/images/push-up.png assets/exercises/images/chest-dip.png assets/exercises/images/decline-barbell-bench-press.png assets/exercises/images/smith-machine-bench-press.png assets/exercises/images/dumbbell-fly.png assets/exercises/images/low-to-high-cable-fly.png assets/exercises/images/high-to-low-cable-fly.png
git commit -m "feat: add expanded chest exercise artwork"
```

---

### Task 3: Add Back Artwork and Review Briefs

**Files:**
- Modify: `assets/exercises/prompt-template.md`
- Modify: `assets/exercises/review-checklist.md`
- Create: PNGs for `conventional-deadlift`, `t-bar-row`, `inverted-row`, `neutral-grip-lat-pulldown`, `wide-grip-lat-pulldown`, `single-arm-cable-row`, and `machine-high-row`.

**Interfaces:**
- Consumes: seven Back manifest rows.
- Produces: seven unique Back PNGs and exact prompt/review rows.

- [ ] **Step 1: Add exact Back briefs and blank review rows**

Specify: Conventional Deadlift (barbell floor pull, hip/knee extension, erector spinae/back chain); T-Bar Row (landmine/T-bar, hip hinge, row to lower chest, middle back); Inverted Row (fixed low bar, straight body, chest to bar, middle back); Neutral-Grip Lat Pulldown (parallel handle to upper chest, lats); Wide-Grip Lat Pulldown (wide pronated bar to upper chest, lats); Single-Arm Cable Row (handle toward hip with square torso, lat); Machine High Row (chest-supported high handles pulled down/back, upper lats). Include a specific failure for each.

- [ ] **Step 2: Generate, inspect, normalize, and save all seven Back images**

Use one `imagegen` call per exercise and the Task 2 acceptance rules.

- [ ] **Step 3: Commit Back assets**

```bash
git add assets/exercises/prompt-template.md assets/exercises/review-checklist.md assets/exercises/images/conventional-deadlift.png assets/exercises/images/t-bar-row.png assets/exercises/images/inverted-row.png assets/exercises/images/neutral-grip-lat-pulldown.png assets/exercises/images/wide-grip-lat-pulldown.png assets/exercises/images/single-arm-cable-row.png assets/exercises/images/machine-high-row.png
git commit -m "feat: add expanded back exercise artwork"
```

---

### Task 4: Add Shoulder Artwork and Review Briefs

**Files:**
- Modify: `assets/exercises/prompt-template.md`
- Modify: `assets/exercises/review-checklist.md`
- Create: PNGs for `arnold-press`, `dumbbell-front-raise`, `cable-front-raise`, `bent-over-reverse-fly`, `reverse-pec-deck`, `landmine-press`, and `dumbbell-shrug`.

**Interfaces:**
- Consumes: seven Shoulders manifest rows.
- Produces: seven unique Shoulder PNGs and exact prompt/review rows.

- [ ] **Step 1: Add exact Shoulder briefs and blank review rows**

Specify: Arnold Press (seated dumbbells rotate palms-in to overhead, deltoids); Dumbbell Front Raise (standing raise forward to shoulder height, anterior deltoids); Cable Front Raise (low cable raises forward, anterior deltoids); Bent-Over Reverse Fly (hip hinge, dumbbells open laterally, posterior deltoids); Reverse Pec Deck (facing machine, arms open backward, posterior deltoids); Landmine Press (single bar end presses up/forward, anterior deltoid); Dumbbell Shrug (vertical scapular elevation, upper trapezius). Include a specific failure for each.

- [ ] **Step 2: Generate, inspect, normalize, and save all seven Shoulder images**

Use one `imagegen` call per exercise and the Task 2 acceptance rules.

- [ ] **Step 3: Commit Shoulder assets**

```bash
git add assets/exercises/prompt-template.md assets/exercises/review-checklist.md assets/exercises/images/arnold-press.png assets/exercises/images/dumbbell-front-raise.png assets/exercises/images/cable-front-raise.png assets/exercises/images/bent-over-reverse-fly.png assets/exercises/images/reverse-pec-deck.png assets/exercises/images/landmine-press.png assets/exercises/images/dumbbell-shrug.png
git commit -m "feat: add expanded shoulder exercise artwork"
```

---

### Task 5: Add Arm Artwork and Review Briefs

**Files:**
- Modify: `assets/exercises/prompt-template.md`
- Modify: `assets/exercises/review-checklist.md`
- Create: PNGs for `ez-bar-curl`, `incline-dumbbell-curl`, `cable-curl`, `concentration-curl`, `bench-dip`, `triceps-dip`, and `single-arm-cable-pushdown`.

**Interfaces:**
- Consumes: seven Arms manifest rows.
- Produces: seven unique Arm PNGs and exact prompt/review rows.

- [ ] **Step 1: Add exact Arms briefs and blank review rows**

Specify: EZ-Bar Curl (standing angled-bar curl, biceps); Incline Dumbbell Curl (incline bench, arms behind torso, biceps); Cable Curl (low cable bar, fixed elbows, biceps); Concentration Curl (seated elbow braced on inner thigh, biceps); Bench Dip (hands behind on bench, elbow flexion/extension, triceps); Triceps Dip (upright parallel-bar dip, triceps); Single-Arm Cable Pushdown (high-cable handle, single elbow extension, triceps). Keep Chest Dip forward-leaning and Triceps Dip upright. Include a specific failure for each.

- [ ] **Step 2: Generate, inspect, normalize, and save all seven Arm images**

Use one `imagegen` call per exercise and the Task 2 acceptance rules.

- [ ] **Step 3: Commit Arm assets**

```bash
git add assets/exercises/prompt-template.md assets/exercises/review-checklist.md assets/exercises/images/ez-bar-curl.png assets/exercises/images/incline-dumbbell-curl.png assets/exercises/images/cable-curl.png assets/exercises/images/concentration-curl.png assets/exercises/images/bench-dip.png assets/exercises/images/triceps-dip.png assets/exercises/images/single-arm-cable-pushdown.png
git commit -m "feat: add expanded arm exercise artwork"
```

---

### Task 6: Add Leg Artwork and Review Briefs

**Files:**
- Modify: `assets/exercises/prompt-template.md`
- Modify: `assets/exercises/review-checklist.md`
- Create: PNGs for `goblet-squat`, `hack-squat`, `sumo-deadlift`, `walking-lunge`, `hip-thrust`, `lying-leg-curl`, and `seated-calf-raise`.

**Interfaces:**
- Consumes: seven Legs manifest rows.
- Produces: seven unique Leg PNGs and exact prompt/review rows.

- [ ] **Step 1: Add exact Legs briefs and blank review rows**

Specify: Goblet Squat (dumbbell at chest, squat, quadriceps/glutes); Hack Squat (sled machine, supported squat, quadriceps); Sumo Deadlift (wide barbell pull, glutes/adductors); Walking Lunge (alternating forward steps with dumbbells, quadriceps/glutes); Hip Thrust (upper back on bench, bar across hips, hip extension, gluteus maximus); Lying Leg Curl (prone machine knee flexion, hamstrings); Seated Calf Raise (bent knees, plantarflexion, soleus). Include a specific failure for each.

- [ ] **Step 2: Generate, inspect, normalize, and save all seven Leg images**

Use one `imagegen` call per exercise and the Task 2 acceptance rules.

- [ ] **Step 3: Commit Leg assets**

```bash
git add assets/exercises/prompt-template.md assets/exercises/review-checklist.md assets/exercises/images/goblet-squat.png assets/exercises/images/hack-squat.png assets/exercises/images/sumo-deadlift.png assets/exercises/images/walking-lunge.png assets/exercises/images/hip-thrust.png assets/exercises/images/lying-leg-curl.png assets/exercises/images/seated-calf-raise.png
git commit -m "feat: add expanded leg exercise artwork"
```

---

### Task 7: Add Core Artwork and Complete Asset Validation

**Files:**
- Modify: `assets/exercises/prompt-template.md`
- Modify: `assets/exercises/review-checklist.md`
- Modify: `assets/exercises/contact-sheet-index.md`
- Modify: `assets/exercises/contact-sheet.png`
- Create: PNGs for `plank`, `side-plank`, `dead-bug`, `bird-dog`, `russian-twist`, `bicycle-crunch`, and `mountain-climber`.

**Interfaces:**
- Consumes: all preceding catalog assets and seven Core manifest rows.
- Produces: the complete 90-image source catalog and refreshed review artifacts.

- [ ] **Step 1: Add exact Core briefs and blank review rows**

Specify: Plank (forearms/toes, straight brace, rectus abdominis); Side Plank (one forearm and stacked feet, lateral brace, obliques); Dead Bug (supine, opposite arm/leg lower, deep core); Bird Dog (quadruped, opposite arm/leg extend, deep core); Russian Twist (seated with plate, controlled rotation, obliques); Bicycle Crunch (opposite elbow/knee cycle, obliques); Mountain Climber (high plank, alternating knee drive, rectus abdominis). Include a specific failure for each.

- [ ] **Step 2: Generate, inspect, normalize, and save all seven Core images**

Use one `imagegen` call per exercise and the Task 2 acceptance rules.

- [ ] **Step 3: Refresh contact-sheet artifacts**

List all 90 entries in `contact-sheet-index.md` in manifest order. Rebuild `contact-sheet.png` from all 90 individual sources in that same order with no substitution or repetition.

- [ ] **Step 4: Run complete manifest and asset tests**

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter FullyQualifiedName~ExerciseCatalogManifestTests
```

Expected: PASS for exact counts, snapshots, prompt/checklist coverage, 1024 × 1024 RGBA format, and 90 unique hashes.

- [ ] **Step 5: Commit Core and review artifacts**

```bash
git add assets/exercises/prompt-template.md assets/exercises/review-checklist.md assets/exercises/contact-sheet-index.md assets/exercises/contact-sheet.png assets/exercises/images/plank.png assets/exercises/images/side-plank.png assets/exercises/images/dead-bug.png assets/exercises/images/bird-dog.png assets/exercises/images/russian-twist.png assets/exercises/images/bicycle-crunch.png assets/exercises/images/mountain-climber.png
git commit -m "feat: complete 90-exercise artwork catalog"
```

---

### Task 8: Make Seed, Deployment, and Publication Upgrade-Safe

**Files:**
- Modify: `src/TrackZ.Infrastructure/Persistence/Seed/ExerciseCatalogPublicationService.cs`
- Modify: `tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogSeederTests.cs`
- Modify: `tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogDeploymentTests.cs`
- Modify: `tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogPublicationTests.cs`
- Modify: `tests/TrackZ.Api.Tests/Exercises/ExerciseCatalogDeploymentCommandTests.cs`
- Modify: `tests/TrackZ.Api.Tests/Exercises/ExerciseCatalogPublicationCommandTests.cs`

**Interfaces:**
- Consumes: the 90-entry manifest, `ExerciseCatalogSeeder.MasterKey/ThumbnailKey`, and valid existing Published rows.
- Produces: idempotent 90-entry deployment plus mixed-state publication that publishes only new Draft rows and preserves existing Published metadata.

- [ ] **Step 1: Replace test magic counts and write a failing mixed-state upgrade test**

Use `ExerciseManifest.SystemExerciseCount`, `SystemExerciseCount * 2`, and twice that object-read count for concurrent verification. Add a scenario with 48 valid Published images, 42 exact Draft images, one custom exercise, and one workout referencing an original system exercise. Capture the custom exercise and workout snapshots before publishing, then assert:

```csharp
Assert.Equal(90, images.Length);
Assert.All(images.Take(48), image =>
{
    Assert.Equal(originalReviewerId, image.ReviewedByUserId);
    Assert.Equal(originalRightsReference, image.RightsReference);
    Assert.Equal(originalPublishedAt, image.PublishedAt);
});
Assert.All(images.Skip(48), image =>
{
    Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
    Assert.Equal(expansionReviewerId, image.ReviewedByUserId);
    Assert.Equal(expansionRightsReference, image.RightsReference);
});
var customAfter = await database.Exercises.AsNoTracking()
    .SingleAsync(exercise => exercise.Id == customBefore.Id);
var workoutAfter = await database.WorkoutSessions.AsNoTracking()
    .SingleAsync(workout => workout.Id == workoutBefore.Id);
Assert.Equal(customBefore.Name, customAfter.Name);
Assert.Equal(customBefore.OwnerId, customAfter.OwnerId);
Assert.Equal(customBefore.IsArchived, customAfter.IsArchived);
Assert.Equal(workoutBefore.Status, workoutAfter.Status);
Assert.Equal(workoutBefore.StartedAt, workoutAfter.StartedAt);
Assert.Equal(workoutBefore.Version, workoutAfter.Version);
```

- [ ] **Step 2: Run focused tests and verify failures**

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ExerciseCatalogSeederTests|FullyQualifiedName~ExerciseCatalogDeploymentTests|FullyQualifiedName~ExerciseCatalogPublicationTests"
```

Expected: FAIL on old 48/96/192 expectations and rejection of mixed Published/Draft rows.

- [ ] **Step 3: Generalize publication preflight**

Interpolate `manifest.Count` in exact-count errors. Classify each exact identity-validated image as either an empty-metadata Draft or a valid Published row:

```csharp
var draftImages = images.Where(image =>
    image.ReviewState == ExerciseImageReviewState.Draft
    && HasEmptyReviewMetadata(image)).ToArray();
var publishedImages = images.Where(image =>
    image.ReviewState == ExerciseImageReviewState.Published
    && HasValidPublicationMetadata(image)).ToArray();

if (draftImages.Length + publishedImages.Length != images.Count)
    throw new InvalidOperationException(
        $"Exercise catalog publication requires every one of the {manifest.Count} exact rows to be Draft or Published.");
```

`HasValidPublicationMetadata` requires non-empty reviewer and rights reference, all approval booleans, ordered timestamps, and `IsReadyForUse`. It must not require prior Published rows to match the reviewer/rights arguments for new Draft rows. Review and publish only `draftImages`.

- [ ] **Step 4: Preserve concurrency and rerun idempotence**

Update concurrency tests so the winner publishes the Draft subset and the serialized second call observes a valid fully Published catalog without writing. Assert old publication metadata remains unchanged and publication performs no storage writes or deletes.

- [ ] **Step 5: Run all catalog infrastructure and command tests**

```bash
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter FullyQualifiedName~ExerciseCatalog
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "FullyQualifiedName~ExerciseCatalogDeploymentCommandTests|FullyQualifiedName~ExerciseCatalogPublicationCommandTests"
```

Expected: PASS with 90 definitions, 90 images, 180 exact objects, safe mixed-state upgrade, and idempotent reruns.

- [ ] **Step 6: Commit deployment/publication changes**

```bash
git add src/TrackZ.Infrastructure/Persistence/Seed/ExerciseCatalogPublicationService.cs tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogSeederTests.cs tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogDeploymentTests.cs tests/TrackZ.Infrastructure.Tests/Seed/ExerciseCatalogPublicationTests.cs tests/TrackZ.Api.Tests/Exercises/ExerciseCatalogDeploymentCommandTests.cs tests/TrackZ.Api.Tests/Exercises/ExerciseCatalogPublicationCommandTests.cs
git commit -m "feat: support incremental catalog publication"
```

---

### Task 9: Verify API Pagination and Mobile 90-Item Aggregation

**Files:**
- Modify: `tests/TrackZ.Api.Tests/Exercises/ListExercisesEndpointTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Exercises/ExercisePickerViewModelTests.cs`
- Modify only if a test exposes a defect: `src/TrackZ.Mobile.Core/Features/Exercises/Services/TrackZExerciseApiClient.cs`

**Interfaces:**
- Consumes: API cursor pages capped at 50 and `TrackZExerciseApiClient.GetAllAsync(CancellationToken)`.
- Produces: proof that every system exercise reaches the picker/cache across two pages.

- [ ] **Step 1: Update API catalog acceptance coverage**

Assert deployment makes 90 definitions listable, creates 90 Draft image rows, and stores 180 renditions. Fetch page size 50 twice; assert 50 then 40 items, one non-null then null cursor, and 90 unique combined IDs.

- [ ] **Step 2: Add a 50-plus-40 mobile client test**

Build two `CursorPage<ExerciseSummaryDto>` responses and assert:

```csharp
var result = await client.GetAllAsync();

Assert.Equal(90, result.Count);
Assert.Equal(90, result.Select(item => item.Id).Distinct().Count());
Assert.Equal("/api/v1/exercises?pageSize=50", handler.Requests[0].PathAndQuery);
Assert.Equal(
    "/api/v1/exercises?pageSize=50&cursor=expanded.catalog.page.2",
    handler.Requests[1].PathAndQuery);
```

- [ ] **Step 3: Run API and mobile catalog tests**

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter FullyQualifiedName~ListExercisesEndpointTests
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter "FullyQualifiedName~ExercisePickerViewModelTests|FullyQualifiedName~ExerciseArtworkStateTests"
```

Expected: PASS. Do not change production pagination if its existing loop passes.

- [ ] **Step 4: Commit pagination verification**

```bash
git add tests/TrackZ.Api.Tests/Exercises/ListExercisesEndpointTests.cs tests/TrackZ.Mobile.Tests/Exercises/ExercisePickerViewModelTests.cs
git commit -m "test: verify complete catalog pagination"
```

If a production fix was required, add `TrackZExerciseApiClient.cs` explicitly to this commit.

---

### Task 10: Full Verification and TrackZ-Only Local Deployment

**Files:**
- Modify: `docs/deployment/catalog-and-media.md`
- Modify only if its boundary wording becomes stale: `assets/exercises/README.md`

**Interfaces:**
- Consumes: the complete tested catalog, explicit TrackZ PostgreSQL/object-storage settings, and an approved reviewer/rights reference.
- Produces: 90 visible system exercises with 90 Published thumbnails in the local TrackZ environment.

- [ ] **Step 1: Run formatting and targeted suites**

```bash
dotnet format TrackZ.slnx --verify-no-changes
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter FullyQualifiedName~ExerciseCatalog
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "FullyQualifiedName~ExerciseCatalog|FullyQualifiedName~ListExercisesEndpointTests"
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter "FullyQualifiedName~ExercisePickerViewModelTests|FullyQualifiedName~ExerciseArtworkStateTests"
```

Expected: all targeted commands PASS. If unrelated dirty-worktree files block solution-wide formatting, format only files changed by this plan and report the unrelated issue.

- [ ] **Step 2: Capture a TrackZ-only pre-deployment snapshot**

With explicit TrackZ connection settings, record system definitions, system images grouped by review state, custom exercises, users, and workouts. Confirm the target database and storage endpoint are the TrackZ local services before mutation.

- [ ] **Step 3: Deploy and seed**

```bash
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__TrackZ='Host=127.0.0.1;Port=55433;Database=trackz;Username=trackz;Password=trackz_local_only;Application Name=TrackZ.ExpandedCatalog' ObjectStorage__ServiceUrl='http://127.0.0.1:9000' ObjectStorage__Bucket='trackz-private' ObjectStorage__AccessKey='trackz_api' ObjectStorage__SecretKey='trackz_api_local_development_secret' dotnet run --project src/TrackZ.Api/TrackZ.Api.csproj --no-restore -- deploy-exercise-catalog --manifest /Users/mikeyoshino/gitRepos/TrackZ/assets/exercises/catalog.json
```

Expected: 90 exact definitions/image rows; old 48 unchanged and new 42 Draft.

- [ ] **Step 4: Publish after human artwork approval**

```bash
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__TrackZ='Host=127.0.0.1;Port=55433;Database=trackz;Username=trackz;Password=trackz_local_only;Application Name=TrackZ.ExpandedCatalog' ObjectStorage__ServiceUrl='http://127.0.0.1:9000' ObjectStorage__Bucket='trackz-private' ObjectStorage__AccessKey='trackz_api' ObjectStorage__SecretKey='trackz_api_local_development_secret' dotnet run --project src/TrackZ.Api/TrackZ.Api.csproj --no-restore -- publish-exercise-catalog --manifest /Users/mikeyoshino/gitRepos/TrackZ/assets/exercises/catalog.json --reviewer-id '2a91bddc-cfa2-498f-8992-0112aaabc2b0' --rights-reference 'trackz-project-owned-expanded-catalog-2026-09-05'
```

Expected: all 90 Published; the original 48 keep their reviewer, rights, and timestamps, and the new 42 use the new approval metadata.

- [ ] **Step 5: Verify post-deployment invariants and idempotence**

Confirm 90 system definitions, 90 Published image rows, 180 private objects, unchanged custom-exercise/user/workout counts, and no missing image route. Rerun deploy and publish; counts and metadata must remain unchanged.

- [ ] **Step 6: Verify mobile behavior**

Refresh the catalog. Confirm each of the six filters shows 15 system exercises, search one new exercise from every category, and verify its own thumbnail appears. Repeat offline after cache population.

- [ ] **Step 7: Update and commit deployment documentation**

Replace the documented 48 source records and 96 renditions with 90 source records and 180 renditions. Document that an expansion may contain existing Published rows plus new Draft rows and that publication preserves existing review metadata.

```bash
git add assets/exercises/README.md docs/deployment/catalog-and-media.md
git commit -m "docs: update expanded catalog deployment checks"
```

Always include `docs/deployment/catalog-and-media.md`; include `assets/exercises/README.md` only if its wording changed.
