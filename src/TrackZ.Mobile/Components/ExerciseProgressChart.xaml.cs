namespace TrackZ.Mobile.Components;

public partial class ExerciseProgressChart : ContentView
{
    public static readonly BindableProperty ExerciseNameProperty = BindableProperty.Create(nameof(ExerciseName), typeof(string), typeof(ExerciseProgressChart));
    public static readonly BindableProperty PersonalRecordTextProperty = BindableProperty.Create(nameof(PersonalRecordText), typeof(string), typeof(ExerciseProgressChart));
    public ExerciseProgressChart() => InitializeComponent();
    public string? ExerciseName { get => (string?)GetValue(ExerciseNameProperty); set => SetValue(ExerciseNameProperty, value); }
    public string? PersonalRecordText { get => (string?)GetValue(PersonalRecordTextProperty); set => SetValue(PersonalRecordTextProperty, value); }
}
