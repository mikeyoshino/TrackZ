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
            if (relativePath == "Features/Exercises/ExercisePickerPage.xaml")
            {
                var nativeTitle = document.Root!.Attribute("Title")?.Value;
                Assert.Equal("{Binding Text.ChooseExercises}", nativeTitle);
                Assert.DoesNotContain(document.Descendants(), element =>
                    element.Name.LocalName == "Label" && element.Attribute("Text")?.Value == nativeTitle);
            }
            else
            {
                Assert.Contains(document.Descendants(), element =>
                    element.Name.LocalName == "Label" && ResourceKey(element.Attribute("Style")?.Value) == "TrackZPageTitleStyle");
            }
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
            LoggedSetText: "2 sets", LastText: "LAST 45 kg × 8",
            AccessibilitySummary: "Machine Shoulder Press, 2 sets. Open set logger.");
        Assert.True(row.HasArtwork);
        Assert.Equal(2, row.LoggedSetCount);
        Assert.DoesNotContain("of", row.LoggedSetText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logged", row.LoggedSetText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LAST", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Weight", row.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open set logger", row.AccessibilitySummary, StringComparison.Ordinal);
        Assert.NotNull(typeof(ProgressReveal));
    }

    [Fact]
    public void Momentum_home_keeps_the_approved_named_hierarchy_one_primary_and_quiet_home_copy()
    {
        var mobileDirectory = Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile");
        var document = XDocument.Load(Path.Combine(mobileDirectory, "Features/Train/TrainPage.xaml"));
        var namedElements = document.Descendants()
            .Select(element => new
            {
                Name = element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName == "Name" && attribute.Name.NamespaceName.Contains("xaml", StringComparison.Ordinal))?.Value,
                Element = element.Name.LocalName
            })
            .Where(item => item.Name is not null)
            .ToArray();

        var approvedHierarchy = new[]
        {
            "MomentumHomeScroll", "HomeContextLabel", "HomeHero", "HeroActionButton", "MotivationStrip",
            "WeeklyGoalMetric", "StreakMetric", "LevelMetric", "TrainAgainCard", "RecentMomentumCard"
        };
        Assert.Equal(
            approvedHierarchy,
            namedElements.Where(item => approvedHierarchy.Contains(item.Name!)).Select(item => item.Name));
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attributes().Any(attribute => attribute.Value.Contains("TrackZPrimaryButtonStyle", StringComparison.Ordinal)));

        var homeCopy = HomeCopy(WorkoutResources.English).Concat(HomeCopy(WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("th-TH"))));
        Assert.DoesNotContain(homeCopy, value => value.Contains("offline", StringComparison.OrdinalIgnoreCase)
            || value.Contains("saved on this device", StringComparison.OrdinalIgnoreCase)
            || value.Contains("recommend", StringComparison.OrdinalIgnoreCase)
            || value.Contains("แนะนำ", StringComparison.Ordinal));
    }

    private static IEnumerable<string> HomeCopy(WorkoutTextSet text) =>
    [
        text.ReadyWhenYouAre,
        text.YouAreInMotion,
        text.StartTraining,
        text.ChooseTodaysWorkout,
        text.ChooseWorkoutSupporting,
        text.WorkoutInProgress,
        text.ContinueWorkout,
        text.ThisWeek,
        text.WeekStreak,
        text.TrainAgain,
        text.RecentMomentum,
        text.HomeExerciseProgressFormat,
        text.RepeatWorkoutAccessibilityFormat,
        text.HomeLoadFailed,
        text.HomeRepeatFailed,
        text.HomeOpenWorkoutFailed,
        text.HomeContextFormat
    ];

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
