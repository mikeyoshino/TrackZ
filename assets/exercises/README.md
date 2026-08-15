# Exercise asset deployment boundary

`assets/exercises/images/<slug>.png` is a repository source asset only. It is never an API URL and its filesystem path is never stored in `ExerciseImage`.

After a PNG passes the automated PNG/decode/hash checks, the release process runs the explicit
`TrackZ.Api deploy-exercise-catalog --manifest <path>` command documented in
`docs/deployment/catalog-and-media.md`. The command uploads deterministic master and thumbnail
renditions to private object storage, verifies the whole set, and only then runs the seeder. The
metadata stays `Draft` with `ai-generated-project-owned-draft` provenance and no reviewer, rights
reference, approval, or publication timestamp. A separate human-approved workflow is the only
route to `Review` and `Publish`.
