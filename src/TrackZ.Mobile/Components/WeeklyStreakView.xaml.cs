namespace TrackZ.Mobile.Components;

public partial class WeeklyStreakView : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(WeeklyStreakView));
    public static readonly BindableProperty CompletedTextProperty = BindableProperty.Create(nameof(CompletedText), typeof(string), typeof(WeeklyStreakView));
    public static readonly BindableProperty AccessibilityTextProperty = BindableProperty.Create(nameof(AccessibilityText), typeof(string), typeof(WeeklyStreakView));
    public static readonly BindableProperty CurrentWeeksProperty = BindableProperty.Create(nameof(CurrentWeeks), typeof(int), typeof(WeeklyStreakView));
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(nameof(Progress), typeof(double), typeof(WeeklyStreakView));
    public WeeklyStreakView() => InitializeComponent();
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? CompletedText { get => (string?)GetValue(CompletedTextProperty); set => SetValue(CompletedTextProperty, value); }
    public string? AccessibilityText { get => (string?)GetValue(AccessibilityTextProperty); set => SetValue(AccessibilityTextProperty, value); }
    public int CurrentWeeks { get => (int)GetValue(CurrentWeeksProperty); set => SetValue(CurrentWeeksProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
}
