# Task 6: Refresh Rotation, Localization, and Identity Security

## Delivered

- Added refresh rotation/replay protection, signed-JWT session logout, English/Thai resources, and fixed-window identity rate limits.
- Refresh input is immediately SHA-256 hashed; database lookups use only `TokenHash`, never a raw token.
- PostgreSQL transactions lock refresh rows with `FOR UPDATE`; Fix Round 1 also adds a transaction-scoped PostgreSQL advisory session lock used by refresh and logout before their state mutation.

## RED -> GREEN evidence

1. Original RED: focused API tests found missing refresh/logout routes, missing rate limiting, and incorrect `Accept-Language` q-value handling.
2. Original GREEN: focused tests passed 9/9 and final API suite passed 34/34 against PostgreSQL Testcontainers.
3. Fix Round 1 RED: POST logout returned 404, refresh accepted a mismatched device, and refresh request validation used the framework response instead of the shared error contract.
4. Fix Round 1 GREEN: targeted endpoint tests cover POST logout, device mismatch rejection, and Thai shared validation details.
5. Fix Round 1 deterministic concurrency GREEN: `SessionSerializationTests` passed against PostgreSQL Testcontainers. Its coordination barrier holds refresh immediately after advisory-lock acquisition, starts logout and proves it is blocked, releases refresh to commit a replacement, then verifies logout leaves no active token and both original/replacement refresh attempts return `10003`.

## Fix Round 1 security decisions

- `RefreshToken` persists an uppercase trimmed device identity bounded to 128 characters; rotation copies it and rejects a mismatched refresh request device.
- Both refresh and logout use the same transaction-scoped advisory lock for the session, then re-query current database state. Lock release is automatic on commit or rollback.
- `POST /api/v1/auth/logout` accepts an authenticated `{ sessionId }` request; the existing DELETE session route remains compatible. Ownership is taken from the signed JWT subject and unknown/cross-user sessions remain non-disclosing.
- Request validation now produces localized `ApiProblemDetails` with field-level errors, trace ID, and immutable auth code `10009`.

## Fix Round 2

- The pre-advisory-lock refresh lookup is explicitly `AsNoTracking`; the subsequent `FOR UPDATE` read is authoritative and tracked, so it observes committed revocation/expiry state before rotation.
- Deterministic PostgreSQL barriers cover refresh-then-logout and refresh-then-refresh serialization. The waiting operation is asserted blocked behind the advisory lock, then is released only after the first transaction commits.
- Migration V2 assigns legacy rows `DeviceName = LEGACY` and revokes all formerly active rows, forcing safe reauthentication instead of leaving active sessions without a trustworthy device identity.

## Fix Rounds 3-4 validation evidence

- Identity routes explicitly parse only JSON/`+json` UTF-8 bodies after authorization, map only whitelisted JSON paths to field names, and return `10009` with safe localized field errors. Unsupported/missing content types map to `body`; unauthenticated logout remains 401.
- Focused localization checks verify exact English and Thai values for malformed body, required refresh token, and overlong device name; field values contain no parser diagnostics.

## Fix Round 5

- Media types are parsed with ASP.NET's standard parser. Tests cover quoted/whitespace and case-varied unsupported charsets, invalid media syntax, and accepted structured `+json` UTF-8 registration.

## Localization/rate limits

- Supported cultures are only `en` and `th`; negotiation honors quality order, ignores q=0, supports `th-TH` fallback, and continues to Thai after unsupported higher-quality entries.
- Identity endpoints are limited to five requests per minute per remote-IP/endpoint with generic 429 `10008` responses that contain no credentials.

## Environment limitation

Android compilation was attempted previously and failed with XA5300 because the Android SDK directory is not installed/configured; no Android success is claimed.
