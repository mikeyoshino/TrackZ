# Task 6: Refresh Rotation, Localization, and Identity Security

## Delivered

- Added `POST /api/v1/auth/refresh` and authenticated `DELETE /api/v1/auth/sessions/{sessionId}`.
- Added refresh-token replay protection, atomic rotation, session-scoped logout, and signed-JWT authorization.
- Added English and Thai `.resx` business-message resources with supported-culture negotiation.
- Added a deterministic fixed-window rate limiter to register, login, and refresh with a stable 429 ProblemDetails response (`10008`) and `Retry-After` when the limiter provides it.

## Files

- Application: refresh and logout command/handlers plus explicit transaction interfaces.
- Infrastructure: PostgreSQL `FOR UPDATE` lookup methods and transaction implementation on `AppDbContext`.
- API: refresh/logout endpoint wiring, JWT authorization, localization/rate-limit configuration, resource files, and resource-backed business messages.
- Tests: real PostgreSQL Testcontainers refresh/security/localization/rate-limit coverage; API test parallelization is disabled because test-host configuration uses process environment variables.

## RED -> GREEN evidence

1. RED: `dotnet test tests/TrackZ.Api.Tests --filter "FullyQualifiedName~RefreshRotationTests|FullyQualifiedName~LocalizedProblemDetailsTests" --no-restore --disable-build-servers`
   - Refresh and logout routes returned 404; the sixth login was not limited; `th;q=0` still selected Thai; `fr;q=1, th;q=0.8` incorrectly selected English.
2. GREEN: the same focused command after implementation passed 9/9 against PostgreSQL 17 Testcontainers.
3. GREEN final API verification: `dotnet test tests/TrackZ.Api.Tests --no-restore --disable-build-servers` passed 34/34.
4. Backend verification: Application 10/10, Infrastructure 10/10, and Domain 1/1 passed using their respective no-restore commands.

## Security and transaction decisions

- Refresh input is immediately SHA-256 hashed; database lookups use only `TokenHash`, never a raw refresh token.
- Rotation starts a database transaction, locks the matching PostgreSQL row with `SELECT ... FOR UPDATE`, rejects absent/revoked/expired rows with `10003`, revokes the old row, inserts a hashed replacement in the same session, saves, and commits atomically.
- Concurrent duplicate refreshes serialize on the row lock: one succeeds and every replay observes the revoked old row and receives `10003`.
- Logout locks all active refresh rows for the authenticated user/session in its own transaction. This prevents a concurrent refresh from leaving a replacement usable after logout.
- Logout is JWT-protected; the signed `sub` claim supplies the user owner. A session belonging to another user is indistinguishable from a missing session (404), preventing cross-user revocation and ownership disclosure.
- All newly created/revoked timestamps use `DateTimeOffset.UtcNow`; neither raw credentials nor token values are logged.

## Localization and rate limits

- Supported cultures are only `en` and `th`. Header selection honors descending quality values, ignores `q=0`, falls back from `th-TH` to `th`, and continues past unsupported preferences (for example, `fr;q=1, th;q=0.8` selects Thai).
- Identity rate limits are five requests per one-minute fixed window, partitioned by remote IP and endpoint with no queue. The generic 429 body contains no request credentials or identity information.

## Carried review minors

- Signed JWT validation and tampered-token rejection are covered by the logout integration test.
- A real PostgreSQL concurrent duplicate-registration test verifies one 201 and one `10002` response.
- Language-negotiation tests cover both `fr;q=1, th;q=0.8` selecting Thai and `th;q=0` selecting English.

## Environment limitation

`dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android --no-restore --disable-build-servers` was attempted and failed with XA5300: the Android SDK directory is not installed/configured. The output instructs setting `AndroidSdkDirectory` for a custom path or installing the Android SDK. This is not reported as a successful Android build.
