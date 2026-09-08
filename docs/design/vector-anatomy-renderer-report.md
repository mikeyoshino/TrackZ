# Native vector anatomy renderer report

Date: 2026-09-08

## Result

The Progress muscle-coverage flow now renders the packaged `muscles/body.svg` through native MAUI `GraphicsView` and `PathF` geometry. It has no raster-atlas, WebView, network, or runtime JavaScript dependency.

The parser validates the flat SVG contract, resolves all 22 training region IDs, caches the parsed document for the application lifetime, and caches flattened hit geometry and bounds for each path. The original parsed `PathF` is retained as the source for fill, outline, clipping, crop bounds, and curve-derived hit testing.

## Rendering and interaction

- Overview renders the complete front/back anatomy. Training regions receive one resolved color: neutral gray, primary lime, or secondary teal. Decorative source paths remain neutral.
- Group and focus modes use padded crops derived from the selected native paths while retaining nearby neutral contours.
- Fill and clipping use even-odd winding. The deep-core compound path therefore keeps its center hole and appears only in deep-core focus, accompanied by a visible Thai/English cross-section and deep-layer label.
- Detail fibers are non-interactive. Each is drawn only while clipped to matching target muscle paths on the same side.
- Hit testing transforms the touch back into the same SVG coordinate space used by drawing, then applies even-odd point-in-path testing to cached native `PathF` flattening. A point inside a path's rectangular bounds but outside its Bézier contour is rejected.
- Statuses are applied directly to each path, without overlapping alpha status overlays.

## Page preservation and polish

The existing page constructor, `_group` and `_region` selection fields, week navigation, accessible status list, exercise filtering, thumbnail preview, selection, add flow, account-generation cancellation, retry/fallback state, and back behavior remain in place.

Drilldown geometry is compact: group diagrams are 185 pt and focused diagrams are 190 pt. Recommendation thumbnails are 72 pt. Select controls are centered 44-by-44 pt borderless controls. The Add action retains its 52 pt primary target but uses the shared disabled surface/text colors until a valid selection exists and while an add is running. No-record anatomy remains `#626B72`; small no-record copy uses the readable secondary text color instead.

At the existing largest-text threshold, group rows stack the status below the region name so Dynamic Type does not squeeze names into one-syllable columns. Normal text retains the compact horizontal row.

## Parser compatibility finding

The first device screenshot showed that MAUI `PathBuilder` could return nonempty paths while misinterpreting compact arc flags and shorthand from the upstream source. The build-time import now normalizes the bundled SVG to explicit absolute `M`, `L`, `C`, `Q`, and `Z` commands. No normalization library ships with or runs in the app.

Focused regression coverage now checks more than path count: it verifies reference curved-path bounds, representative front/back interior points, curve-vs-bounds hit behavior, the deep-core even-odd hole, and the null-focus decorative outline case.

## Verification

Focused command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --framework net10.0 \
  --filter 'FullyQualifiedName~MuscleBodyDiagramTests|FullyQualifiedName~MuscleCoveragePageTests' \
  --no-restore
```

Last confirmed focused result after the final source change: 22 passed, 0 failed. A later redundant rerun collided with another active MSBuild process before compilation; it did not produce a test result.

Coverage includes:

- literal inline cubic and quadratic SVG parsing;
- the packaged 310-path SVG and all 22 domain regions;
- unknown and missing region rejection;
- normalized native geometry reference bounds and inside points;
- curve-accurate and transformed focus hit testing;
- deep-only visibility and even-odd hole behavior;
- identical parsed geometry for status, drawing, and hit testing;
- actual `PictureCanvas` fill/outline/clipped-detail command recording;
- no-record status and text contrast separation;
- overview decorative outline behavior;
- crop sizing and recommendation control sizing/disabled state.
- largest-text group row reflow.

The root integration owner performs the final iOS build, simulator screenshots for overview/group/focus/deep modes, complete mobile/domain/application suites, and temporary fixture cleanup as required by the anatomy contract. Those integration checks remain outside this renderer report until root verification and cleanup are complete.
