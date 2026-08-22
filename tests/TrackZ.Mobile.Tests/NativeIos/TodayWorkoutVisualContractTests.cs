using System.Xml.Linq;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class TodayWorkoutVisualContractTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2009/xaml";

    [Fact]
    public void Native_page_matches_the_approved_compact_illustrated_queue_reference()
    {
        var page = XDocument.Load(Path.Combine(Root(), "src/TrackZ.Mobile/Features/Workout/WorkoutPage.xaml"));
        var component = XDocument.Load(Path.Combine(Root(), "src/TrackZ.Mobile/Components/ActiveWorkoutExerciseRow.xaml"));

        Assert.DoesNotContain(page.Descendants("{http://schemas.microsoft.com/dotnet/2021/maui}Label"),
            label => label.Attribute("Text")?.Value == "{Binding Text.WorkoutTitle}");

        var context = page.Descendants().Single(element => Name(element) == "WorkoutContext");
        Assert.Equal("{Binding WorkoutContextText}", context.Attribute("Text")?.Value);
        var notice = page.Descendants().Single(element => Name(element) == "WorkoutNotice");
        Assert.Equal("TrackZNoticeBanner", notice.Name.LocalName);
        Assert.Equal("2", notice.Attribute("Grid.Row")?.Value);
        Assert.Equal("18,0", notice.Attribute("Margin")?.Value);
        Assert.Equal("{Binding NoticeMessage}", notice.Attribute("Text")?.Value);
        Assert.Equal("{Binding NoticeSeverity}", notice.Attribute("Severity")?.Value);
        Assert.Equal("{Binding DismissNoticeCommand}", notice.Attribute("DismissCommand")?.Value);
        Assert.Equal("{Binding Text.DismissNotice}", notice.Attribute("DismissText")?.Value);
        Assert.DoesNotContain(page.Descendants(), element =>
            element.Name.LocalName == "Label"
            && element.Attribute("Text")?.Value == "{Binding ErrorMessage}");

        var root = page.Root!.Elements().Single();
        Assert.Equal("{DynamicResource TrackZSpace12}", root.Attribute("RowSpacing")?.Value);
        var actions = page.Descendants().Single(element => Name(element) == "WorkoutActions");
        Assert.Equal("3", actions.Attribute("Grid.Row")?.Value);
        Assert.Equal("0,0,0,12", actions.Attribute("Margin")?.Value);

        var artwork = component.Descendants().Single(element => Name(element) == "ActiveWorkoutArtwork");
        Assert.Equal("{DynamicResource TrackZExerciseArtworkSize}", artwork.Attribute("HeightRequest")?.Value);
        Assert.Equal("{DynamicResource TrackZExerciseArtworkSize}", artwork.Attribute("WidthRequest")?.Value);
        Assert.Contains(component.Descendants(), element => Name(element) == "WorkoutExercisePerformanceLine");
        Assert.Contains(component.Descendants(), element => Name(element) == "WorkoutExerciseLastText");
        Assert.Contains(component.Descendants(), element => Name(element) == "WorkoutExerciseTodayText");
    }

    [Fact]
    public void Persistent_html_reference_covers_the_exact_native_hierarchy_and_spacing()
    {
        var html = File.ReadAllText(Path.Combine(Root(), "docs/design/todays-workout-reference.html"));

        Assert.Contains("data-page-margin=\"18\"", html, StringComparison.Ordinal);
        Assert.Contains("data-card-gap=\"12\"", html, StringComparison.Ordinal);
        Assert.Contains("data-card-height=\"112\"", html, StringComparison.Ordinal);
        Assert.Contains("4 exercises · 3 sets logged", html, StringComparison.Ordinal);
        Assert.Contains("data-actions-navbar-gap=\"12\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h2>Today's workout</h2>", html, StringComparison.Ordinal);
    }

    private static string? Name(XElement element) => element.Attribute(X + "Name")?.Value;

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("TrackZ.slnx was not found.");
    }
}
