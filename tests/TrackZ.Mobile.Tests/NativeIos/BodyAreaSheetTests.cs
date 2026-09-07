using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Presentation;
using TrackZ.Mobile.Features.Workout;
using System.Globalization;
using Microsoft.Maui.Dispatching;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class BodyAreaSheetTests : IDisposable
{
    private readonly IDispatcherProvider _originalDispatcher = DispatcherProvider.Current;
    public BodyAreaSheetTests() => DispatcherProvider.SetCurrent(new TestDispatcherProvider());
    public void Dispose() => DispatcherProvider.SetCurrent(_originalDispatcher);
    private sealed class TestDispatcherProvider : IDispatcherProvider
    {
        public IDispatcher GetForCurrentThread() => new TestDispatcher();
    }
    private sealed class TestDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }
    [Fact]
    public async Task Add_flow_distinguishes_all_categories_from_cancellation()
    {
        var presenter = new RecordingSheetPresenter();
        var picker = new MauiBodyAreaPicker(presenter, () => new BodyAreaSheetPage(WorkoutResources.English));
        var pending = picker.PickForAddingAsync();
        if (pending.IsFaulted) await pending;
        var page = Assert.IsType<BodyAreaSheetPage>(presenter.Page);
        Assert.True(page.IsAdding);
        page.FindByName<Button>("AllBodyAreasButton").SendClicked();
        var result = await pending;
        Assert.True(result.Confirmed);
        Assert.Null(result.BodyPart);

        pending = picker.PickForAddingAsync();
        await Assert.IsType<BodyAreaSheetPage>(presenter.Page).CancelAsync();
        Assert.False((await pending).Confirmed);
    }

    [Fact]
    public async Task Dismissing_body_area_sheet_returns_null()
    {
        var presenter = new RecordingSheetPresenter();
        var picker = new MauiBodyAreaPicker(
            presenter,
            () => new BodyAreaSheetPage(WorkoutResources.English));

        var pending = picker.PickAsync();
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.IsType<BodyAreaSheetPage>(presenter.Page).CancelAsync();

        Assert.Null(await pending);
        Assert.Equal(1, presenter.DismissCount);
    }

    [Fact]
    public void Body_area_sheet_uses_the_compact_header_and_has_no_footer_cancel_bar()
    {
        var page = new BodyAreaSheetPage(WorkoutResources.ForCulture(
            CultureInfo.GetCultureInfo("th-TH")));

        Assert.Equal("เลือกส่วนที่ต้องการฝึก", page.FindByName<Label>("BodyAreaTitle").Text);
        Assert.Equal(
            "เลือก 1 ส่วนเพื่อดูท่าออกกำลังกาย",
            page.FindByName<Label>("BodyAreaSupporting").Text);
        Assert.NotNull(page.FindByName<ImageButton>("BodyAreaCloseButton"));
        Assert.DoesNotContain(Descendants(page).OfType<Button>(), button =>
            button.Text == "ยกเลิก");
    }

    [Fact]
    public async Task Selecting_body_area_completes_once_and_dismisses()
    {
        var presenter = new RecordingSheetPresenter();
        var picker = new MauiBodyAreaPicker(
            presenter,
            () => new BodyAreaSheetPage(WorkoutResources.English));

        var pending = picker.PickAsync();
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var page = Assert.IsType<BodyAreaSheetPage>(presenter.Page);
        await page.SelectAsync(BodyPart.Legs);
        await page.SelectAsync(BodyPart.Back);

        Assert.Equal(BodyPart.Legs, await pending);
        Assert.Equal(1, presenter.DismissCount);
    }

    [Theory]
    [InlineData("en-US", "Chest", "Legs", "Cancel")]
    [InlineData("th-TH", "หน้าอก", "ขา", "ยกเลิก")]
    public void Body_area_copy_is_localized(
        string cultureName,
        string expectedChest,
        string expectedLegs,
        string expectedCancel)
    {
        var text = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expectedChest, text.BodyPartChest);
        Assert.Equal(expectedLegs, text.BodyPartLegs);
        Assert.Equal(expectedCancel, text.Cancel);
    }

    private sealed class RecordingSheetPresenter : INativeSheetPresenter
    {
        public TaskCompletionSource Presented { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ContentPage? Page { get; private set; }
        public int DismissCount { get; private set; }

        public Task ShowAsync(
            ContentPage page,
            NativeSheetDetent detent,
            CancellationToken cancellationToken = default)
        {
            Page = page;
            Presented.TrySetResult();
            return Task.CompletedTask;
        }

        public Task DismissAsync(
            ContentPage page,
            CancellationToken cancellationToken = default)
        {
            Assert.Same(Page, page);
            DismissCount++;
            return Task.CompletedTask;
        }
    }

    private static IEnumerable<Element> Descendants(IVisualTreeElement root)
    {
        foreach (var child in root.GetVisualChildren().OfType<Element>())
        {
            yield return child;
            if (child is IVisualTreeElement tree)
                foreach (var descendant in Descendants(tree))
                    yield return descendant;
        }
    }
}
