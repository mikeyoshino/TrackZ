using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Presentation;
using TrackZ.Mobile.Features.Workout;
using System.Globalization;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class BodyAreaSheetTests
{
    [Fact]
    public async Task Dismissing_body_area_sheet_returns_null()
    {
        var presenter = new RecordingSheetPresenter();
        var picker = new MauiBodyAreaPicker(presenter, () => new BodyAreaSheetPage());

        var pending = picker.PickAsync();
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.IsType<BodyAreaSheetPage>(presenter.Page).CancelAsync();

        Assert.Null(await pending);
        Assert.Equal(1, presenter.DismissCount);
    }

    [Fact]
    public async Task Selecting_body_area_completes_once_and_dismisses()
    {
        var presenter = new RecordingSheetPresenter();
        var picker = new MauiBodyAreaPicker(presenter, () => new BodyAreaSheetPage());

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
}
