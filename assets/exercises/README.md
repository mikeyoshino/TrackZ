# Exercise asset deployment boundary

`assets/exercises/images/<slug>.png` is a repository source asset only. It is never an API URL and its filesystem path is never stored in `ExerciseImage`.

After a PNG passes the automated PNG/decode/hash checks, the release process uploads its master and thumbnail to private object storage at the deterministic keys produced by `ExerciseCatalogSeeder`. That process supplies an `IExerciseCatalogAssetDeployment` implementation which confirms the object-store upload; only then may the seeder add its version-1 system-artwork metadata. The metadata stays `Draft` with `ai-generated-project-owned-draft` provenance and no reviewer, rights reference, approval, or publication timestamp. A separate human-approved workflow is the only route to `Review` and `Publish`.
