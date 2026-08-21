using System.Globalization;
using System.Xml.Linq;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class AppWideVisualConsistencyTests
{
    private static readonly string[] ShippedPages =
    [
        "Features/Auth/AuthGatePage.xaml",
        "Features/Auth/SignInPage.xaml",
        "Features/Auth/CreateAccountPage.xaml",
        "Features/Train/TrainPage.xaml",
        "Features/Train/BodyAreaSheetPage.xaml",
        "Features/Exercises/ExercisePickerPage.xaml",
        "Features/Exercises/CustomExercisePage.xaml",
        "Features/Workout/WorkoutPage.xaml",
        "Features/Workout/SetLoggerPage.xaml",
        "Features/Workout/SetEntrySheetPage.xaml",
        "Features/History/WorkoutHistoryPage.xaml",
        "Features/History/WorkoutHistoryDetailPage.xaml",
        "Features/History/HistorySetEditorSheetPage.xaml",
        "Features/History/HistoryConflictSheetPage.xaml",
        "Features/Summary/WorkoutSummaryPage.xaml",
        "Features/Progress/ExerciseProgressPage.xaml",
        "Features/Profile/ProfilePage.xaml"
    ];

    private static readonly IReadOnlyDictionary<string, int> ExpectedPagePaddingCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Features/Auth/AuthGatePage.xaml"] = 1,
            ["Features/Auth/SignInPage.xaml"] = 2,
            ["Features/Auth/CreateAccountPage.xaml"] = 2,
            ["Features/Train/TrainPage.xaml"] = 3,
            ["Features/Train/BodyAreaSheetPage.xaml"] = 2,
            ["Features/Exercises/ExercisePickerPage.xaml"] = 5,
            ["Features/Exercises/CustomExercisePage.xaml"] = 2,
            ["Features/Workout/WorkoutPage.xaml"] = 2,
            ["Features/Workout/SetLoggerPage.xaml"] = 1,
            ["Features/Workout/SetEntrySheetPage.xaml"] = 2,
            ["Features/History/WorkoutHistoryPage.xaml"] = 1,
            ["Features/History/WorkoutHistoryDetailPage.xaml"] = 1,
            ["Features/History/HistorySetEditorSheetPage.xaml"] = 2,
            ["Features/History/HistoryConflictSheetPage.xaml"] = 2,
            ["Features/Summary/WorkoutSummaryPage.xaml"] = 1,
            ["Features/Progress/ExerciseProgressPage.xaml"] = 1,
            ["Features/Profile/ProfilePage.xaml"] = 1
        };

    private static readonly IReadOnlyDictionary<string, int> ExpectedPrimaryActionCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Features/Auth/AuthGatePage.xaml"] = 0,
            ["Features/Auth/SignInPage.xaml"] = 1,
            ["Features/Auth/CreateAccountPage.xaml"] = 1,
            ["Features/Train/TrainPage.xaml"] = 1,
            ["Features/Train/BodyAreaSheetPage.xaml"] = 0,
            ["Features/Exercises/ExercisePickerPage.xaml"] = 1,
            ["Features/Exercises/CustomExercisePage.xaml"] = 1,
            ["Features/Workout/WorkoutPage.xaml"] = 2,
            ["Features/Workout/SetLoggerPage.xaml"] = 1,
            ["Features/Workout/SetEntrySheetPage.xaml"] = 1,
            ["Features/History/WorkoutHistoryPage.xaml"] = 0,
            ["Features/History/WorkoutHistoryDetailPage.xaml"] = 0,
            ["Features/History/HistorySetEditorSheetPage.xaml"] = 1,
            ["Features/History/HistoryConflictSheetPage.xaml"] = 1,
            ["Features/Summary/WorkoutSummaryPage.xaml"] = 0,
            ["Features/Progress/ExerciseProgressPage.xaml"] = 0,
            ["Features/Profile/ProfilePage.xaml"] = 1
        };

    private static readonly string[] RootTitlePages =
    [
        "Features/Auth/SignInPage.xaml",
        "Features/Train/TrainPage.xaml",
        "Features/Exercises/ExercisePickerPage.xaml",
        "Features/History/WorkoutHistoryPage.xaml",
        "Features/Progress/ExerciseProgressPage.xaml",
        "Features/Profile/ProfilePage.xaml"
    ];

    private static readonly string[] BottomActionPages =
    [
        "Features/Auth/SignInPage.xaml",
        "Features/Auth/CreateAccountPage.xaml",
        "Features/Train/TrainPage.xaml",
        "Features/Train/BodyAreaSheetPage.xaml",
        "Features/Exercises/ExercisePickerPage.xaml",
        "Features/Exercises/CustomExercisePage.xaml",
        "Features/Workout/WorkoutPage.xaml",
        "Features/Workout/SetLoggerPage.xaml",
        "Features/Workout/SetEntrySheetPage.xaml",
        "Features/History/WorkoutHistoryDetailPage.xaml",
        "Features/History/HistorySetEditorSheetPage.xaml",
        "Features/History/HistoryConflictSheetPage.xaml",
        "Features/Profile/ProfilePage.xaml"
    ];

    private static readonly string[] AuditedComponents =
    [
        "BadgeTile.xaml",
        "ExerciseProgressChart.xaml",
        "LastSetTable.xaml",
        "WeeklyStreakView.xaml",
        "XpBar.xaml"
    ];

    public static TheoryData<string> ShippedPageFiles => new(ShippedPages);
    public static TheoryData<string> ComponentFiles => new(AuditedComponents);

    [Theory]
    [MemberData(nameof(ShippedPageFiles))]
    public void Shipped_page_uses_the_native_semantic_layout_contract(string relativePath)
    {
        var path = Path.Combine(MobileDirectory(), relativePath);
        var document = XDocument.Load(path);
        var errors = AuditPage(relativePath, document).ToArray();

        Assert.True(errors.Length == 0, $"{relativePath}:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Select(error => $"- {error}"))}");
    }

    [Theory]
    [MemberData(nameof(ComponentFiles))]
    public void Remaining_gamification_component_uses_semantic_cards_type_and_spacing(string fileName)
    {
        var path = Path.Combine(MobileDirectory(), "Components", fileName);
        var document = XDocument.Load(path);
        var errors = AuditCommon(document).ToList();

        foreach (var spacing in document.Root!.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.LocalName is "Spacing" or "RowSpacing" or "ColumnSpacing" or "Padding"))
        {
            if (spacing.Value.Contains("TrackZSpace", StringComparison.Ordinal) ||
                spacing.Value.Contains("TrackZVerticalRowPadding", StringComparison.Ordinal))
                continue;
            errors.Add($"{spacing.Parent?.Name.LocalName}.{spacing.Name.LocalName} must use a TrackZSpace resource.");
        }

        foreach (var label in document.Descendants().Where(element => element.Name.LocalName == "Label"))
        {
            if (label.Attribute("Style") is null)
                errors.Add("Every component label must use a semantic typography style.");
        }

        Assert.True(errors.Count == 0, $"{fileName}:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Select(error => $"- {error}"))}");
    }

    [Fact]
    public void Explicit_shipped_page_set_cannot_silently_shrink()
    {
        Assert.Equal(17, ShippedPages.Length);
        Assert.Equal(17, ShippedPages.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ShippedPages, relativePath => Assert.True(File.Exists(Path.Combine(MobileDirectory(), relativePath)), relativePath));
    }

    [Fact]
    public void Auth_forms_keep_identical_field_and_footer_anchors_while_requirements_change_visibility()
    {
        var signIn = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Auth/SignInPage.xaml"));
        var create = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Auth/CreateAccountPage.xaml"));
        Assert.Equal(FormAnchorSignature(signIn), FormAnchorSignature(create));

        var signRequirement = Assert.Single(signIn.Descendants(), element => element.Attribute("Text")?.Value == "{Binding Text.CreateAccountRequirements}");
        var createRequirement = Assert.Single(create.Descendants(), element => element.Attribute("Text")?.Value == "{Binding Text.CreateAccountRequirements}");
        Assert.Equal("0", signRequirement.Attribute("Opacity")?.Value);
        Assert.Equal("True", signRequirement.Attribute("InputTransparent")?.Value);
        Assert.Equal("True", signRequirement.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.Null(createRequirement.Attribute("Opacity"));
        Assert.Equal(ElementPath(signRequirement), ElementPath(createRequirement));
    }

    [Fact]
    public void Audit_rejects_structural_mutations_even_when_semantic_resource_names_remain_elsewhere()
    {
        const string train = "Features/Train/TrainPage.xaml";
        var document = XDocument.Load(Path.Combine(MobileDirectory(), train));
        var sticky = Assert.Single(document.Descendants(), element => ResourceKey(element.Attribute("Style")?.Value) == "TrackZStickyActionContainerStyle");
        sticky.SetAttributeValue(XName.Get("Row", "http://schemas.microsoft.com/dotnet/2021/maui"), "0");
        var primary = Assert.Single(sticky.Descendants(), element => ResourceKey(element.Attribute("Style")?.Value) == "TrackZPrimaryButtonStyle");
        sticky.Add(new XElement(primary));

        var errors = AuditPage(train, document).ToArray();
        Assert.Contains(errors, error => error.Contains("last grid row", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("one visible primary action", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_account_is_a_pushed_destination_and_uses_navigation_title_semantics()
    {
        var path = Path.Combine(MobileDirectory(), "Features/Auth/CreateAccountPage.xaml");
        var document = XDocument.Load(path);
        var title = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Label" &&
            string.Equals(element.Attribute("Text")?.Value, "{Binding Text.CreateAccountTitle}", StringComparison.Ordinal));

        Assert.Equal("TrackZNavigationTitleStyle", ResourceKey(title.Attribute("Style")?.Value));
    }

    [Fact]
    public void Custom_exercise_pickers_use_the_50_point_dark_semantic_field_contract()
    {
        var controls = XDocument.Load(Path.Combine(MobileDirectory(), "Resources/Styles/TrackZControls.xaml"));
        var style = Assert.Single(controls.Descendants(), element =>
            element.Name.LocalName == "Style" &&
            string.Equals(element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value, "TrackZPickerStyle", StringComparison.Ordinal));
        var setters = style.Elements().Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => element.Attribute("Property")!.Value, element => element.Attribute("Value")!.Value);

        Assert.Equal("{StaticResource TrackZFieldHeight}", setters["MinimumHeightRequest"]);
        Assert.Equal("{StaticResource TrackZTextPrimary}", setters["TextColor"]);
        Assert.Equal("{StaticResource TrackZTextSecondary}", setters["TitleColor"]);

        var page = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Exercises/CustomExercisePage.xaml"));
        Assert.All(page.Descendants().Where(element => element.Name.LocalName == "Picker"), picker =>
            Assert.Equal("TrackZPickerStyle", ResourceKey(picker.Attribute("Style")?.Value)));
    }

    [Fact]
    public void Profile_controls_expose_actual_unit_selection_and_native_targets()
    {
        var page = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Profile/ProfilePage.xaml"));
        var kilogram = Assert.Single(page.Descendants(), element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "KilogramsButton");
        var pounds = Assert.Single(page.Descendants(), element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "PoundsButton");
        Assert.Contains(kilogram.Descendants(), element => element.Name.LocalName == "DataTrigger" && element.Attribute("Binding")?.Value == "{Binding Source={x:Reference Page}, Path=IsKilogramsSelected}");
        Assert.Contains(pounds.Descendants(), element => element.Name.LocalName == "DataTrigger" && element.Attribute("Binding")?.Value == "{Binding Source={x:Reference Page}, Path=IsPoundsSelected}");

        Assert.All(page.Descendants().Where(element => element.Name.LocalName == "Stepper"), control =>
            Assert.Equal("TrackZStepperStyle", ResourceKey(control.Attribute("Style")?.Value)));
        Assert.All(page.Descendants().Where(element => element.Name.LocalName == "Switch"), control =>
            Assert.Equal("TrackZSwitchStyle", ResourceKey(control.Attribute("Style")?.Value)));

        var controls = XDocument.Load(Path.Combine(MobileDirectory(), "Resources/Styles/TrackZControls.xaml"));
        AssertControlStyle(controls, "TrackZStepperStyle", "Stepper", ("MinimumHeightRequest", "{StaticResource TrackZMinimumTarget}"), ("MinimumWidthRequest", "{StaticResource TrackZMinimumTarget}"));
        AssertControlStyle(controls, "TrackZSwitchStyle", "Switch", ("MinimumHeightRequest", "{StaticResource TrackZMinimumTarget}"), ("MinimumWidthRequest", "{StaticResource TrackZMinimumTarget}"), ("OnColor", "{StaticResource TrackZPrimary}"));
    }

    [Fact]
    public void Summary_uses_authoritative_xp_progress_and_distinct_confirmation_roles()
    {
        var page = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Summary/WorkoutSummaryPage.xaml"));
        var reveal = Assert.Single(page.Descendants(), element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "SummaryReveal");
        Assert.Equal("{Binding HasProgressReveal}", reveal.Attribute("IsVisible")?.Value);
        var xpBar = Assert.Single(page.Descendants(), element => element.Name.LocalName == "XpBar");
        Assert.Equal("{Binding LevelProgress}", xpBar.Attribute("Progress")?.Value);
        Assert.Equal("{Binding Text.Level}", xpBar.Attribute("LevelLabel")?.Value);
        Assert.Equal("{Binding Text.Xp}", xpBar.Attribute("XpLabel")?.Value);

        var confirmed = Assert.Single(page.Descendants(), element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "ConfirmedRevealStatus");
        var pending = Assert.Single(page.Descendants(), element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "PendingRevealStatus");
        Assert.Equal("TrackZPerformanceNumberStyle", ResourceKey(confirmed.Attribute("Style")?.Value));
        Assert.Equal("{Binding IsProgressRevealConfirmed}", confirmed.Attribute("IsVisible")?.Value);
        Assert.Equal("TrackZPerformanceMetadataStyle", ResourceKey(pending.Attribute("Style")?.Value));
        Assert.Equal("{Binding IsProgressRevealPending}", pending.Attribute("IsVisible")?.Value);

        var volume = Assert.Single(page.Descendants(), element => element.Attribute("Text")?.Value == "{Binding TotalVolumeText}");
        Assert.Equal("Label", volume.Name.LocalName);

        var component = File.ReadAllText(Path.Combine(MobileDirectory(), "Components/XpBar.xaml"));
        Assert.DoesNotContain("LEVEL", component, StringComparison.Ordinal);
        Assert.DoesNotContain(" XP'", component, StringComparison.Ordinal);
    }

    [Fact]
    public void Badge_tile_renders_localized_contract_presentation_instead_of_raw_reward_keys()
    {
        var path = Path.Combine(MobileDirectory(), "Components/BadgeTile.xaml");
        var xaml = File.ReadAllText(path);
        var document = XDocument.Load(path);

        Assert.Contains("EarnedBadgePresentation", document.Root!.Attribute(XName.Get("DataType", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value, StringComparison.Ordinal);
        Assert.Contains(document.Descendants(), element => element.Attribute("Text")?.Value == "{Binding IconGlyph}");
        Assert.Contains(document.Descendants(), element => element.Attribute("Text")?.Value == "{Binding Name}");
        Assert.Contains(document.Descendants(), element => element.Attribute("Text")?.Value == "{Binding Description}");
        Assert.Contains(document.Descendants(), element => element.Attribute("Text")?.Value == "{Binding EarnedText}");
        Assert.DoesNotContain("NameResourceKey", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DescriptionResourceKey", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("★", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RoundRectangle 22", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Features/Workout/SetEntrySheetPage.xaml", "RewardOverlay")]
    [InlineData("Features/Workout/SetLoggerPage.xaml", "SavedPulse")]
    public void Hidden_saved_feedback_is_not_hit_tested_or_exposed_to_accessibility(string relativePath, string elementName)
    {
        var page = XDocument.Load(Path.Combine(MobileDirectory(), relativePath));
        var feedback = Assert.Single(page.Descendants(), element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == elementName);
        Assert.Equal("0", feedback.Attribute("Opacity")?.Value);
        Assert.Equal("True", feedback.Attribute("InputTransparent")?.Value);
        Assert.Equal("True", feedback.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
    }

    private static IEnumerable<string> AuditPage(string relativePath, XDocument document)
    {
        var root = Assert.IsType<XElement>(document.Root);
        var background = root.Attribute("BackgroundColor")?.Value;
        if (background is null || !background.Contains("DynamicResource TrackZ", StringComparison.Ordinal))
            yield return "ContentPage must use a semantic dynamic page background.";
        if (!string.Equals(root.Attribute("SafeAreaEdges")?.Value, "All", StringComparison.Ordinal))
            yield return "ContentPage must opt into all safe-area edges.";

        var pagePadding = root.DescendantsAndSelf().Attributes("Padding")
            .Where(attribute => PagePaddingKeys.Contains(ResourceKey(attribute.Value), StringComparer.Ordinal))
            .ToArray();
        var expectedPaddingCount = ExpectedPagePaddingCounts[relativePath];
        if (pagePadding.Length != expectedPaddingCount)
            yield return $"Page margin ownership must have exactly {expectedPaddingCount} direct content region(s), found {pagePadding.Length}.";
        foreach (var padding in pagePadding)
        {
            if (padding.Parent!.Ancestors().Attributes("Padding").Any(attribute => PagePaddingKeys.Contains(ResourceKey(attribute.Value), StringComparer.Ordinal)))
                yield return "Page margin resources must not be nested or doubled.";
        }

        foreach (var error in AuditCommon(document))
            yield return error;

        if (!string.Equals(relativePath, "Features/Auth/AuthGatePage.xaml", StringComparison.Ordinal))
        {
            var expectedTitleStyle = RootTitlePages.Contains(relativePath, StringComparer.Ordinal)
                ? "TrackZPageTitleStyle"
                : "TrackZNavigationTitleStyle";
            if (!document.Descendants().Any(element => element.Name.LocalName == "Label" && ResourceKey(element.Attribute("Style")?.Value) == expectedTitleStyle))
                yield return $"The page title must use {expectedTitleStyle}.";
        }

        foreach (var button in document.Descendants().Where(element => element.Name.LocalName == "Button"))
        {
            var style = ResourceKey(button.Attribute("Style")?.Value);
            if (style is not ("TrackZPrimaryButtonStyle" or "TrackZSecondaryButtonStyle" or "TrackZDestructiveButtonStyle" or "TrackZQuietButtonStyle"))
                yield return $"Button '{button.Attribute("Text")?.Value ?? "unnamed"}' must use a semantic button style.";

            var minimum = button.Attribute("MinimumHeightRequest")?.Value;
            if (double.TryParse(minimum, NumberStyles.Number, CultureInfo.InvariantCulture, out var height) && height < 44)
                yield return $"Button minimum height {height} is below the 44-point native target.";

            var destructiveSignal = string.Concat(
                button.Attribute("Text")?.Value,
                button.Attribute("Clicked")?.Value,
                button.Attribute("Command")?.Value);
            if (ContainsDestructiveSignal(destructiveSignal) && style != "TrackZDestructiveButtonStyle")
                yield return $"Destructive button '{button.Attribute("Text")?.Value ?? "unnamed"}' must use TrackZDestructiveButtonStyle.";
        }

        foreach (var label in document.Descendants().Where(element => element.Name.LocalName == "Label"))
        {
            if (ResourceKey(label.Attribute("TextColor")?.Value) == "TrackZDanger" &&
                ResourceKey(label.Attribute("Style")?.Value) is not ("TrackZFieldErrorStyle" or "TrackZInlineErrorStyle"))
                yield return "Danger copy must use a destructive semantic typography role.";
        }

        foreach (var control in document.Descendants().Where(element => element.Name.LocalName is "Picker" or "Switch" or "Stepper"))
        {
            var expectedStyle = $"TrackZ{control.Name.LocalName}Style";
            if (ResourceKey(control.Attribute("Style")?.Value) != expectedStyle)
                yield return $"{control.Name.LocalName} must use {expectedStyle} for native target geometry and semantic colors.";
        }

        var primaryActions = document.Descendants()
            .Where(element => element.Name.LocalName == "Button" && ResourceKey(element.Attribute("Style")?.Value) == "TrackZPrimaryButtonStyle")
            .ToArray();
        var expectedPrimaryCount = ExpectedPrimaryActionCounts[relativePath];
        if (primaryActions.Length != expectedPrimaryCount)
            yield return $"The page must expose exactly {expectedPrimaryCount} primary action(s); one visible primary action is allowed unless complementary actions are explicitly allowlisted.";
        if (expectedPrimaryCount == 2 &&
            !primaryActions.Select(action => action.Attribute("IsVisible")?.Value).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "{Binding HasStarted}", "{Binding IsDraft}" }.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            yield return "Workout complementary primary actions must remain mutually exclusive through IsDraft and HasStarted.";

        if (BottomActionPages.Contains(relativePath, StringComparer.Ordinal))
        {
            var stickyActions = document.Descendants()
                .Where(element => ResourceKey(element.Attribute("Style")?.Value) == "TrackZStickyActionContainerStyle")
                .ToArray();
            if (stickyActions.Length != 1)
            {
                yield return "Bottom actions must have exactly one TrackZStickyActionContainerStyle owner.";
            }
            else
            {
                var sticky = stickyActions[0];
                var contentRoot = root.Elements().Single();
                if (sticky.Parent != contentRoot)
                    yield return "The sticky action container must directly own the bottom band at full page width.";
                var rows = contentRoot.Attribute("RowDefinitions")?.Value.Split(',').Length ?? 0;
                if (rows == 0 || GridRow(sticky) != rows - 1)
                    yield return "The sticky action container must occupy the last grid row.";
                if (sticky.Attribute("HorizontalOptions")?.Value is { } horizontal && horizontal is not ("Fill" or "FillAndExpand"))
                    yield return "The sticky action container must remain full width.";
                if (primaryActions.Any(action => !action.Ancestors().Contains(sticky)))
                    yield return "Primary actions on sticky pages must stay inside the safe-area action container.";
            }
        }
    }

    private static IEnumerable<string> AuditCommon(XDocument document)
    {
        foreach (var attribute in document.Root!.DescendantsAndSelf().Attributes())
        {
            if (attribute.Value.StartsWith('#'))
                yield return $"{attribute.Parent?.Name.LocalName}.{attribute.Name.LocalName} contains a literal color.";
            if (attribute.Name.LocalName is "FontSize" or "CornerRadius")
                yield return $"{attribute.Parent?.Name.LocalName}.{attribute.Name.LocalName} must come from a semantic style.";
            if (attribute.Name.LocalName == "StrokeShape" && attribute.Value.Any(char.IsDigit))
                yield return $"{attribute.Parent?.Name.LocalName}.StrokeShape must use a semantic shape/style instead of a literal radius.";
            if (attribute.Name.LocalName is "HeightRequest" or "WidthRequest" or "MinimumHeightRequest" or "MinimumWidthRequest" &&
                double.TryParse(attribute.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var dimension) &&
                !AllowedLocalDimensions.Contains(dimension))
                yield return $"{attribute.Parent?.Name.LocalName}.{attribute.Name.LocalName} uses off-scale local dimension {dimension}.";
        }
    }

    private static readonly string[] PagePaddingKeys =
    [
        "TrackZPageHorizontalPadding", "TrackZPageContentPadding", "TrackZPageBottomContentPadding", "TrackZAuthGatePadding"
    ];

    private static readonly double[] AllowedLocalDimensions = [0, 4, 8, 12, 16, 24, 32, 44, 50, 52, 88, 112];

    private static int GridRow(XElement element) =>
        int.TryParse(element.Attribute(XName.Get("Row", "http://schemas.microsoft.com/dotnet/2021/maui"))?.Value ?? element.Attribute("Grid.Row")?.Value, out var row)
            ? row
            : 0;

    private static string FormAnchorSignature(XDocument document)
    {
        var rootGrid = Assert.Single(document.Root!.Elements());
        var anchors = new[] { "WelcomeBodyLabel", "EmailFieldContainer", "PasswordFieldContainer", "SubmitAction", "SwitchModeAction" };
        return string.Join('|', new[] { rootGrid.Attribute("Padding")?.Value, rootGrid.Attribute("RowDefinitions")?.Value }
            .Concat(anchors.Select(name =>
            {
                var element = Assert.Single(document.Descendants(), candidate => candidate.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == name);
                var band = element.Ancestors().First(ancestor => ancestor.Parent == rootGrid);
                return $"{name}:{GridRow(band)}:{ElementPath(element)}";
            })));
    }

    private static string ElementPath(XElement element)
    {
        var indices = new Stack<int>();
        for (var current = element; current.Parent is not null && current.Parent.Name.LocalName != "ContentPage"; current = current.Parent)
            indices.Push(current.Parent.Elements().ToList().IndexOf(current));
        return string.Join('.', indices);
    }

    private static bool ContainsDestructiveSignal(string value) =>
        new[] { "Delete", "Discard", "Remove", "SignOut" }
            .Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static string? ResourceKey(string? value)
    {
        if (value is null)
            return null;
        var marker = "Resource ";
        var start = value.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0 || !value.EndsWith('}'))
            return null;
        return value[(start + marker.Length)..^1];
    }

    private static void AssertControlStyle(XDocument document, string key, string targetType, params (string Property, string Value)[] expected)
    {
        var style = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Style" &&
            element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == key);
        Assert.Equal(targetType, style.Attribute("TargetType")?.Value);
        var setters = style.Elements().Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => element.Attribute("Property")!.Value, element => element.Attribute("Value")!.Value);
        foreach (var pair in expected)
            Assert.Equal(pair.Value, setters[pair.Property]);
    }

    private static string MobileDirectory() =>
        Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile");

    private static string FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("TrackZ.slnx was not found from the test output directory.");
    }
}
