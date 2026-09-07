using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.Workout;

public sealed class EmptyWorkoutSheetPage : ContentPage
{
    public EmptyWorkoutSheetPage(WorkoutTextSet text, Func<bool, Task> complete)
    {
        SetDynamicResource(BackgroundColorProperty, "TrackZSurface");
        SafeAreaEdges = SafeAreaEdges.All;
        var layout = new Grid
        {
            Padding = new Thickness(24, 12, 24, 20),
            RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            RowSpacing = 12
        };
        var close = new ImageButton
        {
            Source = "close.png", HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start,
            WidthRequest = 44, HeightRequest = 44, Padding = 12,
            BackgroundColor = Colors.Transparent
        };
        SemanticProperties.SetDescription(close, text.Cancel);
        var content = new VerticalStackLayout
        {
            Spacing = 12, VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
        };
        var icon = new Image { Source = "workout_empty.png", WidthRequest = 56, HeightRequest = 56, HorizontalOptions = LayoutOptions.Center };
        AutomationProperties.SetExcludedWithChildren(icon, true);
        content.Add(icon);
        var title = new Label { Text = text.AddBeforeStartingTitle, HorizontalTextAlignment = TextAlignment.Center };
        title.SetDynamicResource(StyleProperty, "TrackZHomeEmptyTitleStyle");
        content.Add(title);
        var helper = new Label { Text = text.AddBeforeStartingHelper, HorizontalTextAlignment = TextAlignment.Center };
        helper.SetDynamicResource(StyleProperty, "TrackZSecondaryStyle");
        content.Add(helper);
        var actions = new Grid { ColumnDefinitions = { new(new GridLength(0.4, GridUnitType.Star)), new(new GridLength(0.6, GridUnitType.Star)) }, ColumnSpacing = 12 };
        var later = new Button { Text = text.NotNow, AutomationId = "EmptyWorkoutLater" };
        later.SetDynamicResource(StyleProperty, "TrackZSecondaryButtonStyle");
        var add = new Button { Text = text.AddExercise, AutomationId = "EmptyWorkoutAdd" };
        add.SetDynamicResource(StyleProperty, "TrackZPrimaryButtonStyle");
        var completing = false;
        async Task Finish(bool shouldAdd)
        {
            if (completing) return;
            completing = true;
            try { await complete(shouldAdd); }
            finally { completing = false; }
        }
        close.Clicked += async (_, _) => await Finish(false);
        later.Clicked += async (_, _) => await Finish(false);
        add.Clicked += async (_, _) => await Finish(true);
        actions.Add(later);
        actions.Add(add, 1);
        layout.Add(new ScrollView { Content = content });
        layout.Add(close);
        layout.Add(actions, 0, 1);
        Content = layout;
    }
}
