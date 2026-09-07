# TrackZ Production Deployment

TrackZ runs on the same Ubuntu VPS as SyToyV2 but owns a separate Compose project, database, object storage, credentials, volumes, lock, backup directory, and deployment command. The only shared component is SyToyV2's Caddy frontend network.

Production URL: `https://api.trackz.sytoys.shop/`

## What GitHub stores

The GitHub `production` Environment stores only SSH transport settings:

| Kind | Name | Value |
|---|---|---|
| Variable | `VPS_HOST` | VPS hostname or IP |
| Variable | `VPS_PORT` | SSH port, normally `22` |
| Variable | `VPS_USER` | `trackz-deploy` |
| Variable | `PRODUCTION_URL` | `https://api.trackz.sytoys.shop` |
| Secret | `VPS_SSH_PRIVATE_KEY` | Dedicated TrackZ deploy private key |
| Secret | `VPS_SSH_KNOWN_HOSTS` | Verified host-key line for this VPS and port |

Database, JWT, media-signing, MinIO, and future Stripe secrets remain only under `/etc/trackz` on the VPS. `trackz-bootstrap` generates them once and never prints their values.

## 1. Create DNS

Create an `A` record (and `AAAA` when the VPS has working IPv6) for `api.trackz.sytoys.shop` pointing to the existing VPS. Confirm from an independent resolver before requesting TLS:

```bash
dig +short api.trackz.sytoys.shop A
dig +short api.trackz.sytoys.shop AAAA
```

## 2. Install reviewed TrackZ files on the VPS

Run these steps from a trusted checkout of the exact TrackZ revision to deploy. Upload into a narrow temporary directory first:

```bash
scp -P <ssh-port> deploy/compose.production.yaml deploy/trackz-bootstrap deploy/trackz-deploy <admin>@<vps>:/tmp/
ssh -p <ssh-port> <admin>@<vps>
```

On the VPS:

```bash
sudo install -d -o root -g root -m 0755 /opt/trackz
sudo install -o root -g root -m 0644 /tmp/compose.production.yaml /opt/trackz/compose.production.yaml
sudo install -o root -g root -m 0755 /tmp/trackz-bootstrap /usr/local/sbin/trackz-bootstrap
sudo install -o root -g root -m 0755 /tmp/trackz-deploy /usr/local/sbin/trackz-deploy
sudo /usr/local/sbin/trackz-bootstrap
```

The bootstrap command must find the existing `toystore-production_frontend` Docker network. It creates the `trackz` group, `trackz-deploy` account, runtime directories, and four environment files. Running it again leaves existing secrets unchanged.

Verify metadata without displaying secret contents:

```bash
sudo stat -c '%U:%G %a %n' /etc/trackz/postgres.env /etc/trackz/minio.env /etc/trackz/trackz.env /etc/trackz/compose.env
sudo docker compose --env-file /etc/trackz/compose.env -f /opt/trackz/compose.production.yaml config --quiet
```

Expected permissions:

- `root:root 600` for `postgres.env`, `minio.env`, and `trackz.env`;
- `root:trackz 640` for `compose.env`.

`compose.env` has no image on the first setup, so validate it with a temporary shell-only image value if Compose reports the required image is empty:

```bash
sudo env TRACKZ_IMAGE="ghcr.io/bootstrap/validation@sha256:$(printf '0%.0s' {1..64})" \
  docker compose --env-file /etc/trackz/compose.env -f /opt/trackz/compose.production.yaml config --quiet
```

## 3. Install the restricted deployment SSH account

Create a dedicated key on the operator machine, outside the repository:

```bash
trackz_key_dir="$(mktemp -d)"
ssh-keygen -t ed25519 -a 100 -f "$trackz_key_dir/id_ed25519" -C trackz-production-deploy
```

Install only its public key for `trackz-deploy` on the VPS:

```bash
sudo install -d -o trackz-deploy -g trackz -m 0700 /home/trackz-deploy/.ssh
sudo install -o trackz-deploy -g trackz -m 0600 /dev/null /home/trackz-deploy/.ssh/authorized_keys
sudoedit /home/trackz-deploy/.ssh/authorized_keys
```

Paste the single public-key line from `id_ed25519.pub`.

Create `/etc/sudoers.d/trackz-deploy` with exactly:

```text
trackz-deploy ALL=(root) NOPASSWD: /usr/local/sbin/trackz-deploy *
```

Then secure and validate it:

```bash
sudo chown root:root /etc/sudoers.d/trackz-deploy
sudo chmod 0440 /etc/sudoers.d/trackz-deploy
sudo visudo -cf /etc/sudoers.d/trackz-deploy
```

Do not add `trackz-deploy` to the `docker` or `sudo` groups.

## 4. Allow the VPS to pull the private GHCR image

If the GitHub package is private, create a classic GitHub token with only `read:packages`. Enter it interactively on the VPS so it is not stored in shell history:

```bash
read -rsp 'GHCR read token: ' trackz_ghcr_token
printf '%s' "$trackz_ghcr_token" | sudo docker login ghcr.io -u <github-user> --password-stdin
unset trackz_ghcr_token
```

Public packages do not require this login.

## 5. Add the TrackZ route to the existing Caddy

Use the committed SyToyV2 `deploy/Caddyfile`, which retains both Toy Store blocks and adds:

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

Upload the reviewed full SyToyV2 Caddyfile to `/tmp/Caddyfile.trackz-next`. On the VPS, validate before replacement and reload only Caddy:

```bash
sudo docker run --rm -e TOYSTORE_DOMAIN=sytoys.shop \
  -v /tmp/Caddyfile.trackz-next:/etc/caddy/Caddyfile:ro \
  caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile
sudo install -o root -g root -m 0644 /tmp/Caddyfile.trackz-next /opt/toystore/Caddyfile
sudo docker compose -f /opt/toystore/compose.production.yaml exec -T caddy \
  caddy reload --config /etc/caddy/Caddyfile
```

This does not restart the Toy Store web or PostgreSQL services. Before the first TrackZ deployment, the new host can temporarily return `502` because `trackz-api` does not exist yet.

## 6. Configure the GitHub production Environment

In GitHub, open **Settings → Environments → New environment**, create `production`, and add the variables/secrets listed at the top of this document.

Generate the known-host line from a trusted network, then compare its fingerprint with the VPS host key shown through the VPS provider console:

```bash
ssh-keyscan -p <ssh-port> -H <vps-host> > "$trackz_key_dir/known_hosts"
ssh-keygen -lf "$trackz_key_dir/known_hosts"
```

Set:

- `VPS_SSH_PRIVATE_KEY` to the complete contents of `$trackz_key_dir/id_ed25519`;
- `VPS_SSH_KNOWN_HOSTS` to the verified complete known-host line.

After GitHub is configured, securely remove the local temporary key directory using the operating system's approved secret-file cleanup method.

## 7. Deploy a selected branch

Open **Actions → Deploy TrackZ production → Run workflow**, enter the branch, and start the run. The workflow:

1. builds the API in Release;
2. retains an idempotent migration SQL artifact for 14 days;
3. pushes a `linux/amd64` GHCR image;
4. deploys the exact registry digest;
5. waits for TrackZ's private readiness check;
6. checks the public Caddy/TLS path.

No production application secret passes through GitHub Actions.

## Health and diagnostics

Public checks:

```bash
curl --fail --show-error https://api.trackz.sytoys.shop/health/live
curl --fail --show-error https://api.trackz.sytoys.shop/health/ready
```

TrackZ-only service status and logs on the VPS:

```bash
sudo docker compose --env-file /etc/trackz/compose.env -f /opt/trackz/compose.production.yaml ps
sudo docker compose --env-file /etc/trackz/compose.env -f /opt/trackz/compose.production.yaml logs --tail 200 api catalog-deploy minio-bootstrap postgres minio
```

`live` proves the process can serve HTTP. `ready` also verifies PostgreSQL and authenticated access to the private MinIO health marker. Neither endpoint returns credentials or dependency details.

## Backups and rollback

Before every image replacement, `trackz-deploy` stops TrackZ API writes and creates:

```text
/var/backups/trackz/<UTC timestamp>-<digest prefix>/
  database.dump
  objects/
  release.env
```

List backup metadata without printing runtime secrets:

```bash
sudo find /var/backups/trackz -mindepth 1 -maxdepth 2 -type f -printf '%TY-%Tm-%Td %TH:%TM %p\n' | sort
```

If the new API fails readiness, the deployment command automatically restores the previous image digest and retains the backup. It deliberately does not reverse database migrations or overwrite objects.

To validate a database backup, restore only into a disposable database first:

```bash
sudo docker compose --env-file /etc/trackz/compose.env -f /opt/trackz/compose.production.yaml exec -T postgres \
  sh -eu -c 'createdb --username="$POSTGRES_USER" trackz_restore_check'
sudo docker compose --env-file /etc/trackz/compose.env -f /opt/trackz/compose.production.yaml exec -T postgres \
  sh -eu -c 'pg_restore --exit-on-error --no-owner --username="$POSTGRES_USER" --dbname=trackz_restore_check' \
  < /var/backups/trackz/<backup-id>/database.dump
```

Validate objects by mirroring them into a disposable bucket, never over the live bucket. Use the MinIO root credentials interactively or from the root-readable `/etc/trackz/minio.env` within a root-only shell. Delete the disposable database/bucket only after the rehearsal result is recorded and the exact disposable name is rechecked.

To redeploy a known prior application version without changing data manually:

```bash
sudo /usr/local/sbin/trackz-deploy 'ghcr.io/<owner>/<repository>@sha256:<64-lowercase-hex>'
```

Database schema downgrade or live object restoration requires a separate reviewed recovery plan because both can destroy newer customer data.

## Stripe configuration later

The bootstrap writes empty Stripe entries. When the server-side Stripe flow is ready, edit `/etc/trackz/trackz.env` as root and set the real Stripe secret and monthly Price ID. Never put them in GitHub Actions, the mobile app, Compose YAML, or this repository. Restart only the TrackZ API after validation.

## Reboot verification

After the first successful release and a scheduled maintenance window, reboot the VPS and verify both applications independently:

```bash
curl --fail --show-error https://sytoys.shop/health/ready
curl --fail --show-error https://api.trackz.sytoys.shop/health/ready
```

On the VPS, confirm the `toystore-production` and `trackz-production` Compose projects are healthy and that neither project's database, volume, lock, or deployment command is shared with the other.
