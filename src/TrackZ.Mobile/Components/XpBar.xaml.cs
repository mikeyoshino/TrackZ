namespace TrackZ.Mobile.Components;

public partial class XpBar : ContentView
{
    public static readonly BindableProperty LevelProperty = BindableProperty.Create(nameof(Level), typeof(int), typeof(XpBar), 1);
    public static readonly BindableProperty TotalXpProperty = BindableProperty.Create(nameof(TotalXp), typeof(int), typeof(XpBar), 0);
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(nameof(Progress), typeof(double), typeof(XpBar), 0d);

    public XpBar() => InitializeComponent();
    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public int TotalXp { get => (int)GetValue(TotalXpProperty); set => SetValue(TotalXpProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
}
