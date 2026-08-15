using TrackZ.Mobile.Features.Exercises;

namespace TrackZ.Mobile;

public partial class AppShell : Shell
{
	public AppShell(ExercisePickerPage exercisePickerPage)
	{
		InitializeComponent();
		Items.Add(new ShellContent
		{
			Title = "Exercises",
			Route = "exercises",
			Content = exercisePickerPage
		});
		Routing.RegisterRoute(nameof(CustomExercisePage), typeof(CustomExercisePage));
	}
}
