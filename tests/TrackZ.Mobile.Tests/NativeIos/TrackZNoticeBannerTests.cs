using Microsoft.Maui.Dispatching;
using TrackZ.Mobile.Components;
using TrackZ.Mobile.Features.Shared;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class TrackZNoticeBannerTests : IDisposable
{
    private readonly IDispatcherProvider _originalDispatcher = DispatcherProvider.Current;

    public TrackZNoticeBannerTests() =>
        DispatcherProvider.SetCurrent(new InlineDispatcherProvider());

    public void Dispose() => DispatcherProvider.SetCurrent(_originalDispatcher);

    [Theory]
    [InlineData(TrackZNoticeSeverity.Information, "i")]
    [InlineData(TrackZNoticeSeverity.Warning, "!")]
    [InlineData(TrackZNoticeSeverity.Error, "×")]
    [InlineData(TrackZNoticeSeverity.Success, "✓")]
    public void Severity_is_expressed_with_a_non_color_icon_and_accessible_message(
        TrackZNoticeSeverity severity,
        string expectedIcon)
    {
        var banner = new TrackZNoticeBanner
        {
            Severity = severity,
            Text = "Two exercises still need a set"
        };
        var surface = banner.FindByName<Border>("NoticeSurface");
        var icon = banner.FindByName<Label>("NoticeIcon");
        var message = banner.FindByName<Label>("NoticeMessage");

        Assert.True(banner.IsVisible);
        Assert.Equal(expectedIcon, icon.Text);
        Assert.Equal("Two exercises still need a set", message.Text);
        Assert.Equal("Two exercises still need a set", SemanticProperties.GetDescription(surface));
        Assert.False(AutomationProperties.GetIsInAccessibleTree(icon));
    }

    [Fact]
    public void Empty_notice_is_hidden_and_dismiss_action_is_forwarded()
    {
        var dismissCount = 0;
        var banner = new TrackZNoticeBanner
        {
            Text = "Could not save",
            DismissText = "Dismiss",
            DismissCommand = new Command(() => dismissCount++)
        };
        var dismiss = banner.FindByName<Button>("DismissNoticeButton");

        dismiss.Command.Execute(null);
        banner.Text = string.Empty;

        Assert.Equal(1, dismissCount);
        Assert.Equal("Dismiss", SemanticProperties.GetDescription(dismiss));
        Assert.False(banner.IsVisible);
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
