# Native iOS simulator walkthrough

Use this checklist for the TrackZ native iOS release candidate. The exercise catalog and artwork
must come from the local authenticated API. Repository image paths are deployment inputs only and
must never be rendered directly by the app.

## Prerequisites

- Xcode with an available iOS simulator runtime.
- Docker Desktop running only for PostgreSQL and MinIO during the walkthrough.
- A local TrackZ account whose access and refresh tokens are already stored in the simulator
  keychain. The current native shell consumes the existing identity/session flow; it does not
  contain a registration screen.
- Human-reviewed exercise artwork published through the normal review lifecycle. The checked-in
  catalog deployment intentionally creates Draft artwork, so Draft images must not be exposed by
  the API.

The simulator uses these development origins:

```text
TRACKZ_API_ORIGIN=http://127.0.0.1:5080
TRACKZ_MEDIA_ORIGIN=http://127.0.0.1:9000
```

The bearer token is sent only to the API origin. Artwork metadata comes from the API, then the app
requests a short-lived signed API media URL and downloads it with the separate credential-free
client into the bounded local cache.

## Start the local dependencies

From the repository root, start the two required Compose services:

```bash
docker compose up -d postgres minio minio-bootstrap
docker compose ps
```

For a new local database, deploy the catalog once. This applies migrations and uploads the source
assets to private object storage, but correctly leaves their review state as Draft:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project src/TrackZ.Api/TrackZ.Api.csproj --no-restore -- \
  deploy-exercise-catalog --manifest assets/exercises/catalog.json
```

After the required artwork has been reviewed and published, start the API in its own terminal:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project src/TrackZ.Api/TrackZ.Api.csproj --no-restore --launch-profile http
```

Confirm `http://127.0.0.1:5080` responds before launching the app. A protected endpoint returning
`401` is sufficient to prove the server is reachable.

## Build and launch

Build the simulator app without parallel workers:

```bash
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj \
  -f net10.0-ios -r iossimulator-arm64 --no-restore -m:1
```

When launching with `simctl`, pass the origins as child-process variables:

```bash
SIMCTL_CHILD_TRACKZ_API_ORIGIN=http://127.0.0.1:5080 \
SIMCTL_CHILD_TRACKZ_MEDIA_ORIGIN=http://127.0.0.1:9000 \
xcrun simctl launch booted com.trackz.app
```

## Functional walkthrough

1. Confirm the saved account session is active; if it expired, complete the existing sign-in
   bootstrap before continuing.
2. Open **Train**, choose **Shoulders**, and search `shoulder press`.
3. Verify each result keeps its own name and API artwork. A failed image shows the neutral
   placeholder and a per-item retry action; it must not hide the other exercises.
4. Verify LAST and PR are visible when history exists. No card may show a prescribed set target.
5. Select two exercises and start the workout. Add, remove, and reorder once, then relaunch and
   confirm the active workout and order survive.
6. Open Machine Shoulder Press, tap **+ Add set**, and confirm the inline editor scrolls into view.
   Use Match Last, edit weight/reps, and tap the sticky **Save set** action. Confirm the durable row
   appears under TODAY before the haptic or visual success feedback occurs. Open another draft,
   change its values, cancel, and confirm reopening restores the durable suggestion.
7. Stop the API, save another set offline, relaunch, and verify both the set and cached artwork are
   still present. Restart the API and verify the sync state resolves without duplicating the set.
8. Edit and delete a historical set through native actions. Exercise conflicts must present
   explicit Keep Server/Apply Local choices rather than only a color.
9. Finish the workout and verify Summary shows the real totals, PR reveal, XP, level, streak, and
   badge state derived from the authoritative snapshot.
10. Relaunch and verify History, Progress, sync state, and the bounded artwork cache remain correct.
11. Repeat the essential log-and-finish path with **Reduce Motion** enabled. No scale/count-up
    animation should run, and no state change may depend on animation completion.
12. Set iOS Dynamic Type to a large accessibility size and repeat Train → picker → logger. Text may
    wrap, but primary actions must remain reachable above the safe area and every action target must
    remain at least 44 points.

## Screenshot checklist

Capture the same states on the smallest supported iPhone and an available Pro Max simulator:

- Train Today with and without an active workout;
- Shoulders picker showing API artwork, LAST, PR, selected and failed-artwork states;
- active workout with two exercises and logged-set counts;
- inline set editor, TODAY rows, and saved/PR feedback;
- history list and detail;
- workout summary and Progress;
- You settings at a large Dynamic Type size.

Record device/runtime names and screenshot paths in the verification notes. Do not claim the
walkthrough complete when the account session or published API artwork prerequisite is absent.

## Shutdown

Stop only the processes started for this walkthrough. Keep database and object-storage volumes:

```bash
docker compose stop postgres minio minio-bootstrap
xcrun simctl shutdown booted
```

Stop the exact API process from its terminal with `Ctrl+C`. Confirm no TrackZ API, testhost,
simulator app, PostgreSQL, MinIO, Java, or `aapt2` process remains from the walkthrough. Do not use
`docker compose down -v` unless local test data was explicitly approved for deletion.

## Verification record

- Date/device/runtime: 2026-08-21, iPhone 17e, iOS 26.5
- Build: `iossimulator-arm64` passed with 0 warnings and 0 errors
- Launch smoke: passed; native four-tab shell and Train Today rendered
- Small iPhone screenshots: `/private/tmp/trackz-iphone17e-final.png` (local verification artifact)
- Pro Max screenshots: pending
- API artwork result: pending human-published artwork and saved simulator session
- Offline/relaunch result: pending simulator execution
- Reduce Motion/Dynamic Type result: pending simulator execution
- Android: compile was not claimed; XA5300 reports that no Android SDK directory is installed
