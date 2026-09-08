# Muscle coverage review

## Verdict

- **Spec:** Changes required. The shared calculator, authorized server query, account predicate, local-week projection, deleted/future filtering, stable IDs, six body groups, recommendation exclusion, and backwards drill-up are sound. Two presentation/data-parity requirements remain incomplete.
- **Quality:** Changes requested. No critical security issue found, but one offline/server parity bug should be fixed before acceptance. This was a read-only review; the already reported targeted test results were not re-run.

## Findings

### High

1. **A known canonical system exercise becomes unmapped offline whenever its cached definition is absent, while the server still counts it.** `MuscleCoverageSource.Facts` marks every missing cached definition as custom (`src/TrackZ.Mobile.Core/Features/Progress/MuscleCoverageSource.cs:41-42`), and the calculator deliberately refuses to map anything marked custom (`src/TrackZ.Domain/Muscles/MuscleCoverage.cs:32`). A cache refresh can legitimately omit an archived/removed definition while durable historical sets remain; the server joins the retained system definition and maps its stable ID, so the same set produces different coverage online and offline. **Fix:** determine canonical mapping from `MuscleCatalog.Find(e.ExerciseDefinitionId)` first. Only set `IsCustom` when the cached definition explicitly says custom, or when the ID is non-canonical and custom provenance is known; a missing cache row must not override a known stable system mapping. Add a mobile adapter test for a canonical ID absent from `definitions` and assert parity with `MuscleCoverageCalculator`/server behavior.

### Medium

2. **The UI does not present primary and secondary counts separately.** The group row collapses a region to one status and hides the secondary count (`src/TrackZ.Mobile/Features/Progress/MuscleCoveragePage.cs:146-152`, `:260-265`). Region detail shows secondary count only when a primary count exists, and shows neither count when the region is secondary-only (`:165-170`). This fails the approved requirement that primary and secondary set counts remain separate and prevents users from seeing the same quantitative projection represented by the report. **Fix:** render both `PrimarySets` and `SecondarySets` as separate labeled values in group rows and detail, including zero/secondary-only cases; retain primary precedence only for color/status.

### Low

3. **Past-week navigation has no lower bound and can eventually throw.** `MoveWeekAsync` applies `DateOnly.AddDays` indefinitely (`src/TrackZ.Mobile/Features/Progress/MuscleCoveragePage.cs:266`) while only the forward button is bounded (`:118-123`). This is inconsistent with the API's 2000–2100 validation and can crash after repeated navigation. **Fix:** disable previous navigation at the supported lower bound and clamp/validate `_week` before arithmetic; add a boundary test.

## Explicit limitation

`MuscleBodyDiagram` is an original scalable schematic built from polygon coordinates (`src/TrackZ.Mobile/Features/Progress/MuscleBodyDiagram.cs:6-49`). It is not photorealistic, pixel-perfect to the approved mock, or anatomy-approved. Its stable region IDs and common status projection are appropriate, but rendered-device visual/anatomical review remains outstanding and must not be claimed as complete.

## Notes

- No unsupported activation percentages, growth claims, or overtraining diagnoses were found.
- The server query scopes workouts to `OwnerId`, admits only that user's custom definitions, and excludes deleted/future records (`src/TrackZ.Infrastructure/Progress/MuscleCoverageReadStore.cs:23-34`).
- The recommendation flow excludes exercises already in the active/draft workout, supports preview, and mutates through the existing workout coordinator (`src/TrackZ.Mobile/Features/Progress/MuscleCoveragePage.cs:179-240`). The supplemental shoulder press currently has no dedicated catalogue artwork, so its preview is the neutral placeholder; that asset limitation should remain explicit.
