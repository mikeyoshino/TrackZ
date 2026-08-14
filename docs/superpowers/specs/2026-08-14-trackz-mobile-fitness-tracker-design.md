# TrackZ Mobile Fitness Tracker Design

**Date:** 2026-08-14  
**Status:** Approved in collaborative design review  
**Platforms:** Android and iOS  
**Primary stack:** .NET MAUI XAML, ASP.NET Core Web API, PostgreSQL

## 1. Product Summary

TrackZ is a strength-training log for people who already know how they want to train. It does not prescribe exercises, programs, or target weights. Its job is to make logging fast and to surface the user's own previous performance so they can decide what to lift today.

The core prompt is **"What are you training today?"** The user selects one or more body parts, selects multiple exercises, and starts a workout. Every exercise shows the best set from the most recent session and the all-time personal record before the user selects it. During the workout, the set logger shows every set from the previous session alongside today's sets.

TrackZ has a dark performance visual style based on the existing HTML prototype. Its game-like feel comes primarily from high-quality motion, transitions, haptics, and meaningful feedback. XP, levels, weekly streaks, and badges reinforce consistency without turning the product into an RPG or prescribing training behavior.

## 2. Goals

- Log weighted, bodyweight, and assisted strength exercises with sets and reps.
- Show previous-session performance and all-time bests before and during a workout.
- Allow users to select multiple exercises before starting and add, remove, or reorder exercises during a workout.
- Work without internet access and synchronize safely when connectivity returns.
- Support editing and deleting historical workouts and recompute dependent records.
- Provide a system catalog of 40–60 exercises, each with a unique anatomy-style illustration.
- Allow custom exercises with a selected library image or a user-uploaded image.
- Motivate consistency through XP, levels, configurable weekly streaks, badges, and polished motion.
- Support Thai and English on Android and iOS.
- Keep server business logic authoritative and expose stable numeric business error codes.

## 3. Non-Goals for MVP

- Training plans, exercise recommendations, coaching, or suggested weights.
- Cardio, distance, timed holds, or duration-based sets.
- Social feeds, leaderboards, direct messaging, or public profiles.
- Real-time AI generation of user-created exercise images.
- Microservices or an event broker.
- Web application or admin portal beyond operational necessities.
- Apple/Google social login.
- Daily quests that dictate which exercise the user should perform.

## 4. Product Flow

1. The user registers or signs in with email and password.
2. Home asks, "What are you training today?"
3. The user selects one or more body parts: Chest, Back, Shoulders, Arms, Legs, or Core.
4. Exercise Picker displays matching exercises with image, last performed date, last-session best, and all-time PR.
5. The user selects multiple exercises and starts the workout.
6. Active Workout lets the user add, remove, and reorder exercises.
7. Set Logger displays the exact previous sets and accepts today's weight and reps.
8. Saving a set writes to SQLite first, then plays motion/haptic feedback.
9. Finishing produces a summary with volume, PRs, XP, level progress, streak progress, badges, and sync state.
10. History permits editing or deleting workouts, exercises, and sets at any time.

The app restores an active workout after process termination. It never requires a network response before accepting a set.

## 5. Exercise Performance Semantics

### 5.1 Weighted

- `LAST` is the highest weight from the most recent completed session, paired with the reps achieved in that set.
- `PR` is the highest weight in all completed history.
- When weights tie, the set with more reps ranks higher.

### 5.2 Bodyweight

- `LAST` is the highest rep count from the most recent completed session.
- `PR` is the highest rep count in all completed history.
- The UI displays `BODYWEIGHT` rather than a numeric weight.

### 5.3 Assisted

- `LAST` is the lowest assistance weight from the most recent completed session, paired with its reps.
- `PR` is the lowest assistance weight in all completed history.
- When assistance weights tie, the set with more reps ranks higher.

The Exercise Picker query returns `lastPerformedAt`, `lastBestSet`, and `allTimeBest` with every exercise. The mobile client caches this projection so it remains available offline. Editing or deleting historical data triggers a server-side recalculation of affected projections.

## 6. Visual and Interaction Design

### 6.1 Visual Direction

- Dark charcoal background and surfaces with lime as the primary action color.
- High contrast numeric typography for weight, reps, volume, and records.
- The existing HTML prototype is a behavioral and visual reference, not production code.
- Production UI uses native .NET MAUI XAML controls; it does not embed the prototype in a WebView.

### 6.2 Motion Language

- Press feedback: 120–180 ms scale response and optional haptic.
- Navigation: 220–300 ms directional/shared-context transition.
- Saving a set: the completed row moves into history only after SQLite persistence succeeds.
- PR: short glow and particle celebration that does not block the next action.
- Summary: numeric count-up, XP progress, and badge reveal; the full celebration lasts no more than 1.2 seconds and is interruptible.
- Target 60 FPS on representative mid-range devices.
- Respect operating-system Reduce Motion settings. Fall back to fades and immediate state changes.
- Haptics can be disabled. Sound is off by default.

### 6.3 Primary Navigation

- **Home:** today's entry point, resume active workout, recent performance.
- **Train:** active workout, exercise list, set logger, summary.
- **Progress:** workout history, exercise charts, personal records.
- **Profile:** account, level, streak, badges, unit/language/motion settings, custom exercises.

### 6.4 Reusable XAML Components

- `ExercisePerformanceCard`
- `AnatomyImage`
- `LastSetTable`
- `WeightStepper`
- `RepsStepper`
- `SyncStatusPill`
- `XpBar`
- `BadgeTile`
- `PageTransitionBehavior`
- `PressFeedbackBehavior`
- `CounterAnimation`
- `SetSavedAnimation`
- `CelebrationOverlay`

XAML Views own bindings and visual states. ViewModels own observable UI state and commands. Use cases own local persistence and sync orchestration. HTTP and SQL access are prohibited in code-behind and ViewModels.

## 7. Exercise Image System

Each standard exercise has one original image dedicated to that exercise. A single image can show the start and finish phases or use a directional arrow, but it must not include unrelated exercises.

The approved style is a polished exercise-anatomy illustration:

- realistic adult proportions;
- fine graphite/ink linework with restrained grayscale shading;
- target muscle highlighted in muted anatomical red;
- recognizable equipment, grip, joint alignment, and movement path;
- clean neutral background;
- no title, label, logo, or other embedded copy;
- not cartoon, stick figure, simple icon, glossy 3D, or photorealistic.

The user's supplied poster is a style reference only. TrackZ must not crop, trace, collage, or republish that image. System illustrations are newly created original assets. The approved Incline Barbell Bench Press preview establishes the quality target.

### 7.1 Production Pipeline

1. Write an exercise brief describing movement, equipment, grip, body position, and target muscle.
2. Create original artwork through an AI-assisted or commissioned workflow.
3. Review anatomy, equipment geometry, grip, movement path, and safety cues with a qualified human reviewer.
4. Produce a master and optimized card/detail renditions.
5. Record asset version, source, rights metadata, review state, and storage keys.
6. Publish only assets with `Reviewed` status.

AI output is never published automatically. The catalog workflow is `Draft -> Reviewed -> Published`.

### 7.2 Custom Exercise Images

- Users may select a related TrackZ library image or upload JPEG, PNG, or WebP.
- If no image is selected, the app uses a body-part placeholder.
- Uploaded images are private to the user.
- The backend validates type and dimensions, limits file size, strips EXIF metadata, and creates thumbnails.
- Database rows store object keys and metadata. Binary files live in object storage.
- Mobile caches thumbnails and loads detail renditions on demand.

## 8. System Architecture

TrackZ uses a **modular monolith with Clean Architecture** and an offline-first mobile client.

### 8.1 Mobile

- **Presentation:** MAUI XAML Pages, reusable Views, ViewModels, navigation, animations, and haptics.
- **Mobile Application:** use cases, DTO mapping, client validation for immediate UX, and sync orchestration.
- **Local Data:** SQLite caches catalog/progress data and persists workouts, sets, tombstones, and outbox operations.
- **Infrastructure:** typed API client, secure token storage, connectivity monitoring, image caching, and media upload.
- **Coordinators:** `ActiveWorkoutCoordinator`, `SyncCoordinator`, and authenticated `SessionState` are lifecycle-safe application services.

### 8.2 Backend

- **API:** HTTP endpoints, authentication, API versioning, ProblemDetails middleware, localization, correlation IDs, and rate limiting.
- **Application:** MediatR commands/queries organized as feature slices, validators, authorization rules, and pipeline behaviors.
- **Domain:** entities, value objects, domain services/events, and business rules with no dependency on HTTP, EF Core, PostgreSQL, or MAUI.
- **Infrastructure:** PostgreSQL persistence, EF Core/Npgsql integration, repositories where a domain abstraction is required, JWT/refresh tokens, password hashing, object storage, email delivery, and migrations.

Application features are grouped into `Identity`, `Exercises`, `Workouts`, `Progress`, `Gamification`, `Media`, and `Sync`. A feature keeps its command/query, handler, validator, DTO, and mapping together.

### 8.3 MediatR Pipeline

Requests pass through these behaviors in order:

1. validation;
2. authorization;
3. idempotency where applicable;
4. transaction boundary for mutations;
5. structured logging and timing.

The server is authoritative for PRs, XP, levels, streaks, and badges. The mobile app can show explicitly marked optimistic values while offline but replaces them with server results after synchronization.

## 9. Domain Model

### 9.1 Identity and Catalog

- `User`: email, password hash, preferred unit, language, weekly goal, timestamps.
- `RefreshToken`: token hash, device, expiry, rotation/revocation state.
- `ExerciseDefinition`: nullable owner, name, body part, tracking mode, system/custom state, archive state.
- `ExerciseImage`: exercise, storage keys, source type, version, rights and review metadata.

### 9.2 Training Aggregate

- `WorkoutSession`: client operation ID, user, start/completion timestamps, status, aggregate version, timestamps.
- `WorkoutExercise`: session, exercise definition, order, optional notes.
- `SetEntry`: workout exercise, order, reps, optional weight in kilograms, optional assisted weight in kilograms, completion time, version.

`WorkoutSession` is the aggregate root. All workout mutations enforce ownership and state rules through the aggregate/application boundary.

### 9.3 Progress and Motivation

- `ExercisePerformance`: last performed time, last-session best, all-time best.
- `XpLedger`: immutable reason, delta, source ID, and timestamp. Source IDs prevent duplicate rewards.
- `UserProgress`: total XP, current level, version.
- `BadgeDefinition` and `UserBadge`: criteria/version and earned state.
- `StreakState`: weekly goal, current streak, best streak, last evaluated week.
- `ProcessedClientOperation`: operation ID and stored result for idempotent retries.

### 9.4 Stable Numeric Enums

- `BodyPart`: `Chest=1`, `Back=2`, `Shoulders=3`, `Arms=4`, `Legs=5`, `Core=6`.
- `TrackingMode`: `Weighted=1`, `Bodyweight=2`, `Assisted=3`.
- `WorkoutStatus`: `Draft=1`, `Active=2`, `Completed=3`.

Published numeric enum values are never reordered or reused.

All timestamps are stored in UTC. Calendar-week streak evaluation uses the user's configured time zone.

## 10. API Contract

The API is versioned under `/api/v1`.

### 10.1 Identity

- `POST /auth/register`
- `POST /auth/login`
- `POST /auth/refresh`
- `POST /auth/logout`
- `POST /auth/verify-email`
- `POST /auth/forgot-password`
- `POST /auth/reset-password`

### 10.2 Exercises and Media

- `GET /exercises?bodyPart={value}&search={term}&cursor={cursor}`
- `GET /exercises/{id}`
- `POST /exercises/custom`
- `PUT /exercises/custom/{id}`
- `DELETE /exercises/custom/{id}`
- `POST /media/upload-request`
- `POST /media/upload-complete`

Exercise list responses contain current-user `lastPerformedAt`, `lastBestSet`, and `allTimeBest` projections.

### 10.3 Workouts and Progress

- `GET /workouts?cursor={cursor}`
- `GET /workouts/{id}`
- `GET /exercises/{id}/history?cursor={cursor}`
- `GET /progress/summary`
- `GET /gamification/profile`

Offline mobile mutations use the sync contract instead of requiring individual online calls.

### 10.4 Sync

- `POST /sync/push`
- `GET /sync/pull?cursor={cursor}`

Every pushed operation contains:

- stable `operationId` generated on the client;
- entity/aggregate type;
- operation type;
- payload;
- base version;
- client timestamp for diagnostics, not ordering authority.

The server returns a result per operation. A partial batch can contain successes, permanent business failures, and retryable failures. The mobile client retries only retryable operations. Processed operation IDs return the stored prior result instead of executing again.

Pull responses include changed records, tombstones for deletes, server versions, and a new opaque cursor.

### 10.5 Conflict Policy

If the base version is stale, the server returns `VersionConflict = 60001` with the current server version. Mobile pulls the latest state and shows a concise comparison. The user can keep the server value or apply the local value against the new version. Local unsynchronized data is never silently discarded.

## 11. Business Errors

Business error codes use the five-digit `CCEEE` format: two category digits and three sequence digits.

- `10001–10999`: Identity and authentication.
- `20001–20999`: Exercise catalog.
- `30001–30999`: Workouts and sets.
- `40001–40999`: Progress and gamification.
- `50001–50999`: Media.
- `60001–60999`: Offline sync.

Initial named codes include:

- `InvalidCredentials = 10001`
- `RefreshTokenInvalid = 10003`
- `ExerciseNotFound = 20001`
- `WorkoutNotFound = 30001`
- `WorkoutAlreadyCompleted = 30002`
- `InvalidSetValue = 30004`
- `ImageTooLarge = 50002`
- `VersionConflict = 60001`

A published code is never renumbered, reused, or assigned a new meaning.

Typed Domain/Application business exceptions are converted by one API middleware into RFC ProblemDetails:

```json
{
  "type": "https://api.trackz.app/problems/workout/not-found",
  "title": "Business rule violation",
  "status": 404,
  "errorCode": 30001,
  "message": "Workout session was not found.",
  "traceId": "00-a42...",
  "fieldErrors": null
}
```

The API localizes `message` using `Accept-Language`. The numeric code does not change by language. Mobile uses the enum for action-specific handling and displays the server message as the fallback. Unknown codes show the message and trace ID. Stack traces, database details, token values, and internal exception text never leave the API.

## 12. Gamification Rules

Gamification rewards consistency rather than heavier lifting.

- Completed workout: 100 XP.
- Valid completed set: 5 XP, capped at 100 set XP per workout.
- Meeting the user-configured weekly goal: 150 XP.
- PRs receive a celebration and badge progress but no extra XP.
- Weekly goal is configurable from 1–7 completed workouts.
- A week meeting the goal increments current streak.
- A missed week resets current streak but preserves best streak.
- XP is represented by an immutable, idempotent ledger.
- Editing or deleting history creates compensating XP entries and recalculates projections.
- Level thresholds are a versioned server-owned table so future balancing does not require a mobile release.

Initial badge families cover first workout, workout-count milestones, weekly streak milestones, exercise variety, and personal-record milestones. Badges are never based on comparing one user with another.

## 13. Authentication and Security

- Email/password registration with verification and password reset.
- Short-lived JWT access tokens.
- Rotating refresh tokens stored hashed on the server and revocable per device.
- Mobile stores tokens only in platform Secure Storage, never SQLite or preferences.
- Authentication endpoints are rate limited and avoid email enumeration.
- Every user-owned query and mutation is scoped by authenticated user ID.
- Custom images are private and use time-limited signed access.
- Logs exclude credentials, tokens, image contents, and personally identifying request bodies.
- Account deletion revokes sessions and removes or anonymizes user records/media according to a documented retention policy.

## 14. Localization and Units

- Mobile supports Thai and English through localized resource files.
- Initial language follows the operating system and can be overridden in Settings.
- API business messages honor `Accept-Language` for Thai and English.
- Weight display supports kilograms and pounds.
- Canonical stored weight is decimal kilograms. Unit conversion occurs at the presentation boundary.
- Unit changes never alter canonical history or PR calculations.

## 15. Error and Recovery UX

- Local validation prevents impossible values before an outbox operation is created.
- Save failures keep user input on screen and provide a retry action.
- Pending sync, syncing, synced, permanent failure, and conflict are distinct visible states.
- `RefreshTokenInvalid` clears the authenticated session and routes to Login.
- `WorkoutAlreadyCompleted` reloads the workout read-only.
- `ImageTooLarge` returns the user to image selection with limits explained.
- `VersionConflict` pulls current server state and opens conflict resolution.
- Unexpected errors show a generic localized message and trace ID.
- Network absence is an operating state, not an error toast.

## 16. Testing Strategy

### 16.1 Unit Tests

- Weighted, bodyweight, and assisted LAST/PR comparison.
- Recalculation after edit/delete.
- Workout aggregate state transitions and ownership rules.
- XP ledger idempotency, compensation, levels, weekly streaks, and badges.
- Business-error mapping and stable numeric values.
- MediatR handlers and ViewModels.
- kg/lb conversion and rounding.

### 16.2 Integration and Contract Tests

- Run persistence tests against real PostgreSQL behavior.
- Verify migrations up and rollback in a disposable environment.
- Test authentication, refresh rotation/reuse rejection, authorization, and rate limits.
- Test ProblemDetails status, code, localized message, field errors, and trace ID.
- Test sync push/pull, idempotent retries, tombstones, pagination, partial results, and conflicts.
- Lock OpenAPI compatibility for supported mobile clients.
- Test object-storage upload authorization and image metadata processing.

### 16.3 Mobile and End-to-End Tests

- Log sets offline, terminate the app, restore the workout, reconnect, and sync exactly once.
- Edit/delete history and verify LAST, PR, XP, level, streak, and badges update.
- Create conflicts from two devices and resolve without silent loss.
- Switch kg/lb and Thai/English without changing canonical data.
- Verify secure token handling and logout/revocation.
- Exercise critical flows on Android and iOS.
- Verify screen-reader labels, dynamic text, contrast, Reduce Motion, and animation performance.

## 17. Definition of Done for MVP

- Android and iOS builds succeed from the same MAUI codebase.
- Registration, verification, login, refresh, logout, and reset-password flows work.
- Users can select body parts and multiple exercises, see LAST/PR, log offline, finish, edit, and delete workouts.
- Active workouts survive process termination.
- Sync retries do not duplicate workouts or sets.
- 40–60 reviewed standard exercise illustrations are published and cached correctly.
- Custom exercises support library images and private user uploads.
- XP, levels, weekly streaks, badges, and PRs remain correct after history changes.
- Thai/English and kg/lb work throughout the main flows.
- Automated unit, integration, contract, and critical mobile UI tests pass.
- PostgreSQL migrations have been exercised in a disposable environment.
- No credentials, tokens, private media, or PII appear in logs.

## 18. Implementation Constraints

- Start as a modular monolith; do not introduce microservices for MVP.
- Keep files feature-focused and classes single-purpose.
- Do not duplicate authoritative business calculations in Mobile. Mobile validation is for immediate UX only.
- Do not expose EF entities directly over the API.
- Do not hardcode numeric error values in ViewModels; consume a named contract enum.
- Do not publish generated exercise art without human review.
- Preserve the existing HTML prototype as reference material while rebuilding the production UI in native XAML.
