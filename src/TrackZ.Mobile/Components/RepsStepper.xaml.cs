using System.Windows.Input;

namespace TrackZ.Mobile.Components;

public partial class RepsStepper : ContentView
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(nameof(Value), typeof(int), typeof(RepsStepper), 0, BindingMode.TwoWay);
    public static readonly BindableProperty CaptionProperty = BindableProperty.Create(nameof(Caption), typeof(string), typeof(RepsStepper), string.Empty);
    public static readonly BindableProperty IncrementLabelProperty = BindableProperty.Create(nameof(IncrementLabel), typeof(string), typeof(RepsStepper), string.Empty);
    public static readonly BindableProperty DecrementLabelProperty = BindableProperty.Create(nameof(DecrementLabel), typeof(string), typeof(RepsStepper), string.Empty);
    public static readonly BindableProperty IncrementCommandProperty = BindableProperty.Create(nameof(IncrementCommand), typeof(ICommand), typeof(RepsStepper));
    public static readonly BindableProperty DecrementCommandProperty = BindableProperty.Create(nameof(DecrementCommand), typeof(ICommand), typeof(RepsStepper));

    public RepsStepper() => InitializeComponent();
    public Entry Input => ValueInput;
    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string IncrementLabel { get => (string)GetValue(IncrementLabelProperty); set => SetValue(IncrementLabelProperty, value); }
    public string DecrementLabel { get => (string)GetValue(DecrementLabelProperty); set => SetValue(DecrementLabelProperty, value); }
    public ICommand? IncrementCommand { get => (ICommand?)GetValue(IncrementCommandProperty); set => SetValue(IncrementCommandProperty, value); }
    public ICommand? DecrementCommand { get => (ICommand?)GetValue(DecrementCommandProperty); set => SetValue(DecrementCommandProperty, value); }
}
