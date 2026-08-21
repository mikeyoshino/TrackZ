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
