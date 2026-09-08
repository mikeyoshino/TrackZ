# Native vector anatomy replacement

User approved SVG on 2026-09-08 after rejecting hand-mapped polygons over a raster. Replace that rendering path, preserving reports, navigation, recommendations and per-set coaching.

## Contract

- Root owns SVG source and attribution/import tool; renderer implementer owns Progress C# files and focused native tests.
- Packaged file `Resources/Raw/muscles/body.svg`, SVG viewBox `0 0 1440 1450`. Front occupies x~100..650; back x~800..1380. Both y~100..1400.
- Flat list of `<path id="..." data-region="quads" data-side="front" d="..."/>` elements. Decorative paths omit data-region. All coordinates absolute shared viewBox; no transforms, embedded raster, scripts or external resources. Build-time normalization expands source commands to explicit absolute M/L/C/Q/Z for reliable native parsing.
- Optional `<path data-detail-for="quads" ...>` are illustrative line details, not hit targets. Renderer shades each actual shape with a restrained gradient and clips the details to their own muscle paths. No raster overlays or hand-mapped colored polygons.
- Optional `data-layer="deep"` shapes are inset/detail-only; deep-core is not falsely painted on the visible abdominal surface. Default main view omits these. Focus deep-core shows its own vector cross-sectional illustration and visible Thai/English layer label.
- Each of 22 domain training region IDs has an explicit illustration or deep inset. Chest subdivisions are training emphasis, not separate isolated muscles. Do not claim clinical validation or pixel-perfect 3D mock appearance.
- Native parsed PathF is the single shape for fill, outline and hit test. Resolve one status per region from report; neutral gray/primary lime/secondary teal. No alpha blending of conflicting training statuses.
- Preserve native GraphicsView, accessible text list and all existing report flows. Cache parsed bundled data, no network/runtime package download or WebView needed. Replace and remove unused raster loader.
- Overview shows complete front/back. Group crops keep relevant contours; focus only relevant side(s). Compact group and focus heights allow list/CTA space. Recommendation check control44x44 centered, thumbnails72pt, no tall button borders.
- Root verifies simulator with isolated existing fixture, then removes diagnostic App hook/fixture/rawreview files before final build. No commits, deployment, production migrations, Docker changes or worktree creation.

## Verification

Test parser with literal curved SVG fixtures; missing/invalid region handling; point hit inside curve vs outside bounding box; status changes affect same shape; crop transforms; no-record state. Full mobile/domain/application tests and iOS/API builds, actual overview/group/focus screenshots. Asset quality and anatomy are visual checks, not substitutes for professional content review.
