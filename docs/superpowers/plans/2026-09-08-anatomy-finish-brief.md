# Task: finish anatomical UI matching approved mock

User approved finishing the existing implementation, not a new design. Existing working tree belongs to this task; preserve unrelated changes. No worktrees, commits, pushes, deployment, Docker changes or subagents.

## Ownership

Own `src/TrackZ.Mobile/Features/Progress/MuscleBodyDiagram.cs`, `MuscleCoveragePage.cs`, new support files in that folder, and focused tests. Root owns simulator fixture/testing and generated image assets. Coordinate interfaces with root.

## Requirements

- Replace schematic flat silhouettes with the detailed generated grayscale anatomical atlas now at `src/TrackZ.Mobile/Resources/Raw/muscles/body_atlas.png`. Inspect image before coding. Atlas is 1024×1536, front left/back right. Actual body extents about y64–1348. Do not recolor raster destructively: use runtime native clipped overlays/paths with alpha to retain muscle fiber shading. Neutral background #090D0E.
- Define explicit anatomical masks aligned to this atlas for all existing 22 region IDs. Primary lime, secondary teal, no-record neutral. Do not color background, head, joints or whole limbs when only a muscle is involved. A region's figure color MUST match list status; focus outline cannot falsely imply training status.
- Support overview front/back; group crops; single focused region detail (e.g. hamstrings shows posterior thighs large, NOT two full-body figures). Hit testing uses SAME geometry/transforms as rendering.
- Cache and asynchronously load atlas once via packaged asset APIs with safe lifetime. Every platform build including unit-test net10.0 must compile; don't introduce a network dependency. No invented fallback anatomy claims.
- Keep approved dark/lime/Noto Sans Thai pattern. Compact 44pt+ accessible controls, icon labels, list alternative to diagram. Region detail initially two recommendations plus 'ดูท่าอื่น' expansion, selected state and prominent bottom add action; preserve equipment filter. Avoid huge controls, cropped text, footer covering content.
- All existing behavior stays: week selection, account reset, offline projection, image tips preview retaining selected recommendation, no duplicate add, old performance drilldown.
- Add regression tests BEFORE behavior edits for focus bounds/hit transforms and all region masks/status correctness. Run targeted mobile tests and compile. Root performs full suite and actual simulator screenshots.
- Report exact changes, tests and known limitations to `docs/superpowers/plans/2026-09-08-anatomy-finish-report.md`. No claim 100% parity absent visual comparison.

## Review contract

This is execution of approved mock. Respect silhouette/muscle region geometry and truthful data visualization over decorative detail. Return status and concise evidence; do not run another agent.
