using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Workout;
using System.Xml.Linq;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class ActiveWorkoutExerciseRowTests
{
    [Fact]
    public void Remove_action_executes_with_a_short_swipe_and_uses_compact_copy()
    {
        var component = XDocument.Load(Path.Combine(
            Root(), "src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.xaml"));
        var swipeView = component.Descendants().Single(element => element.Name.LocalName == "SwipeView");
        var rightItems = component.Descendants().Single(element =>
            element.Name.LocalName == "SwipeView.RightItems");
        var swipeItems = rightItems.Elements().Single(element => element.Name.LocalName == "SwipeItems");
        var action = swipeItems.Elements().Single();
        var actionLayout = action.Elements().Single();
        var label = actionLayout.Descendants().Single(element => element.Name.LocalName == "Label");

        Assert.Equal("48", swipeView.Attribute("Threshold")?.Value);
        Assert.Equal("Execute", swipeItems.Attribute("Mode")?.Value);
        Assert.Equal("SwipeItemView", action.Name.LocalName);
        Assert.Equal("72", actionLayout.Attribute("WidthRequest")?.Value);
        Assert.Equal("44", actionLayout.Attribute("MinimumHeightRequest")?.Value);
        Assert.Equal("14", label.Attribute("FontSize")?.Value);
        Assert.Equal("{Binding RemoveText, Source={x:Reference Root}}", label.Attribute("Text")?.Value);
    }

    [Fact]
    public void Active_row_copy_is_descriptive_not_a_planned_total()
    {
        var row = new WorkoutExerciseDraftItem(
            Guid.NewGuid(),
            "Shoulder Press",
            TrackingMode.Weighted,
            "Weight",
            "/cache/press.jpg",
            LoggedSetCount: 2,
            LoggedSetText: "2 sets",
            LastText: "LAST  50 kg × 8",
            AccessibilitySummary: "Shoulder Press, 2 sets. Open set logger.");

        Assert.Equal(2, row.LoggedSetCount);
        Assert.DoesNotContain(" of ", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logged", row.LoggedSetText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LAST", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Weight", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open set logger", row.AccessibilitySummary, StringComparison.Ordinal);
        Assert.True(row.HasArtwork);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
