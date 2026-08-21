using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;
using System.Xml.Linq;

namespace TrackZ.Mobile.Tests.Acceptance;

public sealed class NativeIosExperienceAcceptanceTests
{
    [Fact]
    public void Root_destinations_share_the_native_title_margin_and_safe_area_contract()
    {
        var mobileDirectory = Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile");
        var rootDestinations = new[]
        {
            "Features/Train/TrainPage.xaml",
            "Features/Exercises/ExercisePickerPage.xaml",
            "Features/Progress/ExerciseProgressPage.xaml",
            "Features/Profile/ProfilePage.xaml"
        };

        foreach (var relativePath in rootDestinations)
        {
            var document = XDocument.Load(Path.Combine(mobileDirectory, relativePath));
            Assert.Contains(document.Descendants(), element =>
                element.Name.LocalName == "Label" && ResourceKey(element.Attribute("Style")?.Value) == "TrackZPageTitleStyle");
            Assert.Contains(document.Root!.DescendantsAndSelf().Attributes("Padding"), attribute =>
                ResourceKey(attribute.Value) is "TrackZPageHorizontalPadding" or "TrackZPageContentPadding" or "TrackZPageBottomContentPadding");
            Assert.Equal("All", document.Root!.Attribute("SafeAreaEdges")?.Value);
        }
    }

    [Fact]
    public void Root_contract_does_not_accept_a_TrackZPage_substring_as_page_margin_evidence()
    {
        var document = XDocument.Parse("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui" SafeAreaEdges="All">
              <Grid Padding="{DynamicResource TrackZPageBogus}">
                <Label Style="{DynamicResource TrackZPageTitleStyle}" />
              </Grid>
            </ContentPage>
            """);

        Assert.DoesNotContain(document.Root!.DescendantsAndSelf().Attributes("Padding"), attribute =>
            ResourceKey(attribute.Value) is "TrackZPageHorizontalPadding" or "TrackZPageContentPadding" or "TrackZPageBottomContentPadding");
    }

    [Fact]
    public void Active_workout_row_carries_api_artwork_and_logged_sets_without_a_prescribed_target()
    {
        var row = new WorkoutExerciseDraftItem(
            Guid.NewGuid(), "Machine Shoulder Press", TrackingMode.Weighted, "Weight",
            "/bounded-cache/shoulder-press.jpg", LoggedSetCount: 2,
            LoggedSetText: "2 sets logged", LastText: "LAST 45 kg × 8",
            AccessibilitySummary: "Machine Shoulder Press, 2 sets logged");
        Assert.True(row.HasArtwork);
        Assert.Equal(2, row.LoggedSetCount);
        Assert.DoesNotContain("of", row.LoggedSetText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(typeof(ProgressReveal));
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

    private static string? ResourceKey(string? value)
    {
        const string marker = "Resource ";
        var start = value?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
        return start >= 0 && value!.EndsWith('}') ? value[(start + marker.Length)..^1] : null;
    }
}
