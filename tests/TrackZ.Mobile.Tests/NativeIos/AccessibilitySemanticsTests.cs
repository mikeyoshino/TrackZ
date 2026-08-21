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
