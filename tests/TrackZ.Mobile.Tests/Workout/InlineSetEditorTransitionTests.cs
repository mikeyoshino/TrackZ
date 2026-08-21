using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class InlineSetEditorTransitionTests
{
    [Fact]
    public async Task Reveal_centers_editor_vertically_without_moving_the_horizontal_viewport()
    {
        var spacer = new BoxView();
        var editor = new Border();
        var content = new VerticalStackLayout
        {
            Children = { spacer, editor }
        };
        var scroll = new ScrollView
        {
            Content = content,
            Orientation = ScrollOrientation.Vertical
        };
        var focusTarget = new Entry();
        scroll.Arrange(new Rect(0, 0, 390, 600));
        content.Arrange(new Rect(0, 0, 700, 1_000));
        spacer.Arrange(new Rect(0, 0, 700, 700));
        editor.Arrange(new Rect(0, 700, 700, 300));
        using var cancellation = new CancellationTokenSource();
        ScrollRequest? request = null;
        var transition = new MauiInlineSetEditorTransition(
            invokeOnMainThread: action => action(),
            scrollTo: (viewport, x, y, animated) =>
            {
                request = new ScrollRequest(viewport, x, y, animated);
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        var reveal = transition.RevealAsync(
            scroll,
            editor,
            focusTarget,
            "Set 1",
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reveal);
        Assert.NotNull(request);
        Assert.Same(scroll, request.Viewport);
        Assert.Equal(0, request.X);
        Assert.Equal(550, request.Y);
        Assert.True(request.Animated);
    }

    private sealed record ScrollRequest(ScrollView Viewport, double X, double Y, bool Animated);
}
