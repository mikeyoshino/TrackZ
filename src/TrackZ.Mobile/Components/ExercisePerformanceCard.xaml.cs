using System.Windows.Input;
using TrackZ.Mobile.Features.Exercises.Models;

namespace TrackZ.Mobile.Components;

public partial class ExercisePerformanceCard : ContentView
{
    public static readonly BindableProperty ExerciseProperty = BindableProperty.Create(
        nameof(Exercise),
        typeof(ExercisePickerItem),
        typeof(ExercisePerformanceCard),
        propertyChanged: OnExerciseChanged);

    public static readonly BindableProperty ToggleCommandProperty = BindableProperty.Create(
        nameof(ToggleCommand),
        typeof(ICommand),
        typeof(ExercisePerformanceCard),
        propertyChanged: OnToggleCommandChanged);

    public static readonly BindableProperty EditCommandProperty = BindableProperty.Create(
        nameof(EditCommand),
        typeof(ICommand),
        typeof(ExercisePerformanceCard),
        propertyChanged: OnEditCommandChanged);

    public ExercisePerformanceCard() => InitializeComponent();

    public ExercisePickerItem? Exercise
    {
        get => (ExercisePickerItem?)GetValue(ExerciseProperty);
        set => SetValue(ExerciseProperty, value);
    }

    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    public ICommand? EditCommand
    {
        get => (ICommand?)GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    private static void OnExerciseChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var card = (ExercisePerformanceCard)bindable;
        card.CardBody.BindingContext = newValue;
        card.SelectionTap.CommandParameter = newValue;
        card.UpdateToggleCommand();
    }

    private static void OnToggleCommandChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((ExercisePerformanceCard)bindable).UpdateToggleCommand();

    private static void OnEditCommandChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((ExercisePerformanceCard)bindable).EditAction.Command = (ICommand?)newValue;

    private void UpdateToggleCommand() =>
        SelectionTap.Command = ToggleCommand ?? Exercise?.ToggleSelectionCommand;
}
