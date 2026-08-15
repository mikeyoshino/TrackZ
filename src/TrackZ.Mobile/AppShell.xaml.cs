using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile;

public partial class AppShell : Shell
{
	public AppShell(WorkoutPage workoutPage, ExercisePickerPage exercisePickerPage)
	{
		InitializeComponent();
		Items.Add(new ShellContent
		{
			Title = WorkoutResources.Current.WorkoutTitle,
			Route = "train",
			Content = workoutPage
		});
		Items.Add(new ShellContent
		{
			Title = WorkoutResources.Current.AddExercise,
			Route = "exercises",
			Content = exercisePickerPage
		});
		Routing.RegisterRoute(nameof(CustomExercisePage), typeof(CustomExercisePage));
		Routing.RegisterRoute(nameof(SetLoggerPage), typeof(SetLoggerPage));
	}
}
