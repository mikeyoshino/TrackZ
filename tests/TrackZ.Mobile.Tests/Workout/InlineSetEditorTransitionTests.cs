using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class InlineSetEditorTransitionTests
{
    [Fact]
    public async Task Reveal_resets_a_retained_previous_draft_vertical_position()
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
        scroll.Arrange(new Rect(0, 0, 390, 600));
        content.Arrange(new Rect(0, 0, 390, 1_000));
        spacer.Arrange(new Rect(0, 0, 390, 700));
        editor.Arrange(new Rect(0, 700, 390, 300));
        using var cancellation = new CancellationTokenSource();
        var request = new TaskCompletionSource<double>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var transition = new MauiInlineSetEditorTransition(
            invokeOnMainThread: action => action(),
            scrollTo: (_, _, y, _) =>
            {
                request.TrySetResult(y);
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        var reveal = transition.RevealAsync(
            scroll,
            editor,
            "Set 2",
            cancellation.Token);
        var requestedY = await request.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reveal);
        Assert.Equal(0, requestedY);
    }

    [Fact]
    public async Task Reveal_never_requests_a_negative_vertical_offset_for_an_editor_near_the_top()
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
        scroll.Arrange(new Rect(0, 0, 390, 600));
        content.Arrange(new Rect(0, 0, 390, 800));
        spacer.Arrange(new Rect(0, 0, 390, 50));
        editor.Arrange(new Rect(0, 50, 390, 200));
        using var cancellation = new CancellationTokenSource();
        double? requestedY = null;
        var transition = new MauiInlineSetEditorTransition(
            invokeOnMainThread: action => action(),
            scrollTo: (_, _, y, _) =>
            {
                requestedY = y;
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        var reveal = transition.RevealAsync(
            scroll,
            editor,
            "Set 1",
            cancellation.Token);
        content.Arrange(new Rect(0, 0, 390, 801));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reveal);
        Assert.Equal(0, requestedY);
    }

    [Fact]
    public async Task Reveal_waits_for_the_newly_visible_editor_to_receive_a_positive_layout()
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
        scroll.Arrange(new Rect(0, 0, 390, 600));
        Assert.True(editor.Width <= 0 || editor.Height <= 0);
        using var cancellation = new CancellationTokenSource();
        var request = new TaskCompletionSource<ScrollRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var transition = new MauiInlineSetEditorTransition(
            invokeOnMainThread: action => action(),
            scrollTo: (viewport, x, y, animated) =>
            {
                request.TrySetResult(new ScrollRequest(viewport, x, y, animated));
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        var reveal = transition.RevealAsync(
            scroll,
            editor,
            "Set 1",
            cancellation.Token);
        var requestedBeforeLayout = request.Task.IsCompleted;

        spacer.Arrange(new Rect(0, 0, 700, 700));
        editor.Arrange(new Rect(0, 700, 700, 300));
        content.Arrange(new Rect(0, 0, 700, 1_000));

        var completedRequest = await request.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reveal);
        Assert.False(requestedBeforeLayout);
        Assert.Equal(0, completedRequest.X);
        Assert.Equal(0, completedRequest.Y);
    }

    [Fact]
    public async Task Cancelling_before_layout_prevents_a_late_scroll_request()
    {
        var editor = new Border();
        var content = new VerticalStackLayout
        {
            Children = { editor }
        };
        var scroll = new ScrollView
        {
            Content = content,
            Orientation = ScrollOrientation.Vertical
        };
        scroll.Arrange(new Rect(0, 0, 390, 600));
        using var cancellation = new CancellationTokenSource();
        var scrollCount = 0;
        var transition = new MauiInlineSetEditorTransition(
            invokeOnMainThread: action => action(),
            scrollTo: (_, _, _, _) =>
            {
                scrollCount++;
                return Task.CompletedTask;
            });

        var reveal = transition.RevealAsync(
            scroll,
            editor,
            "Set 1",
            cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reveal);
        content.Arrange(new Rect(0, 0, 390, 800));
        editor.Arrange(new Rect(0, 0, 390, 300));
        await Task.Yield();

        Assert.Equal(0, scrollCount);
    }

    [Fact]
    public async Task Cancellation_winning_after_layout_prevents_the_scroll_request()
    {
        var editor = new Border();
        var content = new VerticalStackLayout
        {
            Children = { editor }
        };
        var scroll = new ScrollView
        {
            Content = content,
            Orientation = ScrollOrientation.Vertical
        };
        scroll.Arrange(new Rect(0, 0, 390, 600));
        using var cancellation = new CancellationTokenSource();
        var scrollCount = 0;
        var transition = new MauiInlineSetEditorTransition(
            invokeOnMainThread: action => action(),
            scrollTo: (_, _, _, _) =>
            {
                scrollCount++;
                return Task.CompletedTask;
            });
        var reveal = transition.RevealAsync(
            scroll,
            editor,
            "Set 1",
            cancellation.Token);

        editor.Arrange(new Rect(0, 0, 390, 300));
        content.Arrange(new Rect(0, 0, 390, 800));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reveal);
        Assert.Equal(0, scrollCount);
    }

    [Fact]
    public async Task Reveal_resets_the_vertical_viewport_without_moving_horizontally()
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
            "Set 1",
            cancellation.Token);
        content.Arrange(new Rect(0, 0, 700, 1_001));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reveal);
        Assert.NotNull(request);
        Assert.Same(scroll, request.Viewport);
        Assert.Equal(0, request.X);
        Assert.Equal(0, request.Y);
        Assert.True(request.Animated);
    }

    private sealed record ScrollRequest(ScrollView Viewport, double X, double Y, bool Animated);
}
