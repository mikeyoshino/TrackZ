using System.Xml.Linq;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class TrackSetsVisualContractTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2009/xaml";

    [Fact]
    public void Native_page_matches_the_approved_action_first_track_sets_reference()
    {
        var page = XDocument.Load(Path.Combine(Root(), "src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml"));
        var content = page.Descendants().Single(element => Name(element) == "TrackSetsContent");

        var exercise = content.Elements().Single(element => Name(element) == "ExerciseSummary");
        var editor = content.Elements().Single(element => Name(element) == "InlineSetEditor");
        var today = content.Elements().Single(element => Name(element) == "TodaySetsSection");
        var previous = content.Elements().Single(element => Name(element) == "LastWorkoutSection");
        var children = content.Elements().ToList();

        Assert.DoesNotContain(page.Descendants(), element => Name(element) == "PreviousBestLabel");
        var reference = content.Elements().Single(element =>
            Name(element) == "PreviousWorkoutReferenceCard");
        var referenceLoad = reference.Descendants().Single(element =>
            Name(element) == "PreviousWorkoutReferenceLoad");
        var referenceReps = reference.Descendants().Single(element =>
            Name(element) == "PreviousWorkoutReferenceReps");
        var referenceReason = reference.Descendants().Single(element =>
            Name(element) == "PreviousWorkoutReferenceReason");
        Assert.Equal("{Binding HasPreviousReference}", reference.Attribute("IsVisible")?.Value);
        Assert.Equal("{DynamicResource TrackZPerformanceNumberStyle}",
            referenceLoad.Attribute("Style")?.Value);
        Assert.Equal("{Binding PreviousReferenceLoad}",
            referenceLoad.Attribute("Text")?.Value);
        Assert.Equal("{DynamicResource TrackZSecondaryStyle}",
            referenceReps.Attribute("Style")?.Value);
        Assert.Equal("{Binding PreviousReferenceReps}",
            referenceReps.Attribute("Text")?.Value);
        Assert.Equal("{DynamicResource TrackZSecondaryStyle}",
            referenceReason.Attribute("Style")?.Value);
        Assert.Equal("{Binding PreviousReferenceReason}",
            referenceReason.Attribute("Text")?.Value);
        Assert.Equal(children.IndexOf(exercise) + 1, children.IndexOf(reference));
        Assert.True(children.IndexOf(reference) < children.IndexOf(editor));

        Assert.True(children.IndexOf(exercise) < children.IndexOf(editor));
        Assert.True(children.IndexOf(editor) < children.IndexOf(today));
        Assert.True(children.IndexOf(today) < children.IndexOf(previous));
        Assert.DoesNotContain(page.Descendants(), element => element.Name.LocalName == "CollectionView");
        Assert.DoesNotContain(page.Descendants(), element => element.Name.LocalName == "LastSetTable");

        var draftControls = editor.Descendants().Single(element => Name(element) == "DraftControlGrid");
        Assert.Equal("*,*", draftControls.Attribute("ColumnDefinitions")?.Value);
        Assert.Contains(editor.Descendants(), element => Name(element) == "SaveDraftSetButton");
        Assert.DoesNotContain(
            page.Root!.Elements().Where(element => element.Name.LocalName == "Grid").SelectMany(element => element.Elements()),
            element => Name(element) == "SaveDraftSetButton");

        Assert.Equal("{Binding HasTodaySets}", today.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding HasLastSets}", previous.Attribute("IsVisible")?.Value);
        Assert.Contains(previous.Descendants(), element =>
            Name(element) == "LastWorkoutDisclosure" &&
            element.Attribute("Command")?.Value == "{Binding ToggleLastWorkoutCommand}");
        Assert.Contains(previous.Descendants(), element =>
            Name(element) == "LastWorkoutSets" &&
            element.Attribute("IsVisible")?.Value == "{Binding IsLastWorkoutExpanded}");
    }

    [Fact]
    public void Persistent_html_reference_covers_kilograms_pounds_and_both_disclosure_states()
    {
        var html = File.ReadAllText(Path.Combine(Root(), "docs/design/track-sets-reference.html"));

        Assert.Contains("data-unit=\"kg\"", html, StringComparison.Ordinal);
        Assert.Contains("data-unit=\"lb\"", html, StringComparison.Ordinal);
        Assert.Contains("data-last-workout=\"collapsed\"", html, StringComparison.Ordinal);
        Assert.Contains("data-last-workout=\"expanded\"", html, StringComparison.Ordinal);
        Assert.Contains("Save set 3", html, StringComparison.Ordinal);
        Assert.Contains("Assistance", html, StringComparison.Ordinal);
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
