using System.Windows.Input;
using TrackZ.Mobile.Features.Exercises.Models;

namespace TrackZ.Mobile.Components;

public partial class ExercisePerformanceCard : ContentView
{
    public static readonly BindableProperty ExerciseProperty = BindableProperty.Create(
        nameof(Exercise), typeof(CachedExercise), typeof(ExercisePerformanceCard));

    public static readonly BindableProperty ToggleCommandProperty = BindableProperty.Create(
        nameof(ToggleCommand), typeof(ICommand), typeof(ExercisePerformanceCard));

    public static readonly BindableProperty EditCommandProperty = BindableProperty.Create(
        nameof(EditCommand), typeof(ICommand), typeof(ExercisePerformanceCard));

    public ExercisePerformanceCard() => InitializeComponent();

    public CachedExercise? Exercise
    {
        get => (CachedExercise?)GetValue(ExerciseProperty);
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
}
