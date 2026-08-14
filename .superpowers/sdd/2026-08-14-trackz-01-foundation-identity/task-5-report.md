# Task 5 report — registration and login

## Delivered

- Application feature slices: `Identity/Register` and `Identity/Login`, `AuthTokenPair`, password/token abstractions, and the `RegisteredUser` contract.
- Infrastructure implementations: ASP.NET Core Identity password hashes, HMAC-SHA256 JWT access tokens, cryptographically random refresh tokens, SHA-256 refresh-token hashes, and validated JWT options/authentication configuration.
- API routes: `POST /api/v1/auth/register` returns `201 application/json`; `POST /api/v1/auth/login` returns `200 application/json`. Required DTO fields return `400 application/problem+json` validation responses.
- The existing business-exception middleware is in the runtime pipeline. It uses `Accept-Language` (`th`/`th-*`, otherwise English) to localize the first user-facing identity errors while retaining their numeric codes and problem contract.
- Added error codes `10006 PasswordPolicyViolation` and `10007 InvalidRegistrationInput`; existing published values are unchanged.

## Security and configuration decisions

- Email uses the persistence model's `Trim().ToUpperInvariant()` normalization for lookup/uniqueness; registration also catches the PostgreSQL unique-index race (`IX_users_NormalizedEmail`).
- Password policy is explicit: at least 12 characters with uppercase, lowercase, digit, and non-alphanumeric symbol. Passwords are hashed with the ASP.NET Core Identity PBKDF2 hasher and are never logged or persisted in plaintext.
- Unknown-user and bad-password login both execute password verification and return the identical `401 / 10001` response path. Login creates a per-session GUID (`sid` JWT claim), then persists only the SHA-256 hash of a 64-byte random refresh token.
- JWT access tokens include `sub`, `jti`, and `sid`, use UTC issue/expiry times, and require configured issuer, audience, a minimum 32-character signing key, positive bounded lifetimes, issuer/audience/signing-key/lifetime validation, and zero clock skew.
- The development signing key is intentionally local-development-only. Production must provide `Jwt__Issuer`, `Jwt__Audience`, and `Jwt__SigningKey` (plus optional lifetime overrides) from protected configuration; startup rejects missing/invalid values. No refresh rotation or logout endpoint was implemented (Task 6 remains separate).

## Strict TDD evidence

### RED

1. `dotnet test tests/TrackZ.Application.Tests --filter RegisterHandlerTests --no-restore` failed because the new test packages had not yet restored.
2. After restore, `dotnet test tests/TrackZ.Application.Tests --filter RegisterHandlerTests` failed with the expected missing `TrackZ.Application.Identity.Register` and `TrackZ.Infrastructure.Identity` namespaces.
3. `dotnet test tests/TrackZ.Api.Tests --filter LoginEndpointTests` failed for the intentionally absent `BusinessErrorCode.PasswordPolicyViolation` (and, before the test import was corrected, its Testcontainers type). This established the missing Task 5 contract before production implementation.

### GREEN / final verification

- `dotnet test tests/TrackZ.Application.Tests --filter RegisterHandlerTests` — 6 passed (real PostgreSQL): normalized registration, non-plaintext persistence, duplicate rejection, and four password-policy cases.
- `dotnet test tests/TrackZ.Api.Tests --filter LoginEndpointTests` — 4 passed (real PostgreSQL/API): registration/login status and content types, claims, hashed refresh persistence, indistinguishable credential failure contract, Thai localization, duplicate and policy problems.
- `dotnet test tests/TrackZ.Infrastructure.Tests` — 10 passed, including configuration validation and PostgreSQL persistence tests.
- `dotnet test tests/TrackZ.Application.Tests --filter Identity` — 6 passed.
- `dotnet test tests/TrackZ.Api.Tests --filter Identity` — 4 passed.
- `dotnet test tests/TrackZ.Api.Tests` — 24 passed.
- `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore` — exit code 0.

`dotnet test TrackZ.slnx` was attempted, but cannot complete in this environment because the Android SDK is absent (`XA5300` from `TrackZ.Mobile`). All backend and Task 5 verification commands above completed successfully.
