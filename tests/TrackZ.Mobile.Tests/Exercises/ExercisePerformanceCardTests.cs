using TrackZ.Domain.Exercises;
using Microsoft.Maui.Dispatching;
using TrackZ.Mobile.Components;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class ExercisePerformanceCardTests
{
    [Fact]
    public void Api_cached_artwork_card_renders_identity_and_tap_selects_the_exercise()
    {
        var originalDispatcher = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new InlineDispatcherProvider());
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        const string thumbnail = "/local/cache/barbell-bench-press.png";
        var item = new ExercisePickerItem(new CachedExercise
        {
            Id = id,
            Name = "Barbell Bench Press",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted,
            ThumbnailUri = thumbnail,
            LastSyncedAt = DateTimeOffset.UtcNow
        }, WorkoutResources.English);
        try
        {
            var selected = new List<Guid>();
            var card = new ExercisePerformanceCard
            {
                Exercise = item,
                ToggleCommand = new Command<ExercisePickerItem>(selectedItem => selected.Add(selectedItem.Id))
            };

            var name = Assert.IsType<Label>(card.FindByName("ExerciseNameLabel"));
            var artwork = Assert.IsType<Image>(card.FindByName("ArtworkImage"));
            var edit = Assert.IsType<Button>(card.FindByName("EditAction"));
            var tap = Assert.IsType<TapGestureRecognizer>(card.FindByName("SelectionTap"));

            Assert.Equal("Barbell Bench Press", name.Text);
            Assert.True(artwork.IsVisible);
            Assert.Equal(thumbnail, Assert.IsType<FileImageSource>(artwork.Source).File);
            Assert.False(edit.IsVisible);

            Assert.True(tap.Command!.CanExecute(tap.CommandParameter));
            tap.Command.Execute(tap.CommandParameter);
            Assert.Equal([id], selected);
        }
        finally
        {
            DispatcherProvider.SetCurrent(originalDispatcher);
        }
    }

    [Fact]
    public void Missing_api_artwork_uses_neutral_placeholder_state()
    {
        var item = new ExercisePickerItem(new CachedExercise
        {
            Id = Guid.NewGuid(),
            Name = "Cable Raise",
            BodyPart = BodyPart.Shoulders,
            TrackingMode = TrackingMode.Weighted,
            RemoteThumbnailRoute = "/api/v1/media/exercise-images/2/thumbnail",
            LastSyncedAt = DateTimeOffset.UtcNow
        }, WorkoutResources.English);

        Assert.False(item.HasArtwork);
        Assert.True(item.ShowsArtworkPlaceholder);
        Assert.NotEqual(ExerciseArtworkState.Ready, item.ArtworkState);
    }

    private sealed class InlineDispatcherProvider : IDispatcherProvider
    {
        public IDispatcher GetForCurrentThread() => new InlineDispatcher();
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => new InlineTimer();
    }

    private sealed class InlineTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }
}
