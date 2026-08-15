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

## Round 1/5 fixes — lifecycle safety, provenance, grouping, and targeted Draft replacements

### RED/GREEN evidence

- **RED:** `dotnet test tests/TrackZ.Infrastructure.Tests --filter "FullyQualifiedName~ExerciseCatalogManifestTests&Category!=Asset" --no-restore --verbosity minimal` reported 2 failures / 8 passes: an unsupported `sourceReference` and a Barbell Bench Press/Lat Pulldown body-part swap that preserved the 8-per-group count were both accepted.
- **GREEN:** the same manifest command passed 10 / 10 after adding the canonical source-provenance literal and exact 48 name-to-body-part validation.
- **RED:** `dotnet test tests/TrackZ.Infrastructure.Tests --filter FullyQualifiedName~ExerciseCatalogSeederTests --no-restore --verbosity minimal` reported 2 failures / 5 passes: reseeding valid Reviewed and Published version-1 system images raised a Draft conflict.
- **GREEN:** the same seeder command passed 7 / 7 after accepting only state-consistent Draft, Reviewed, or Published lifecycle metadata while retaining system ownership, version, key, and provenance checks. The deployed-assets repeat-run acceptance test was already green against the existing idempotent path; it is mutation-sensitive to any duplicate image, regenerated ID, changed version, or changed master/thumbnail key.
- **GREEN:** `dotnet test tests/TrackZ.Infrastructure.Tests --filter "FullyQualifiedName~ExerciseCatalogManifestTests&Category=Asset" --no-restore --verbosity minimal` passed 1 / 1 after the three replacements; it verifies every manifest PNG exists, decodes as RGBA PNG at 1024×1024, and has a unique SHA-256 hash.

### Targeted Draft image provenance

All three are original, built-in `imagegen` Draft outputs. The original generated files remain retained under `/Users/mikeyoshino/.codex/generated_images/01a00440-d8c1-7961-b4d6-2be3f8faa8d8`; `sips -z 1024 1024` wrote only their stable repository counterparts.

| Asset | Original generated file | Stable Draft file | Actual prompt focus |
| --- | --- | --- | --- |
| Lat Pulldown | `/Users/mikeyoshino/.codex/generated_images/01a00440-d8c1-7961-b4d6-2be3f8faa8d8/exec-00c58ab8-0541-416e-a257-8156e4a6407a.png` | `assets/exercises/images/lat-pulldown.png` | Grayscale scientific anatomy illustration; wide-overhand bar is pulled **in front of the face to the upper chest/clavicle**, never behind the neck; lats muted red; no text/glyphs/logos/watermarks. |
| Face Pull | `/Users/mikeyoshino/.codex/generated_images/01a00440-d8c1-7961-b4d6-2be3f8faa8d8/exec-789f33eb-bdf4-4a3b-a3ee-cf7e4d839bb5.png` | `assets/exercises/images/face-pull.png` | Grayscale scientific anatomy illustration; rope finishes at forehead/temples with elbows high and external rotation; **only posterior deltoids** muted red, all traps/upper arms/other anatomy grayscale; no text/glyphs/logos/watermarks. |
| Front Squat | `/Users/mikeyoshino/.codex/generated_images/01a00440-d8c1-7961-b4d6-2be3f8faa8d8/exec-4177da00-a46c-446a-b243-b6b9208e9f62.png` | `assets/exercises/images/front-squat.png` | Grayscale scientific anatomy illustration; clean front rack and elbows high; every plate completely plain and unbranded with no embossed glyphs, letters, numbers, markings, or logos; no text/glyphs/watermarks. |

`assets/exercises/contact-sheet.png` was rebuilt from manifest-order source files as eight columns by six body-part rows (1024×768). It is a Draft review aid only. No artwork was marked Reviewed or Published, and no product-owner, anatomy, or rights decision was recorded.
