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

        var counter = component.Descendants().Single(element => Name(element) == "WorkoutExerciseSetCounter");
        Assert.Equal("2", counter.Attribute("Grid.Column")?.Value);
        Assert.Equal("{Binding Exercise.LoggedSetText, Source={x:Reference Root}}", counter.Attribute("Text")?.Value);
        Assert.Equal("{DynamicResource TrackZPerformanceNumberStyle}", counter.Attribute("Style")?.Value);
        Assert.Equal("False", counter.Attribute("AutomationProperties.IsInAccessibleTree")?.Value);

        var row = component.Descendants().Single(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("Style")?.Value == "{DynamicResource TrackZListRowStyle}");
        Assert.Equal("True",
            row.Attribute("AutomationProperties.IsInAccessibleTree")?.Value);
        var visualContent = row.Elements().Single(element =>
            element.Name.LocalName == "Grid");
        Assert.Equal("True",
            visualContent.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.DoesNotContain(visualContent.DescendantsAndSelf(), element =>
            element.Attribute("AutomationProperties.IsInAccessibleTree")?.Value == "True"
            || element.Attribute("SemanticProperties.Description") is not null);
        Assert.Single(component.Descendants(), element =>
            element.Attribute("SemanticProperties.Description")?.Value
                == "{Binding Exercise.AccessibilitySummary, Source={x:Reference Root}}");

        var chevron = component.Descendants().Single(element => Name(element) == "WorkoutExerciseChevron");
        Assert.Equal("3", chevron.Attribute("Grid.Column")?.Value);

        Assert.DoesNotContain(component.Descendants(), element => Name(element) == "WorkoutExercisePerformanceLine");
        Assert.DoesNotContain(component.Descendants(), element => Name(element) == "WorkoutExerciseLastText");
        Assert.DoesNotContain(component.Descendants(), element => Name(element) == "WorkoutExerciseTodayText");
        Assert.DoesNotContain(component.Descendants(), element =>
            element.Attribute("Text")?.Value is "{Binding Exercise.TrackingModeText, Source={x:Reference Root}}"
                or "{Binding Exercise.LastText, Source={x:Reference Root}}");
        Assert.Single(component.Descendants(), element =>
            element.Attribute("Text")?.Value == "{Binding Exercise.Name, Source={x:Reference Root}}");
        Assert.Equal("{Binding Exercise.AccessibilitySummary, Source={x:Reference Root}}",
            row.Attribute("SemanticProperties.Description")?.Value);
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
        Assert.Equal(4, Count(html, "class=\"set-count\""));
        Assert.Contains(">2 sets<", html, StringComparison.Ordinal);
        Assert.Contains(">1 set<", html, StringComparison.Ordinal);
        Assert.Contains(">0 sets<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"mode\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"performance\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("LAST", html, StringComparison.Ordinal);

        Assert.Contains(
            "<article class=\"card\"><img class=\"art\" src=\"../../assets/exercises/images/assisted-pull-up.png\" alt=\"Assisted Pull-Up movement artwork\"><div class=\"name\">Assisted Pull-Up</div><div class=\"set-count\">2 sets</div><span class=\"chevron\">›</span></article>",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "<article class=\"card\"><img class=\"art\" src=\"../../assets/exercises/images/barbell-row.png\" alt=\"Barbell Row movement artwork\"><div class=\"name\">Barbell Row</div><div class=\"set-count\">1 set</div><span class=\"chevron\">›</span></article>",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "<article class=\"card\"><img class=\"art\" src=\"../../assets/exercises/images/cable-fly.png\" alt=\"Cable Fly movement artwork\"><div class=\"name\">Cable Fly</div><div class=\"set-count\">0 sets</div><span class=\"chevron\">›</span></article>",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "<article class=\"card\"><img class=\"art\" src=\"../../assets/exercises/images/close-grip-bench-press.png\" alt=\"Close-Grip Bench Press movement artwork\"><div class=\"name\">Close-Grip Bench Press</div><div class=\"set-count\">0 sets</div><span class=\"chevron\">›</span></article>",
            html,
            StringComparison.Ordinal);
    }

    private static string? Name(XElement element) => element.Attribute(X + "Name")?.Value;

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

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
