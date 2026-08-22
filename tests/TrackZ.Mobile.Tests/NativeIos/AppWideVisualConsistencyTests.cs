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

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedPagePaddingOwners =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Features/Auth/AuthGatePage.xaml"] = ["$=TrackZAuthGatePadding"],
            ["Features/Auth/SignInPage.xaml"] = ["0=TrackZPageHorizontalPadding", "1=TrackZPageHorizontalPadding"],
            ["Features/Auth/CreateAccountPage.xaml"] = ["0=TrackZPageHorizontalPadding", "1=TrackZPageHorizontalPadding"],
            ["Features/Train/TrainPage.xaml"] = ["$=TrackZPageContentPadding"],
            ["Features/Train/BodyAreaSheetPage.xaml"] = ["0=TrackZPageHorizontalPadding", "1=TrackZPageHorizontalPadding"],
            ["Features/Exercises/ExercisePickerPage.xaml"] = ["0=TrackZPageHorizontalPadding", "1=TrackZPageHorizontalPadding", "2=TrackZPageHorizontalPadding"],
            ["Features/Exercises/CustomExercisePage.xaml"] = ["0.0=TrackZPageHorizontalPadding"],
            ["Features/Workout/WorkoutPage.xaml"] = ["0=TrackZPageHorizontalPadding", "1=TrackZPageHorizontalPadding"],
            ["Features/Workout/SetLoggerPage.xaml"] = ["0.0=TrackZPageBottomContentPadding"],
            ["Features/Workout/SetEntrySheetPage.xaml"] = ["0=TrackZPageHorizontalPadding", "1.0=TrackZPageHorizontalPadding"],
            ["Features/History/WorkoutHistoryPage.xaml"] = ["$=TrackZPageContentPadding"],
            ["Features/History/WorkoutHistoryDetailPage.xaml"] = ["0.0=TrackZPageBottomContentPadding"],
            ["Features/History/HistorySetEditorSheetPage.xaml"] = ["0=TrackZPageHorizontalPadding", "1=TrackZPageHorizontalPadding"],
            ["Features/History/HistoryConflictSheetPage.xaml"] = ["0=TrackZPageHorizontalPadding", "1=TrackZPageHorizontalPadding"],
            ["Features/Summary/WorkoutSummaryPage.xaml"] = ["0=TrackZPageContentPadding"],
            ["Features/Progress/ExerciseProgressPage.xaml"] = ["0=TrackZPageContentPadding"],
            ["Features/Profile/ProfilePage.xaml"] = ["0.0=TrackZPageBottomContentPadding"]
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
            ["Features/Workout/SetLoggerPage.xaml"] = 2,
            ["Features/Workout/SetEntrySheetPage.xaml"] = 1,
            ["Features/History/WorkoutHistoryPage.xaml"] = 0,
            ["Features/History/WorkoutHistoryDetailPage.xaml"] = 0,
            ["Features/History/HistorySetEditorSheetPage.xaml"] = 1,
            ["Features/History/HistoryConflictSheetPage.xaml"] = 1,
            ["Features/Summary/WorkoutSummaryPage.xaml"] = 0,
            ["Features/Progress/ExerciseProgressPage.xaml"] = 0,
            ["Features/Profile/ProfilePage.xaml"] = 1
        };

    private static readonly IReadOnlyDictionary<string, ArtworkContract[]> ArtworkContracts =
        new Dictionary<string, ArtworkContract[]>(StringComparer.Ordinal)
        {
            ["Features/Train/TrainPage.xaml"] =
            [
                new(
                    "TrainAgainArtwork",
                    "Border",
                    "ContentPage/ScrollView/VerticalStackLayout/VerticalStackLayout/Border/Grid/Border",
                    "TrackZArtworkFrameStyle"),
                new(
                    "RecentMomentumArtwork",
                    "Border",
                    "ContentPage/ScrollView/VerticalStackLayout/VerticalStackLayout/Border/Grid/Border",
                    "TrackZArtworkFrameStyle")
            ],
            ["Features/Workout/SetLoggerPage.xaml"] =
            [
                new(
                    "SetLoggerArtwork",
                    "Grid",
                    "ContentPage/Grid/ScrollView/VerticalStackLayout/Grid/Grid",
                    null)
            ]
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
        "Features/Train/BodyAreaSheetPage.xaml",
        "Features/Exercises/ExercisePickerPage.xaml",
        "Features/Exercises/CustomExercisePage.xaml",
        "Features/Workout/WorkoutPage.xaml",
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
    public void Auth_anchor_geometry_is_independent_of_measured_header_height_without_weakening_title_roles()
    {
        var signIn = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Auth/SignInPage.xaml"));
        var create = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Auth/CreateAccountPage.xaml"));

        Assert.Empty(AuditAuthHeaderGeometry(signIn, create));

        var mutatedCreate = new XDocument(create);
        var mutatedRoot = Assert.Single(mutatedCreate.Root!.Elements());
        mutatedRoot.SetAttributeValue("RowDefinitions", "Auto,*,Auto");
        mutatedRoot.Elements().ElementAt(1).SetAttributeValue(XName.Get("Row", "http://schemas.microsoft.com/dotnet/2021/maui"), "1");
        mutatedRoot.Elements().ElementAt(2).SetAttributeValue(XName.Get("Row", "http://schemas.microsoft.com/dotnet/2021/maui"), "2");
        Assert.Contains(
            AuditAuthHeaderGeometry(signIn, mutatedCreate),
            error => error.Contains("header-independent overlay", StringComparison.Ordinal));
    }

    [Fact]
    public void Momentum_home_audit_rejects_duplicate_or_outside_hero_primary_actions()
    {
        const string train = "Features/Train/TrainPage.xaml";
        var document = XDocument.Load(Path.Combine(MobileDirectory(), train));
        var hero = Named(document, "HomeHero");
        var primary = Assert.Single(hero.Descendants(), element => ResourceKey(element.Attribute("Style")?.Value) == "TrackZPrimaryButtonStyle");
        primary.Parent!.Add(new XElement(primary));

        var errors = AuditPage(train, document).ToArray();
        Assert.Contains(errors, error => error.Contains("one visible primary action", StringComparison.Ordinal));

        var outside = XDocument.Load(Path.Combine(MobileDirectory(), train));
        var outsideHero = Named(outside, "HomeHero");
        var outsidePrimary = Named(outside, "HeroActionButton");
        outsidePrimary.Remove();
        outsideHero.AddAfterSelf(outsidePrimary);

        Assert.Contains(
            AuditPage(train, outside),
            error => error.Contains("inside HomeHero", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit_rejects_page_padding_moved_from_the_direct_content_band_to_an_arbitrary_child()
    {
        const string train = "Features/Train/TrainPage.xaml";
        var document = XDocument.Load(Path.Combine(MobileDirectory(), train));
        var contentRoot = Assert.Single(document.Root!.Elements());
        var padding = contentRoot.Attribute("Padding")!.Value;
        contentRoot.Attribute("Padding")!.Remove();
        contentRoot.Elements().Single().SetAttributeValue("Padding", padding);

        var errors = AuditPage(train, document).ToArray();

        Assert.Contains(errors, error => error.Contains("exact direct content-band owner", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit_rejects_a_page_whose_only_primary_action_is_statically_hidden()
    {
        const string train = "Features/Train/TrainPage.xaml";
        var document = XDocument.Load(Path.Combine(MobileDirectory(), train));
        var primary = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Button" && ResourceKey(element.Attribute("Style")?.Value) == "TrackZPrimaryButtonStyle");
        primary.SetAttributeValue("IsVisible", "False");

        var errors = AuditPage(train, document).ToArray();

        Assert.Contains(errors, error => error.Contains("visible/action-capable primary action", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    public void Audit_rejects_a_page_whose_only_primary_action_has_static_zero_opacity(string opacity)
    {
        const string train = "Features/Train/TrainPage.xaml";
        var document = XDocument.Load(Path.Combine(MobileDirectory(), train));
        var primary = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Button" && ResourceKey(element.Attribute("Style")?.Value) == "TrackZPrimaryButtonStyle");
        primary.SetAttributeValue("Opacity", opacity);

        var errors = AuditPage(train, document).ToArray();

        Assert.Contains(errors, error => error.Contains("visible/action-capable primary action", StringComparison.Ordinal));
    }

    [Fact]
    public void Momentum_home_artwork_columns_reserve_the_semantic_artwork_width_and_gap()
    {
        var train = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Train/TrainPage.xaml"));
        Assert.Empty(AuditArtworkGridGeometry("Features/Train/TrainPage.xaml", train));

        var mutated = new XDocument(train);
        var artwork = Named(mutated, "TrainAgainArtwork");
        artwork.SetAttributeValue("WidthRequest", "{DynamicResource TrackZExerciseCardHeight}");

        Assert.Contains(
            AuditArtworkGridGeometry("Features/Train/TrainPage.xaml", mutated),
            error => error.Contains("artwork width", StringComparison.Ordinal));

        var heightMutation = new XDocument(train);
        var heightArtwork = Named(heightMutation, "RecentMomentumArtwork");
        heightArtwork.SetAttributeValue("HeightRequest", "{DynamicResource TrackZExerciseCardHeight}");
        Assert.Contains(
            AuditArtworkGridGeometry("Features/Train/TrainPage.xaml", heightMutation),
            error => error.Contains("height and width", StringComparison.Ordinal));
    }

    [Fact]
    public void Momentum_home_artwork_audit_rejects_missing_fallback_or_decorative_exclusion()
    {
        var train = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Train/TrainPage.xaml"));
        Assert.Empty(AuditMomentumArtworkSemantics(train));

        var missingFallback = new XDocument(train);
        Named(missingFallback, "TrainAgainFallback").SetAttributeValue("Source", "another_image.png");
        Assert.Contains(
            AuditMomentumArtworkSemantics(missingFallback),
            error => error.Contains("shared exercise fallback", StringComparison.Ordinal));

        var accessibleDecoration = new XDocument(train);
        Named(accessibleDecoration, "RecentMomentumGlyph")
            .Attribute("AutomationProperties.ExcludedWithChildren")!
            .Remove();
        Assert.Contains(
            AuditMomentumArtworkSemantics(accessibleDecoration),
            error => error.Contains("decorative artwork", StringComparison.Ordinal));

        var duplicateDescription = new XDocument(train);
        Named(duplicateDescription, "TrainAgainFallback").SetAttributeValue(
            "SemanticProperties.Description",
            "{Binding RepeatWorkoutAccessibilityText}");
        Assert.Contains(
            AuditMomentumArtworkSemantics(duplicateDescription),
            error => error.Contains("one authoritative semantic description", StringComparison.Ordinal));
    }

    [Fact]
    public void Momentum_home_artwork_audit_rejects_swapped_or_detached_image_layers()
    {
        var train = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Train/TrainPage.xaml"));

        var swapped = new XDocument(train);
        var swappedFallback = Named(swapped, "TrainAgainFallback");
        var swappedThumbnail = Named(swapped, "TrainAgainThumbnail");
        swappedThumbnail.Remove();
        swappedFallback.AddBeforeSelf(swappedThumbnail);

        var detachedFallback = new XDocument(train);
        var fallbackArtwork = Named(detachedFallback, "TrainAgainArtwork");
        var fallback = Named(detachedFallback, "TrainAgainFallback");
        fallback.Remove();
        fallbackArtwork.AddAfterSelf(fallback);

        var detachedThumbnail = new XDocument(train);
        var thumbnailArtwork = Named(detachedThumbnail, "TrainAgainArtwork");
        var thumbnail = Named(detachedThumbnail, "TrainAgainThumbnail");
        thumbnail.Remove();
        thumbnailArtwork.AddAfterSelf(thumbnail);

        Assert.All(
            new[] { swapped, detachedFallback, detachedThumbnail },
            mutation => Assert.Contains(
                AuditMomentumArtworkSemantics(mutation),
                error => error.Contains("direct fallback-first image layer", StringComparison.Ordinal)));
    }

    [Fact]
    public void Momentum_home_audit_rejects_metric_height_and_section_order_mutations()
    {
        const string trainPath = "Features/Train/TrainPage.xaml";
        var controls = XDocument.Load(Path.Combine(MobileDirectory(), "Resources/Styles/TrackZControls.xaml"));
        Assert.Empty(AuditHomeMetricStyle(controls));
        var metricStyle = Style(controls, "TrackZHomeMetricCardStyle");
        Setter(metricStyle, "MinimumHeightRequest").SetAttributeValue("Value", "72");
        Assert.Contains(
            AuditHomeMetricStyle(controls),
            error => error.Contains("equal 76-point height", StringComparison.Ordinal));

        var train = XDocument.Load(Path.Combine(MobileDirectory(), trainPath));
        var repeat = Named(train, "TrainAgainCard").Parent!;
        repeat.Remove();
        Named(train, "HomeHero").AddBeforeSelf(repeat);
        Assert.Contains(
            AuditPage(trainPath, train),
            error => error.Contains("after the hero", StringComparison.Ordinal));
    }

    [Fact]
    public void Momentum_home_hero_states_keep_the_approved_ready_and_active_emphasis()
    {
        const string trainPath = "Features/Train/TrainPage.xaml";
        var train = XDocument.Load(Path.Combine(MobileDirectory(), trainPath));

        Assert.Empty(AuditMomentumHeroStates(train));

        var mutated = new XDocument(train);
        var ready = Assert.Single(mutated.Descendants(), element =>
            element.Name.LocalName == "VisualState" && ElementName(element) == "Ready");
        var background = Assert.Single(ready.Descendants(), element =>
            element.Name.LocalName == "Setter" &&
            element.Attribute("Property")?.Value == "BackgroundColor" &&
            element.Attribute("TargetName") is null);
        background.SetAttributeValue("Value", "{DynamicResource TrackZSurfaceRaised}");

        Assert.Contains(
            AuditMomentumHeroStates(mutated),
            error => error.Contains("lime ready hero", StringComparison.Ordinal));
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
        var languageCard = Assert.Single(page.Descendants(), element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "LanguageSettingsCard");
        var weightCard = Assert.Single(page.Descendants(), element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "WeightUnitSettingsCard");
        Assert.True(languageCard.IsBefore(weightCard));

        var thai = Assert.Single(page.Descendants(), element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "ThaiLanguageButton");
        var english = Assert.Single(page.Descendants(), element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "EnglishLanguageButton");
        Assert.Equal("TrackZSecondaryButtonStyle", ResourceKey(thai.Attribute("Style")?.Value));
        Assert.Equal("TrackZSecondaryButtonStyle", ResourceKey(english.Attribute("Style")?.Value));
        Assert.Equal("{Binding ChangeLanguageCommand}", thai.Attribute("Command")?.Value);
        Assert.Equal("{Binding ChangeLanguageCommand}", english.Attribute("Command")?.Value);
        Assert.Contains(thai.Descendants(), element => element.Name.LocalName == "DataTrigger" && element.Attribute("Binding")?.Value == "{Binding Languages[0].IsSelected}");
        Assert.Contains(english.Descendants(), element => element.Name.LocalName == "DataTrigger" && element.Attribute("Binding")?.Value == "{Binding Languages[1].IsSelected}");

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
    public void Profile_is_the_only_weight_unit_selector()
    {
        var profile = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Profile/ProfilePage.xaml"));
        var profileUnitButtons = profile.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => element.Attribute("Text")?.Value is
                "{Binding Source={x:Reference Page}, Path=WorkoutText.Kilograms}" or
                "{Binding Source={x:Reference Page}, Path=WorkoutText.Pounds}")
            .ToArray();
        Assert.Equal(2, profileUnitButtons.Length);

        var setEntry = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Workout/SetEntrySheetPage.xaml"));
        var duplicateUnitButtons = setEntry.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => element.Attribute("Command")?.Value is
                "{Binding UseKilogramsCommand}" or "{Binding UsePoundsCommand}")
            .ToArray();
        Assert.Empty(duplicateUnitButtons);
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
    public void Xp_surfaces_are_hidden_until_their_view_model_has_authoritative_progress_data()
    {
        var summary = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Summary/WorkoutSummaryPage.xaml"));
        AssertAuthoritativeVisibility(summary, "SummaryXpCard");
        AssertAuthoritativeVisibility(summary, "SummarySyncStatus");

        var progress = XDocument.Load(Path.Combine(MobileDirectory(), "Features/Progress/ExerciseProgressPage.xaml"));
        AssertAuthoritativeVisibility(progress, "ProgressSnapshotContent");
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
        var contentRoot = root.Elements().Single();
        var actualPaddingOwners = pagePadding
            .Select(attribute => $"{RelativeElementPath(contentRoot, attribute.Parent!)}={ResourceKey(attribute.Value)}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedPaddingOwners = ExpectedPagePaddingOwners[relativePath].Order(StringComparer.Ordinal).ToArray();
        if (!actualPaddingOwners.SequenceEqual(expectedPaddingOwners, StringComparer.Ordinal))
            yield return $"Page margin token must stay on each exact direct content-band owner. Expected [{string.Join(", ", expectedPaddingOwners)}], found [{string.Join(", ", actualPaddingOwners)}].";
        foreach (var padding in pagePadding)
        {
            if (padding.Parent!.Ancestors().Attributes("Padding").Any(attribute => PagePaddingKeys.Contains(ResourceKey(attribute.Value), StringComparer.Ordinal)))
                yield return "Page margin resources must not be nested or doubled.";
        }

        foreach (var error in AuditCommon(document))
            yield return error;
        foreach (var error in AuditArtworkGridGeometry(relativePath, document))
            yield return error;

        var usesVisibleNativeNavigationTitle = root.Attribute("Title") is not null &&
            !string.Equals(root.Attribute("Shell.NavBarIsVisible")?.Value, "False", StringComparison.Ordinal);
        if (!string.Equals(relativePath, "Features/Auth/AuthGatePage.xaml", StringComparison.Ordinal)
            && !usesVisibleNativeNavigationTitle)
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
        var actionCapablePrimaryActions = primaryActions.Where(IsStaticallyActionCapable).ToArray();
        var expectedPrimaryCount = ExpectedPrimaryActionCounts[relativePath];
        if (actionCapablePrimaryActions.Length != expectedPrimaryCount)
            yield return $"The page must expose exactly {expectedPrimaryCount} visible/action-capable primary action(s); one visible primary action is allowed unless complementary actions are explicitly allowlisted.";
        if (expectedPrimaryCount == 2 &&
            !primaryActions.Select(action => action.Attribute("IsVisible")?.Value).Order(StringComparer.Ordinal)
                .SequenceEqual(
                    (relativePath == "Features/Workout/SetLoggerPage.xaml"
                        ? new[] { "{Binding HasDraftSet}", "{Binding HasNoDraftSet}" }
                        : new[] { "{Binding HasStarted}", "{Binding IsDraft}" })
                    .Order(StringComparer.Ordinal),
                    StringComparer.Ordinal))
            yield return "Complementary primary actions must remain mutually exclusive through their exact phase visibility bindings.";

        if (string.Equals(relativePath, "Features/Train/TrainPage.xaml", StringComparison.Ordinal))
        {
            var hero = document.Descendants().SingleOrDefault(element => ElementName(element) == "HomeHero");
            if (hero is null || primaryActions.Any(action => !action.Ancestors().Contains(hero)))
                yield return "Momentum Home primary action must stay inside HomeHero.";

            var orderedNames = new[] { "HomeHero", "MotivationStrip", "TrainAgainCard", "RecentMomentumCard" };
            var descendants = document.Descendants().ToList();
            var orderedElements = orderedNames
                .Select(name => descendants.SingleOrDefault(element => ElementName(element) == name))
                .ToArray();
            if (orderedElements.Any(element => element is null) ||
                !orderedElements.Select(element => descendants.IndexOf(element!)).SequenceEqual(
                    orderedElements.Select(element => descendants.IndexOf(element!)).Order()))
                yield return "Momentum Home hierarchy must keep motivation, Train again, and recent momentum after the hero in the approved order.";

            foreach (var metricName in new[] { "WeeklyGoalMetric", "StreakMetric", "LevelMetric" })
            {
                var metric = descendants.SingleOrDefault(element => ElementName(element) == metricName);
                if (metric is null || ResourceKey(metric.Attribute("Style")?.Value) != "TrackZHomeMetricCardStyle")
                    yield return $"Momentum metric '{metricName}' must use the shared equal-height metric style.";
            }
        }

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

    private static IEnumerable<string> AuditHomeMetricStyle(XDocument controls)
    {
        var metric = controls.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "Style" && XamlKey(element) == "TrackZHomeMetricCardStyle");
        if (metric is null)
        {
            yield return "Momentum metrics need one shared semantic card style.";
            yield break;
        }

        var minimum = metric.Elements().SingleOrDefault(element =>
            element.Name.LocalName == "Setter" && element.Attribute("Property")?.Value == "MinimumHeightRequest")?
            .Attribute("Value")?.Value;
        if (minimum != "76")
            yield return "Momentum metrics must retain one equal 76-point height.";
    }

    private static IEnumerable<string> AuditMomentumHeroStates(XDocument document)
    {
        var hero = document.Descendants().SingleOrDefault(element => ElementName(element) == "HomeHero");
        if (hero is null)
        {
            yield return "Momentum Home needs a state-aware hero.";
            yield break;
        }

        var ready = hero.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "VisualState" && ElementName(element) == "Ready");
        var active = hero.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "VisualState" && ElementName(element) == "Active");
        if (ready is null || active is null)
        {
            yield return "Momentum Home needs approved Ready and Active hero states.";
            yield break;
        }

        static string? Value(XElement state, string property) =>
            state.Descendants().SingleOrDefault(element =>
                element.Name.LocalName == "Setter" &&
                element.Attribute("Property")?.Value == property)?
                .Attribute("Value")?.Value;

        if (ResourceKey(Value(ready, "BackgroundColor")) != "TrackZPrimary")
            yield return "Momentum Home must preserve the approved lime ready hero with its dark primary action.";

        if (ResourceKey(Value(active, "BackgroundColor")) != "TrackZSurface")
            yield return "Momentum Home must preserve the approved dark active hero with its lime primary action.";

        var primary = hero.Descendants().SingleOrDefault(element => ElementName(element) == "HeroActionButton");
        var readyAction = primary?.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "DataTrigger" &&
            element.Attribute("Binding")?.Value == "{Binding ShowStartHero}" &&
            element.Attribute("Value")?.Value == "True");
        if (primary is null || ResourceKey(primary.Attribute("Style")?.Value) != "TrackZPrimaryButtonStyle" ||
            readyAction is null ||
            ResourceKey(Value(readyAction, "BackgroundColor")) != "TrackZPrimaryContrast" ||
            ResourceKey(Value(readyAction, "TextColor")) != "TrackZPrimary")
            yield return "Momentum Home ready action must invert the shared lime primary style without creating another button.";

        foreach (var labelName in new[] { "HeroEyebrowLabel", "HeroTitleLabel", "HeroSupportingLabel" })
        {
            var label = hero.Descendants().SingleOrDefault(element => ElementName(element) == labelName);
            var trigger = label?.Descendants().SingleOrDefault(element =>
                element.Name.LocalName == "DataTrigger" &&
                element.Attribute("Binding")?.Value == "{Binding ShowStartHero}" &&
                element.Attribute("Value")?.Value == "True");
            if (trigger is null || ResourceKey(Value(trigger, "TextColor")) != "TrackZPrimaryContrast")
                yield return $"Momentum Home ready hero label '{labelName}' must use the high-contrast semantic color.";
        }
    }

    private static IEnumerable<string> AuditMomentumArtworkSemantics(XDocument document)
    {
        var artworks = document.Descendants().Where(element => ElementName(element) == "TrainAgainArtwork").ToArray();
        var grids = artworks.Length == 1
            ? artworks[0].Elements().Where(element => element.Name.LocalName == "Grid").ToArray()
            : [];
        var images = grids.Length == 1
            ? grids[0].Elements().Where(element => element.Name.LocalName == "Image").ToArray()
            : [];
        var fallbacks = document.Descendants().Where(element => ElementName(element) == "TrainAgainFallback").ToArray();
        var thumbnails = document.Descendants().Where(element => ElementName(element) == "TrainAgainThumbnail").ToArray();
        if (artworks.Length != 1 || grids.Length != 1 || images.Length != 2 ||
            fallbacks.Length != 1 || thumbnails.Length != 1 ||
            !ReferenceEquals(images[0], fallbacks[0]) || !ReferenceEquals(images[1], thumbnails[0]))
        {
            yield return "Train again artwork must contain one immediate Grid with one direct fallback-first image layer followed by its thumbnail.";
        }
        else if (images[0].Attribute("Source")?.Value != "exercise_placeholder.png" ||
                 images[1].Attribute("Source")?.Value != "{Binding RepeatWorkout.ThumbnailPath}")
        {
            yield return "Train again must layer the shared exercise fallback beneath its optional real thumbnail.";
        }

        foreach (var name in new[] { "TrainAgainArtwork", "TrainAgainChevron", "RecentMomentumArtwork", "RecentMomentumGlyph" })
        {
            var decoration = document.Descendants().SingleOrDefault(element => ElementName(element) == name);
            if (decoration?.Attribute("AutomationProperties.ExcludedWithChildren")?.Value != "True")
                yield return $"Momentum decorative artwork '{name}' must stay outside the accessibility tree.";
        }

        if (document.Descendants().Any(element => element.Attribute("Text")?.Value == "↻"))
            yield return "Train again must not use the raw refresh glyph instead of exercise artwork.";

        foreach (var (name, description) in new[]
        {
            ("TrainAgainCard", "{Binding RepeatWorkoutAccessibilityText}"),
            ("RecentMomentumCard", "{Binding RecentMomentumText}")
        })
        {
            var card = document.Descendants().SingleOrDefault(element => ElementName(element) == name);
            var descriptions = card?.DescendantsAndSelf()
                .SelectMany(element => element.Attributes("SemanticProperties.Description"))
                .ToArray() ?? [];
            if (descriptions.Length != 1 || descriptions[0].Parent != card || descriptions[0].Value != description)
                yield return $"Momentum card '{name}' must retain one authoritative semantic description.";
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

    private static IEnumerable<string> AuditArtworkGridGeometry(string relativePath, XDocument document)
    {
        if (!ArtworkContracts.TryGetValue(relativePath, out var contracts))
            yield break;

        foreach (var contract in contracts)
        {
            var artwork = document.Descendants().SingleOrDefault(element =>
                element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == contract.Name);
            if (artwork is null)
            {
                yield return $"Shipped artwork role '{contract.Name}' is missing from {relativePath}.";
                continue;
            }
            if (artwork.Name.LocalName != contract.ElementType ||
                ElementRolePath(artwork) != contract.Path ||
                contract.Style is not null && ResourceKey(artwork.Attribute("Style")?.Value) != contract.Style)
                yield return $"Shipped artwork role '{contract.Name}' must remain at its exact page path and semantic role.";

            var grid = ArtworkColumnGrid(artwork);
            if (grid is null)
            {
                yield return "Semantic exercise artwork must have a containing column grid.";
                continue;
            }

            var firstWidth = grid.Elements()
                .SingleOrDefault(element => element.Name.LocalName == "Grid.ColumnDefinitions")?
                .Elements().FirstOrDefault()?.Attribute("Width")?.Value
                ?? grid.Attribute("ColumnDefinitions")?.Value.Split(',').FirstOrDefault();
            var artworkWidth = ResolveDimension(artwork.Attribute("WidthRequest")?.Value);
            var artworkHeight = ResolveDimension(artwork.Attribute("HeightRequest")?.Value);
            var columnWidth = ResolveDimension(firstWidth);
            if (artworkWidth != 88d || columnWidth != artworkWidth)
                yield return $"Exercise artwork width must resolve to 88 and exactly match its reserved column; artwork '{artwork.Attribute("WidthRequest")?.Value ?? "missing"}', column '{firstWidth ?? "missing"}'.";
            if (string.Equals(relativePath, "Features/Train/TrainPage.xaml", StringComparison.Ordinal) &&
                (ResourceKey(artwork.Attribute("WidthRequest")?.Value) != "TrackZExerciseArtworkSize" ||
                 ResourceKey(firstWidth) != "TrackZExerciseArtworkColumnWidth"))
                yield return "Exercise artwork width and reserved column must use the semantic artwork tokens.";
            if (artworkHeight != artworkWidth)
                yield return $"Exercise artwork height and width must resolve to the same stable geometry; height '{artwork.Attribute("HeightRequest")?.Value ?? "missing"}', width '{artwork.Attribute("WidthRequest")?.Value ?? "missing"}'.";
            if (ResourceKey(grid.Attribute("ColumnSpacing")?.Value) != "TrackZSpace12")
                yield return "Exercise artwork column must retain the semantic 12-point text gap.";
        }
    }

    private static string ElementRolePath(XElement element) =>
        string.Join('/', element.AncestorsAndSelf().Reverse().Select(ancestor => ancestor.Name.LocalName));

    private static double? ResolveDimension(string? declaration)
    {
        if (double.TryParse(declaration, NumberStyles.Number, CultureInfo.InvariantCulture, out var literal))
            return literal;
        var key = ResourceKey(declaration);
        if (key is null) return null;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(MobileDirectory(), "Resources/Styles"), "*.xaml"))
        {
            var resource = XDocument.Load(file).Descendants().SingleOrDefault(element =>
                element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == key);
            if (double.TryParse(resource?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var resolved))
                return resolved;
        }
        return null;
    }

    private static XElement? ArtworkColumnGrid(XElement artwork) =>
        artwork.AncestorsAndSelf().FirstOrDefault(element =>
            element.Name.LocalName == "Grid" &&
            (element.Attribute("ColumnDefinitions") is not null ||
             element.Elements().Any(child => child.Name.LocalName == "Grid.ColumnDefinitions")));

    private sealed record ArtworkContract(string Name, string ElementType, string Path, string? Style);

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

    private static string RelativeElementPath(XElement root, XElement element)
    {
        if (element == root) return "$";
        var indices = new Stack<int>();
        for (var current = element; current != root; current = current.Parent!)
        {
            if (current.Parent is null) return "!";
            indices.Push(current.Parent.Elements().ToList().IndexOf(current));
        }
        return string.Join('.', indices);
    }

    private static bool ContainsDestructiveSignal(string value) =>
        new[] { "Delete", "Discard", "Remove", "SignOut" }
            .Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static bool IsStaticallyActionCapable(XElement element) =>
        !string.Equals(element.Attribute("IsVisible")?.Value, "False", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(element.Attribute("IsEnabled")?.Value, "False", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(element.Attribute("InputTransparent")?.Value, "True", StringComparison.OrdinalIgnoreCase) &&
        !IsStaticZero(element.Attribute("Opacity")?.Value);

    private static bool IsStaticZero(string? value) =>
        double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && number == 0d;

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

    private static string? ElementName(XElement element) =>
        element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value;

    private static string? XamlKey(XElement element) =>
        element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value;

    private static XElement Named(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element => ElementName(element) == name);

    private static XElement Style(XDocument document, string key) =>
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Style" && XamlKey(element) == key);

    private static XElement Setter(XElement style, string property) =>
        Assert.Single(style.Elements(), element =>
            element.Name.LocalName == "Setter" && element.Attribute("Property")?.Value == property);

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

    private static void AssertAuthoritativeVisibility(XDocument document, string name)
    {
        var element = Assert.Single(document.Descendants(), candidate =>
            candidate.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == name);
        Assert.Equal("{Binding HasAuthoritativeProgressData}", element.Attribute("IsVisible")?.Value);
    }

    private static IEnumerable<string> AuditAuthHeaderGeometry(XDocument signIn, XDocument create)
    {
        var signRoot = signIn.Root!.Elements().Single();
        var createRoot = create.Root!.Elements().Single();
        var signHeader = signRoot.Elements().First();
        var createHeader = createRoot.Elements().First();

        foreach (var (root, header) in new[] { (signRoot, signHeader), (createRoot, createHeader) })
        {
            var email = root.Descendants().Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "EmailFieldContainer");
            var submit = root.Descendants().Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "SubmitAction");
            var formBand = email.Ancestors().First(ancestor => ancestor.Parent == root);
            var footerBand = submit.Ancestors().First(ancestor => ancestor.Parent == root);
            if (root.Attribute("RowDefinitions")?.Value != "*,Auto" ||
                GridRow(header) != 0 || GridRow(formBand) != 0 || GridRow(footerBand) != 1 ||
                header.Attribute("VerticalOptions")?.Value != "Start" ||
                formBand.Attribute("VerticalOptions")?.Value != "Center")
                yield return "Auth pages must use the header-independent overlay: header/form row 0 with Start/Center alignment and sticky footer row 1.";
        }
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
