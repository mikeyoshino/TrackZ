using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Features.Shared;

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
        var mobileDirectory = Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile");
        var xaml = File.ReadAllText(Path.Combine(mobileDirectory, "Features", "Train", "TrainPage.xaml"));
        var controls = File.ReadAllText(Path.Combine(mobileDirectory, "Resources", "Styles", "TrackZControls.xaml"));

        Assert.Contains("x:Name=\"HeroActionButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.Description=\"{Binding HeroActionText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.Description=\"{Binding RepeatWorkoutAccessibilityText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.Description=\"{Binding RecentMomentumText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HomeRetryButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.Description=\"{Binding Text.TryAgain}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{DynamicResource TrackZSecondaryButtonStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TrainAgainCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RecentMomentumCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TrackZPrimaryButtonStyle", controls, StringComparison.Ordinal);
        Assert.Contains("TrackZExerciseCardHeight", controls, StringComparison.Ordinal);
        Assert.True(NativeAccessibility.MinimumActionTarget >= 44);

        var thai = WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("th-TH"));
        Assert.NotEqual(WorkoutResources.English.ContinueWorkout, thai.ContinueWorkout);
        Assert.NotEqual(WorkoutResources.English.RepeatWorkoutAccessibilityFormat, thai.RepeatWorkoutAccessibilityFormat);
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
