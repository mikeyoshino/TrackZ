# Anatomy finish implementation report

## Status

BLOCKED on the final visual approach. The generated anatomical atlas loads and renders on the iOS simulator, but actual screenshots showed that hand-aligned polygon masks do not meet the approved anatomical-accuracy bar. The user rejected further cosmetic refinement of those overlays and requested an alternative without generated overlay images. No 100% parity claim is made.

## Changes made

- Replaced the schematic body drawable with the packaged 1024×1536 grayscale atlas at `Resources/Raw/muscles/body_atlas.png`.
- Added explicit atlas-coordinate masks for all 22 `MuscleCatalog` region IDs.
- Added one crop/transform model shared by drawing and hit testing for overview, body-group, and focused-region views. The hamstring focus uses a posterior-thigh crop rather than two full bodies.
- Added a process-wide asynchronous atlas cache. It opens the packaged asset once, retains the decoded native image for app lifetime, and lets callers cancel their wait without cancelling the shared load.
- Fixed the simulator loader failure: `IImageLoadingService` is optional in MAUI DI, so iOS/Android now fall back to MAUI's public platform-native `PlatformImageLoadingService`; generic `net10.0` uses the portable implementation.
- Added deterministic overlap ownership. Drawing repaints the original raster inside each successive mask before applying the winning status tint; hit testing uses the same reverse precedence. A no-record lower-back region therefore stays neutral where it overlaps primary lats instead of inheriting lime.
- Preserved the `MuscleCoveragePage` constructor and private `_group`/`_region` fixture contract.
- Kept week selection, account-generation reset, offline snapshot projection, recommendation image preview, selected recommendation state, duplicate filtering, add-to-workout behavior, and the existing exercise-performance drilldown paths.
- Kept the equipment filter and added progressive recommendation disclosure: two recommendations initially, then `ดูท่าอื่น` / `See other exercises` to expand.
- Made back/week chevrons quiet 44pt controls with explicit accessibility descriptions; changed body-group controls to quiet filled buttons; kept region rows as the list alternative to the diagram.
- Made recommendation selection descriptions explicit and retained a prominent 52pt bottom add action.

## Regression tests added first

`tests/TrackZ.Mobile.Tests/NativeIos/MuscleBodyDiagramTests.cs`

- every catalog region has an explicit mask;
- every mask stays inside the atlas/body extents;
- every mask resolves its own report status;
- focused hamstrings excludes the front figure and uses a materially tighter crop;
- focused hit testing uses the same atlas transform as rendering;
- neighboring back overlap points resolve to one documented owner;
- a no-record lower-back region wins over primary lats without inheriting the neighbor status;
- sampled atlas points resolve to at most one training region.

`tests/TrackZ.Mobile.Tests/NativeIos/MuscleCoveragePageTests.cs`

- atlas decoding does not require an optional DI registration;
- recommendation disclosure shows at most two initially and all items after expansion.

## Verification

- `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter "FullyQualifiedName~MuscleCoveragePageTests|FullyQualifiedName~MuscleBodyDiagramTests|FullyQualifiedName~MuscleCoverageSourceTests" --no-restore -m:1`
  - Passed: 18, Failed: 0, Skipped: 0.
- `dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios --no-restore -m:1`
  - Build succeeded with 0 warnings and 0 errors.
- Generic `net10.0` compiled as part of the focused test run.
- Android build was attempted but stopped before compilation with `XA5300` because no Android SDK is installed on this machine. No dependency installation was attempted.
- Actual iPhone 17e simulator screenshots were captured at 390pt in `artifacts/anatomy-review/overview.png`, `group.png`, and `region.png`.

## Known limitations and visual findings

- The current hand-aligned overlays are not anatomically precise enough for release. Actual screenshots show angular region boundaries and inaccurate surface placement, especially calf/shin and several torso boundaries.
- The source mask polygons overlap geometrically. Rendering and hit testing now resolve overlap deterministically and without mixed-color bleed, but that safety mechanism does not make the underlying anatomy accurate.
- The no-record state is the unmodified grayscale atlas within overlapping masks after the safety fix; a post-fix screenshot was not requested after the user paused cosmetic work.
- The focused-region screenshot is too tall/dense at 390×844: the image and 88pt recommendation thumbnails push the add action against the bottom edge. Planned reductions to the image, thumbnails, picker chrome, and row density were paused with the overlay approach.
- The legs group crop does not show the complete feet, and the seventh row/note remain below the initial viewport. Planned crop/height/row refinements were paused.
- Deep core is not a directly visible surface muscle on this raster atlas; any surface locator would be an approximation. This reinforces the need for an approved layered anatomical source or another truthful representation.
- Primary and secondary masks use translucent lime/teal so atlas fiber shading remains visible; the raster itself is never destructively recolored.
- Final visual direction still needs a product decision (for example, licensed/owned layered SVG geometry or a validated native 3D anatomical asset). Network-fetched anatomy and invented fallback shapes were not introduced.
