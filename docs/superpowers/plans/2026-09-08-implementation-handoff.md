# Muscle coverage and per-set coaching: implementation handoff

## Delivered code

- Shared versioned taxonomy: six body groups and 22 training regions. Explicit canonical exercise-ID mappings, separate primary/secondary set counts, no guessed attribution for custom exercises.
- Authenticated `GET /api/v1/progress/muscles?week=YYYY-MM-DD`, using the account's time zone and owner-scoped records. Mobile computes the same projection offline from synchronized sets.
- Native overview → group → region flow, equipment filtering, technique preview and adding an available exercise to the current workout. Existing weight/repetition performance drilldown is retained.
- Front/back curved SVG shapes share region IDs and status colors with the lists; native fills and hit testing use the same geometry. A text-list alternative remains available. The rejected raster/polygon approach is replaced, not shipped alongside it; deep-core has a labeled cross-section rather than a superficial mask.
- Nullable per-set warmup, exact effort score and pain metadata, SQLite/EF migrations and push/pull support; saved-set editing retains the original set identity.
- Supplemental Seated Barbell Shoulder Press definition, barbell-specific technique text, own bundled thumbnail and stable-ID artwork fallback. It does not borrow a different exercise's thumbnail.

## Data interpretation

This is recorded training exposure, not a measurement of muscle activation, growth, recovery or overtraining. Warmups are excluded. Historical sets with unknown warmup classification remain unclassified; their coverage is not silently inferred. One physical working set counts once in the total even if multiple regions participate. Primary and secondary counts must not be added together as equivalent sets.

Chest subdivisions describe training emphasis, not three isolated muscles. Mapping content still needs qualified anatomical/content review before presenting it as professionally validated advice.

## Visual/device release gates still open

- The approved replacement uses a stylized curved SVG illustration with editable muscle shapes and illustrative fiber lines. It is **not** photorealistic artwork from the original mock; pixel-perfect parity is **not** claimed. Source attribution and build-time normalization are in `assets/anatomy/README.md`.
- Actual iPhone 17e Simulator checks reproduced native SVG parsing corruption and numeric-keyboard occlusion, then verified corrected geometry and keyboard visibility. The continuous effort slider, endpoints and save action were inspected in production XAML with isolated sample data. See `docs/testing/vector-anatomy-review.md` for scope, final screenshots and remaining device/accessibility checks. Physical-iPhone and VoiceOver audits remain open.
- Production migration, catalog deployment and installation on the user's phone have **not** been performed. Update the server before releasing a client that writes the new metadata; do not assume an older server preserves unknown fields.
- No Docker integration tests, production writes or SyToy service changes were performed.

## Verification

- Full mobile unit suite after final SVG fixes and temporary-fixture removal: 990 passed, 0 failed.
- Full domain unit suite: 173 passed, 0 failed.
- Full application unit suite: 74 passed, 0 failed.
- API build: passed, 0 warnings/errors.
- iOS Simulator (`net10.0-ios`, `iossimulator-arm64`) build: passed, 0 warnings/errors.
- Catalogue cross-check: all 90 manifest IDs have explicit muscle mappings; supplemental barbell shoulder press has its own mapping.
- `git diff --check`: passed.

The reviewer findings and their follow-up are in `2026-09-08-final-review.md`; per-set implementation notes are in `2026-09-08-set-coaching-report.md`. Build success is not device or visual-parity evidence.
