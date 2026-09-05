# TrackZ Expanded Exercise Catalog Design

## Goal

Expand the system exercise catalog from 48 to 90 curated exercises while keeping the app simple to browse. The catalog will retain the existing six body-part categories and contain exactly 15 system exercises per category. Existing exercise IDs, workout history, user-created exercises, and published artwork remain intact.

“Complete” in this design means a practical catalog for a typical commercial gym, not every possible grip, machine brand, or minor movement variation.

## Scope

### Included

- Preserve all 48 existing system exercises and their UUIDs.
- Add 42 system exercises: seven to each existing body-part category.
- Supply a unique 1024 × 1024 RGBA PNG illustration for every new exercise.
- Deploy a master image and thumbnail for every new exercise.
- Keep the existing `Chest`, `Back`, `Shoulders`, `Arms`, `Legs`, and `Core` filters.
- Keep the existing `Weighted`, `Bodyweight`, and `Assisted` tracking modes.
- Make catalog deployment and publication safe to rerun.
- Verify names, IDs, slugs, categories, tracking modes, image paths, asset uniqueness, and publication metadata.

### Excluded

- New body-part, equipment, difficulty, or muscle-detail filters.
- Exercise instructions, video demonstrations, or form analysis.
- Renaming or reclassifying the existing 48 exercises.
- Automatically deleting system exercises that are absent from a later manifest.
- Modifying or deleting user-created exercises or workout history.

## Catalog

The first eight entries in each group are the existing catalog. The final seven entries are new.

### Chest

1. Barbell Bench Press — Weighted
2. Incline Barbell Bench Press — Weighted
3. Dumbbell Bench Press — Weighted
4. Incline Dumbbell Press — Weighted
5. Chest Press Machine — Weighted
6. Cable Fly — Weighted
7. Pec Deck Fly — Weighted
8. Decline Push-Up — Bodyweight
9. Push-Up — Bodyweight
10. Chest Dip — Bodyweight
11. Decline Barbell Bench Press — Weighted
12. Smith Machine Bench Press — Weighted
13. Dumbbell Fly — Weighted
14. Low-to-High Cable Fly — Weighted
15. High-to-Low Cable Fly — Weighted

### Back

1. Lat Pulldown — Weighted
2. Pull-Up — Bodyweight
3. Assisted Pull-Up — Assisted
4. Seated Cable Row — Weighted
5. Chest-Supported Row — Weighted
6. Barbell Row — Weighted
7. One-Arm Dumbbell Row — Weighted
8. Straight-Arm Pulldown — Weighted
9. Conventional Deadlift — Weighted
10. T-Bar Row — Weighted
11. Inverted Row — Bodyweight
12. Neutral-Grip Lat Pulldown — Weighted
13. Wide-Grip Lat Pulldown — Weighted
14. Single-Arm Cable Row — Weighted
15. Machine High Row — Weighted

### Shoulders

1. Overhead Press — Weighted
2. Dumbbell Shoulder Press — Weighted
3. Machine Shoulder Press — Weighted
4. Lateral Raise — Weighted
5. Cable Lateral Raise — Weighted
6. Rear Delt Fly — Weighted
7. Face Pull — Weighted
8. Upright Row — Weighted
9. Arnold Press — Weighted
10. Dumbbell Front Raise — Weighted
11. Cable Front Raise — Weighted
12. Bent-Over Reverse Fly — Weighted
13. Reverse Pec Deck — Weighted
14. Landmine Press — Weighted
15. Dumbbell Shrug — Weighted

### Arms

1. Barbell Curl — Weighted
2. Dumbbell Curl — Weighted
3. Hammer Curl — Weighted
4. Preacher Curl — Weighted
5. Triceps Pushdown — Weighted
6. Overhead Triceps Extension — Weighted
7. Skull Crusher — Weighted
8. Close-Grip Bench Press — Weighted
9. EZ-Bar Curl — Weighted
10. Incline Dumbbell Curl — Weighted
11. Cable Curl — Weighted
12. Concentration Curl — Weighted
13. Bench Dip — Bodyweight
14. Triceps Dip — Bodyweight
15. Single-Arm Cable Pushdown — Weighted

### Legs

1. Back Squat — Weighted
2. Front Squat — Weighted
3. Leg Press — Weighted
4. Romanian Deadlift — Weighted
5. Leg Extension — Weighted
6. Seated Leg Curl — Weighted
7. Bulgarian Split Squat — Weighted
8. Standing Calf Raise — Weighted
9. Goblet Squat — Weighted
10. Hack Squat — Weighted
11. Sumo Deadlift — Weighted
12. Walking Lunge — Weighted
13. Hip Thrust — Weighted
14. Lying Leg Curl — Weighted
15. Seated Calf Raise — Weighted

### Core

1. Cable Crunch — Weighted
2. Hanging Knee Raise — Bodyweight
3. Hanging Leg Raise — Bodyweight
4. Ab Wheel Rollout — Bodyweight
5. Weighted Sit-Up — Weighted
6. Decline Sit-Up — Bodyweight
7. Reverse Crunch — Bodyweight
8. Pallof Press — Weighted
9. Plank — Bodyweight
10. Side Plank — Bodyweight
11. Dead Bug — Bodyweight
12. Bird Dog — Bodyweight
13. Russian Twist — Weighted
14. Bicycle Crunch — Bodyweight
15. Mountain Climber — Bodyweight

## Manifest and Validation

`assets/exercises/catalog.json` remains the source of truth. The manifest schema stays at version 1 because its serialized shape does not change.

Validation will require:

- exactly 90 manifest entries;
- exactly 15 entries for every published `BodyPart` value;
- stable snapshot values for all 48 existing entries;
- unique, non-empty IDs, names, slugs, and image paths;
- safe repository-relative PNG paths;
- valid enum and review-state values;
- approved project-owned draft provenance;
- a canonical name-to-category mapping for all 90 exercises.

The hard-coded count changes from 48 to 90. The canonical snapshot expands instead of being replaced, so accidental mutation of an existing exercise remains detectable.

## Artwork

Each new exercise receives a dedicated illustration following the current catalog art direction:

- polished grayscale scientific anatomy illustration;
- target muscle highlighted with the established muted accent;
- recognizable equipment, stance, grip, and joint path;
- no embedded labels, numbers, logos, or watermarks;
- square 1024 × 1024 RGBA PNG;
- visually distinct file content for every exercise.

New prompts will be added to the prompt template, and new review rows will be added to the human review checklist. Artwork remains `Draft` until a reviewer approves anatomy, movement, equipment/grip, target muscle, originality, and rights.

## Deployment and Data Safety

The existing deployment boundary remains unchanged:

1. Load and validate the 90-entry manifest.
2. Validate all 90 local source assets before database mutation.
3. Upload missing master and thumbnail objects using deterministic system keys.
4. Insert the 42 missing exercise definitions with deterministic UUIDs.
5. Insert image metadata only after object storage confirms deployment.
6. Publish only after explicit human review.

Rerunning deployment must not duplicate rows or overwrite conflicting definitions. Existing published image records remain valid. No deletion is inferred from manifest membership, which protects historical workout references and custom content.

## Mobile Behavior

No navigation or layout redesign is required. The existing category filter and search flow will receive the additional exercises through the catalog API and local cache. The client must refresh the catalog after server deployment so all 90 items and thumbnails become available offline.

The picker continues to show system exercises plus the signed-in user's custom exercises. A search result or category count may exceed the current page size, so existing cursor pagination must continue loading until the user reaches the end.

## Failure Handling

- Missing, malformed, duplicated, or reused artwork fails validation before deployment.
- A conflicting existing system UUID or normalized name aborts the transaction.
- A failed object upload does not create image metadata for that asset.
- An unpublished image continues to use the app's normal placeholder behavior.
- Deployment does not touch unrelated APIs, databases, containers, user workouts, or custom exercises.

## Testing

Automated coverage will verify:

- 90 unique exercises and 15 per category;
- preservation of all existing UUID/name/category/tracking-mode snapshots;
- the new canonical catalog order and tracking modes;
- rejection of malformed schema, duplicate identity fields, category swaps, unsafe paths, and unsupported provenance;
- presence, dimensions, pixel format, and unique content hash for all 90 PNGs;
- idempotent seed, deployment, and publication behavior for 90 entries;
- no modification of existing published records or user-created exercises;
- API pagination and mobile cache behavior with more than one page of exercises.

The targeted infrastructure, API, and mobile catalog tests must pass before any deployment. Deployment to the local TrackZ environment will be performed only after tests pass and will use explicit TrackZ connection and storage settings.

## Acceptance Criteria

- The catalog contains 90 system exercises, exactly 15 in each of the six existing categories.
- All 48 current exercises retain their existing UUIDs and behavior.
- Every new exercise displays its own reviewed image after publication.
- Category filtering and search can reach every exercise.
- Repeating seed/deploy/publish creates no duplicates.
- Existing workouts and user-created exercises remain unchanged.
