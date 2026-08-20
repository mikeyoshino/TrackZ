using System.Windows.Input;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Components;

public partial class ActiveWorkoutExerciseRow : ContentView
{
    public static readonly BindableProperty ExerciseProperty = BindableProperty.Create(
        nameof(Exercise), typeof(WorkoutExerciseDraftItem), typeof(ActiveWorkoutExerciseRow));
    public static readonly BindableProperty OpenCommandProperty = BindableProperty.Create(
        nameof(OpenCommand), typeof(ICommand), typeof(ActiveWorkoutExerciseRow));
    public static readonly BindableProperty MoveUpCommandProperty = BindableProperty.Create(
        nameof(MoveUpCommand), typeof(ICommand), typeof(ActiveWorkoutExerciseRow));
    public static readonly BindableProperty MoveDownCommandProperty = BindableProperty.Create(
        nameof(MoveDownCommand), typeof(ICommand), typeof(ActiveWorkoutExerciseRow));
    public static readonly BindableProperty RemoveCommandProperty = BindableProperty.Create(
        nameof(RemoveCommand), typeof(ICommand), typeof(ActiveWorkoutExerciseRow));
    public static readonly BindableProperty MoveUpTextProperty = BindableProperty.Create(
        nameof(MoveUpText), typeof(string), typeof(ActiveWorkoutExerciseRow));
    public static readonly BindableProperty MoveDownTextProperty = BindableProperty.Create(
        nameof(MoveDownText), typeof(string), typeof(ActiveWorkoutExerciseRow));
    public static readonly BindableProperty RemoveTextProperty = BindableProperty.Create(
        nameof(RemoveText), typeof(string), typeof(ActiveWorkoutExerciseRow));

    public ActiveWorkoutExerciseRow() => InitializeComponent();

    public WorkoutExerciseDraftItem? Exercise { get => (WorkoutExerciseDraftItem?)GetValue(ExerciseProperty); set => SetValue(ExerciseProperty, value); }
    public ICommand? OpenCommand { get => (ICommand?)GetValue(OpenCommandProperty); set => SetValue(OpenCommandProperty, value); }
    public ICommand? MoveUpCommand { get => (ICommand?)GetValue(MoveUpCommandProperty); set => SetValue(MoveUpCommandProperty, value); }
    public ICommand? MoveDownCommand { get => (ICommand?)GetValue(MoveDownCommandProperty); set => SetValue(MoveDownCommandProperty, value); }
    public ICommand? RemoveCommand { get => (ICommand?)GetValue(RemoveCommandProperty); set => SetValue(RemoveCommandProperty, value); }
    public string? MoveUpText { get => (string?)GetValue(MoveUpTextProperty); set => SetValue(MoveUpTextProperty, value); }
    public string? MoveDownText { get => (string?)GetValue(MoveDownTextProperty); set => SetValue(MoveDownTextProperty, value); }
    public string? RemoveText { get => (string?)GetValue(RemoveTextProperty); set => SetValue(RemoveTextProperty, value); }
}
