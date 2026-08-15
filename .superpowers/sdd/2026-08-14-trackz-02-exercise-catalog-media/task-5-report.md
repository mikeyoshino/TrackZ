# Task 5 report — manifest and draft artwork scaffold

## Scope

Implemented only Plan 2 Task 5's catalog manifest, draft-artwork workflow artifacts, Infrastructure parser/seeder, and focused tests. No MAUI Task 6 work was added. No record is claimed to have human review, rights approval, or publication.

## RED/GREEN record

1. **Manifest RED:** `ExerciseCatalogManifestTests` initially failed to compile because `TrackZ.Infrastructure.Persistence.Seed` did not exist. **GREEN:** strict manifest parser and committed 48-row catalog made the cardinality/group test pass.
2. **Seeder RED:** `ExerciseCatalogSeederTests` initially failed to compile because the deployment boundary/seeder did not exist. **GREEN:** idempotent PostgreSQL seeding passed. The repeat-run test then exposed an over-broad name-conflict predicate; the predicate was narrowed to the matching normalized name and the tests passed.
3. **Artwork history sensitivity RED:** a version-2 image without version 1 did not fail the seeder. **GREEN:** seeder now rejects any existing image history lacking the exact Draft version-1 manifest image.
4. **Prompt/checklist RED:** the artifact test failed because `prompt-template.md` was absent. **GREEN:** shared scientific-educational invariants, 48 per-exercise prompt rows, and a 48-row blank review checklist made the non-asset manifest suite pass.
5. **Asset dependency:** the dedicated image suite correctly fails while a required image is missing (observed `Chest Press Machine` missing). It is intentionally marked `Category=Asset` so focused structural/seeder verification remains runnable while the root image-generation workflow supplies the 48 source PNGs. The test requires each file to exist, decode as PNG/RGBA, be 1024×1024, and have a unique SHA-256 hash.

## Sequential verification

| Command | Result |
| --- | --- |
| `dotnet test tests/TrackZ.Infrastructure.Tests --filter "FullyQualifiedName~ExerciseCatalogManifestTests&Category!=Asset" --no-restore` | PASS — 8 passed |
| `dotnet test tests/TrackZ.Infrastructure.Tests --filter FullyQualifiedName~ExerciseCatalogSeederTests --no-restore` | PASS — 4 passed |
| `dotnet test tests/TrackZ.Infrastructure.Tests --filter FullyQualifiedName~Catalog_migration_matches_the_current_model --no-restore` | PASS — 1 passed; migration/model parity clean |
| `git diff --check` | PASS — no whitespace errors |

`dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore -v:minimal` was also invoked after the Infrastructure changes; the runner completed without diagnostic output.

## Deployment and review boundary

`ExerciseCatalogSeeder` never creates system `ExerciseImage` metadata unless an `IExerciseCatalogAssetDeployment` confirms that the corresponding source asset crossed the documented private-object-storage deployment boundary. Its deterministic keys are internal (`system/exercises/<id>/v1/...`), never local filesystem paths or API URLs. Created metadata stays `Draft` and has no reviewer, rights reference, approvals, or timestamps. Existing conflicting ID/name/ownership/mode/body-part/image history raises `InvalidOperationException` without mutation.

The existing catalog API query returns no thumbnail for Draft system artwork, and existing endpoint coverage already asserts opaque media URLs rather than private object keys for ready custom artwork.

## Draft asset evidence — 2026-08-15

- Built-in `imagegen` generated the 48 original Draft PNGs using the common scientific-educational prompt style recorded in `assets/exercises/prompt-template.md`.
- The original generated files are retained at `/Users/mikeyoshino/.codex/generated_images/01a000d5-7c02-7380-b403-d93b502b19a9`.
- Technical normalization used `sips` to resize each source image from 1254×1254 to 1024×1024. The isolated asset test confirms all 48 manifest files exist, decode as PNG/RGBA at 1024×1024, and have unique SHA-256 content hashes.
- `assets/exercises/contact-sheet.png` follows manifest order as six body-part rows of eight exercises.
- Every artwork file and manifest row remains `Draft`, pending product-owner, anatomy, and rights review. No reviewer, approval, rights reference, review timestamp, or publication timestamp has been created by this task.

| Command | Result |
| --- | --- |
| `dotnet test tests/TrackZ.Infrastructure.Tests --filter "FullyQualifiedName~ExerciseCatalogManifestTests&Category=Asset" --no-restore --verbosity minimal` | PASS — 1 passed |
