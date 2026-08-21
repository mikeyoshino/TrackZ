using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Features.Progress;
using TrackZ.Mobile.Features.Profile;
using TrackZ.Mobile.Features.Summary;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Presentation;
using TrackZ.Mobile.Features.Train;

namespace TrackZ.Mobile;

public partial class AppShell : Shell
{
	public AppShell(
		TrainPage trainPage,
		WorkoutPage workoutPage,
		IServiceProvider services)
	{
		InitializeComponent();
		var tabs = new TabBar();
		tabs.Items.Add(CreateTab(
			WorkoutResources.Current.TrainTab,
			"tab_train.png",
			new ShellContent { Route = "train", Content = trainPage }));
		tabs.Items.Add(CreateTab(
			WorkoutResources.Current.HistoryTab,
			"tab_history.png",
			new ShellContent
			{
				Route = "history",
				ContentTemplate = new DataTemplate(() => services.GetRequiredService<WorkoutHistoryPage>())
			}));
		tabs.Items.Add(CreateTab(
			GamificationResources.Current.ProgressTitle,
			"tab_progress.png",
			new ShellContent
			{
				Route = "progress",
				ContentTemplate = new DataTemplate(() => services.GetRequiredService<ExerciseProgressPage>())
			}));
		tabs.Items.Add(CreateTab(
			GamificationResources.Current.YouTab,
			"tab_you.png",
			new ShellContent
			{
				Route = "you",
				ContentTemplate = new DataTemplate(() => services.GetRequiredService<ProfilePage>())
			}));
		Items.Add(tabs);

		Routing.RegisterRoute(nameof(ExercisePickerPage), typeof(ExercisePickerPage));
		Routing.RegisterRoute("active-workout", typeof(WorkoutPage));
		Routing.RegisterRoute(nameof(CustomExercisePage), typeof(CustomExercisePage));
		Routing.RegisterRoute(nameof(SetLoggerPage), typeof(SetLoggerPage));
		Routing.RegisterRoute(nameof(WorkoutHistoryPage), typeof(WorkoutHistoryPage));
		Routing.RegisterRoute("workout-history-detail", typeof(WorkoutHistoryDetailPage));
		Routing.RegisterRoute(nameof(WorkoutSummaryPage), typeof(WorkoutSummaryPage));
	}

	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
#if IOS
		NativeNavigationConfiguration.Configure();
#endif
	}

	private static Tab CreateTab(string title, string icon, ShellContent content)
	{
		var tab = new Tab
		{
			Title = title,
			Icon = ImageSource.FromFile(icon)
		};
		tab.Items.Add(content);
		return tab;
	}
}
