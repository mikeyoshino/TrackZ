# Task 5 — Momentum Home production composition and acceptance contract

## Delivered contract

- `TrainTodayViewModel` is registered as an explicit production singleton, matching the existing
  singleton `TrainPage` and the account-scoped collaborators it owns.
- `ITrainNavigator` remains the production singleton `MauiTrainNavigator`; progress source,
  weight preference, active-workout coordinator, and account boundary keep their singleton
  composition identities.
- A real temp-data `MauiApp` resolves the production `TrainPage` and verifies that three real
  page appear/disappear cycles retain one page and one view model, load a real SQLite active
  workout through `LocalTrainDashboardSource` plus an actual `ProgressSnapshotCache` through
  `ProgressSnapshotSource`, and do not add page-cycle boundary, connectivity, or unit
  subscriptions.
- Acceptance and accessibility guards verify the approved named Home hierarchy, one primary
  action, action-specific semantic bindings, English/Thai semantic differences, shared 44-point
  targets, and the absence of technical offline/device-save or recommendation language in Home
  copy.

## Persistent HTML comparison

`docs/design/momentum-home-reference.html` was opened before implementation and again before this
report. Native assertions preserve its content order exactly:

`context → hero / one state-aware action → motivation → Train again → Recent momentum`.

The checks cover the reference's Ready/Active one-primary rule (`HeroActionButton`), named metric
strip and Ready-only repeat/card hierarchy, EN/TH semantic bindings, and the reference's quiet
offline rule. Production remains native MAUI XAML; no HTML runtime or web layout was introduced.

## RED → GREEN evidence

### RED

The new real-provider lifetime test was added first. With the prior transient registration it
failed as intended:

```text
Assert.Same() Failure: Values are not the same instance
Expected: TrainTodayViewModel { ActiveWorkout = ... }
Actual:   TrainTodayViewModel { ActiveWorkout = null, ... }
```

The failure proves that resolving `TrainTodayViewModel` after the singleton `TrainPage` produced a
second instance, losing the page's loaded state. The test also uses the real protected
`TrainPage.OnAppearing`/`OnDisappearing` lifecycle through reflection rather than calling a
view-model-only surrogate.

### Fix Round 1 RED

The composition test was then hardened to require production local/progress source types. It
failed before the real-source host setup existed:

```text
Assert.IsType() Failure: Value is not the exact type
Expected: typeof(TrackZ.Mobile.Features.Train.LocalTrainDashboardSource)
Actual:   typeof(MauiCompositionTests+FixedTrainDashboardSource)
```

The dedicated Home test now writes a real `ProgressSnapshotCache`, caches a real exercise
definition, creates an active workout, and saves a set through `ActiveWorkoutCoordinator` over a
real `LocalWorkoutRepository`/`TrackZLocalDatabase`. After the shared auth helper runs, the test
removes those helper substitutions and registers only concrete `LocalTrainDashboardSource` and
`ProgressSnapshotSource` mappings over those seeded stores. It asserts interface-to-concrete
identities where possible. Platform/external API, thumbnail, connectivity, auth, and sync-trigger
edges alone are replaced for the portable host.

The same test now requires exactly one connectivity subscription immediately after the singleton
view model resolves, then requires that value to remain exactly one through three completed native
page appearance/disappearance cycles. The exact baseline assertion fails if the Home connectivity
subscription is removed; the later equality fails if page cycles add another one.

### GREEN

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~MauiCompositionTests|FullyQualifiedName~NativeIosExperienceAcceptanceTests" \
  --verbosity minimal -m:1
Passed: 15, Failed: 0, Skipped: 0

dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~Architecture|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~MomentumHomePresentationTests" \
  --verbosity minimal -m:1
Passed: 22, Failed: 0, Skipped: 0

git diff --check
Exit code: 0
```

The sandbox initially denied the test runner's local socket bind; the same commands were rerun in
the approved host context. No API, Docker, container, simulator, or application service was
started.

## Reference-host adaptation

Existing app-lifecycle composition tests bootstrap the real `AppShell`. After Home became a
singleton, that bootstrap correctly resolves Home earlier and the portable MAUI reference assembly
cannot provide `FileSystem.AppDataDirectory`. The shared test-only `ConfigureAuthenticatedServices`
therefore replaces only Home's local dashboard/progress edges with deterministic in-memory sources.
That adaptation remains limited to those pre-existing app-lifecycle tests. The dedicated Momentum
Home provider test explicitly removes it and restores production concrete local SQLite/cache source
types before resolving `TrainPage`. Production registrations remain real local SQLite/cache
implementations. This preserves the tests' sync/auth purposes while allowing their real app
provider to compose on the MAUI reference host.

## Mutation coverage

- Changing `TrainTodayViewModel` back to transient makes the page/view-model identity assertion
  fail.
- Replacing either dedicated Home source with an in-memory fake fails the exact concrete source
  assertions; removing the seeded cached progress or active local set fails literal loaded facts.
- Adding a subscription during later page lifecycle cycles changes the counted subscription
  baseline and fails the composition test; removing the one Home subscription fails the exact-one
  baseline.
- Removing or renaming an approved Home element, adding another primary-style button, or adding
  technical/recommendation copy fails acceptance.
- Removing any Hero, repeat, or recent semantic binding, losing Thai localization, or dropping the
  shared target contract fails accessibility coverage.
