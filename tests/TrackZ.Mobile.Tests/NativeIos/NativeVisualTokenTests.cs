using Microsoft.Maui.Controls;
using RoundRectangle = Microsoft.Maui.Controls.Shapes.RoundRectangle;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Xml.Linq;
using TrackZ.Mobile;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class NativeVisualTokenTests
{
    [Theory]
    [InlineData("TrackZSpace4", 4d)]
    [InlineData("TrackZSpace8", 8d)]
    [InlineData("TrackZSpace12", 12d)]
    [InlineData("TrackZSpace16", 16d)]
    [InlineData("TrackZSpace24", 24d)]
    [InlineData("TrackZSpace32", 32d)]
    [InlineData("TrackZPageMargin", 18d)]
    [InlineData("TrackZCardRadius", 16d)]
    [InlineData("TrackZPrimaryActionHeight", 52d)]
    [InlineData("TrackZMinimumTarget", 44d)]
    [InlineData("TrackZFieldHeight", 50d)]
    public void Spacing_and_geometry_tokens_are_exact(string key, double expected)
    {
        using var app = CreateApp();

        Assert.Equal(expected, Assert.IsType<double>(Resources(app)[key]));
    }

    [Fact]
    public void Shared_controls_meet_native_geometry()
    {
        using var app = CreateApp();

        AssertStyle<Button>(app, "TrackZPrimaryButtonStyle", ("MinimumHeightRequest", 52d), ("CornerRadius", 15));
        AssertStyle<Button>(app, "TrackZSecondaryButtonStyle", ("MinimumHeightRequest", 44d));
        AssertStyle<Button>(app, "TrackZDestructiveButtonStyle", ("MinimumHeightRequest", 44d));
        AssertStyle<Entry>(app, "TrackZFieldStyle", ("MinimumHeightRequest", 50d));
        AssertStyle<Border>(app, "TrackZFieldContainerStyle", ("MinimumHeightRequest", 50d));
        AssertStyle<Border>(app, "TrackZCardStyle", ("StrokeShape", "RoundRectangle 16"));
        AssertStyle<Border>(app, "TrackZListRowStyle", ("StrokeShape", "RoundRectangle 16"));

        var fieldMinimumHeight = Assert.IsType<double>(SetterValue<Entry>(app, "TrackZFieldStyle", "MinimumHeightRequest"));
        var fieldContainerMinimumHeight = Assert.IsType<double>(SetterValue<Border>(app, "TrackZFieldContainerStyle", "MinimumHeightRequest"));
        var fieldContainerPadding = AsThickness(SetterValue<Border>(app, "TrackZFieldContainerStyle", "Padding"));
        Assert.Equal(50d, fieldMinimumHeight);
        Assert.Equal(0d, fieldContainerPadding.Top);
        Assert.Equal(0d, fieldContainerPadding.Bottom);
        Assert.Equal(50d, Math.Max(fieldContainerMinimumHeight, fieldMinimumHeight + fieldContainerPadding.Top + fieldContainerPadding.Bottom));

        var primaryPadding = Assert.IsType<Thickness>(SetterValue<Button>(app, "TrackZPrimaryButtonStyle", "Padding"));
        Assert.Equal(16, primaryPadding.Left);
        Assert.Equal(12, primaryPadding.Top);
        var stickyPadding = Assert.IsType<Thickness>(SetterValue<Border>(app, "TrackZStickyActionContainerStyle", "Padding"));
        Assert.Equal(18, stickyPadding.Left);
        Assert.Equal(18, stickyPadding.Right);
        Assert.Equal(12, stickyPadding.Top);
        Assert.Equal(12, stickyPadding.Bottom);
    }

    [Fact]
    public void Semantic_typography_and_lime_roles_are_exact()
    {
        using var app = CreateApp();

        AssertStyle<Label>(app, "TrackZPageTitleStyle", ("FontSize", 32d), ("FontAttributes", FontAttributes.Bold));
        AssertStyle<Label>(app, "TrackZNavigationTitleStyle", ("FontSize", 17d), ("FontAttributes", FontAttributes.Bold));
        AssertStyle<Label>(app, "TrackZSectionTitleStyle", ("FontSize", 20d), ("FontAttributes", FontAttributes.Bold));
        AssertStyle<Label>(app, "TrackZBodyStyle", ("FontSize", 15d), ("FontAttributes", FontAttributes.None));
        AssertStyle<Label>(app, "TrackZSecondaryStyle", ("FontSize", 13d), ("FontAttributes", FontAttributes.None));
        AssertStyle<Label>(app, "TrackZFieldErrorStyle", ("FontSize", 12d), ("FontAttributes", FontAttributes.None));
        AssertStyle<Label>(app, "TrackZPerformanceMetadataStyle", ("FontSize", 13d), ("FontAttributes", FontAttributes.None));

        var primary = Assert.IsType<Color>(Resources(app)["TrackZPrimary"]);
        Assert.Equal(primary, SetterValue<Label>(app, "TrackZPerformanceNumberStyle", "TextColor"));
        foreach (var style in new[] { "TrackZPageTitleStyle", "TrackZNavigationTitleStyle", "TrackZSectionTitleStyle", "TrackZBodyStyle", "TrackZSecondaryStyle", "TrackZFieldErrorStyle", "TrackZPerformanceMetadataStyle" })
            Assert.NotEqual(primary, SetterValue<Label>(app, style, "TextColor"));
    }

    [Fact]
    public void Disabled_implicit_control_states_use_semantic_neutral_resources_in_both_themes()
    {
        using var app = CreateApp();
        var disabledText = Assert.IsType<Color>(Resources(app)["TrackZDisabledText"]);
        var disabledSurface = Assert.IsType<Color>(Resources(app)["TrackZDisabledSurface"]);

        AssertDisabledState(app, typeof(Button), ("TextColor", disabledText), ("BackgroundColor", disabledSurface));
        AssertDisabledState(app, typeof(Entry), ("TextColor", disabledText), ("PlaceholderColor", disabledText));
        AssertDisabledState(app, typeof(SearchBar), ("TextColor", disabledText), ("PlaceholderColor", disabledText));
    }

    [Fact]
    public void Audited_components_resolve_native_spacing_artwork_targets_and_card_geometry()
    {
        using var app = CreateApp();
        var componentDirectory = Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile", "Components");
        var components = new[]
        {
            "TrackZStateView.xaml", "ExerciseListSkeleton.xaml", "ExercisePerformanceCard.xaml",
            "ActiveWorkoutExerciseRow.xaml", "RepsStepper.xaml", "WeightStepper.xaml", "SyncStatusPill.xaml"
        };

        foreach (var component in components)
        {
            var document = XDocument.Load(Path.Combine(componentDirectory, component));
            AssertNativeSpacing(app, document);

            foreach (var button in document.Descendants().Where(element => element.Name.LocalName == "Button"))
            {
                var styleKey = ResourceKey(button.Attribute("Style")?.Value);
                Assert.False(string.IsNullOrEmpty(styleKey), $"{component} buttons must use a semantic style.");
                Assert.True(Convert.ToDouble(SetterValue<Button>(app, styleKey!, "MinimumHeightRequest"), CultureInfo.InvariantCulture) >= 44);
                Assert.True(Convert.ToDouble(SetterValue<Button>(app, styleKey!, "MinimumWidthRequest"), CultureInfo.InvariantCulture) >= 44);
            }
        }

        Assert.Equal(88d, Assert.IsType<double>(Resources(app)["TrackZExerciseArtworkSize"]));
        Assert.Equal(88d, Assert.IsType<double>(Resources(app)["TrackZExerciseArtworkColumnWidth"]));
        Assert.Equal(112d, Assert.IsType<double>(Resources(app)["TrackZExerciseCardHeight"]));
        var rowPadding = AsThickness(SetterValue<Border>(app, "TrackZListRowStyle", "Padding"));
        Assert.Equal(12, rowPadding.Top);
        Assert.Equal(12, rowPadding.Bottom);
        Assert.True(88 + rowPadding.Top + rowPadding.Bottom <= 112);
        foreach (var component in new[] { "ExercisePerformanceCard.xaml", "ActiveWorkoutExerciseRow.xaml", "ExerciseListSkeleton.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(componentDirectory, component));
            Assert.Contains("ColumnDefinitions=\"88", xaml, StringComparison.Ordinal);
            Assert.Contains("TrackZExerciseArtworkSize", xaml, StringComparison.Ordinal);
        }

        var skeleton = XDocument.Load(Path.Combine(componentDirectory, "ExerciseListSkeleton.xaml"));
        var loadedCards = new[] { "ExercisePerformanceCard.xaml", "ActiveWorkoutExerciseRow.xaml" }
            .Select(component => XDocument.Load(Path.Combine(componentDirectory, component)));
        Assert.Equal(3, skeleton.Descendants().Count(element => element.Name.LocalName == "Border" && element.Attribute("Style")?.Value.Contains("TrackZListRowStyle", StringComparison.Ordinal) == true));
        foreach (var loaded in loadedCards)
            Assert.Equal(1, loaded.Descendants().Count(element => element.Name.LocalName == "Border" && element.Attribute("Style")?.Value.Contains("TrackZListRowStyle", StringComparison.Ordinal) == true));
        foreach (var card in skeleton.Descendants().Where(element => element.Name.LocalName == "Border").Concat(loadedCards.SelectMany(document => document.Descendants().Where(element => element.Name.LocalName == "Border"))))
        {
            var style = card.Attribute("Style")?.Value;
            if (style is null || !style.Contains("TrackZListRowStyle", StringComparison.Ordinal)) continue;
            Assert.Equal("{DynamicResource TrackZExerciseCardHeight}", card.Attribute("MinimumHeightRequest")?.Value);
            Assert.Null(card.Attribute("StrokeShape"));
            Assert.Null(card.Attribute("CornerRadius"));
        }

        var statusPadding = Assert.IsType<Thickness>(SetterValue<Border>(app, "TrackZStatusPillStyle", "Padding"));
        Assert.Equal(12, statusPadding.Left);
        Assert.Equal(12, statusPadding.Right);
        Assert.Equal(4, statusPadding.Top);
        Assert.Equal(4, statusPadding.Bottom);
    }

    [Fact]
    public void Shared_component_xaml_uses_semantic_native_resources()
    {
        var components = new[]
        {
            "TrackZStateView.xaml", "ExerciseListSkeleton.xaml", "ExercisePerformanceCard.xaml",
            "ActiveWorkoutExerciseRow.xaml", "RepsStepper.xaml", "WeightStepper.xaml", "SyncStatusPill.xaml"
        };
        var componentDirectory = Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile", "Components");

        foreach (var component in components)
        {
            var xaml = File.ReadAllText(Path.Combine(componentDirectory, component));
            Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", xaml);
            Assert.DoesNotContain("FontAutoScalingEnabled=\"False\"", xaml, StringComparison.Ordinal);
        }

        var state = File.ReadAllText(Path.Combine(componentDirectory, "TrackZStateView.xaml"));
        Assert.Contains("TrackZSecondaryButtonStyle", state, StringComparison.Ordinal);

        var skeleton = File.ReadAllText(Path.Combine(componentDirectory, "ExerciseListSkeleton.xaml"));
        Assert.Contains("TrackZSkeletonStyle", skeleton, StringComparison.Ordinal);
        Assert.True(CountOccurrences(skeleton, "TrackZSkeletonStyle") >= 3);

        foreach (var stepper in new[] { "RepsStepper.xaml", "WeightStepper.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(componentDirectory, stepper));
            Assert.Equal(1, CountOccurrences(xaml, "SemanticProperties.Description=\"{Binding DecrementLabel"));
            Assert.Equal(1, CountOccurrences(xaml, "SemanticProperties.Description=\"{Binding IncrementLabel"));
            Assert.Equal(2, CountOccurrences(xaml, "TrackZSecondaryButtonStyle"));
            Assert.DoesNotContain("CornerRadius=", xaml, StringComparison.Ordinal);
        }

        var cards = string.Concat(
            File.ReadAllText(Path.Combine(componentDirectory, "ExercisePerformanceCard.xaml")),
            File.ReadAllText(Path.Combine(componentDirectory, "ActiveWorkoutExerciseRow.xaml")));
        Assert.DoesNotContain("FontSize=\"17\"", cards, StringComparison.Ordinal);
        Assert.DoesNotContain("TextColor=\"{DynamicResource TrackZPrimary}\"", cards, StringComparison.Ordinal);
    }

    [Fact]
    public void Audited_components_and_semantic_styles_use_only_the_native_spacing_scale_and_no_direct_lime_body_copy()
    {
        using var app = CreateApp();
        var solution = FindSolutionDirectory();
        var componentDirectory = Path.Combine(solution, "src", "TrackZ.Mobile", "Components");
        var semanticStyles = XDocument.Load(Path.Combine(solution, "src", "TrackZ.Mobile", "Resources", "Styles", "TrackZControls.xaml"));
        var componentFiles = new[]
        {
            "TrackZStateView.xaml", "ExerciseListSkeleton.xaml", "ExercisePerformanceCard.xaml",
            "ActiveWorkoutExerciseRow.xaml", "RepsStepper.xaml", "WeightStepper.xaml", "SyncStatusPill.xaml"
        };

        foreach (var document in componentFiles.Select(component => XDocument.Load(Path.Combine(componentDirectory, component))).Append(semanticStyles))
            AssertNativeSpacing(app, document);

        var primary = Assert.IsType<Color>(Resources(app)["TrackZPrimary"]);
        foreach (var component in componentFiles)
        {
            var document = XDocument.Load(Path.Combine(componentDirectory, component));
            foreach (var label in document.Descendants().Where(element => element.Name.LocalName == "Label"))
            {
                var style = ResourceKey(label.Attribute("Style")?.Value);
                if (style is "TrackZPerformanceNumberStyle") continue;
                var directColor = ResourceKey(label.Attribute("TextColor")?.Value);
                Assert.NotEqual("TrackZPrimary", directColor);
                if (style is not null)
                    Assert.NotEqual(primary, SetterValue<Label>(app, style, "TextColor"));
            }
        }
    }

    [Theory]
    [InlineData("<Root ColumnSpacing=\"3\" />", false)]
    [InlineData("<Root Padding=\"3\" />", false)]
    [InlineData("<Root Spacing=\"0\" />", false)]
    [InlineData("<Root Padding=\"12,0\" />", true)]
    [InlineData("<Root Padding=\"12,3\" />", false)]
    public void Native_spacing_audit_applies_property_specific_zero_rules(string xaml, bool shouldPass)
    {
        using var app = CreateApp();

        if (shouldPass)
            AssertNativeSpacing(app, XDocument.Parse(xaml));
        else
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNativeSpacing(app, XDocument.Parse(xaml)));
    }

    private static void AssertStyle<T>(MauiApp app, string key, params (string Property, object Expected)[] expectedSetters)
        where T : BindableObject
    {
        var style = Assert.IsType<Style>(Resources(app)[key]);
        Assert.Equal(typeof(T), style.TargetType);

        foreach (var (property, expected) in expectedSetters)
        {
            var setter = FindSetter(style, property);
            Assert.NotNull(setter);
            if (expected is "RoundRectangle 16")
            {
                var shape = Assert.IsType<RoundRectangle>(setter.Value);
                Assert.Equal(16, shape.CornerRadius.TopLeft);
            }
            else if (expected is string expectedText)
                Assert.Equal(expectedText, setter.Value?.ToString());
            else
                Assert.Equal(expected, setter.Value);
        }
    }

    private static object? SetterValue<T>(MauiApp app, string key, string property)
        where T : BindableObject
    {
        var style = Assert.IsType<Style>(Resources(app)[key]);
        Assert.Equal(typeof(T), style.TargetType);
        var setter = FindSetter(style, property);
        Assert.NotNull(setter);
        return setter.Value;
    }

    private static Thickness AsThickness(object? value) => value switch
    {
        Thickness thickness => thickness,
        double uniform => new Thickness(uniform),
        _ => throw new Xunit.Sdk.XunitException($"Expected a Thickness or uniform spacing value, but got {value?.GetType().Name ?? "null"}.")
    };

    private static void AssertDisabledState(MauiApp app, Type targetType, params (string Property, Color Expected)[] expected)
    {
        var style = Assert.Single(
            AllResources(Resources(app)).SelectMany(resources => resources.Values).OfType<Style>(),
            style => style.TargetType == targetType && FindSetter(style, "VisualStateGroups") is not null);
        var stateSetter = FindSetter(style, "VisualStateGroups");
        Assert.NotNull(stateSetter);
        var stateGroups = Assert.IsType<VisualStateGroupList>(stateSetter.Value);
        var disabled = Assert.Single(Assert.Single(stateGroups).States, state => state.Name == "Disabled");

        foreach (var (property, color) in expected)
        {
            var setter = Assert.Single(disabled.Setters, candidate => candidate.Property.PropertyName == property);
            AssertThemeBindingColor(setter.Value, color);
        }
    }

    private static void AssertThemeBindingColor(object? value, Color expected)
    {
        Assert.NotNull(value);
        var type = value.GetType();
        var light = type.GetProperty("Light")?.GetValue(value);
        var dark = type.GetProperty("Dark")?.GetValue(value);
        Assert.Equal(expected, Assert.IsType<Color>(light));
        Assert.Equal(expected, Assert.IsType<Color>(dark));
    }

    private static void AssertNativeSpacing(MauiApp app, XDocument document)
    {
        var allowed = new HashSet<double> { 4, 8, 12, 16, 24, 32 };
        var allowedPadding = new HashSet<double>(allowed) { 0 };
        foreach (var (property, value, stickyActionContainer) in SpacingValues(document))
        {
            var parts = ResolveThicknessParts(app, value).ToArray();
            if (stickyActionContainer && property == "Padding")
            {
                Assert.Equal([18d, 12d], parts);
                continue;
            }

            Assert.All(parts, part => Assert.Contains(part, property == "Padding" ? allowedPadding : allowed));
        }
    }

    private static IEnumerable<(string Property, string Value, bool StickyActionContainer)> SpacingValues(XDocument document)
    {
        foreach (var element in document.Root?.DescendantsAndSelf() ?? [])
        {
            var stickyActionContainer = element.Name.LocalName == "Style" &&
                element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "TrackZStickyActionContainerStyle");
            foreach (var attribute in element.Attributes().Where(attribute => attribute.Name.LocalName is "Spacing" or "RowSpacing" or "ColumnSpacing" or "Padding"))
                yield return (attribute.Name.LocalName, attribute.Value, stickyActionContainer);

            if (element.Name.LocalName != "Setter") continue;
            var property = element.Attribute("Property")?.Value;
            if (property is not ("Spacing" or "RowSpacing" or "ColumnSpacing" or "Padding")) continue;
            var value = element.Attribute("Value")?.Value;
            if (value is not null)
                yield return (property, value, element.Parent?.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "TrackZStickyActionContainerStyle") == true);
        }
    }

    private static IEnumerable<double> ResolveThicknessParts(MauiApp app, string value)
    {
        if (value.StartsWith("{DynamicResource ", StringComparison.Ordinal) || value.StartsWith("{StaticResource ", StringComparison.Ordinal))
        {
            var key = ResourceKey(value);
            yield return Assert.IsType<double>(Resources(app)[key!]);
            yield break;
        }

        foreach (var part in value.Split(','))
            yield return double.Parse(part, CultureInfo.InvariantCulture);
    }

    private static string? ResourceKey(string? value) =>
        value is null || !value.EndsWith('}') ? null : value switch
        {
            var dynamicResource when dynamicResource.StartsWith("{DynamicResource ", StringComparison.Ordinal) => dynamicResource["{DynamicResource ".Length..^1],
            var staticResource when staticResource.StartsWith("{StaticResource ", StringComparison.Ordinal) => staticResource["{StaticResource ".Length..^1],
            _ => null
        };

    private static IEnumerable<ResourceDictionary> AllResources(ResourceDictionary resources)
    {
        yield return resources;
        foreach (var merged in resources.MergedDictionaries)
            foreach (var dictionary in AllResources(merged))
                yield return dictionary;
    }

    private static ResourceDictionary Resources(MauiApp app) =>
        app.Services.GetRequiredService<App>().Resources;

    private static Setter? FindSetter(Style style, string property)
    {
        for (Style? candidate = style; candidate is not null; candidate = candidate.BasedOn)
        {
            var setter = candidate.Setters.FirstOrDefault(item => item.Property.PropertyName == property);
            if (setter is not null) return setter;
        }

        return null;
    }

    private static MauiApp CreateApp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-native-tokens-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton(new ProgressSnapshotCache(Path.Combine(root, "progress.json")));
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IConnectivityService>(new HeadlessConnectivity());
        });
    }

    private sealed class HeadlessThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class HeadlessConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

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

    private static int CountOccurrences(string value, string match) =>
        value.Split(match, StringSplitOptions.None).Length - 1;
}
