using System.Globalization;
using System.Xml.Linq;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class AppWideVisualConsistencyTests
{
    private static readonly string[] ShippedPages =
    [
        "Features/Auth/SignInPage.xaml",
        "Features/Auth/CreateAccountPage.xaml",
        "Features/Train/TrainPage.xaml",
        "Features/Train/BodyAreaSheetPage.xaml",
        "Features/Exercises/ExercisePickerPage.xaml",
        "Features/Exercises/CustomExercisePage.xaml",
        "Features/Workout/WorkoutPage.xaml",
        "Features/Workout/SetLoggerPage.xaml",
        "Features/Workout/SetEntrySheetPage.xaml",
        "Features/History/WorkoutHistoryPage.xaml",
        "Features/History/WorkoutHistoryDetailPage.xaml",
        "Features/History/HistorySetEditorSheetPage.xaml",
        "Features/History/HistoryConflictSheetPage.xaml",
        "Features/Summary/WorkoutSummaryPage.xaml",
        "Features/Progress/ExerciseProgressPage.xaml",
        "Features/Profile/ProfilePage.xaml"
    ];

    private static readonly string[] RootTitlePages =
    [
        "Features/Auth/SignInPage.xaml",
        "Features/Auth/CreateAccountPage.xaml",
        "Features/Train/TrainPage.xaml",
        "Features/Exercises/ExercisePickerPage.xaml",
        "Features/History/WorkoutHistoryPage.xaml",
        "Features/Progress/ExerciseProgressPage.xaml",
        "Features/Profile/ProfilePage.xaml"
    ];

    private static readonly string[] BottomActionPages =
    [
        "Features/Auth/SignInPage.xaml",
        "Features/Auth/CreateAccountPage.xaml",
        "Features/Train/TrainPage.xaml",
        "Features/Train/BodyAreaSheetPage.xaml",
        "Features/Exercises/ExercisePickerPage.xaml",
        "Features/Exercises/CustomExercisePage.xaml",
        "Features/Workout/WorkoutPage.xaml",
        "Features/Workout/SetLoggerPage.xaml",
        "Features/Workout/SetEntrySheetPage.xaml",
        "Features/History/WorkoutHistoryDetailPage.xaml",
        "Features/History/HistorySetEditorSheetPage.xaml",
        "Features/History/HistoryConflictSheetPage.xaml",
        "Features/Profile/ProfilePage.xaml"
    ];

    private static readonly string[] AuditedComponents =
    [
        "BadgeTile.xaml",
        "ExerciseProgressChart.xaml",
        "LastSetTable.xaml",
        "WeeklyStreakView.xaml",
        "XpBar.xaml"
    ];

    public static TheoryData<string> ShippedPageFiles => new(ShippedPages);
    public static TheoryData<string> ComponentFiles => new(AuditedComponents);

    [Theory]
    [MemberData(nameof(ShippedPageFiles))]
    public void Shipped_page_uses_the_native_semantic_layout_contract(string relativePath)
    {
        var path = Path.Combine(MobileDirectory(), relativePath);
        var document = XDocument.Load(path);
        var errors = AuditPage(relativePath, document).ToArray();

        Assert.True(errors.Length == 0, $"{relativePath}:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Select(error => $"- {error}"))}");
    }

    [Theory]
    [MemberData(nameof(ComponentFiles))]
    public void Remaining_gamification_component_uses_semantic_cards_type_and_spacing(string fileName)
    {
        var path = Path.Combine(MobileDirectory(), "Components", fileName);
        var document = XDocument.Load(path);
        var errors = AuditCommon(document).ToList();

        foreach (var spacing in document.Root!.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.LocalName is "Spacing" or "RowSpacing" or "ColumnSpacing" or "Padding"))
        {
            if (spacing.Value.Contains("TrackZSpace", StringComparison.Ordinal) ||
                spacing.Value.Contains("TrackZVerticalRowPadding", StringComparison.Ordinal))
                continue;
            errors.Add($"{spacing.Parent?.Name.LocalName}.{spacing.Name.LocalName} must use a TrackZSpace resource.");
        }

        foreach (var label in document.Descendants().Where(element => element.Name.LocalName == "Label"))
        {
            if (label.Attribute("Style") is null)
                errors.Add("Every component label must use a semantic typography style.");
        }

        Assert.True(errors.Count == 0, $"{fileName}:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Select(error => $"- {error}"))}");
    }

    [Fact]
    public void Explicit_shipped_page_set_cannot_silently_shrink()
    {
        Assert.Equal(16, ShippedPages.Length);
        Assert.Equal(16, ShippedPages.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ShippedPages, relativePath => Assert.True(File.Exists(Path.Combine(MobileDirectory(), relativePath)), relativePath));
    }

    private static IEnumerable<string> AuditPage(string relativePath, XDocument document)
    {
        var root = Assert.IsType<XElement>(document.Root);
        var background = root.Attribute("BackgroundColor")?.Value;
        if (background is null || !background.Contains("DynamicResource TrackZ", StringComparison.Ordinal))
            yield return "ContentPage must use a semantic dynamic page background.";
        if (!string.Equals(root.Attribute("SafeAreaEdges")?.Value, "All", StringComparison.Ordinal))
            yield return "ContentPage must opt into all safe-area edges.";

        if (!root.DescendantsAndSelf().Attributes("Padding").Any(attribute =>
                new[] { "TrackZPageHorizontalPadding", "TrackZPageContentPadding", "TrackZPageBottomContentPadding" }
                    .Any(key => attribute.Value.Contains(key, StringComparison.Ordinal))))
            yield return "Side padding must use TrackZPageMargin.";

        foreach (var error in AuditCommon(document))
            yield return error;

        var expectedTitleStyle = RootTitlePages.Contains(relativePath, StringComparer.Ordinal)
            ? "TrackZPageTitleStyle"
            : "TrackZNavigationTitleStyle";
        if (!document.Descendants().Any(element => element.Name.LocalName == "Label" && ResourceKey(element.Attribute("Style")?.Value) == expectedTitleStyle))
            yield return $"The page title must use {expectedTitleStyle}.";

        foreach (var button in document.Descendants().Where(element => element.Name.LocalName == "Button"))
        {
            var style = ResourceKey(button.Attribute("Style")?.Value);
            if (style is not ("TrackZPrimaryButtonStyle" or "TrackZSecondaryButtonStyle" or "TrackZDestructiveButtonStyle" or "TrackZQuietButtonStyle"))
                yield return $"Button '{button.Attribute("Text")?.Value ?? "unnamed"}' must use a semantic button style.";

            var minimum = button.Attribute("MinimumHeightRequest")?.Value;
            if (double.TryParse(minimum, NumberStyles.Number, CultureInfo.InvariantCulture, out var height) && height < 44)
                yield return $"Button minimum height {height} is below the 44-point native target.";

            var destructiveSignal = string.Concat(
                button.Attribute("Text")?.Value,
                button.Attribute("Clicked")?.Value,
                button.Attribute("Command")?.Value);
            if (ContainsDestructiveSignal(destructiveSignal) && style != "TrackZDestructiveButtonStyle")
                yield return $"Destructive button '{button.Attribute("Text")?.Value ?? "unnamed"}' must use TrackZDestructiveButtonStyle.";
        }

        foreach (var label in document.Descendants().Where(element => element.Name.LocalName == "Label"))
        {
            if (ResourceKey(label.Attribute("TextColor")?.Value) == "TrackZDanger" &&
                ResourceKey(label.Attribute("Style")?.Value) is not ("TrackZFieldErrorStyle" or "TrackZInlineErrorStyle"))
                yield return "Danger copy must use a destructive semantic typography role.";
        }

        if (BottomActionPages.Contains(relativePath, StringComparer.Ordinal) &&
            !document.Descendants().Any(element => ResourceKey(element.Attribute("Style")?.Value) == "TrackZStickyActionContainerStyle"))
            yield return "Bottom actions must be owned by TrackZStickyActionContainerStyle.";
    }

    private static IEnumerable<string> AuditCommon(XDocument document)
    {
        foreach (var attribute in document.Root!.DescendantsAndSelf().Attributes())
        {
            if (attribute.Value.StartsWith('#'))
                yield return $"{attribute.Parent?.Name.LocalName}.{attribute.Name.LocalName} contains a literal color.";
            if (attribute.Name.LocalName is "FontSize" or "CornerRadius")
                yield return $"{attribute.Parent?.Name.LocalName}.{attribute.Name.LocalName} must come from a semantic style.";
        }
    }

    private static bool ContainsDestructiveSignal(string value) =>
        new[] { "Delete", "Discard", "Remove", "SignOut" }
            .Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static string? ResourceKey(string? value)
    {
        if (value is null)
            return null;
        var marker = "Resource ";
        var start = value.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0 || !value.EndsWith('}'))
            return null;
        return value[(start + marker.Length)..^1];
    }

    private static string MobileDirectory() =>
        Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile");

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
