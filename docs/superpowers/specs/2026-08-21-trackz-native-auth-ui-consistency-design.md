# TrackZ Native Authentication and UI Consistency Design

**Date:** 2026-08-21
**Status:** Approved in brainstorming; awaiting written-spec review
**Scope:** .NET MAUI native iOS authentication, protected-screen state handling, app-wide visual consistency, and safe local publication of the checked-in exercise artwork

## Context

TrackZ already has server-side registration, login, refresh, and logout endpoints, a secure mobile token store, account-session isolation, an authenticated exercise API client, and native workout screens. A fresh install nevertheless opens the main shell without a session because the app has no authentication entry flow. The Exercise Picker then calls a protected endpoint without a bearer token, receives `401`, and renders the same empty view used for a genuine empty filter. The result is an empty Chest list even though the API contains 48 exercise definitions.

The native screens also use inconsistent title placement, typography, margins, button geometry, selection colors, and content alignment. Several values are defined per page instead of through one visual system. This design fixes the authentication root cause and establishes a single native iOS presentation contract across the app.

The checked-in exercise images are deployment inputs under `assets/exercises/images`. The catalog deployment correctly uploads master and thumbnail renditions to private object storage and creates Draft image records. Draft artwork is intentionally absent from catalog `thumbnailUrl` values. A separate, explicit local-development publication path is required for simulator testing without weakening production's Draft-by-default lifecycle.

## Goals

- Present native Sign in and Create account screens before protected content when no valid session exists.
- Restore or refresh a saved session before displaying the main shell.
- Route authentication failures to an honest recovery state instead of an empty catalog.
- Preserve account isolation and offline-first SQLite behavior across login, logout, refresh, and account changes.
- Apply one consistent native iOS visual system to all shipped pages.
- Distinguish loading, authentication, connectivity, empty-filter, and successful catalog states.
- Allow explicitly reviewed local simulator artwork to cross the real domain Review-to-Publish lifecycle without changing production deployment behavior.
- Keep API bearer credentials off signed-media requests and keep repository paths out of mobile responses.

## Non-goals

- Social login, passkeys, password reset, email verification, or onboarding tutorials.
- A production artwork-review administration portal.
- Automatic publication during API startup or catalog deployment.
- Rendering repository PNG paths directly in the mobile app.
- Prescribing workouts, set targets, or training programs.
- Redesigning API identity contracts beyond what the native flow requires.

## Approved Visual Direction

The approved direction is **Native Performance**:

- iOS system typography with Dynamic Type support.
- Deep neutral backgrounds and surfaces.
- One lime accent reserved for primary actions, selections, progress, and success.
- No decorative lime body copy and no competing gradients.
- A spacing scale of 4, 8, 12, 16, 24, and 32 points.
- Page side margins of 17–18 points.
- Primary buttons 50–52 points high with a 15-point radius.
- Secondary buttons and steppers at least 44 points high.
- Text fields 50 points high with a 13–15-point radius.
- Cards use 16-point radii and one shared surface/stroke treatment.
- Every interactive target is at least 44 by 44 points.

Typography roles are semantic rather than page-specific:

| Role | Default metrics | Use |
| --- | --- | --- |
| Large title | 32/36, bold | Today and primary destinations |
| Navigation title | 17/22, semibold | Pushed pages and sheets |
| Section heading | 20/25, semibold | Content sections |
| Body | 15/21, regular | Primary copy |
| Metadata | 13/18, regular | LAST, PR, timestamps, modes |
| Field error | 12/16, regular | Localized validation feedback |

Central resource dictionaries own color roles, typography styles, spacing constants, fields, cards, chips, buttons, sticky-action containers, loading skeletons, and validation presentation. Pages consume semantic styles and must not introduce one-off colors, font sizes, or action dimensions unless a platform safe-area requirement demands it.

## Authentication Architecture

### Root auth gate

A root authentication coordinator owns four states:

- `CheckingSession`
- `SignedOut`
- `Refreshing`
- `SignedIn`

The app initially displays a native neutral launch/checking state. It does not construct or reveal protected pages until the coordinator resolves the current session.

Session bootstrap follows this order:

1. Read access token, refresh token, session ID, and user ID from `MobileTokenStore`.
2. If the required values are absent or malformed, clear the incomplete token set and enter `SignedOut`.
3. If a saved access token is usable, enter `SignedIn` and create the main `AppShell`.
4. If the access token is expired, attempt one refresh through `TrackZIdentityRefreshClient`.
5. If refresh succeeds, atomically save the rotated tokens and enter `SignedIn`.
6. If refresh is rejected, clear tokens and account-scoped private data through the existing account-session boundary, then enter `SignedOut`.
7. If refresh cannot reach the server, show a retryable connection state. Do not destroy a potentially valid offline session solely because transport is unavailable.

Only the auth coordinator swaps between `AuthShell` and `AppShell`. Protected pages never decide root navigation independently.

### Native auth shell

`AuthShell` contains native Sign in and Create account pages. Both use the approved geometry so switching modes does not move controls unexpectedly.

Sign in contains:

- Email field with email keyboard and appropriate content type.
- Password field using native secure entry and password content type.
- One primary Sign in action.
- A Create account text action.

Create account contains:

- Email field.
- Password field with the API-supported requirement copy.
- One primary Create account action.
- A Sign in text action.
- Terms and Privacy copy only when corresponding product links exist; no dead links are shipped.

Registration calls the existing register endpoint and then performs login with the same normalized credentials and device name. Tokens are stored only after a valid token response passes identity parsing. The successful account transition clears stale account-scoped cache and opens Today.

Submitting disables the primary action and prevents duplicate requests. Keyboard dismissal, Return-key behavior, focus order, VoiceOver labels, and safe-area behavior use native MAUI/iOS semantics.

### Protected-request recovery

A protected request that returns `401` attempts the existing bounded refresh flow once. A successful refresh retries the original safe request once. A rejected refresh transitions through the root auth gate to `SignedOut`. Requests are never retried indefinitely.

Logout continues to revoke the server session when possible, then clears secure tokens and all private local data even if the network call fails. Account changes remain serialized by `IAccountSessionBoundary` so stale asynchronous work cannot repopulate a new account's caches.

## Exercise Picker State Contract

The picker exposes explicit presentation states rather than deriving all UI from `Exercises.Count`:

| State | Presentation | Recovery |
| --- | --- | --- |
| Initial loading | Stable skeleton exercise rows | None; request in progress |
| Authentication required | Sign-in explanation | Open root Sign in flow |
| Offline with cache | Cached catalog and cached artwork | Continue offline; background retry later |
| Offline without cache | Offline explanation | Try again when connected |
| Request failure | Localized retryable error | Retry |
| No filter matches | Search/filter-specific empty copy | Clear search/filter |
| Results | Cards with artwork, LAST, PR, and selection | Select and continue |

`BusinessErrorCode`, HTTP status, connectivity, and cache contents determine the state. A `401`, timeout, malformed response, or offline condition cannot become the genuine no-matches state.

Exercise cards keep artwork, name, mode/body-part metadata, LAST, PR, and selection controls in stable positions. A failed artwork request affects only that card and provides a per-card retry action. It does not remove the exercise or block other artwork.

## Error Handling and Localization

API `ApiProblemDetails` remains the source of stable business semantics. Mobile presentation maps known error codes to localized English and Thai copy without rendering numeric codes.

- Field errors bind beneath the matching email or password field.
- Invalid credentials use a form-level authentication message without identifying whether an email exists.
- Duplicate registration uses the server's stable business code and a localized email-field or form message.
- `401` and rejected refresh enter the auth-recovery flow.
- Transport and timeout failures are retryable connection states.
- Malformed success or problem payloads fail closed as an invalid server response.
- Cancellation caused by account reset, navigation teardown, or app shutdown does not display an error.

Error labels reserve or animate bounded space so the primary action does not jump unpredictably. Reduce Motion disables nonessential transitions; no state change depends on animation completion.

## App-wide Component Consistency

The following shared components and styles become the only standard building blocks for shipped native screens:

- Large destination title and compact navigation title.
- Section heading, body, metadata, and field-error text roles.
- Primary, secondary, destructive, and quiet button roles.
- Standard text field and secure text field.
- Selection chip and circular selection indicator.
- Exercise/workout content card.
- Sticky safe-area action container.
- Stepper with 44-point decrement/increment targets.
- Loading skeleton, inline error, empty state, and retry state.

Today, Exercise Picker, active Workout, Set Logger, History, History Detail, Summary, Progress, Profile, Sign in, and Create account are audited against these roles. Hard-coded presentation values are removed when a semantic resource exists. Destructive actions remain red and never reuse lime. Secondary actions do not visually compete with the one primary action on a screen.

## Local Exercise Artwork Publication

Catalog deployment remains unchanged: it validates the manifest, creates deterministic renditions in private object storage, and persists Draft image metadata.

An additional explicit command supports local simulator testing. Its contract is:

```text
publish-exercise-catalog \
  --manifest <absolute-manifest-path> \
  --reviewer-id <non-empty-guid> \
  --rights-reference <non-empty-reference>
```

The command:

1. Runs only when `ASPNETCORE_ENVIRONMENT=Development`.
2. Loads and strictly validates the exact manifest.
3. Requires all 48 definitions, Draft image records, and deterministic deployed objects to match.
4. Invokes `ExerciseImage.Review(reviewerId, rightsReference, true, true, true, reviewedAt)` followed by `Publish(publishedAt)` for each image.
5. Uses one database transaction so partial publication cannot occur.
6. Is idempotent only when existing publication metadata matches the requested review contract; conflicting metadata fails closed.
7. Never runs during ordinary startup, migrations, or the existing deploy command.

This command records an explicit human-approved local review. It is not available as a production shortcut. Production assets remain Draft until they pass the normal organization-approved review process.

After publication, the existing catalog query returns opaque authenticated thumbnail routes. Mobile exchanges those routes for short-lived signed media capabilities and downloads bytes through the separate credential-free client. Object keys, repository paths, API bearer tokens, and storage credentials remain private.

## Data Flow

### Sign in and catalog load

1. Root auth gate presents Sign in.
2. User submits credentials.
3. Identity client receives and validates tokens.
4. Account-session boundary clears stale private data and saves the new identity atomically.
5. Root auth gate creates the main shell and opens Today.
6. Exercise Picker loads account-scoped SQLite data immediately.
7. When online, it requests the authenticated catalog.
8. The API returns definitions, performance projection, and opaque thumbnail authorization routes.
9. Mobile replaces catalog metadata transactionally, obtains signed media URLs, downloads through the credential-free client, and updates each card independently.

### Offline relaunch

1. Root auth gate finds a structurally valid saved identity.
2. If token refresh cannot reach the server, it preserves the offline session and enters the main shell with an offline indicator.
3. Picker and workout screens read account-scoped SQLite/cache data.
4. Connectivity recovery triggers bounded auth refresh followed by catalog/sync refresh.

## Security and Privacy

- Access and refresh tokens remain in iOS SecureStorage.
- Passwords are never persisted or logged.
- API bearer tokens are attached only to the exact configured API origin.
- Signed-media downloads never receive the bearer token and never follow redirects.
- Account reset cancels in-flight work before clearing private data.
- Auth and API logs exclude passwords, refresh tokens, access tokens, signed media URLs, object keys, and raw SDK exception messages.
- Local publication requires explicit Development environment and review metadata.
- Production startup cannot auto-register users, auto-login, or auto-publish images.

## Accessibility and Motion

- All user-facing text participates in Dynamic Type and can wrap.
- VoiceOver receives unique labels for email, password, show/hide password, primary actions, selection controls, artwork retry, and stepper decrement/increment actions.
- Color is never the only indication of selection, error, or sync state.
- Every action target is at least 44 points in both dimensions.
- Primary actions remain above the keyboard and safe area.
- Reduce Motion removes decorative scale, count-up, and transition effects.
- State commits, navigation, durable saves, and feedback never depend on animation completion.

## Testing Strategy

### Mobile.Core and MAUI

- Auth-gate state transitions for missing, valid, expired, malformed, refreshable, rejected, and offline identities.
- Register-then-login success, validation failure, duplicate account, invalid credentials, cancellation, and duplicate-submit fencing.
- Account transition clears old catalog, workout, history, progress, thumbnail, and outbox data.
- Protected-request `401` refreshes once and retries once; rejection returns to Sign in.
- Picker state matrix covers loading, auth required, offline with/without cache, request failure, no matches, and results.
- Resource/style tests reject hard-coded replacement values in audited pages and verify required semantic resources resolve in real MAUI composition.
- Accessibility tests verify distinct semantic labels and minimum action dimensions.

### API and infrastructure

- Existing identity acceptance remains green for register, login, refresh rotation, logout, localization, and rate limiting.
- Local publication command rejects non-Development execution, missing metadata, mismatched manifest, missing object, conflicting bytes, and partial/conflicting lifecycle state.
- Real PostgreSQL and MinIO acceptance proves all 48 Draft records transition atomically to Published and catalog responses contain opaque thumbnail routes.
- Media authorization and signed-download tests prove bearer isolation, expiry, tamper rejection, and owner/public rules.

### Simulator acceptance

- Fresh install opens Sign in rather than protected content.
- Create account automatically enters Today.
- Relaunch restores the session.
- Chest picker shows the expected exercise names and API-served artwork.
- Expired token refreshes without flashing an empty picker.
- Revoked session returns to Sign in and clears private data.
- Offline relaunch uses cached exercises and artwork.
- Small iPhone and available Pro Max screenshots cover auth, picker states, logger, history, progress, and profile.
- Essential flows are repeated with large Dynamic Type and Reduce Motion.

## Success Criteria

- A fresh simulator install can create an account or sign in without external token injection.
- Selecting Chest never displays a false empty list when the authenticated API contains Chest exercises.
- Locally approved catalog images arrive through API metadata and signed media URLs, not local asset paths.
- Auth, offline, loading, failure, and no-match states are visually and semantically distinct.
- All audited screens use the approved Native Performance roles for typography, spacing, color, controls, and alignment.
- Full Domain, Application, Infrastructure, API, Mobile, architecture, and iOS simulator verification passes, with external platform gates reported truthfully.
