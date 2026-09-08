# Vector anatomy review — 2026-09-08

## Scope

Replaces the rejected raster + polygon overlay with native rendering of the packaged SVG paths. User approved SVG as a stylized front/back illustration; no clinical validation or photorealistic/pixel-identical mock claim is made.

Source/provenance: `assets/anatomy/README.md`. Runtime contract: `docs/design/vector-anatomy-contract.md`.

## Data and device isolation

Visual review uses a temporary DEBUG-only `AnatomyReviewFixture` with its own cache databases and in-memory workouts, not the user's data or API. It uses production coverage calculation, page/components and native entry handler. The set editor fixture renders production XAML but deliberately omits production lifecycle journaling hooks; it is not an end-to-end server test.

Review device: iPhone 17e simulator, iOS 26.5, 390-point portrait. Modes: overview, group (legs), region (hamstrings), chest, deep, empty, editor-effort and editor-keyboard. Screenshots go to `artifacts/anatomy-review/`.

## Fixes reproduced before correction

- Numeric keyboard obscured the weight entry. Native `ScrollRectToVisible` is suppressed by MAUI keyboard handling; managed MakeVisible also introduced horizontal shifting. Bounded vertical `SetContentOffset` plus keyboard-safe page layout made the weight visible without horizontal movement. Screenshot `editor-keyboard-green.png` captured the corrected field above the actual software keyboard.
- Saved-set effort hydration failed to notify description/color/clear command. `Editing_and_canceling_refresh_all_effort_bindings_and_clear_availability` failed on missing `EffortDescription`; it passed after shared dependent-binding notifications were applied on edit/cancel/account reset.
- The raster loader and overlapping polygon colors were fixed earlier, but that visual approach was rejected. Those earlier screenshots and reports are historical evidence, not the final vector result.

## Release boundary

This local implementation does not deploy the API/migration, publish a catalog, install on the physical iPhone, or alter Docker/SyToy services. Backend metadata migration must precede release of a client writing new set metadata.

Final screenshots and cleanup evidence are recorded below.

## Supplemental checks

- Fresh domain suite: 173 passed; application suite: 74 passed (2026-09-08).
- `effort-slider.png` was an insufficiently scrolled initial capture. `effort-slider-full.png` supersedes it: production XAML shows the uninterrupted green-to-red track, orange description at score 72, both endpoint captions, clear/pain controls and save action after scrolling.
- Asset generation emits 310 flat SVG paths and no bitmap elements. Vendor source is pinned and its MIT license is bundled; muscle subdivision/fiber additions and their limitations are documented in `assets/anatomy/README.md`.
- UI/UX Pro Max final accessibility pass found the neutral anatomy fill (`#626B72`) has only 3.25:1 contrast on the card surface; that is not sufficient for status text. The renderer implementer was asked to keep this fill but use secondary text (`#A7AFB8`, 7.96:1) for no-record labels. Teal/lime text measure 7.50:1 and 15.00:1 respectively.

## Native SVG reproduction

The first native screenshot (`svg-overview.png`) failed despite a successful build and path-count smoke test: MAUI accepted compact relative/arc SVG syntax but produced fragmented geometry. Quick Look rendered that original SVG correctly. The build-time generator now uses pinned `svgpath` to expand it into explicit absolute M/L/C/Q/Z commands. `svg-normalized-overview.png` confirms complete native front/back geometry after this single asset change. It still shows the separately identified null-focus decorative-outline issue; do not treat it as the final screenshot.

`svg-region.png` verifies the relevant-side hamstring crop, two recommendation images and the add action all fit on the simulator. `svg-deep.png` verifies a separate labeled cross-section with an empty center, not a highlight over superficial abs. Initial disabled-add appearance is being corrected before the final captures.

## Final inspected screenshots

- `final-overview.png`: complete front/back curved geometry, opaque primary/secondary/neutral fills, no erroneous white decorative outlines.
- `final-group.png`: leg/hip contours and seven named region rows; scroll container retains remaining content below the viewport.
- `final-region.png`: hamstring crop with clipped fiber lines, two 72pt recommendation images, centered selectors, visibly disabled Add until a selection is made.
- `final-empty.png`: all training shapes neutral when there are no records.
- `final-chest.png`: curved chest emphasis subdivisions agree with row statuses.
- `final-largest-text.png`: maximum iOS Dynamic Type now stacks region name and status, instead of squeezing the name into a narrow column. Original `large` text size restored afterward.
- `effort-slider-full.png` and `editor-keyboard-green.png`: per-set gradient/controls and numeric keyboard reveal, within the isolated production-XAML fixture scope described above.

All paths above are relative to `artifacts/anatomy-review/`. These are actual simulator captures using sample records, not image-generated UI mockups.

## Final verification and cleanup

- Full mobile suite after fixture removal: **990 passed, 0 failed**.
- Domain: **173 passed**; application: **74 passed**. API build: **0 warnings, 0 errors**.
- Both the visual-check build and clean normal-app iOS `Rebuild` after fixture removal compiled with **0 warnings, 0 errors**. The manual clean rebuild used `EnableCodeSigning=false`; although installation succeeded, iOS correctly rejected its linker-signed executable at launch (`CODESIGNING / Invalid Page`). This was a verification-command defect, not an app startup exception. Rebuilding with the repository's normal simulator command (`-r iossimulator-arm64`, signing enabled) produced a bundle that passed `codesign --verify --deep --strict`, installed, launched as PID 10864 and remained open on the normal training home screen. Screenshot: `startup-repaired.png`. The final bundle contains `muscles/body.svg` and its license, with no rejected atlas or temporary leg-curl fixtures.
- Removed the DEBUG App hook; `App.xaml.cs` has no diff. Archived the fixture source and temporary leg-curl images under `artifacts/anatomy-review/`, outside app inputs. Archived the rejected raster there too. Source/test search finds no `TRACKZ_ANATOMY_REVIEW`, `AnatomyReviewFixture`, `body_atlas` or `MuscleAtlasImageCache` references.
- Focused independent code review found no Important/Critical issues; the subsequent simulator-discovered parser/outline, disabled-state and Dynamic Type problems were reproduced and corrected with regression coverage.
- Independent read-only final delta review also approved the normalization/provenance, focus-outline guard, disabled button and large-text fixes with no Important/Critical findings.
- `git diff --check` passed.

Not verified here: physical iPhone, VoiceOver interaction, 375pt/landscape/tablet layouts, or Android (local SDK unavailable). No claim of a complete accessibility audit, clinical anatomy validation, production API round-trip or 100% photorealistic mock parity is made.
