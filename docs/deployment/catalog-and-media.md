# Production catalog and private-media deployment

The API does not seed the exercise catalog during ordinary startup. Deploy the exact checked-in
manifest and its 48 source PNGs with the explicit one-shot command:

```bash
dotnet TrackZ.Api.dll deploy-exercise-catalog \
  --manifest /opt/trackz/release/assets/exercises/catalog.json
```

The release bundle must retain the repository-relative `assets/exercises/catalog.json` and
`assets/exercises/images/*.png` layout. The command applies pending database migrations, strictly
validates the schema-v1 manifest and all 48 Draft records, identifies each PNG before decoding,
creates deterministic PNG master/thumbnail renditions, and writes them under the deterministic
`system/exercises/...` private-object keys. It verifies all 96 objects byte-for-byte before calling
the idempotent database seeder.

If storage fails partway through, no catalog rows are seeded; rerunning the same command reuses
matching objects and uploads the missing remainder. A pre-existing object with different bytes or
content type fails closed before any new upload. The command never reviews or publishes artwork:
all newly created image rows remain `Draft` with no reviewer, rights approval, or publication time.

Configure the process through the standard .NET configuration providers (environment-variable
names shown):

- `ConnectionStrings__TrackZ`: production PostgreSQL connection string.
- `ObjectStorage__ServiceUrl`, `ObjectStorage__Bucket`, `ObjectStorage__AccessKey`, and
  `ObjectStorage__SecretKey`: the private S3-compatible store.
- `ObjectStorage__StagingExpirationDays`: whole days before objects below `staging/` expire;
  required range 1-30 and production default 1.
- `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey`, `Jwt__AccessTokenMinutes`, and
  `Jwt__RefreshTokenDays`: normal API authentication settings.
- `MediaAccess__PublicOrigin`: the externally reachable origin serving `/media/v1/...` routes.
- `MediaAccess__SigningKey`: a separate secret of at least 32 characters.
- `MediaAccess__LifetimeSeconds`: signed GET lifetime from 15 through 300 seconds (60 recommended).

Private and published-system images are exposed only after an authenticated request to the opaque
`/api/v1/media/exercise-images/{imageId}/{rendition}` authorization route. That route returns a
short-lived signed URL on `MediaAccess__PublicOrigin`; it never returns an object key. Clients must
send API bearer credentials only to the API origin and must use a credential-free HTTP client for
the signed URL. That client must not follow redirects; the shipped mobile registration disables
automatic redirects. An expired capability returns HTTP 410 so the client can authenticate again
and obtain a fresh URL.

The object-store principal must also have `s3:GetLifecycleConfiguration` and
`s3:PutLifecycleConfiguration` on the configured bucket. API startup verifies or installs one
enabled TrackZ-owned expiration rule scoped exactly to `staging/`, while preserving every unrelated
bucket lifecycle rule. The same 1-30 day bound applies to current objects and noncurrent object
versions, so enabling bucket versioning cannot retain abandoned upload bytes indefinitely. A TrackZ
rule with a conflicting prefix, status, current expiration, noncurrent expiration, or other action
causes startup to fail. Authorization failures, connectivity failures, and an unverified write also
fail startup; the API does not serve traffic without the crash-safe staging cleanup bound. Startup
surfaces a constant sanitized error and logs only the dependency exception type, never SDK messages,
credentials, endpoints, object keys, or inner exceptions. The rule does not match `private/` or
`system/`, and the bucket remains private.
