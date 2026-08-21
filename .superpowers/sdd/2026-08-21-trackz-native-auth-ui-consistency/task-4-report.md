# Task 4 — Semantic Native Performance Resource System

## RED → GREEN evidence

RED command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests" --verbosity minimal -m:1
Failed! - Failed: 13, Passed: 0, Skipped: 0, Total: 13
```

The failure reasons were the absent semantic spacing/geometry keys and secondary control style, plus the temporary state action and skeleton XAML not using their semantic resources.

GREEN focused command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests" --verbosity minimal -m:1
Passed! - Failed: 0, Passed: 20, Skipped: 0, Total: 20
```

GREEN component command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~ExercisePickerViewModelTests" --verbosity minimal -m:1
Passed! - Failed: 0, Passed: 70, Skipped: 0, Total: 70
```

iOS XAML compile:

```text
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -verbosity:minimal
exit 0
```

## Semantic resources

Spacing: `TrackZSpace4/8/12/16/24/32 = 4/8/12/16/24/32`; page margin `18`; card radius `16`; primary action `52`; minimum target `44`; field height `50`.

Typography: page `32 bold`; navigation `17 bold` (the MAUI system font's bold face is used for the specified semibold treatment); section `20 bold`; body `15`; metadata `13`; field error `12`.

Controls include primary, secondary, destructive, quiet, field/container, card/list row, selection chip, sticky action, inline error, skeleton, and search-bar styles. Sync state surfaces are named color resources, not component code literals.

## Component and accessibility review

- `TrackZStateView` retains bindable title/message/action text/command; its action is a 44-point secondary button and appears only when both text and command exist.
- `ExerciseListSkeleton` is three fixed card silhouettes with reduced-opacity `TrackZSkeletonStyle`; it has no animation and therefore requires no Reduce Motion branch.
- Exercise cards, active rows, sync status, and steppers consume shared styles. Stepper increment/decrement controls retain their distinct localized bindings and get 44-point targets from the secondary style.
- Static checks reject hex colors and disabled Dynamic Type in audited XAML, reject direct lime card labels, require semantic skeleton/state styles, and validate card and control geometry through real merged resources.

## Self-review and concerns

`git diff --check` passed. The iOS compile and focused suites above passed. No workout/domain behavior changed. The design says “semibold”; MAUI's built-in `FontAttributes` exposes Bold rather than a separate semibold enum, so the native system bold face is used for the 17/20 semantic heading styles. No other concerns.

## Fix Round 1/5 — Important review findings

RED command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~ExercisePickerViewModelTests" --verbosity minimal -m:1
Failed! - Failed: 3, Passed: 70, Skipped: 0, Total: 73
```

The failures proved the off-scale `3`/`2` card stack spacing, lime `TrackZPerformanceMetadataStyle`, and absent `TrackZDisabledText`/`TrackZDisabledSurface` resources.

GREEN command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~ExercisePickerViewModelTests" --verbosity minimal -m:1
Passed! - Failed: 0, Passed: 73, Skipped: 0, Total: 73
```

iOS XAML compile:

```text
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -verbosity:minimal
exit 0
```

Fixes: all audited component `Spacing`/`Padding` values now resolve to the 4/8/12/16/24/32 scale; the status pill uses `12,4`. Performance cards, active rows, and skeletons share the exact `88` artwork size and an `88` XAML artwork column. Both themes of implicit Button, Entry, and SearchBar disabled states use semantic `TrackZDisabledText` and `TrackZDisabledSurface`; no legacy gray binding remains in those states. The mutation-sensitive test gate resolves merged style setters and state bindings, validates typography, 44-point button targets, card radii, spacing axes, artwork resource usage, and lime role restrictions. `TrackZSelectionChipStyle` now also carries a 44-point minimum target.

## Fix Round 2/5 — remaining partial findings

RED command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~ExercisePickerViewModelTests" --verbosity minimal -m:1
Failed! - Failed: 3, Passed: 71, Skipped: 0, Total: 74
```

The failures proved the primary button's off-scale `18,12` padding, missing explicit `None` attributes for body/metadata/error typography, and the stale 104-point exercise row height.

GREEN command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~ExercisePickerViewModelTests" --verbosity minimal -m:1
Passed! - Failed: 0, Passed: 74, Skipped: 0, Total: 74
```

iOS XAML compile:

```text
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -verbosity:minimal
exit 0
```

Fixes: primary and sticky-action padding is `16,12`; semantic style and audited component spacing/padding axes are statically constrained to the native scale (with zero only as an omitted axis). `TrackZExerciseCardHeight` is `112`, which exactly accommodates 88-point artwork plus shared 12-point list-row padding above and below. Skeleton and both loaded exercise card components use `TrackZListRowStyle`, the same 88-point column/artwork resource, and the same 112-point minimum outer geometry. Typography tests now require explicit `Bold` for page/navigation/section and explicit `None` for body/metadata/error. The gate also rejects local shape overrides on outer shared cards and direct lime label copy across every audited component, except the explicit performance-number role.

## Fix Round 3/5 — spacing audit and safe-area exception

RED command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~ExercisePickerViewModelTests" --verbosity minimal -m:1
Failed! - Failed: 5, Passed: 72, Skipped: 0, Total: 77
```

The semantic-style audit found the literal zero axis in `TrackZFieldContainerStyle`; the sticky merged-resource assertion also proved its incorrect 16-point horizontal padding. The three mutation rows intentionally fail for `ColumnSpacing=3`, root `Padding=3`, and `Spacing=0`.

GREEN command:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativePresentationCompositionTests|FullyQualifiedName~ExercisePickerViewModelTests" --verbosity minimal -m:1
Passed! - Failed: 0, Passed: 77, Skipped: 0, Total: 77
```

iOS XAML compile:

```text
dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -verbosity:minimal
exit 0
```

Fixes: a single audit helper now inspects every XAML root and descendant, direct `Spacing`/`RowSpacing`/`ColumnSpacing`/`Padding` attributes, and semantic style setter values. It rejects zero and off-scale axes everywhere except the named `TrackZStickyActionContainerStyle` page-edge exception. That style is explicitly `18,12` and merged-resource-tested; ordinary primary button padding remains `16,12`. `TrackZFieldContainerStyle` uses the uniform `TrackZSpace12` resource instead of a literal zero vertical axis. Existing 88+12+12=112 geometry, disabled resources, typography attributes, and lime-role gates remain green.
