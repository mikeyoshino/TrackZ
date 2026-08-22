# Today Workout Set Counter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace each Today Workout row's metadata and previous-performance line with one prominent, localized count of sets actually logged.

**Architecture:** Keep the existing `WorkoutExerciseDraftItem` data shape and row interactions, but give row-specific count copy and its “open set logger” accessibility action their own localization keys while removing previous-performance data from the semantic summary. Render the count in a named trailing counter while retaining the existing artwork, exercise name, chevron, swipe actions, and 112-point card geometry.

**Tech Stack:** .NET 10, C# 14, .NET MAUI XAML, RESX localization, xUnit, XML/HTML contract tests

**Spec:** `docs/superpowers/specs/2026-08-23-hypertrophy-load-guidance-design.md`

## Global Constraints

- The row shows only exercise artwork or placeholder, exercise name, actual logged-set count, and navigation chevron.
- The row must not render tracking mode, previous performance, previous weight, the separator dot, or “sets logged” prose.
- Zero is explicit as `0 sets` in English and `0 เซ็ต` in Thai; never show a planned total such as “0 of 3.”
- Keep tap-to-open, swipe-to-move, swipe-to-remove, existing artwork sizing, and `TrackZExerciseCardHeight = 112` unchanged.
- The row's single accessibility description contains the exercise name, localized set count, and localized “open set logger” action, with none of the removed metadata.
- All new user-facing copy belongs in both `WorkoutStrings.resx` and `WorkoutStrings.th.resx`; XAML contains no literal localized copy.
- Do not modify Set Logger or guidance behavior in this independently shippable plan.
- Use TDD and commit after every task; do not combine this work with unrelated workspace changes.

---

## File Structure

**Modify:**

- `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx` — English row-count and accessibility-action formats.
- `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx` — Thai row-count and accessibility-action formats.
- `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs` — expose singular/plural set-count formats through `WorkoutTextSet`.
- `src/TrackZ.Mobile.Core/Features/Workout/WorkoutViewModel.cs` — create terse row count and accessibility copy while leaving the workout-level context unchanged.
- `src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.xaml` — render the prominent trailing counter and remove metadata visuals.
- `docs/design/todays-workout-reference.html` — maintained visual reference for the approved hierarchy.
- `tests/TrackZ.Mobile.Tests/Workout/WorkoutViewModelTests.cs` — localized row-copy and semantic-summary behavior.
- `tests/TrackZ.Mobile.Tests/NativeIos/ActiveWorkoutExerciseRowTests.cs` — presentation-model contract.
- `tests/TrackZ.Mobile.Tests/NativeIos/TodayWorkoutVisualContractTests.cs` — exact XAML and persistent HTML hierarchy.
- `tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs` — product-level no-target/no-previous-performance contract.

No new runtime files are needed. `TrackingModeText` and `LastText` remain on `WorkoutExerciseDraftItem` because other construction and refresh paths already use the record; this plan removes them from this row's visual and semantic output rather than performing an unrelated model migration.

### Task 1: Localized terse set count and accessibility summary

**Files:**

- Modify: `tests/TrackZ.Mobile.Tests/Workout/WorkoutViewModelTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/ActiveWorkoutExerciseRowTests.cs`
- Modify: `tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx`
- Modify: `src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs`
- Modify: `src/TrackZ.Mobile.Core/Features/Workout/WorkoutViewModel.cs`

**Interfaces:**

- Consumes: existing `WorkoutTextSet`, `WorkoutExerciseDraftItem`, `WorkoutViewModel.RestoreAsync()`.
- Produces: `WorkoutTextSet.SetCountSingularFormat`, `SetCountPluralFormat`, and `OpenSetLoggerAccessibilityFormat`; `WorkoutExerciseDraftItem.LoggedSetText` values such as `1 set`, `2 sets`, and `2 เซ็ต`; `AccessibilitySummary` containing name, count, and the localized open action.

- [ ] **Step 1: Tighten the failing view-model test**

Replace the final assertions in `Restored_active_workout_reports_only_sets_actually_logged` and add the Thai case:

```csharp
Assert.Equal([2, 0], viewModel.Exercises.Select(item => item.LoggedSetCount));
Assert.Equal(["2 sets", "0 sets"], viewModel.Exercises.Select(item => item.LoggedSetText));
Assert.Equal("2 exercises · 2 sets logged", viewModel.WorkoutContextText);
Assert.Equal(
    ["Press, 2 sets. Open set logger.",
     "Pull-up, 0 sets. Open set logger."],
    viewModel.Exercises.Select(item => item.AccessibilitySummary));
Assert.All(viewModel.Exercises, item =>
{
    Assert.DoesNotContain(" of ", item.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("LAST", item.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Weight", item.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("Open set logger", item.AccessibilitySummary, StringComparison.Ordinal);
});
```

Add this resource-level assertion in the same class:

```csharp
[Fact]
public void Set_count_and_open_action_copy_are_localized_in_english_and_thai()
{
    var english = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("en-US"));
    var thai = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

    Assert.Equal("1 set", string.Format(english.SetCountSingularFormat, 1));
    Assert.Equal("2 sets", string.Format(english.SetCountPluralFormat, 2));
    Assert.Equal("1 เซ็ต", string.Format(thai.SetCountSingularFormat, 1));
    Assert.Equal("2 เซ็ต", string.Format(thai.SetCountPluralFormat, 2));
    Assert.Equal("Press, 2 sets. Open set logger.", string.Format(
        english.OpenSetLoggerAccessibilityFormat, "Press", "2 sets"));
    Assert.Equal("Press, 2 เซ็ต เปิดหน้าบันทึกเซ็ต", string.Format(
        thai.OpenSetLoggerAccessibilityFormat, "Press", "2 เซ็ต"));
    Assert.Equal("2 sets logged", string.Format(english.SetsLoggedFormat, 2));
}
```

Add `using System.Globalization;` at the top of the test file.

Update `ActiveWorkoutExerciseRowTests.Active_row_copy_is_descriptive_not_a_planned_total` and `NativeIosExperienceAcceptanceTests.Active_workout_row_carries_api_artwork_and_logged_sets_without_a_prescribed_target` to construct:

```csharp
LoggedSetText: "2 sets",
AccessibilitySummary: "Shoulder Press, 2 sets. Open set logger."
```

and assert:

```csharp
Assert.DoesNotContain("logged", row.LoggedSetText, StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain("LAST", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain("Weight", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
Assert.Contains("Open set logger", row.AccessibilitySummary, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the focused tests and confirm the contract fails**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutViewModelTests|FullyQualifiedName~ActiveWorkoutExerciseRowTests|FullyQualifiedName~NativeIosExperienceAcceptanceTests" -m:1
```

Expected: FAIL because `WorkoutTextSet.SetCountSingularFormat` and `SetCountPluralFormat` do not exist and restored rows still contain `sets logged`, tracking mode, and previous-performance text.

- [ ] **Step 3: Add the dedicated localization key**

Add next to `SetsLoggedFormat` in `WorkoutStrings.resx`:

```xml
<data name="SetCountSingularFormat" xml:space="preserve"><value>{0} set</value></data>
<data name="SetCountPluralFormat" xml:space="preserve"><value>{0} sets</value></data>
<data name="OpenSetLoggerAccessibilityFormat" xml:space="preserve"><value>{0}, {1}. Open set logger.</value></data>
```

Add at the matching location in `WorkoutStrings.th.resx`:

```xml
<data name="SetCountSingularFormat" xml:space="preserve"><value>{0} เซ็ต</value></data>
<data name="SetCountPluralFormat" xml:space="preserve"><value>{0} เซ็ต</value></data>
<data name="OpenSetLoggerAccessibilityFormat" xml:space="preserve"><value>{0}, {1} เปิดหน้าบันทึกเซ็ต</value></data>
```

In `WorkoutResources.cs`, add `string SetCountSingularFormat`, `string SetCountPluralFormat`, and `string OpenSetLoggerAccessibilityFormat` immediately before `string SetsLoggedFormat` in the `WorkoutTextSet` positional record, and add the corresponding resource reads in `ForCulture`:

```csharp
Value("ArtworkForExerciseFormat", culture),
Value("SetCountSingularFormat", culture),
Value("SetCountPluralFormat", culture),
Value("OpenSetLoggerAccessibilityFormat", culture),
Value("SetsLoggedFormat", culture),
```

Keeping the two formats distinct is required: `WorkoutContextText` continues to say “sets logged,” while an exercise row says only “sets.”

- [ ] **Step 4: Generate row copy without removed semantic metadata**

In `WorkoutViewModel.CreateItem`, replace the `logged` and summary construction with:

```csharp
var logged = string.Format(
    loggedSetCount == 1 ? _text.SetCountSingularFormat : _text.SetCountPluralFormat,
    loggedSetCount);
return new WorkoutExerciseDraftItem(
    exerciseDefinitionId,
    name,
    trackingMode,
    mode,
    cached?.ThumbnailUri,
    workoutExerciseId,
    loggedSetCount,
    logged,
    last,
    string.Format(
        CultureInfo.CurrentCulture,
        _text.OpenSetLoggerAccessibilityFormat,
        name,
        logged));
```

Do not change `WorkoutContextText`; it must continue using `SetsLoggedFormat`.

- [ ] **Step 5: Run the focused tests and localization audit**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutViewModelTests|FullyQualifiedName~ActiveWorkoutExerciseRowTests|FullyQualifiedName~NativeIosExperienceAcceptanceTests|FullyQualifiedName~LocalizationAuditTests|FullyQualifiedName~LocalizedUiScopeTests" -m:1
```

Expected: PASS. English/Thai keys have parity, row copy is terse, and the workout-level context remains unchanged.

- [ ] **Step 6: Commit the presentation-model slice**

```bash
git add src/TrackZ.Mobile.Core/Resources/WorkoutStrings.resx src/TrackZ.Mobile.Core/Resources/WorkoutStrings.th.resx src/TrackZ.Mobile.Core/Features/Workout/WorkoutResources.cs src/TrackZ.Mobile.Core/Features/Workout/WorkoutViewModel.cs tests/TrackZ.Mobile.Tests/Workout/WorkoutViewModelTests.cs tests/TrackZ.Mobile.Tests/NativeIos/ActiveWorkoutExerciseRowTests.cs tests/TrackZ.Mobile.Tests/Acceptance/NativeIosExperienceAcceptanceTests.cs
git commit -m "feat: simplify today workout set count copy"
```

### Task 2: Prominent trailing set counter

**Files:**

- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/TodayWorkoutVisualContractTests.cs`
- Modify: `src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.xaml`

**Interfaces:**

- Consumes: `WorkoutExerciseDraftItem.LoggedSetText` and `WorkoutExerciseDraftItem.AccessibilitySummary` from Task 1.
- Produces: named `WorkoutExerciseSetCounter` label and `WorkoutExerciseChevron`; removes all old row metadata elements.

- [ ] **Step 1: Replace the old visual-contract assertions with the approved hierarchy**

In `Native_page_matches_the_approved_compact_illustrated_queue_reference`, replace the three old performance-line assertions with:

```csharp
var counter = component.Descendants().Single(element => Name(element) == "WorkoutExerciseSetCounter");
Assert.Equal("2", counter.Attribute("Grid.Column")?.Value);
Assert.Equal("{Binding Exercise.LoggedSetText, Source={x:Reference Root}}", counter.Attribute("Text")?.Value);
Assert.Equal("{DynamicResource TrackZPerformanceNumberStyle}", counter.Attribute("Style")?.Value);
Assert.Equal("False", counter.Attribute("AutomationProperties.IsInAccessibleTree")?.Value);

var row = component.Descendants().Single(element =>
    element.Name.LocalName == "Border"
    && element.Attribute("Style")?.Value == "{DynamicResource TrackZListRowStyle}");
Assert.Equal("True",
    row.Attribute("AutomationProperties.IsInAccessibleTree")?.Value);
var visualContent = row.Elements().Single(element =>
    element.Name.LocalName == "Grid");
Assert.Equal("True",
    visualContent.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
Assert.DoesNotContain(visualContent.DescendantsAndSelf(), element =>
    element.Attribute("AutomationProperties.IsInAccessibleTree")?.Value == "True"
    || element.Attribute("SemanticProperties.Description") is not null);
Assert.Single(component.Descendants().Where(element =>
    element.Attribute("SemanticProperties.Description")?.Value
        == "{Binding Exercise.AccessibilitySummary, Source={x:Reference Root}}"));

var chevron = component.Descendants().Single(element => Name(element) == "WorkoutExerciseChevron");
Assert.Equal("3", chevron.Attribute("Grid.Column")?.Value);

Assert.DoesNotContain(component.Descendants(), element => Name(element) == "WorkoutExercisePerformanceLine");
Assert.DoesNotContain(component.Descendants(), element => Name(element) == "WorkoutExerciseLastText");
Assert.DoesNotContain(component.Descendants(), element => Name(element) == "WorkoutExerciseTodayText");
Assert.DoesNotContain(component.Descendants(), element =>
    element.Attribute("Text")?.Value is "{Binding Exercise.TrackingModeText, Source={x:Reference Root}}"
        or "{Binding Exercise.LastText, Source={x:Reference Root}}");
Assert.Single(component.Descendants().Where(element =>
    element.Attribute("Text")?.Value == "{Binding Exercise.Name, Source={x:Reference Root}}"));
Assert.Equal("{Binding Exercise.AccessibilitySummary, Source={x:Reference Root}}",
    row.Attribute("SemanticProperties.Description")?.Value);
```

- [ ] **Step 2: Run the visual contract and verify it fails**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TodayWorkoutVisualContractTests" -m:1
```

Expected: FAIL because the current XAML still contains tracking mode and the `LAST · sets logged` performance line, and has no named counter.

- [ ] **Step 3: Replace the row body with the counter hierarchy**

Keep the `SwipeView`, commands, border styling, tap recognizer, and artwork geometry unchanged. Add `AutomationProperties.IsInAccessibleTree="True"` to the existing row border, then replace its inner content grid with:

```xml
<Grid AutomationProperties.ExcludedWithChildren="True"
      ColumnDefinitions="88,*,Auto,Auto"
      ColumnSpacing="{DynamicResource TrackZSpace12}">
    <Grid x:Name="ActiveWorkoutArtwork"
          HeightRequest="{DynamicResource TrackZExerciseArtworkSize}"
          WidthRequest="{DynamicResource TrackZExerciseArtworkSize}">
        <Image Aspect="AspectFit"
               IsVisible="{Binding Exercise.HasArtwork, Source={x:Reference Root}}"
               Source="{Binding Exercise.ThumbnailUri, Source={x:Reference Root}}" />
        <Border BackgroundColor="{DynamicResource TrackZSurfaceRaised}"
                IsVisible="{Binding Exercise.ShowsArtworkPlaceholder, Source={x:Reference Root}}"
                StrokeThickness="0"
                StrokeShape="RoundRectangle 14">
            <Image Aspect="AspectFit" HeightRequest="52" Source="exercise_placeholder.png" WidthRequest="52" />
        </Border>
    </Grid>
    <Label Grid.Column="1"
           LineBreakMode="TailTruncation"
           Style="{DynamicResource TrackZNavigationTitleStyle}"
           Text="{Binding Exercise.Name, Source={x:Reference Root}}"
           VerticalOptions="Center" />
    <Label x:Name="WorkoutExerciseSetCounter"
           Grid.Column="2"
           AutomationProperties.IsInAccessibleTree="False"
           HorizontalTextAlignment="End"
           Style="{DynamicResource TrackZPerformanceNumberStyle}"
           Text="{Binding Exercise.LoggedSetText, Source={x:Reference Root}}"
           VerticalOptions="Center" />
    <Label x:Name="WorkoutExerciseChevron"
           Grid.Column="3"
           AutomationProperties.IsInAccessibleTree="False"
           FontSize="24"
           Text="›"
           TextColor="{DynamicResource TrackZTextSecondary}"
           VerticalOptions="Center" />
</Grid>
```

The border remains the one accessible row with `SemanticProperties.Description`. `AutomationProperties.ExcludedWithChildren="True"` on its visual-content grid excludes artwork, placeholder, exercise-name label, counter, and chevron as one group; the explicit counter/chevron exclusions are retained as defense in depth. Swipe actions remain accessible outside that excluded visual grid.

- [ ] **Step 4: Run visual, accessibility, and token tests**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TodayWorkoutVisualContractTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~ActiveWorkoutExerciseRowTests" -m:1
```

Expected: PASS. The row still resolves the 88-point artwork and 112-point card tokens, the counter uses the performance-number style, and removed bindings do not exist in XAML.

- [ ] **Step 5: Commit the native row**

```bash
git add src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.xaml tests/TrackZ.Mobile.Tests/NativeIos/TodayWorkoutVisualContractTests.cs
git commit -m "feat: emphasize logged sets in today workout rows"
```

### Task 3: Persistent visual reference and release verification

**Files:**

- Modify: `docs/design/todays-workout-reference.html`
- Modify: `tests/TrackZ.Mobile.Tests/NativeIos/TodayWorkoutVisualContractTests.cs`

**Interfaces:**

- Consumes: final XAML hierarchy from Task 2.
- Produces: maintained HTML source of truth with the same card content and an end-to-end verified Today Workout slice.

- [ ] **Step 1: Add failing HTML absence and hierarchy assertions**

Extend `Persistent_html_reference_covers_the_exact_native_hierarchy_and_spacing`:

```csharp
Assert.Equal(4, Count(html, "class=\"set-count\""));
Assert.Contains(">2 sets<", html, StringComparison.Ordinal);
Assert.Contains(">1 set<", html, StringComparison.Ordinal);
Assert.Contains(">0 sets<", html, StringComparison.Ordinal);
Assert.DoesNotContain("class=\"mode\"", html, StringComparison.Ordinal);
Assert.DoesNotContain("class=\"performance\"", html, StringComparison.Ordinal);
Assert.DoesNotContain("LAST", html, StringComparison.Ordinal);
```

Add this helper to the test class:

```csharp
private static int Count(string source, string value)
{
    var count = 0;
    for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
        count++;
    return count;
}
```

- [ ] **Step 2: Run the HTML contract and verify it fails**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~TodayWorkoutVisualContractTests.Persistent_html_reference" -m:1
```

Expected: FAIL because the reference still contains `mode`, `performance`, and `LAST` markup.

- [ ] **Step 3: Update the maintained reference cards**

Keep the page context (`4 exercises · 3 sets logged`), page spacing data attributes, actions, and navigation unchanged. Change the card grid to artwork/name/set count/chevron and use this exact body pattern:

```html
<article class="card"><img class="art" src="../../assets/exercises/images/assisted-pull-up.png" alt="Assisted Pull-Up movement artwork"><div class="name">Assisted Pull-Up</div><div class="set-count">2 sets</div><span class="chevron">›</span></article>
<article class="card"><img class="art" src="../../assets/exercises/images/barbell-row.png" alt="Barbell Row movement artwork"><div class="name">Barbell Row</div><div class="set-count">1 set</div><span class="chevron">›</span></article>
<article class="card"><img class="art" src="../../assets/exercises/images/cable-fly.png" alt="Cable Fly movement artwork"><div class="name">Cable Fly</div><div class="set-count">0 sets</div><span class="chevron">›</span></article>
<article class="card"><img class="art" src="../../assets/exercises/images/close-grip-bench-press.png" alt="Close-Grip Bench Press movement artwork"><div class="name">Close-Grip Bench Press</div><div class="set-count">0 sets</div><span class="chevron">›</span></article>
```

Replace the old `.mode,.performance` rules and update the card rule with this exact CSS:

```css
.card{height:112px;padding:12px;display:grid;grid-template-columns:88px minmax(0,1fr) auto auto;gap:12px;align-items:center;background:var(--surface);border:1px solid var(--line);border-radius:16px}
.set-count{color:var(--lime);font-size:26px;font-weight:700;white-space:nowrap;text-align:right}
.chevron{color:var(--muted);font-size:28px}
```

- [ ] **Step 4: Run the complete plan verification**

Run:

```bash
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkoutViewModelTests|FullyQualifiedName~ActiveWorkoutExerciseRowTests|FullyQualifiedName~TodayWorkoutVisualContractTests|FullyQualifiedName~NativeIosExperienceAcceptanceTests|FullyQualifiedName~AccessibilitySemanticsTests|FullyQualifiedName~NativeVisualTokenTests|FullyQualifiedName~LocalizationAuditTests|FullyQualifiedName~LocalizedUiScopeTests" -m:1
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore -m:1
```

Expected: both commands PASS with zero failed tests. Inspect `git diff --check`; expected: no whitespace errors.

- [ ] **Step 5: Commit the persistent reference**

```bash
git add docs/design/todays-workout-reference.html tests/TrackZ.Mobile.Tests/NativeIos/TodayWorkoutVisualContractTests.cs
git commit -m "docs: update today workout set counter reference"
```
