using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using System.Globalization;
using System.Xml.Linq;
using TrackZ.Mobile;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Features.Shared;
using TrackZ.Mobile.Features.Train;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class AccessibilitySemanticsTests
{
    [Fact]
    public void Increment_and_decrement_actions_are_distinct_and_primary_targets_are_at_least_44_points()
    {
        var text = WorkoutResources.English;
        Assert.NotEqual(text.DecreaseWeight, text.IncreaseWeight);
        Assert.NotEqual(text.DecreaseAssistance, text.IncreaseAssistance);
        Assert.NotEqual(text.DecreaseReps, text.IncreaseReps);
        Assert.True(NativeAccessibility.MinimumActionTarget >= 44);
    }

    [Fact]
    public void Stepper_xaml_keeps_localized_action_specific_descriptions()
    {
        var componentDirectory = Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile", "Components");

        foreach (var component in new[] { "WeightStepper.xaml", "RepsStepper.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(componentDirectory, component));
            Assert.Contains("SemanticProperties.Description=\"{Binding DecrementLabel", xaml, StringComparison.Ordinal);
            Assert.Contains("SemanticProperties.Description=\"{Binding IncrementLabel", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Momentum_home_uses_localized_semantic_bindings_and_shared_44_point_targets()
    {
        using var app = CreateApp();
        _ = app.Services.GetRequiredService<TrackZ.Mobile.App>();
        var page = app.Services.GetRequiredService<TrainPage>();
        var primary = Assert.IsType<Button>(page.FindByName("HeroActionButton"));
        var retry = Assert.IsType<Button>(page.FindByName("HomeRetryButton"));
        var summary = Assert.IsType<Button>(page.FindByName("ViewSummaryAction"));
        var history = Assert.IsType<Button>(page.FindByName("ViewHistoryAction"));

        Assert.Equal(primary.Text, SemanticProperties.GetDescription(primary));
        Assert.Equal(retry.Text, SemanticProperties.GetDescription(retry));
        Assert.Equal(page.Copy.ViewSummary, SemanticProperties.GetDescription(summary));
        Assert.Equal(page.Copy.ViewAll, SemanticProperties.GetDescription(history));
        Assert.Equal(page.Copy.Loading, SemanticProperties.GetDescription(
            Assert.IsType<ActivityIndicator>(page.FindByName("HomeLoadingIndicator"))));
        Assert.All(new[] { primary, retry, summary, history }, button =>
        {
            Assert.True(button.MinimumHeightRequest >= 44);
            Assert.True(button.MinimumWidthRequest >= 44);
        });

        var latest = Assert.IsType<Border>(page.FindByName("LatestWorkoutCard"));
        var artwork = Assert.Single(Descendants(latest).OfType<Border>());
        Assert.True(AutomationProperties.GetExcludedWithChildren(artwork));
        Assert.Null(SemanticProperties.GetDescription(latest));
        Assert.True(NativeAccessibility.MinimumActionTarget >= 44);

        var thai = WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("th-TH"));
        Assert.NotEqual(WorkoutResources.English.ContinueWorkout, thai.ContinueWorkout);
        Assert.NotEqual(
            HomeCopy.ForCulture(CultureInfo.GetCultureInfo("en-US")).Loading,
            HomeCopy.ForCulture(CultureInfo.GetCultureInfo("th-TH")).Loading);
    }

    [Fact]
    public void Effort_sheet_exposes_state_actions_with_localized_semantics_and_native_targets()
    {
        var document = XDocument.Load(Path.Combine(
            FindSolutionDirectory(),
            "src",
            "TrackZ.Mobile",
            "Features",
            "Workout",
            "SetEffortSheetPage.xaml"));
        var heading = document.Descendants().Single(element => Name(element) == "EffortSheetHeading");
        Assert.Equal("Level2", heading.Attribute("SemanticProperties.HeadingLevel")?.Value);

        var actions = new (string Name, string Style)[]
        {
            ("EffortEasyAction", "TrackZSecondaryButtonStyle"),
            ("EffortProductiveAction", "TrackZSecondaryButtonStyle"),
            ("EffortTooHeavyAction", "TrackZSecondaryButtonStyle"),
            ("EffortSkipAction", "TrackZQuietButtonStyle"),
            ("EffortRetryAction", "TrackZSecondaryButtonStyle"),
            ("EffortFailureNotNowAction", "TrackZQuietButtonStyle"),
            ("EffortUnavailableCloseAction", "TrackZQuietButtonStyle"),
            ("GuidanceSaveIncrementAction", "TrackZPrimaryButtonStyle"),
            ("GuidanceIncrementNotNowAction", "TrackZQuietButtonStyle"),
            ("GuidanceUseAction", "TrackZPrimaryButtonStyle"),
            ("GuidanceEditIncrementAction", "TrackZQuietButtonStyle"),
            ("GuidanceNotNowAction", "TrackZSecondaryButtonStyle")
        };
        foreach (var (name, expectedStyle) in actions)
        {
            var button = document.Descendants().Single(element => Name(element) == name);
            Assert.Equal($"{{DynamicResource {expectedStyle}}}", button.Attribute("Style")?.Value);
            Assert.NotNull(button.Attribute("SemanticProperties.Description"));
        }

        Assert.Equal("{Binding NeedsIncrement}",
            document.Descendants().Single(element =>
                Name(element) == "GuidanceSaveIncrementAction")
                .Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding HasUseAction}",
            document.Descendants().Single(element =>
                Name(element) == "GuidanceUseAction")
                .Attribute("IsVisible")?.Value);

        var incrementState = document.Descendants().Single(element =>
            Name(element) == "EffortIncrementState");
        var incrementFinalActions = incrementState.Elements()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();
        Assert.Equal(
            ["GuidanceSaveIncrementAction", "GuidanceIncrementNotNowAction"],
            incrementFinalActions.Select(Name));
        Assert.Equal("{Binding NotNowCommand}",
            incrementFinalActions[1].Attribute("Command")?.Value);
        Assert.Equal("{Binding Text.NotNow}",
            incrementFinalActions[1]
                .Attribute("SemanticProperties.Description")?.Value);
        Assert.Equal("{Binding Text.NotNow}",
            incrementFinalActions[1].Attribute("Text")?.Value);

        var incrementInput = document.Descendants().Single(element =>
            Name(element) == "GuidanceIncrementInput");
        Assert.Equal("{DynamicResource TrackZFieldStyle}",
            incrementInput.Attribute("Style")?.Value);
        Assert.Equal("{Binding Text.GuidanceIncrementTitle}",
            incrementInput.Attribute("SemanticProperties.Description")?.Value);

        using var app = CreateApp();
        var resources = app.Services.GetRequiredService<TrackZ.Mobile.App>().Resources;
        foreach (var styleKey in actions.Select(action => action.Style).Distinct())
        {
            Assert.True(StyleDimension(resources, styleKey, "MinimumHeightRequest") >= 44);
            Assert.True(StyleDimension(resources, styleKey, "MinimumWidthRequest") >= 44);
        }
        Assert.True(StyleDimension(resources, "TrackZFieldStyle", "MinimumHeightRequest") >= 44);

        var presentationBindings = new HashSet<string>(StringComparer.Ordinal)
        {
            "{Binding Label}",
            "{Binding IncrementInput, Mode=TwoWay}",
            "{Binding IncrementUnitLabel}",
            "{Binding IncrementValidationMessage}",
            "{Binding RecommendationTitle}",
            "{Binding RecommendationReason}",
            "{Binding UseActionText}"
        };
        var visibleCopy = document.Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName is "Text" or "Placeholder")
            .Select(attribute => attribute.Value)
            .ToArray();
        Assert.All(visibleCopy, value => Assert.True(
            value.StartsWith("{Binding Text.", StringComparison.Ordinal)
            || presentationBindings.Contains(value),
            $"Unexpected visible copy binding: {value}"));
        Assert.DoesNotContain(visibleCopy, value =>
            value.Contains("RIR", StringComparison.OrdinalIgnoreCase)
            || value.Contains("score", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            document.Descendants().Where(element => element.Name.LocalName == "Label"),
            label => label.Attribute("Text")?.Value.Any(char.IsDigit) == true);
    }

    private static double StyleDimension(
        ResourceDictionary resources,
        string styleKey,
        string property)
    {
        var style = Assert.IsType<Style>(resources[styleKey]);
        for (Style? candidate = style; candidate is not null; candidate = candidate.BasedOn)
        {
            var setter = candidate.Setters.FirstOrDefault(item =>
                item.Property.PropertyName == property);
            if (setter is not null)
                return Convert.ToDouble(setter.Value, CultureInfo.InvariantCulture);
        }

        throw new Xunit.Sdk.XunitException($"{styleKey} does not define {property}.");
    }

    private static string? Name(XElement element) =>
        element.Attribute(XName.Get(
            "Name",
            "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value;

    private static IEnumerable<Element> Descendants(IVisualTreeElement root)
    {
        foreach (var child in root.GetVisualChildren().OfType<Element>())
        {
            yield return child;
            if (child is IVisualTreeElement tree)
                foreach (var descendant in Descendants(tree))
                    yield return descendant;
        }
    }

    private static MauiApp CreateApp()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"trackz-effort-sheet-accessibility-{Guid.NewGuid():N}");
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
        public Task<string?> CacheAsync(
            string? thumbnailUri,
            CancellationToken cancellationToken = default) =>
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
}
