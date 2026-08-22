# Task 4 — Native Momentum Home Visual Hierarchy and Localization

## Delivered contract

- `TrainPage` is now a native MAUI Momentum Home: direct page-padding scroll content,
  localized context/headline, one state-aware hero, one primary button instance, an equal-height
  three-card motivation strip, Ready-only Train again, and Recent momentum in the approved order.
- The Ready hero uses the approved lime emphasis with an inverted dark action. Active keeps the
  same geometry and button instance, switches to a dark surface/lime action, shows exact exercise
  progress, and hides Train again.
- All Home copy is typed and localized in English and Thai. Existing `StartWorkout`, `TryAgain`,
  and Task 2 `Last`/`Best`/`Assistance` labels are reused instead of duplicated.
- The presentation-only derived copy lives in `TrainTodayViewModel`, not converters or XAML
  literals. It covers the headline, hero copy, metrics, repeat metadata/accessibility, combined
  recent Last/Best, and `HomeLoadFailed`; source changes and account reset raise the required
  notifications.
- `HomeContextText` uses `HomeContextFormat` and the reference-compatible ISO week from the
  existing injected `IClock`, converted through an injected device-local `TimeZoneInfo`.
  Production composition registers/passes `TimeZoneInfo.Local`, matching the timezone ID sent by
  progress preferences; reload notification supports a week rollover without making Home depend
  directly on untestable host time or timezone state.
- Artwork retains the app's native 88-point semantic geometry, every action is at least 44 points,
  and the page uses shared semantic colors, typography, spacing, card, and button resources. No
  WebView or HTML runtime was introduced.

## Persistent HTML comparison

`docs/design/momentum-home-reference.html` was opened before tests, before XAML edits, and again
before this report. The native mapping was checked against every reference toggle:

| Reference toggle | Native result and evidence |
| --- | --- |
| Ready / EN | Exact `Today · Week 34`, Ready headline, Start-training hero copy, and Start workout action are asserted from a real inflated `TrainPage`. Ready uses the reference lime hero/dark action emphasis. |
| Active / EN | The same `HeroActionButton` changes to Continue workout; body parts, `3 of 5 exercises logged · 8 sets`, 60% progress, and Ready-only repeat visibility are literal/state tested. Active uses the reference dark hero/lime action emphasis. |
| kg / EN | Recent momentum is `Last 70.125 kg × 8 · Best 72.5 kg × 6`; changing the shared preference updates the existing row to `Last 154.60 lb × 8 · Best 159.84 lb × 6`. |
| kg / TH | Typed Thai copy is literal-tested, including `วันนี้ · สัปดาห์ที่ 34`, hero/metric/repeat text, and `ล่าสุด 70.125 กก. × 8 · สูงสุด 72.5 กก. × 6`. Existing canonical app body-part localization is retained. |
| lb / TH | The same shared unit preference produces `ล่าสุด 154.60 ปอนด์ × 8 · สูงสุด 159.84 ปอนด์ × 6`, with no Home-only unit state. |

The HTML remains the persistent visual source of truth while production stays native XAML. Its
compact web artwork dimensions map to the existing native `TrackZExerciseArtworkSize` and reserved
column contract, as required by the Task 4 brief.

## RED evidence

1. The initial resource contract failed compilation with 17 missing `WorkoutTextSet` properties.
2. The presentation contract then failed compilation for missing derived Home properties; after
   those existed, the real page test failed because `HeroActionButton` was absent.
3. A Home load failure test exposed the generic workout-details error instead of the Home error.
4. The fixed-clock test failed compilation until `HomeContextFormat`, `HomeContextText`, and the
   injected clock path existed. The inflated-page test then failed because `HomeContextLabel` was
   absent:

   ```text
   Assert.IsType() Failure: expected Label; actual null
   ```

5. The reference-emphasis mutation audit failed against the prior raised-surface Ready hero. An
   attempted targeted visual-state setter also exposed the MAUI SourceGen boundary:

   ```text
   MAUIG1001: An error occured while parsing Xaml: The method or operation is not implemented.
   ```

   Supported native `DataTrigger` setters now invert the existing single shared button and Ready
   labels, while hero state setters preserve the approved colors; the audit remains
   mutation-sensitive.
6. Exact Thai Last/Best comparison failed on the word separator and now uses the reference middle
   dot. The EN/TH pounds test first failed against the test fake's immutable unit preference; the
   corrected event-producing fake proves the production `HomeMomentumItem` update path.

The first real `MauiApp` page inflation also encountered the known reference-host platform boundary
at `FileSystem.AppDataDirectory`, followed by platform connectivity/media dependencies. The test
still resolves and inflates the real MAUI page: it replaces only those platform services with
temporary SQLite/cache paths, offline connectivity, memory preferences, and a null thumbnail cache.
Assertions were not weakened and no service, API, Docker container, or simulator was started.

## GREEN evidence

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~MomentumHomePresentationTests|FullyQualifiedName~AppWideVisualConsistencyTests" \
  --verbosity minimal -m:1
Passed: 50, Failed: 0, Skipped: 0

dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TrainTodayViewModelTests|FullyQualifiedName~TrainAgainWorkoutTests|FullyQualifiedName~HomeMomentumItemTests|FullyQualifiedName~NativeVisualTokenTests" \
  --verbosity minimal -m:1
Passed: 58, Failed: 0, Skipped: 0

dotnet msbuild src/TrackZ.Mobile/TrackZ.Mobile.csproj -t:Compile \
  -p:TargetFramework=net10.0-ios -p:BuildProjectReferences=false -m:1 -v:minimal
Exit code: 0; no product warning or error
```

## Mutation-sensitive coverage

- Duplicating the primary, moving it outside `HomeHero`, hiding it, or setting opacity to zero
  fails the structural audit.
- Changing Ready/Active hero emphasis or removing the single-button Ready inversion fails the
  reference-state audit.
- Moving page padding away from the direct content band fails the padding audit.
- Changing shared metric height fails the equal-card audit.
- Replacing artwork size/column semantic tokens or the shared 12-point gap fails geometry audit.
- Moving Train again above the hero fails the approved-order audit.
- Removing presentation notifications fails the load/reset test; replacing injected time with a
  fixed/system-only value fails ISO-week rollover literals.
- Breaking the shared weight event path fails exact EN/TH kg-to-lb recent-momentum literals.

## Review Round 1

Both verified reviewer findings were fixed with separate RED/GREEN cycles:

1. A custom `Asia/Bangkok` test fixes the clock at Sunday 2026-08-23 17:30 UTC, verifies UTC is
   ISO week 34, then requires Home to show literal `Today · Week 35` because device-local time is
   Monday 00:30. RED was the missing `localTimeZone` constructor contract; production now converts
   with `TimeZoneInfo.ConvertTimeFromUtc` before `ISOWeek.GetWeekOfYear`. Existing clock rollover
   tests explicitly use UTC and do not depend on the test host's local timezone.
2. The inflated page and structural audit initially failed because `TrainAgainFallback` and the
   required decorative exclusions did not exist. Train again now layers the shared
   `exercise_placeholder.png` below the optional real thumbnail within the existing 88-point
   artwork frame. The full Train again artwork, chevron, Recent momentum artwork, and Recent
   momentum glyph are excluded from accessibility, while each containing card retains exactly one
   authoritative semantic description. Mutating the fallback source, removing a decorative
   exclusion, or adding a duplicate inner description fails the audit.

Fresh Round 1 verification is the 49/49 focused, 58/58 related, and exit-0 iOS Compile evidence
recorded above. No service, API, Docker container, or simulator was started.

## Review Round 2

The structural artwork audit was hardened without changing product source. The previous audit found
the two named images anywhere in the document, so it could not prove they were sibling layers or
that the fallback rendered first. A deliberate three-row mutation test observed RED for all cases:

```text
Assert.All() Failure: 3 out of 3 items did not pass
swap fallback/thumbnail: audit collection was empty
move fallback outside artwork Grid: audit collection was empty
move thumbnail outside artwork Grid: audit collection was empty
```

`AuditMomentumArtworkSemantics` now requires exactly one `TrainAgainArtwork`, one immediate child
`Grid`, exactly two direct `Image` layers, and object identity/order of
`TrainAgainFallback` followed by `TrainAgainThumbnail`. Source and binding are then verified on
those same direct children. The three mutation rows now fail the audit as intended, while the real
XAML remains unchanged. Fresh verification: focused Task 4 50/50 and iOS XAML Compile exit 0; the
related source regression suite was not rerun because Round 2 changed only test/report files. No
service, API, Docker container, or simulator was started.

## Files changed

- `src/TrackZ.Mobile/Features/Train/TrainPage.xaml`
- `src/TrackZ.Mobile/Resources/Styles/TrackZControls.xaml`
- `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx`
- `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx`
- `tests/TrackZ.Mobile.Tests/NativeIos/MomentumHomePresentationTests.cs`
- `tests/TrackZ.Mobile.Tests/NativeIos/AppWideVisualConsistencyTests.cs`
- `src/TrackZ.Mobile.Core/Features/Train/TrainTodayViewModel.cs`
- `tests/TrackZ.Mobile.Tests/Train/TrainTodayViewModelTests.cs`
- `src/TrackZ.Mobile/MauiProgram.cs`

The last three files are the controller-approved minimal Task 4 expansion for declarative dynamic
presentation copy and the production `IClock` composition path. Task 5 retains provider/composition
acceptance ownership.
