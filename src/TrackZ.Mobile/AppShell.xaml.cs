using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile;

public partial class AppShell : Shell
{
	public AppShell(
		WorkoutPage workoutPage,
		ExercisePickerPage exercisePickerPage,
		IServiceProvider services)
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
		Items.Add(new ShellContent
		{
			Title = WorkoutResources.Current.HistoryTitle,
			Route = "history",
			ContentTemplate = new DataTemplate(() =>
				services.GetRequiredService<WorkoutHistoryPage>())
		});
		Routing.RegisterRoute(nameof(CustomExercisePage), typeof(CustomExercisePage));
		Routing.RegisterRoute(nameof(SetLoggerPage), typeof(SetLoggerPage));
		Routing.RegisterRoute(nameof(WorkoutHistoryPage), typeof(WorkoutHistoryPage));
	}
}
