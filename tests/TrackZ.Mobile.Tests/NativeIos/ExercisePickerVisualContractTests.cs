using System.Xml.Linq;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class ExercisePickerVisualContractTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2009/xaml";

    [Fact]
    public void Native_picker_uses_one_title_compact_filter_trigger_results_and_one_selection_count()
    {
        var page = LoadPage("Features/Exercises/ExercisePickerPage.xaml");
        var root = Assert.Single(page.Root!.Elements());

        Assert.Equal("0,12,0,0", root.Attribute("Padding")?.Value);
        Assert.Equal("Auto,Auto,*,Auto,Auto", root.Attribute("RowDefinitions")?.Value);
        Assert.Equal("{DynamicResource TrackZSpace12}", root.Attribute("RowSpacing")?.Value);
        Assert.DoesNotContain(page.Descendants(), element =>
            element.Name.LocalName == "Label" &&
            element.Attribute("Text")?.Value == "{Binding Text.ChooseExercises}");
        Assert.Equal("False", page.Root!.Attribute("Shell.TabBarIsVisible")?.Value);

        var search = Named(page, "ExerciseSearch");
        Assert.Equal("0", GridRow(search));
        Assert.Equal("{DynamicResource TrackZFieldHeight}", search.Attribute("HeightRequest")?.Value);
        Assert.Equal("Center", search.Attribute("VerticalOptions")?.Value);

        Assert.Equal("1", GridRow(Named(page, "BodyPartFilterBand")));
        var nativeFilter = Named(page, "BodyPartFilterPicker");
        Assert.Equal("Picker", nativeFilter.Name.LocalName);
        Assert.Null(nativeFilter.Attribute("Opacity"));
        Assert.Equal("Transparent", nativeFilter.Attribute("TextColor")?.Value);
        Assert.Equal("Transparent", nativeFilter.Attribute("BackgroundColor")?.Value);
        Assert.Equal(
            "{Binding SelectedBodyPartFilterText}",
            Named(page, "BodyPartFilterLabel").Attribute("Text")?.Value);
        Assert.DoesNotContain(page.Descendants(), element =>
            element.Attribute(X + "Name")?.Value == "BodyPartFilters");

        Assert.Equal("2", GridRow(Named(page, "ExerciseResults")));
        var selectedCounts = page.Descendants().Where(element =>
            element.Name.LocalName == "Label" &&
            element.Attribute("Text")?.Value == "{Binding SelectedCountText}").ToArray();
        Assert.Single(selectedCounts);

        var footer = Named(page, "ExercisePickerActions");
        Assert.Equal("4", GridRow(footer));
        Assert.Equal("0,0,0,12", footer.Attribute("Margin")?.Value);

        var createCustom = Named(page, "CreateCustomExerciseButton");
        Assert.Equal("3", GridRow(createCustom));
        Assert.Equal("{Binding Text.CreateCustom}", createCustom.Attribute("Text")?.Value);
        Assert.Null(createCustom.Attribute("IsVisible"));
    }

    [Fact]
    public void Navigated_pages_do_not_repeat_the_native_navigation_title_inside_content()
    {
        var duplicates = Directory
            .EnumerateFiles(Path.Combine(Root(), "src/TrackZ.Mobile/Features"), "*.xaml", SearchOption.AllDirectories)
            .Select(path => (Path: path, Page: XDocument.Load(path)))
            .Where(item => item.Page.Root?.Name.LocalName == "ContentPage")
            .Where(item => item.Page.Root!.Attribute("Title") is not null)
            .Where(item => item.Page.Root!.Attribute("Shell.NavBarIsVisible")?.Value != "False")
            .SelectMany(item =>
            {
                var title = item.Page.Root!.Attribute("Title")!.Value;
                return item.Page.Descendants()
                    .Where(element => element.Name.LocalName == "Label" && element.Attribute("Text")?.Value == title)
                    .Select(_ => Path.GetRelativePath(Path.Combine(Root(), "src/TrackZ.Mobile"), item.Path));
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("Features/Exercises/ExercisePickerPage.xaml")]
    [InlineData("Features/Exercises/CustomExercisePage.xaml")]
    [InlineData("Features/Workout/WorkoutPage.xaml")]
    [InlineData("Features/History/WorkoutHistoryDetailPage.xaml")]
    [InlineData("Features/Profile/ProfilePage.xaml")]
    public void Shell_page_sticky_actions_keep_a_twelve_point_gap_above_the_tab_bar(string relativePath)
    {
        var page = LoadPage(relativePath);
        var footer = Assert.Single(page.Descendants(), element =>
            ResourceKey(element.Attribute("Style")?.Value) == "TrackZStickyActionContainerStyle");

        Assert.Equal("0,0,0,12", footer.Attribute("Margin")?.Value);
    }

    [Fact]
    public void Persistent_html_reference_covers_the_exact_picker_hierarchy_and_spacing()
    {
        var html = File.ReadAllText(Path.Combine(Root(), "docs/design/exercise-picker-reference.html"));

        Assert.Contains("data-native-title-count=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-page-margin=\"18\"", html, StringComparison.Ordinal);
        Assert.Contains("data-search-height=\"50\"", html, StringComparison.Ordinal);
        Assert.Contains("data-filter-height=\"44\"", html, StringComparison.Ordinal);
        Assert.Contains("data-card-gap=\"12\"", html, StringComparison.Ordinal);
        Assert.Contains("data-selected-count=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-actions-navbar-gap=\"12\"", html, StringComparison.Ordinal);
    }

    private static XDocument LoadPage(string relativePath) =>
        XDocument.Load(Path.Combine(Root(), "src/TrackZ.Mobile", relativePath));

    private static XElement Named(XDocument page, string name) =>
        Assert.Single(page.Descendants(), element => element.Attribute(X + "Name")?.Value == name);

    private static string GridRow(XElement element) =>
        element.Attribute(XName.Get("Row", "http://schemas.microsoft.com/dotnet/2021/maui"))?.Value ??
        element.Attribute("Grid.Row")?.Value ??
        "0";

    private static string? ResourceKey(string? value)
    {
        if (value is null) return null;
        var parts = value.Trim('{', '}').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? parts[1] : value;
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
