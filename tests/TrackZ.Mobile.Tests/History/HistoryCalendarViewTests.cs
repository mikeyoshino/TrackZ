using Microsoft.Maui.Dispatching;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.History;

namespace TrackZ.Mobile.Tests.History;

public sealed class HistoryCalendarViewTests : IDisposable
{
    private readonly IDispatcherProvider _original = DispatcherProvider.Current;
    public HistoryCalendarViewTests() => DispatcherProvider.SetCurrent(new Dispatchers());
    public void Dispose() => DispatcherProvider.SetCurrent(_original);

    [Fact]
    public void Filter_keeps_button_identity_and_does_not_change_applied_selection_until_apply()
    {
        var applied = new HashSet<BodyPart> { BodyPart.Arms };
        object? result = null;
        var sheet = new HistoryFilterSheet(applied, value => { result = value; return Task.CompletedTask; });
        var legs = Assert.Single(Descendants(sheet).OfType<Button>(), button =>
            button.Text.StartsWith(HistoryPresentation.Body(BodyPart.Legs), StringComparison.Ordinal));
        legs.SendClicked();
        Assert.Contains(legs, Descendants(sheet).OfType<Button>());
        Assert.Single(applied);
        Assert.Null(result);
        Assert.Single(Descendants(sheet).OfType<Button>(), button =>
            button.Text == HistoryPresentation.T("แสดงผล", "Apply")).SendClicked();
        var parts = Assert.IsType<BodyPart[]>(result);
        Assert.Contains(BodyPart.Arms, parts);
        Assert.Contains(BodyPart.Legs, parts);
        Assert.Single(applied);
    }

    [Fact]
    public void Clearing_then_cancelling_filter_keeps_the_original_applied_filter()
    {
        var applied = new HashSet<BodyPart> { BodyPart.Arms };
        var completed = false;
        object? result = new object();
        var sheet = new HistoryFilterSheet(applied, value => { completed = true; result = value; return Task.CompletedTask; });
        Assert.Single(Descendants(sheet).OfType<Button>(), button =>
            button.Text == HistoryPresentation.T("ล้างค่า", "Clear")).SendClicked();
        Assert.Single(Descendants(sheet).OfType<Button>(), button =>
            button.Text == HistoryPresentation.T("ยกเลิก", "Cancel")).SendClicked();
        Assert.True(completed);
        Assert.Null(result);
        Assert.Equal(BodyPart.Arms, Assert.Single(applied));
    }

    [Fact]
    public void Primary_action_and_selected_day_resolve_visible_background_not_transparent_local_value()
    {
        var lime = Color.FromArgb("#C8FF3D");
        var gray = Color.FromArgb("#252B31");
        var state = new HistoryCalendarState(new DateOnly(2026, 9, 5), TimeZoneInfo.Utc);
        var calendar = HistoryCalendarViews.Calendar(state, () => { }, _ => { }, out var selected);
        var apply = HistoryCalendarViews.Button("Apply", () => { }, true);
        var root = new ContentPage
        {
            Resources = new ResourceDictionary
            {
                ["TrackZPrimary"] = lime, ["TrackZDisabledSurface"] = gray,
                ["TrackZPrimaryContrast"] = Colors.Black, ["TrackZTextPrimary"] = Colors.White,
                ["TrackZTextSecondary"] = Colors.Gray, ["TrackZSurface"] = Colors.Black,
                ["TrackZBorder"] = Colors.Gray
            },
            Content = HistoryCalendarViews.Stack(calendar, apply)
        };
        Assert.Equal(lime, apply.BackgroundColor);
        Assert.Equal(lime, selected!.BackgroundColor);
        var dayOne = Assert.Single(Descendants(root).OfType<Button>(), button => button.Text == "1");
        Assert.Equal(gray, dayOne.BackgroundColor);
        root.Resources["TrackZPrimary"] = Colors.Yellow;
        Assert.Equal(Colors.Yellow, apply.BackgroundColor);
    }

    private static IEnumerable<Element> Descendants(Element root)
    {
        yield return root;
        foreach (var child in ((IVisualTreeElement)root).GetVisualChildren().OfType<Element>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private sealed class Dispatchers : IDispatcherProvider
    {
        public IDispatcher GetForCurrentThread() => new Dispatcher();
    }
    private sealed class Dispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }
}
