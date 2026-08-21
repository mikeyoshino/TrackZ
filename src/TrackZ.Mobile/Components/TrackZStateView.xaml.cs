using System.Windows.Input;

namespace TrackZ.Mobile.Components;

public partial class TrackZStateView : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(TrackZStateView), string.Empty);

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(TrackZStateView), string.Empty);

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText), typeof(string), typeof(TrackZStateView), null,
        propertyChanged: OnActionChanged);

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(
        nameof(ActionCommand), typeof(ICommand), typeof(TrackZStateView), null,
        propertyChanged: OnActionChanged);

    public TrackZStateView() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? ActionText
    {
        get => (string?)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    private static void OnActionChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((TrackZStateView)bindable).UpdateActionVisibility();

    private void UpdateActionVisibility()
    {
        ActionButton.Text = ActionText;
        ActionButton.Command = ActionCommand;
        ActionButton.IsVisible = !string.IsNullOrWhiteSpace(ActionText) && ActionCommand is not null;
    }
}
