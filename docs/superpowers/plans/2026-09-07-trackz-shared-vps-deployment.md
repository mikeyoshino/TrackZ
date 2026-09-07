# TrackZ Shared VPS Deployment Implementation Plan

> **For Codex:** Use `superpowers:test-driven-development` while implementing each task and `superpowers:verification-before-completion` before claiming the deployment is ready. Preserve all unrelated dirty-worktree changes in TrackZ and SyToyV2.

**Goal:** Ship the TrackZ API as an isolated Docker Compose project on the SyToyV2 VPS, expose it through the existing Caddy instance at `https://api.trackz.sytoys.shop/`, and deploy immutable GHCR image digests from a manually selected Git branch.

**Architecture:** TrackZ owns its API, PostgreSQL, MinIO, initialization jobs, secrets, volumes, lock, backups, and deployment command. Only the API joins SyToyV2's existing external frontend network under alias `trackz-api`; Caddy remains the sole public listener. Runtime secrets are generated once on the VPS, while GitHub stores only SSH transport configuration.

**Tech Stack:** .NET 10 / ASP.NET Core health checks and forwarded headers, EF Core 10, PostgreSQL 17, MinIO, Docker Compose, Caddy 2, GitHub Actions, GHCR, xUnit, .NET MAUI.

**Approved spec:** `docs/superpowers/specs/2026-09-07-trackz-shared-vps-deployment-design.md`

---

## Task 1: Lock the production contract with failing repository tests

**Files:**

- Create: `tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs`
- Create: `.config/dotnet-tools.json`
- Test: `tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs`

**Step 1: Add the local EF tool manifest**

Create `.config/dotnet-tools.json` with `dotnet-ef` pinned to `10.0.10`, matching the EF design packages already used by the API and Infrastructure projects.

**Step 2: Write failing file-contract tests**

Add tests that resolve the TrackZ repository root from `TrackZ.slnx` and require these not-yet-created files:

- `Dockerfile` and `.dockerignore`;
- `deploy/compose.production.yaml`;
- `deploy/trackz-bootstrap` and `deploy/trackz-deploy`;
- `deploy/Caddyfile.trackz`;
- `.github/workflows/deploy-production.yml`;
- `docs/DEPLOYMENT.md`.

Assert the important architecture rather than only file existence:

- runtime image is .NET 10 and ends with `USER app`;
- Compose project is `trackz-production`;
- only `api` joins external `toystore-production_frontend` with alias `trackz-api`;
- TrackZ backend is internal and all volumes have explicit `trackz-production-` names;
- no `ports:` mapping exposes API, PostgreSQL, or MinIO;
- `catalog-deploy` must succeed before `api` starts;
- immutable digest, TrackZ lock, database dump, MinIO mirror, rollback, and readiness logic are present in `trackz-deploy`;
- bootstrap names all required generated settings and applies restrictive permissions;
- workflow is manual, accepts a branch, builds before pushing, deploys the Buildx digest, pins SSH known hosts, checks public readiness, and contains no application credentials.

Use small assertion helpers such as `AssertAppearsBefore` and exact forbidden strings such as `5432:5432`, `9000:9000`, `StrictHostKeyChecking=no`, `ConnectionStrings__TrackZ`, and `Jwt__SigningKey` in the workflow.

**Step 3: Run the focused tests and confirm RED**

Run:

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter FullyQualifiedName~ProductionDeploymentContractTests
```

Expected: FAIL because the production deployment files do not exist yet.

**Step 4: Commit the contract and tool manifest**

```bash
git add .config/dotnet-tools.json tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs
git commit -m "test: define TrackZ production deployment contract"
```

---

## Task 2: Add dependency-aware health and trusted-proxy behavior

**Files:**

- Create: `src/TrackZ.Infrastructure/Health/PostgresReadinessHealthCheck.cs`
- Create: `src/TrackZ.Infrastructure/Health/ObjectStorageReadinessHealthCheck.cs`
- Modify: `src/TrackZ.Infrastructure/Media/ObjectStorage.cs`
- Modify: `src/TrackZ.Infrastructure/DependencyInjection.cs`
- Modify: `src/TrackZ.Api/Program.cs`
- Modify: `src/TrackZ.Api/appsettings.json`
- Create: `tests/TrackZ.Api.Tests/Health/HealthEndpointTests.cs`
- Create: `tests/TrackZ.Api.Tests/Hosting/ForwardedHeadersTests.cs`

**Step 1: Write failing liveness/readiness tests**

Use `WebApplicationFactory<Program>` with in-memory test configuration and replace the existing staging lifecycle hosted service. Assert:

- `GET /health/live` is anonymous and returns `200` without exposing application data;
- `GET /health/ready` returns `200` when both tagged dependency checks are healthy;
- `GET /health/ready` returns `503` when either the database or object-storage check fails;
- readiness response does not print credentials, connection strings, exception stacks, or dependency hostnames.

Register deterministic fake health checks in the test factory so endpoint behavior is tested without making these route tests depend on Docker.

**Step 2: Write failing trusted-proxy tests**

Configure `ReverseProxy:KnownProxy` as `172.30.0.2`. Send forwarded-protocol and forwarded-address headers through TestServer and verify:

- a request whose remote address is the configured proxy receives the forwarded HTTPS scheme and client address;
- the same headers from an untrusted address are ignored;
- only one proxy hop is processed.

Expected before implementation: tests fail because forwarded-header middleware and configuration do not exist.

**Step 3: Implement dependency health checks**

- `PostgresReadinessHealthCheck` creates a scoped `AppDbContext` and calls `Database.CanConnectAsync(cancellationToken)`.
- Add an internal/publicly testable `CheckAvailabilityAsync` operation to `ObjectStorage` that requests the bucket lifecycle configuration. This exercises DNS, credentials, bucket access, and MinIO response without reading customer objects.
- `ObjectStorageReadinessHealthCheck` calls that operation and converts failure to unhealthy without including secret values.
- Register both checks with the tag `ready`; register a self check with tag `live`.

**Step 4: Map health endpoints and configure forwarded headers**

In `Program.cs`:

- bind and validate `ReverseProxy:KnownProxy` as a single IP address;
- configure `ForwardedHeadersOptions` for `X-Forwarded-For | X-Forwarded-Proto`, clear default known networks/proxies, add only the configured address, and set `ForwardLimit = 1`;
- call `UseForwardedHeaders()` before rate limiting, authentication, and request behavior that uses scheme or client IP;
- map `/health/live` and `/health/ready` with tag predicates and a minimal JSON writer.

Set the production-compatible default `ReverseProxy:KnownProxy` to `172.30.0.2` in `appsettings.json`; tests may override it.

**Step 5: Run focused tests and confirm GREEN**

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "FullyQualifiedName~HealthEndpointTests|FullyQualifiedName~ForwardedHeadersTests"
```

Expected: PASS.

**Step 6: Commit**

```bash
git add src/TrackZ.Api/Program.cs src/TrackZ.Api/appsettings.json src/TrackZ.Infrastructure/DependencyInjection.cs src/TrackZ.Infrastructure/Media/ObjectStorage.cs src/TrackZ.Infrastructure/Health tests/TrackZ.Api.Tests/Health tests/TrackZ.Api.Tests/Hosting
git commit -m "feat: add production health and proxy handling"
```

---

## Task 3: Build a non-root immutable API image

**Files:**

- Create: `Dockerfile`
- Create: `.dockerignore`
- Modify: `tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs`

**Step 1: Tighten the failing container assertions**

Require:

- an SDK 10 build stage and ASP.NET 10 runtime stage;
- Release publish of `src/TrackZ.Api/TrackZ.Api.csproj` for `linux-x64`;
- exercise catalog copied to `/app/assets/exercises`;
- `curl` installed for the container health check before dropping to `USER app`;
- port `8080` and `dotnet TrackZ.Api.dll` entry point;
- `.git`, build outputs, local runtime state, tests, documentation, and IDE files excluded from the Docker build context, while `assets/exercises` remains included.

Run the focused contract test and confirm it still fails.

**Step 2: Implement the multi-stage image**

Restore with project files copied first for layer caching, then copy source and publish. Install only the runtime package needed for the local health probe, clean package indexes, copy the published output and catalog assets with `app` ownership, and run as `app`.

**Step 3: Build and inspect the image**

```bash
docker build --platform linux/amd64 -t trackz-api:deployment-test .
docker image inspect trackz-api:deployment-test --format '{{.Config.User}} {{json .Config.ExposedPorts}}'
docker run --rm --entrypoint sh trackz-api:deployment-test -c 'test -f /app/TrackZ.Api.dll && test -f /app/assets/exercises/catalog.json && command -v curl'
```

Expected: image builds; inspection reports user `app` and port `8080/tcp`; all file/tool checks pass.

**Step 4: Re-run the focused contract tests**

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter FullyQualifiedName~ProductionDeploymentContractTests
```

Expected: failures now concern only the remaining deployment files.

**Step 5: Commit**

```bash
git add Dockerfile .dockerignore tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs
git commit -m "build: add TrackZ production API image"
```

---

## Task 4: Define the isolated TrackZ production Compose project

**Files:**

- Create: `deploy/compose.production.yaml`
- Modify: `tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs`

**Step 1: Add failing Compose ordering and isolation assertions**

Assert exact services `postgres`, `minio`, `minio-bootstrap`, `catalog-deploy`, and `api`; pinned PostgreSQL/MinIO/MinIO-client images; restart and health behavior; explicit env files; and no Caddy service in this Compose project.

**Step 2: Implement the production Compose file**

- Use top-level name `trackz-production`.
- Give PostgreSQL and MinIO health checks and named persistent volumes.
- Put PostgreSQL, MinIO, bootstrap, catalog, and API on an internal `backend` network with an explicit stable network name for backup operations.
- Join only `api` to external network `toystore-production_frontend`, alias it `trackz-api`, and publish no host ports.
- Run `minio-bootstrap` as an idempotent one-shot job using root and API credential env files. Create `trackz-private`, remove anonymous access, create the least-privilege user/policy, and permit only the object and lifecycle operations required by TrackZ.
- Run `catalog-deploy` from `${TRACKZ_IMAGE}` with `deploy-exercise-catalog --manifest /app/assets/exercises/catalog.json` after healthy infrastructure and successful MinIO bootstrap.
- Start `api` only after successful catalog deployment. Its health check calls local `/health/ready` using `curl`.
- Supply `ASPNETCORE_ENVIRONMENT=Production`, port `8080`, and `ReverseProxy__KnownProxy=172.30.0.2`.

**Step 3: Validate Compose interpolation with disposable env files**

Create temporary test env files under `/tmp`, point the Compose file at them through a temporary rendered copy or test fixture, and run:

```bash
docker compose --env-file /tmp/trackz-compose-test.env -f deploy/compose.production.yaml config
```

Expected: valid rendered configuration, no host-published ports, and no unresolved required variable.

Remove only the explicitly created temporary test files.

**Step 4: Run the deployment contract tests**

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter FullyQualifiedName~ProductionDeploymentContractTests
```

Expected: Compose assertions pass; scripts/workflow/docs assertions remain red.

**Step 5: Commit**

```bash
git add deploy/compose.production.yaml tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs
git commit -m "ops: define isolated TrackZ production stack"
```

---

## Task 5: Add one-time bootstrap and safe digest deployment commands

**Files:**

- Create: `deploy/trackz-bootstrap`
- Create: `deploy/trackz-deploy`
- Modify: `tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs`

**Step 1: Write failing script safety assertions**

Cover root enforcement, exact arguments, symlink refusal, restrictive umask/modes, TrackZ-only paths, cryptographic secret generation, immutable digest validation, dedicated lock, atomic env replacement, bounded health polling, backup metadata, database dump, MinIO mirror, diagnostic logs, prior-image rollback, and initial-deploy failure behavior.

**Step 2: Implement `trackz-bootstrap`**

The root-only script must:

- accept no arguments and refuse unsafe existing symlinks/non-regular env files;
- require the existing `toystore-production_frontend` Docker network;
- create the `trackz` system group and `trackz-deploy` system user if absent;
- create `/opt/trackz`, `/etc/trackz`, `/var/lib/trackz`, and `/var/backups/trackz` with exact ownership and modes;
- generate secrets with `openssl rand` only when the target env file does not exist;
- atomically write `postgres.env`, `minio.env`, `trackz.env`, and `compose.env` with the approved permissions;
- configure `MediaAccess__PublicOrigin=https://api.trackz.sytoys.shop`, internal service endpoints, JWT values, and optional empty Stripe entries without printing their values;
- validate the installed Compose configuration but not start or modify SyToyV2 services.

Document/install the root-owned sudoers rule separately; do not let this repository script broaden its own sudo privilege.

**Step 3: Implement `trackz-deploy`**

The root-only command must:

1. accept exactly one lower-case immutable `ghcr.io/...@sha256:<64 hex>` reference;
2. lock `/run/lock/trackz-deploy.lock` non-blockingly;
3. validate all regular files, configuration, network, and current image;
4. start/wait for TrackZ PostgreSQL and MinIO and pull only TrackZ images;
5. stop TrackZ API writes before backup;
6. create a non-overwriting UTC/digest backup directory;
7. stream `pg_dump --format=custom` into `database.dump`;
8. use the pinned `minio/mc` image on the TrackZ backend network to verify and mirror `trackz-private` into `objects/`;
9. write current/proposed image metadata and restrictive file modes;
10. atomically replace `TRACKZ_IMAGE` in `/etc/trackz/compose.env`;
11. run infrastructure, MinIO bootstrap, catalog deployment, and API in dependency order;
12. wait a bounded time for API readiness from inside the API container;
13. on failure, log TrackZ services, atomically restore the previous digest, rerun catalog/API with that image, preserve the backup, and state that schema/object restoration is manual;
14. if no previous digest exists, stop TrackZ application jobs/API while retaining infrastructure and logs.

Do not invoke the Toy Store deploy command, lock, project, volumes, or service restarts.

**Step 4: Check script syntax and tests**

```bash
bash -n deploy/trackz-bootstrap
bash -n deploy/trackz-deploy
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter FullyQualifiedName~ProductionDeploymentContractTests
```

Expected: shell syntax passes; remaining test failures are limited to workflow, Caddy source, mobile origin, or documentation.

**Step 5: Commit**

```bash
git add deploy/trackz-bootstrap deploy/trackz-deploy tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs
git commit -m "ops: add TrackZ bootstrap and rollback deployment"
```

---

## Task 6: Add the manual immutable-digest GitHub workflow

**Files:**

- Create: `.github/workflows/deploy-production.yml`
- Modify: `tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs`

**Step 1: Add/confirm failing workflow assertions**

Require branch validation, checkout of the selected ref, .NET 10 setup, EF tool restore, Release build, idempotent migration artifact retained for 14 days, linux/amd64 Buildx push to GHCR, deployment by returned digest, pinned known hosts, TrackZ-specific concurrency, production Environment, bounded public readiness retry, and `always()` SSH cleanup.

**Step 2: Implement the workflow**

Mirror the proven SyToyV2 workflow shape but use:

- concurrency group `trackz-production`;
- `src/TrackZ.Api/TrackZ.Api.csproj` and `src/TrackZ.Infrastructure` for build/migrations;
- GHCR image name derived from the lower-case repository name;
- `sudo /usr/local/sbin/trackz-deploy '$image_ref'`;
- `${PRODUCTION_URL%/}/health/ready`.

Only consume GitHub Environment variables `VPS_HOST`, `VPS_PORT`, `VPS_USER`, `PRODUCTION_URL` and secrets `VPS_SSH_PRIVATE_KEY`, `VPS_SSH_KNOWN_HOSTS`. Never echo or transport TrackZ runtime secrets.

**Step 3: Validate workflow contract**

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter FullyQualifiedName~ProductionDeploymentContractTests
```

If `actionlint` is installed, also run:

```bash
actionlint .github/workflows/deploy-production.yml
```

Expected: workflow assertions pass.

**Step 4: Commit**

```bash
git add .github/workflows/deploy-production.yml tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs
git commit -m "ci: deploy TrackZ API by immutable digest"
```

---

## Task 7: Point Release mobile builds at the production domain

**Files:**

- Modify: `src/TrackZ.Mobile/Networking/MobileEndpointOrigins.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Networking/SimulatorApiConfigurationTests.cs`

**Step 1: Write a failing production-origin test**

Add a test calling `MobileEndpointOrigins.Resolve(_ => null)` and require both API and media origins to equal `https://api.trackz.sytoys.shop/`. Preserve the tests proving development defaults and explicit clean-origin overrides.

**Step 2: Run and confirm RED**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter FullyQualifiedName~SimulatorApiConfigurationTests
```

Expected: FAIL because current defaults are `api.trackz.app` and `media.trackz.app`.

**Step 3: Change only Release defaults**

Set both `DefaultApiOrigin` and `DefaultMediaOrigin` to `https://api.trackz.sytoys.shop/`. Do not change problem-type identifiers or test origins; those are not runtime endpoints. Keep `ResolveDevelopment` on localhost and continue accepting explicit environment overrides.

**Step 4: Run and confirm GREEN**

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --filter FullyQualifiedName~SimulatorApiConfigurationTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/TrackZ.Mobile/Networking/MobileEndpointOrigins.cs tests/TrackZ.Mobile.Tests/Networking/SimulatorApiConfigurationTests.cs
git commit -m "config: use TrackZ production API origin"
```

---

## Task 8: Add and verify the Caddy route without disturbing SyToyV2

**TrackZ files:**

- Create: `deploy/Caddyfile.trackz`
- Modify: `tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs`

**SyToyV2 files:**

- Modify: `/Users/mikeyoshino/gitRepos/SyToyV2/deploy/Caddyfile`
- Modify: `/Users/mikeyoshino/gitRepos/SyToyV2/tests/ToyStore.UnitTests/Architecture/DeploymentConfigurationTests.cs`

**Step 1: Write failing Caddy assertions in both repositories**

TrackZ's contract test requires an exact reviewed site block for `api.trackz.sytoys.shop` with compression, security headers, server-header removal, and `reverse_proxy trackz-api:8080`.

SyToyV2's architecture test requires the same block while continuing to require both existing Toy Store host blocks and their existing upstream. Assert TrackZ is not proxied to `web:8080` and the Toy Store is not proxied to `trackz-api:8080`.

**Step 2: Run both focused tests and confirm RED**

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter FullyQualifiedName~ProductionDeploymentContractTests
dotnet test /Users/mikeyoshino/gitRepos/SyToyV2/tests/ToyStore.UnitTests/ToyStore.UnitTests.csproj --filter FullyQualifiedName~DeploymentConfigurationTests
```

Expected: FAIL because the TrackZ site block is absent.

**Step 3: Add the reviewed Caddy block**

Create TrackZ's source fragment, then append the identical block to SyToyV2's `deploy/Caddyfile`. Do not alter the two existing Toy Store blocks, Compose file, services, or unrelated deleted/modified files in SyToyV2.

**Step 4: Validate syntax using the Caddy image**

Use a temporary combined Caddyfile with `TOYSTORE_DOMAIN=sytoys.shop` and run:

```bash
docker run --rm -e TOYSTORE_DOMAIN=sytoys.shop -v /Users/mikeyoshino/gitRepos/SyToyV2/deploy/Caddyfile:/etc/caddy/Caddyfile:ro caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile
```

Expected: `Valid configuration`.

**Step 5: Re-run both focused tests and confirm GREEN**

Run the two commands from Step 2. Expected: PASS.

**Step 6: Commit each repository separately**

In TrackZ:

```bash
git add deploy/Caddyfile.trackz tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs
git commit -m "ops: define TrackZ Caddy route"
```

In SyToyV2, stage only the two named files:

```bash
git -C /Users/mikeyoshino/gitRepos/SyToyV2 add deploy/Caddyfile tests/ToyStore.UnitTests/Architecture/DeploymentConfigurationTests.cs
git -C /Users/mikeyoshino/gitRepos/SyToyV2 commit -m "ops: route TrackZ API through Caddy"
```

---

## Task 9: Write the operator runbook and secret bootstrap instructions

**Files:**

- Create: `docs/DEPLOYMENT.md`
- Modify: `tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs`

**Step 1: Add failing documentation assertions**

Require explicit instructions for DNS, the GitHub `production` Environment, deploy SSH user/key, GHCR pull authentication, directory/script installation, sudoers validation, one-time bootstrap, Caddy copy/validation/reload, first deployment, health checks, log inspection, backups, restore rehearsal, rollback limits, and reboot verification.

**Step 2: Write exact first-time VPS setup**

Document commands that:

- point the `api.trackz.sytoys.shop` DNS record at the existing VPS;
- create an isolated SSH key for `trackz-deploy` and add only its public key to that account;
- install Compose and both scripts from reviewed repository revisions with root ownership;
- install `/etc/sudoers.d/trackz-deploy` containing only `trackz-deploy ALL=(root) NOPASSWD: /usr/local/sbin/trackz-deploy *`, mode `0440`, then validate with `visudo -cf`;
- authenticate the VPS to GHCR with a read-packages token supplied interactively, never committed;
- run `/usr/local/sbin/trackz-bootstrap` once and verify file ownership/modes without printing values;
- copy and validate the Caddy configuration, then reload only Caddy;
- configure the six approved GitHub Environment variables/secrets;
- dispatch the production workflow with a chosen branch.

Also document Stripe values as a later manual edit to `/etc/trackz/trackz.env`; the bootstrap does not create payment credentials.

**Step 3: Document operations and recovery**

Include exact commands for:

- public `/health/live` and `/health/ready`;
- TrackZ-only Compose status/logs;
- locating backup metadata, PostgreSQL dump, and object mirror;
- restoring into a disposable database/bucket first;
- manually selecting a prior immutable digest;
- rebooting and proving both SyToyV2 and TrackZ recover independently.

Clearly state that image rollback is automatic but database schema/object restoration is manual.

**Step 4: Run contract tests**

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter FullyQualifiedName~ProductionDeploymentContractTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add docs/DEPLOYMENT.md tests/TrackZ.Api.Tests/Deployment/ProductionDeploymentContractTests.cs
git commit -m "docs: add TrackZ production deployment runbook"
```

---

## Task 10: Full verification before any VPS mutation

**Files:** Verify all files changed in Tasks 1-9; do not make unrelated edits.

**Step 1: Review working-tree scope**

```bash
git status --short
git diff --check
git diff --stat 6ba8cde..HEAD
git -C /Users/mikeyoshino/gitRepos/SyToyV2 status --short
git -C /Users/mikeyoshino/gitRepos/SyToyV2 diff --check
```

Confirm pre-existing unrelated TrackZ changes remain preserved and SyToyV2's unrelated `index.html` deletion/change is neither staged nor committed by this work.

**Step 2: Restore/build the API**

```bash
dotnet tool restore
dotnet restore src/TrackZ.Api/TrackZ.Api.csproj
dotnet build src/TrackZ.Api/TrackZ.Api.csproj --configuration Release --no-restore
```

Expected: PASS with no new warnings attributable to this work.

**Step 3: Generate the migration artifact locally**

```bash
mkdir -p artifacts
dotnet ef migrations script --idempotent --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --configuration Release --no-build --output artifacts/trackz-production-migrate.sql
```

Expected: non-empty idempotent SQL artifact. `artifacts/` remains ignored.

**Step 4: Run all relevant TrackZ tests**

```bash
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj
```

Expected: PASS; Docker-dependent tests may report their existing explicit skip only when Docker is unavailable.

**Step 5: Validate deployment artifacts**

```bash
bash -n deploy/trackz-bootstrap
bash -n deploy/trackz-deploy
docker build --platform linux/amd64 -t trackz-api:deployment-test .
docker compose --env-file /tmp/trackz-compose-test.env -f deploy/compose.production.yaml config
docker run --rm -e TOYSTORE_DOMAIN=sytoys.shop -v /Users/mikeyoshino/gitRepos/SyToyV2/deploy/Caddyfile:/etc/caddy/Caddyfile:ro caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile
```

Expected: every command succeeds.

**Step 6: Run the focused SyToyV2 test**

```bash
dotnet test /Users/mikeyoshino/gitRepos/SyToyV2/tests/ToyStore.UnitTests/ToyStore.UnitTests.csproj --filter FullyQualifiedName~DeploymentConfigurationTests
```

Expected: PASS without changing or restarting any SyToyV2 service.

**Step 7: Review security-sensitive output**

Search tracked deployment files for accidental secret material and unsafe SSH/Docker exposure:

```bash
rg -n "StrictHostKeyChecking=no|BEGIN .*PRIVATE KEY|POSTGRES_PASSWORD=.*[^}]$|Jwt__SigningKey=.*[^}]$|9000:9000|5432:5432" .github deploy docs/DEPLOYMENT.md
```

Expected: no committed private key or literal runtime password. Any matches should be placeholders, variable references, forbidden-string tests, or explanatory documentation only and must be manually reviewed.

**Step 8: Stop before production mutation**

Report verification results and obtain explicit approval before copying files to the VPS, editing the live Caddyfile, reloading Caddy, creating DNS/GitHub secrets, or dispatching the first production deployment.
