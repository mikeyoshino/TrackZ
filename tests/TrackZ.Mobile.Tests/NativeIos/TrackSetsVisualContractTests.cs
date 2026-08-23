using System.Text.RegularExpressions;
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
    public void Draft_entries_use_in_field_hints_without_obscuring_entered_values()
    {
        var page = XDocument.Load(Path.Combine(
            Root(), "src/TrackZ.Mobile/Features/Workout/SetLoggerPage.xaml"));
        var controls = page.Descendants().Single(element =>
            Name(element) == "DraftControlGrid");
        var weightInput = controls.Descendants().Single(element =>
            Name(element) == "DraftWeightInput");
        var repsInput = controls.Descendants().Single(element =>
            Name(element) == "DraftRepsInput");
        var weightContainer = Assert.IsType<XElement>(weightInput.Parent);
        var weightCaption = controls.Descendants().Single(element =>
            Name(element) == "DraftWeightCaption");
        var repsCaption = controls.Descendants().Single(element =>
            Name(element) == "DraftRepsCaption");

        Assert.Equal("{Binding WeightUnitLabel}",
            weightInput.Attribute("Placeholder")?.Value);
        Assert.Equal("{Binding Text.Reps}",
            repsInput.Attribute("Placeholder")?.Value);
        Assert.Equal("{Binding WeightFieldLabel}",
            weightCaption.Attribute("Text")?.Value);
        Assert.Equal("{Binding Text.RepsFieldLabel}",
            repsCaption.Attribute("Text")?.Value);
        Assert.Equal("Border", weightContainer.Name.LocalName);
        Assert.All(new[] { weightInput, repsInput }, input =>
            Assert.Equal("0", input.Parent?.Attribute("Padding")?.Value));
        Assert.DoesNotContain(weightContainer.Descendants(), element =>
            element.Name.LocalName == "Label");
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

        foreach (var token in new[]
        {
            "data-previous-reference=\"true\"",
            "Previous workout reference",
            "70 kg × 10 reps",
            "Heaviest set in the 8–12 rep range",
            "data-effort-sheet=\"asking\"",
            "How did this set feel?",
            "Too easy — many reps left",
            "About right — the final reps were hard, with good form",
            "Too heavy — missed the range or form began to break",
            "data-effort-sheet=\"recommendation\"",
            "Try 72.5 kg next set",
            "data-effort-sheet=\"needs-increment\"",
            "data-effort-sheet=\"unavailable\"",
            "This set can no longer be rated. Your original set is saved.",
            "data-effort-unavailable-action=\"dismiss\""
        })
        {
            Assert.Equal(1, Occurrences(html, token));
        }
        Assert.DoesNotContain("data-effort-unavailable-action=\"retry\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-effort-unavailable-action=\"use\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("RIR", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("score", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4+", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Persistent_html_reference_keeps_one_ordered_logger_and_one_exclusive_medium_sheet()
    {
        var document = HtmlReference();
        var logger = document.Descendants("main").Single(element =>
            (string?)element.Attribute("data-unit") == "kg");
        var loggerSections = logger.Elements()
            .Where(element => element.Attribute("data-set-logger-section") is not null)
            .ToArray();

        Assert.Equal(
            ["exercise", "previous-reference", "editor", "today", "last"],
            loggerSections.Select(element =>
                (string)element.Attribute("data-set-logger-section")!));
        var previous = loggerSections.Single(element =>
            (string?)element.Attribute("data-set-logger-section") == "previous-reference");
        Assert.Equal("true", (string?)previous.Attribute("data-previous-reference"));

        var frame = document.Descendants().Single(element =>
            (string?)element.Attribute("data-effort-sheet-frame") == "true");
        Assert.Equal("medium", (string?)frame.Attribute("data-detent"));
        var stateContainer = frame.Descendants().Single(element =>
            (string?)element.Attribute("data-effort-sheet-states") == "exclusive");
        var specimens = stateContainer.Elements()
            .Where(element => element.Attribute("data-effort-sheet") is not null)
            .ToArray();
        Assert.Equal(
            ["asking", "recommendation", "needs-increment", "unavailable"],
            specimens.Select(element => (string)element.Attribute("data-effort-sheet")!));
        Assert.Equal(
            specimens,
            document.Descendants()
                .Where(element => element.Attribute("data-effort-sheet") is not null));
        Assert.All(specimens, specimen =>
        {
            Assert.Same(stateContainer, specimen.Parent);
            Assert.DoesNotContain(specimen.Descendants(), element =>
                element.Attribute("data-effort-sheet") is not null);
        });

        var asking = specimens.Single(element =>
            (string?)element.Attribute("data-effort-sheet") == "asking");
        Assert.Equal(
            "Set saved",
            asking.Descendants().Single(element =>
                (string?)element.Attribute("data-saved-pulse") == "true").Value.Trim());
        Assert.Contains(asking.Descendants("p"), element => element.Value.Trim() ==
            "If you feel pain or cannot keep good form, stop this exercise.");
        Assert.Equal(
            [
                "Too easy — many reps left",
                "About right — the final reps were hard, with good form",
                "Too heavy — missed the range or form began to break",
                "Not sure · skip this time"
            ],
            asking.Descendants("button").Select(element => element.Value.Trim()));

        var recommendation = specimens.Single(element =>
            (string?)element.Attribute("data-effort-sheet") == "recommendation");
        Assert.Equal(
            "Try 72.5 kg next set",
            recommendation.Descendants().Single(element =>
                (string?)element.Attribute("data-recommendation-content") == "title").Value.Trim());
        Assert.Equal(
            "Two comparable sets reached at least 12 reps with good form.",
            recommendation.Descendants().Single(element =>
                (string?)element.Attribute("data-recommendation-content") == "reason").Value.Trim());
        Assert.Equal(
            "Use for next set",
            recommendation.Descendants("button").Single(element =>
                (string?)element.Attribute("data-guidance-action") == "use").Value.Trim());
        Assert.Equal(
            "Not now",
            recommendation.Descendants("button").Single(element =>
                (string?)element.Attribute("data-guidance-action") == "dismiss").Value.Trim());

        var needsIncrement = specimens.Single(element =>
            (string?)element.Attribute("data-effort-sheet") == "needs-increment");
        var incrementControls = needsIncrement.Descendants().Single(element =>
            (string?)element.Attribute("data-increment-controls") == "true");
        var incrementFinalActions = incrementControls.Elements("button").ToArray();
        Assert.Equal(
            ["Use this increment", "Not now"],
            incrementFinalActions.Select(element => element.Value.Trim()));
        Assert.All(incrementFinalActions, action =>
            Assert.Same(incrementControls, action.Parent));

        var unavailable = specimens.Single(element =>
            (string?)element.Attribute("data-effort-sheet") == "unavailable");
        var unavailableAction = Assert.Single(unavailable.Descendants("button"));
        Assert.Same(
            unavailableAction,
            Assert.Single(unavailable.Descendants(), element =>
                element.Name.LocalName is "button" or "input" or "select" or "textarea"));
        Assert.Equal("dismiss",
            (string?)unavailableAction.Attribute("data-effort-unavailable-action"));
        Assert.Equal("Not now", unavailableAction.Value.Trim());
        Assert.DoesNotContain(unavailable.Descendants(), element =>
            element.Attribute("data-guidance-action") is not null
            || element.Attribute("data-increment-controls") is not null
            || element.Attribute("data-recommendation-content") is not null);
        Assert.DoesNotContain(unavailable.Descendants("button"), element =>
            element.Value.Contains("retry", StringComparison.OrdinalIgnoreCase)
            || element.Value.Contains("use", StringComparison.OrdinalIgnoreCase));
    }

    private static XDocument HtmlReference()
    {
        var html = File.ReadAllText(Path.Combine(Root(), "docs/design/track-sets-reference.html"));
        html = html.Replace("<!doctype html>", "<!DOCTYPE html>", StringComparison.Ordinal);
        html = Regex.Replace(
            html,
            @"<(meta|input)(\b[^>]*?)(?<!/)>",
            "<$1$2 />",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        html = Regex.Replace(
            html,
            @"\shidden(?=[\s>])",
            " hidden=\"hidden\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return XDocument.Parse(html, LoadOptions.PreserveWhitespace);
    }

    private static int Occurrences(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }
        return count;
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
