# TrackZ Shared VPS Deployment Design

**Date:** 2026-09-07  
**Status:** Approved  
**Production origin:** `https://api.trackz.sytoys.shop/`

## Objective

Deploy the TrackZ API to the Ubuntu VPS that already hosts SyToyV2 while keeping TrackZ runtime state, deployment permissions, failure handling, and release cadence independent. GitHub Actions must follow the existing SyToyV2 manual production workflow pattern: an operator selects a branch, CI builds an immutable image, and a restricted VPS command activates that exact digest.

The existing Caddy instance remains the only public listener on ports 80 and 443. It terminates TLS for `api.trackz.sytoys.shop` and proxies requests to the TrackZ API over Docker networking.

## Isolation Boundary

TrackZ runs as the Compose project `trackz-production`. It owns these services:

- `api`: the ASP.NET Core TrackZ API;
- `catalog-deploy`: an idempotent one-shot database migration and exercise-catalog deployment job;
- `postgres`: a PostgreSQL 17 instance used only by TrackZ;
- `minio`: private S3-compatible object storage used only by TrackZ;
- `minio-bootstrap`: an idempotent one-shot bucket, user, policy, and lifecycle bootstrap job.

TrackZ owns separate configuration and state paths:

- `/opt/trackz` for the production Compose definition;
- `/etc/trackz` for runtime and Compose environment files;
- `/var/lib/trackz` for logs and any host-owned operational state;
- `/var/backups/trackz` for pre-deployment database and object-storage backups;
- `/usr/local/sbin/trackz-deploy` for the root-owned deployment command;
- `/run/lock/trackz-deploy.lock` for the TrackZ-only deployment lock.

PostgreSQL and MinIO use explicitly named volumes prefixed with `trackz-production-`. They share no volumes, credentials, networks, database instances, or deployment commands with SyToyV2. PostgreSQL, MinIO, and the API publish no host or public ports.

## Shared Edge Routing

The TrackZ `api` service joins two networks:

- a TrackZ-owned internal backend network shared only with TrackZ PostgreSQL and MinIO;
- the existing external Docker network `toystore-production_frontend`, shared only for Caddy-to-API traffic.

On the external network, the service publishes the network alias `trackz-api`. The SyToyV2 Caddy configuration adds this site block:

```caddyfile
api.trackz.sytoys.shop {
	encode zstd gzip

	header {
		X-Content-Type-Options nosniff
		Referrer-Policy strict-origin-when-cross-origin
		-Server
	}

	reverse_proxy trackz-api:8080
}
```

The source-of-truth Caddyfile and its architecture test are updated in `/Users/mikeyoshino/gitRepos/SyToyV2`. The VPS copy at `/opt/toystore/Caddyfile` is updated once, validated inside the running Caddy container, and reloaded without restarting the SyToyV2 web or database services.

TrackZ configures ASP.NET Core forwarded headers and trusts only the existing fixed Caddy address `172.30.0.2`. This preserves the original HTTPS scheme and client address used by request logging and identity rate limits without accepting spoofed forwarded headers from other peers.

## Container Image

The repository-root `Dockerfile` uses a multi-stage .NET 10 build. The build stage publishes only `src/TrackZ.Api/TrackZ.Api.csproj` for `linux-x64`; the runtime stage uses `mcr.microsoft.com/dotnet/aspnet:10.0`, copies the published API and `assets/exercises`, listens on port 8080, and runs as the image's non-root `app` user.

The same immutable image is used by `catalog-deploy` and `api`. This guarantees that migrations, catalog code, manifest data, and the serving API come from the same Git commit.

## Production Configuration and Secrets

Application secrets stay on the VPS and never pass through GitHub Actions. A root-run `trackz-bootstrap` script performs one-time setup and generates cryptographically random values for:

- PostgreSQL password;
- JWT signing key;
- signed-media URL key;
- MinIO root password;
- TrackZ MinIO access key and secret key.

The bootstrap script writes regular, non-symlink files with restrictive ownership and permissions:

- `/etc/trackz/postgres.env`, owned by root and mode `0600`;
- `/etc/trackz/trackz.env`, owned by root and mode `0600`;
- `/etc/trackz/minio.env`, owned by root and mode `0600`;
- `/etc/trackz/compose.env`, owned by `root:trackz` and mode `0640`.

`trackz.env` supplies the production database connection, JWT issuer/audience/signing key and lifetimes, `MediaAccess__PublicOrigin=https://api.trackz.sytoys.shop`, media-signing settings, internal MinIO endpoint and credentials, and optional Stripe settings. Stripe values may remain unset until server-side Stripe checkout is enabled; the bootstrap script never invents payment credentials.

The GitHub Environment `production` contains only deployment transport configuration:

| Kind | Name | Meaning |
|---|---|---|
| Variable | `VPS_HOST` | VPS SSH hostname or address |
| Variable | `VPS_PORT` | SSH port, defaulting to `22` |
| Variable | `VPS_USER` | Dedicated `trackz-deploy` account |
| Variable | `PRODUCTION_URL` | `https://api.trackz.sytoys.shop` |
| Secret | `VPS_SSH_PRIVATE_KEY` | Private key for the TrackZ deploy account |
| Secret | `VPS_SSH_KNOWN_HOSTS` | Pinned VPS host key entry |

The VPS account has one sudo permission only:

```text
trackz-deploy ALL=(root) NOPASSWD: /usr/local/sbin/trackz-deploy *
```

The root-owned command accepts exactly one validated `ghcr.io/...@sha256:<64 lowercase hex>` argument. The deploy account cannot invoke Docker or an unrestricted root shell through the workflow.

## Database, Object Storage, and Catalog Initialization

The deployment command starts PostgreSQL and MinIO first and waits for their native health checks. `minio-bootstrap` then creates the private `trackz-private` bucket, attaches the least-privilege TrackZ API policy, disables anonymous access, and applies the required staging-object lifecycle policy.

The `catalog-deploy` service executes:

```text
dotnet TrackZ.Api.dll deploy-exercise-catalog --manifest /app/assets/exercises/catalog.json
```

This existing command applies EF Core migrations, validates the exact manifest, creates deterministic image renditions in private object storage, and seeds the system exercise catalog idempotently. The API starts only after `catalog-deploy` exits successfully. A migration, catalog, object-storage, or credential failure therefore prevents the new API image from receiving traffic.

## Health Model

The API exposes unauthenticated operational endpoints that return no application data:

- `/health/live` returns success when the process can handle HTTP;
- `/health/ready` returns success only after PostgreSQL connectivity and private object-storage access succeed.

The API container health check uses the local readiness endpoint. The deploy script checks readiness from the Docker network before declaring the release active. GitHub Actions then checks `https://api.trackz.sytoys.shop/health/ready`, proving that DNS, TLS, Caddy, Docker routing, the API, PostgreSQL, and MinIO all work through the public path.

## Manual GitHub Actions Release

`.github/workflows/deploy-production.yml` uses `workflow_dispatch` with a required `branch` input whose default is `main`. It uses the GitHub Environment `production`, grants `contents: read` and `packages: write`, and sets a TrackZ-specific concurrency group with `cancel-in-progress: false`.

The workflow performs these steps:

1. validate and check out the selected branch;
2. install .NET 10 and restore the API and EF tooling;
3. build the API in Release configuration;
4. generate an idempotent EF migration SQL artifact retained for 14 days;
5. build a `linux/amd64` image and push it to GHCR using the commit SHA as a discoverable tag;
6. use the registry-returned digest as the deployment reference;
7. install the pinned SSH host key and TrackZ deployment key in the ephemeral runner;
8. invoke `sudo /usr/local/sbin/trackz-deploy '<immutable-digest>'`;
9. verify the public readiness URL with bounded retries;
10. remove SSH material in an `always()` cleanup step.

Runtime secrets, connection strings, JWT keys, object-storage credentials, and Stripe credentials are absent from the workflow and its logs.

## Deployment, Backup, and Rollback

`trackz-deploy` validates the caller, argument count, immutable image format, Compose files, environment files, and domain before changing runtime state. It uses `flock -n` on the TrackZ-specific lock so two TrackZ deployments cannot overlap; it does not acquire or interfere with the SyToyV2 deployment lock.

Before replacing an existing API image, the command stops TrackZ API writes and creates a timestamped backup directory containing:

- a PostgreSQL custom-format dump;
- an object-level MinIO backup created by a pinned one-shot `minio/mc` container attached to the TrackZ private network: it configures the internal MinIO endpoint, verifies that the source bucket exists, and mirrors the complete `trackz-private` bucket into `/var/backups/trackz/<release-id>/objects` while API writes are stopped;
- release metadata containing the current and proposed immutable image references.

It then records the proposed digest atomically, recreates only TrackZ containers, waits for catalog deployment and API readiness, and leaves the SyToyV2 Compose project running throughout.

If readiness fails and a previous image exists, the script atomically restores the previous digest and recreates the TrackZ catalog/API services with that image. It prints TrackZ service logs and preserves the backup. Database schema downgrade and object deletion are deliberately manual because automatically reversing a migration can destroy data written during or after a partial deployment. An initial deployment failure stops the TrackZ application services and retains the infrastructure and diagnostics for repair.

## Mobile Production Origin

Release builds of the mobile app use `https://api.trackz.sytoys.shop/` as both their API and media origin. Debug builds retain environment-variable overrides for local Simulator and device testing. Signed media URLs also use the production origin, so images follow the same TLS and Caddy path as the API.

Changing the default origin removes the need for a Cloudflare Quick Tunnel in production. The temporary development tunnel remains an operator-controlled testing tool and is not part of the deployment architecture.

## Repository Changes

TrackZ receives:

- `.github/workflows/deploy-production.yml`;
- `Dockerfile` and `.dockerignore`;
- `deploy/compose.production.yaml`;
- `deploy/trackz-deploy`;
- `deploy/trackz-bootstrap`;
- `deploy/Caddyfile.trackz` as the reviewed site-block source;
- API forwarded-header and health-check implementation;
- mobile production-origin configuration;
- deployment architecture tests and health tests;
- `docs/DEPLOYMENT.md` with initial VPS, Caddy, GitHub Environment, release, backup, recovery, and smoke-test commands.

SyToyV2 receives only the TrackZ site block in `deploy/Caddyfile` and the matching architecture assertion. Existing unrelated uncommitted files in both repositories remain untouched.

## Verification

Automated verification covers:

- API liveness and dependency-aware readiness responses;
- forwarded headers accepting only the configured Caddy proxy;
- production Compose isolation, private networks, explicit volume names, non-public database/object-storage/API services, dependency ordering, and health checks;
- deploy-script immutable digest validation, dedicated lock, backup, atomic environment update, readiness wait, and image rollback;
- workflow manual branch input, permissions, concurrency, migration artifact, immutable digest deployment, pinned SSH host, public readiness verification, and absence of application secrets;
- bootstrap-generated file permissions and required configuration keys;
- mobile Release origins;
- SyToyV2 Caddy routing without changes to the existing Toy Store host blocks.

Before the first production release, operational verification consists of `docker compose config`, Caddy validation and reload, a TrackZ deployment from GitHub Actions, public live/ready checks, account registration and login, exercise catalog listing with thumbnails, custom exercise image upload/download, workout synchronization, VPS reboot recovery, and restoration of a disposable backup in an isolated test database and bucket.
