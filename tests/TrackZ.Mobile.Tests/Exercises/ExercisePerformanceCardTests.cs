using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Components;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;
using Microsoft.Maui.Dispatching;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class ExercisePerformanceCardTests
{
    [Fact]
    public void System_exercise_renders_its_name_thumbnail_and_hides_edit_action()
    {
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        try
        {
            using var app = MauiProgram.CreateMauiApp();
            var cached = new CachedExercise
            {
                Id = Guid.Parse("56f212f7-36b4-582a-9283-2cbb8fb264ab"),
                Name = "Barbell Bench Press",
                BodyPart = BodyPart.Chest,
                TrackingMode = TrackingMode.Weighted,
                ThumbnailUri = "/tmp/api-thumbnail.png",
                IsCustom = false,
                LastSyncedAt = DateTimeOffset.Parse("2026-08-20T08:00:00Z")
            };
            var item = new ExercisePickerItem(cached, WorkoutResources.Current);
            var toggleCommand = new Command(() => { });
            var editCommand = new Command(() => { });
            var card = new ExercisePerformanceCard
            {
                BindingContext = item,
                Exercise = item,
                ToggleCommand = toggleCommand,
                EditCommand = editCommand
            };

            var border = Assert.IsType<Border>(card.Content);
            var grid = Assert.IsType<Grid>(border.Content);
            var thumbnail = Assert.IsType<Image>(grid.Children[0]);
            var name = Assert.IsType<Label>(grid.Children[1]);
            var actions = Assert.IsType<VerticalStackLayout>(grid.Children[4]);
            var edit = Assert.IsType<Button>(actions.Children[1]);
            var tap = Assert.IsType<TapGestureRecognizer>(Assert.Single(border.GestureRecognizers));

            Assert.Equal("Barbell Bench Press", name.Text);
            Assert.Equal("/tmp/api-thumbnail.png", Assert.IsType<FileImageSource>(thumbnail.Source).File);
            Assert.False(edit.IsVisible);
            Assert.Same(toggleCommand, tap.Command);
            Assert.Same(item, tap.CommandParameter);
            Assert.Same(editCommand, edit.Command);
            Assert.Same(cached, edit.CommandParameter);
        }
        finally
        {
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
        }
    }

    private sealed class HeadlessDispatcherProvider : IDispatcherProvider
    {
        public IDispatcher GetForCurrentThread() => new HeadlessDispatcher();
    }

    private sealed class HeadlessDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => new HeadlessTimer();
    }

    private sealed class HeadlessTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }
}
