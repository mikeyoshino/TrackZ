using System.Windows.Input;
using TrackZ.Mobile.Features.Shared;

namespace TrackZ.Mobile.Components;

public partial class TrackZNoticeBanner : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(TrackZNoticeBanner), string.Empty,
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty SeverityProperty = BindableProperty.Create(
        nameof(Severity), typeof(TrackZNoticeSeverity), typeof(TrackZNoticeBanner),
        TrackZNoticeSeverity.Information,
        propertyChanged: OnSeverityChanged);

    public static readonly BindableProperty DismissTextProperty = BindableProperty.Create(
        nameof(DismissText), typeof(string), typeof(TrackZNoticeBanner), string.Empty);

    public static readonly BindableProperty DismissCommandProperty = BindableProperty.Create(
        nameof(DismissCommand), typeof(ICommand), typeof(TrackZNoticeBanner), null,
        propertyChanged: OnDismissCommandChanged);

    public TrackZNoticeBanner()
    {
        InitializeComponent();
        ApplySeverity(TrackZNoticeSeverity.Information);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public TrackZNoticeSeverity Severity
    {
        get => (TrackZNoticeSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string DismissText
    {
        get => (string)GetValue(DismissTextProperty);
        set => SetValue(DismissTextProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => (ICommand?)GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((TrackZNoticeBanner)bindable).IsVisible = !string.IsNullOrWhiteSpace((string?)newValue);

    private static void OnSeverityChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((TrackZNoticeBanner)bindable).ApplySeverity((TrackZNoticeSeverity)newValue);

    private static void OnDismissCommandChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((TrackZNoticeBanner)bindable).DismissNoticeButton.IsVisible = newValue is ICommand;

    private void ApplySeverity(TrackZNoticeSeverity severity)
    {
        var (icon, accent, surface) = severity switch
        {
            TrackZNoticeSeverity.Warning => ("!", "TrackZWarning", "TrackZSyncPendingSurface"),
            TrackZNoticeSeverity.Error => ("×", "TrackZDanger", "TrackZSyncFailureSurface"),
            TrackZNoticeSeverity.Success => ("✓", "TrackZPrimary", "TrackZSyncSyncedSurface"),
            _ => ("i", "TrackZInformation", "TrackZSyncingSurface")
        };
        NoticeIcon.Text = icon;
        NoticeIcon.SetDynamicResource(Label.TextColorProperty, accent);
        NoticeSurface.SetDynamicResource(Border.StrokeProperty, accent);
        NoticeSurface.SetDynamicResource(BackgroundColorProperty, surface);
    }
}
