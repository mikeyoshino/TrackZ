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

## Fix Round 1 security decisions

- `RefreshToken` persists an uppercase trimmed device identity bounded to 128 characters; rotation copies it and rejects a mismatched refresh request device.
- Both refresh and logout use the same transaction-scoped advisory lock for the session, then re-query current database state. Lock release is automatic on commit or rollback.
- `POST /api/v1/auth/logout` accepts an authenticated `{ sessionId }` request; the existing DELETE session route remains compatible. Ownership is taken from the signed JWT subject and unknown/cross-user sessions remain non-disclosing.
- Request validation now produces localized `ApiProblemDetails` with field-level errors, trace ID, and immutable auth code `10009`.

## Localization/rate limits

- Supported cultures are only `en` and `th`; negotiation honors quality order, ignores q=0, supports `th-TH` fallback, and continues to Thai after unsupported higher-quality entries.
- Identity endpoints are limited to five requests per minute per remote-IP/endpoint with generic 429 `10008` responses that contain no credentials.

## Environment limitation

Android compilation was attempted previously and failed with XA5300 because the Android SDK directory is not installed/configured; no Android success is claimed.
