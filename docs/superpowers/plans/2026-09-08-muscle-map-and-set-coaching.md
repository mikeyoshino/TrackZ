# Muscle map and per-set coaching implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development for independent scoped work and review.

**Goal:** Implement the approved three-screen muscle coverage flow and per-set effort/editor requirements without replacing measured performance with unsupported muscle-growth claims.

**Architecture:** A shared domain catalogue and deterministic coverage calculator drive API and offline mobile projections. Durable per-set metadata travels with sync; native presentation uses one region/status model for all diagrams and lists. Preserve existing per-exercise performance history.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core/PostgreSQL, MAUI, SQLite.

**Spec:** User-approved design in this conversation, captured below.

## Global constraints / approved spec

- Work in existing checkout; user explicitly requested no worktrees. No deployment, production mutations, or changes to SyToy services.
- Thai default, Noto Sans Thai, near-black/charcoal/lime visual language matching approved mock. No goal onboarding.
- All six body groups and meaningful subregions; legs/hips have quads, hamstrings, adductors, glutes, lateral hips, calves, shins.
- Primary and secondary set counts separate. Warmups, deleted sets/workouts and future records excluded; unclassified metadata remains unknown, not inferred.
- Each physical set counts once in total even when multiple muscles participate. Primary status takes precedence over secondary.
- Region highlights, list counts and detail status use the same projection. Selection focus is not training status.
- Weekly local date boundaries and account isolation; no unsupported percentage activation, growth or overtraining diagnoses.
- Continuous colored effort drag control on every working set, optional response, preserve exact scale value, pain safety; editable set load/reps.
- Missing Seated Barbell Shoulder Press catalogue entry; concise technique guidance and conservative load recommendations.
- Original anatomical layers with stable region IDs; do not falsely claim pixel parity or anatomy approval before visual checks.

### Task 1: Per-set metadata, editor and effort control

- [x] Trace save/edit/sync and CoachJournal classification; write failing tests for round-trip exact effort score, warmup, pain and edited-set invalidation.
- [x] Extend existing optional per-set sync payloads and persistence compatibly. Unknown historical sets remain unknown. Validate effort 0–100, use nullable unanswered state.
- [x] Replace delayed assessment with inline continuous drag gradient, one thumb, optional response, warmup toggle and pain control. Preserve explicit save and existing keyboard handling; physical-device visibility verification remains open.
- [x] Enable editing recorded weight/reps through existing mutation coordinator; no duplicate create on edit.
- [x] Run domain/mobile/API compile and targeted unit tests; report limitations, do not deploy.

### Task 2: Shared muscle taxonomy and coverage backend

- [x] Introduce immutable stable region IDs with localized display names and body groups, explicit system exercise mappings, unknown custom definitions not guessed.
- [x] Write failing calculator tests: primary precedence, distinct totals, warmup/deleted exclusions at adapter, unmapped count, local week boundaries.
- [x] Implement shared calculator consuming classified set facts; map catalogue by stable system exercise ID, not free-form matching.
- [x] Add authorized read endpoint with date/timezone validation and existing user isolation. Return taxonomy, counts, status, unknown count and rule version.
- [x] Run relevant unit tests and backend build.

### Task 3: Mobile muscle map and navigation

- [x] Adapt local durable sets to shared calculator; test stale/deleted/account/date handling.
- [x] Implement overview, group and region detail pages using same coverage output; preserve existing performance drilldown.
- [x] Provide scalable body front/back layers and status colors, accessible list alternative, explicit anatomical/visual review limitation if applicable.
- [x] Route recommendations to real catalogue selection, image preview and safe add workflow; exclude already-added definitions.
- [ ] Verify empty/loading/error states and build mobile target.

### Task 4: Catalogue and whole-change verification

- [x] Verify/add Seated Barbell Shoulder Press with explicit stable mapping, existing seed idempotency and technique guidance.
- [x] Review all changes for spec coverage and quality; fix findings. Final re-review resolved all code findings; visual/device release gates remain open.
- [ ] Run unit suites and iOS build where available; compare rendered UI to reference if simulator available. No integration tests or production deployment.
- [x] Record achieved scope and remaining asset/device checks honestly. See `2026-09-08-implementation-handoff.md`.
