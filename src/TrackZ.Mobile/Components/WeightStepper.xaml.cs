using System.Windows.Input;

namespace TrackZ.Mobile.Components;

public partial class WeightStepper : ContentView
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(decimal), typeof(WeightStepper), 0m, BindingMode.TwoWay);
    public static readonly BindableProperty CaptionProperty = BindableProperty.Create(
        nameof(Caption), typeof(string), typeof(WeightStepper), string.Empty);
    public static readonly BindableProperty UnitLabelProperty = BindableProperty.Create(
        nameof(UnitLabel), typeof(string), typeof(WeightStepper), string.Empty);
    public static readonly BindableProperty IncrementLabelProperty = BindableProperty.Create(
        nameof(IncrementLabel), typeof(string), typeof(WeightStepper), string.Empty);
    public static readonly BindableProperty DecrementLabelProperty = BindableProperty.Create(
        nameof(DecrementLabel), typeof(string), typeof(WeightStepper), string.Empty);
    public static readonly BindableProperty IncrementCommandProperty = BindableProperty.Create(
        nameof(IncrementCommand), typeof(ICommand), typeof(WeightStepper));
    public static readonly BindableProperty DecrementCommandProperty = BindableProperty.Create(
        nameof(DecrementCommand), typeof(ICommand), typeof(WeightStepper));

    public WeightStepper() => InitializeComponent();
    public Entry Input => ValueInput;
    public decimal Value { get => (decimal)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string UnitLabel { get => (string)GetValue(UnitLabelProperty); set => SetValue(UnitLabelProperty, value); }
    public string IncrementLabel { get => (string)GetValue(IncrementLabelProperty); set => SetValue(IncrementLabelProperty, value); }
    public string DecrementLabel { get => (string)GetValue(DecrementLabelProperty); set => SetValue(DecrementLabelProperty, value); }
    public ICommand? IncrementCommand { get => (ICommand?)GetValue(IncrementCommandProperty); set => SetValue(IncrementCommandProperty, value); }
    public ICommand? DecrementCommand { get => (ICommand?)GetValue(DecrementCommandProperty); set => SetValue(DecrementCommandProperty, value); }
}
