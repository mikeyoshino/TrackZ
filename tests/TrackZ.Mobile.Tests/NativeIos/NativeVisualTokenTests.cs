using Microsoft.Maui.Controls;
using RoundRectangle = Microsoft.Maui.Controls.Shapes.RoundRectangle;
using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class NativeVisualTokenTests
{
    [Theory]
    [InlineData("TrackZSpace4", 4d)]
    [InlineData("TrackZSpace8", 8d)]
    [InlineData("TrackZSpace12", 12d)]
    [InlineData("TrackZSpace16", 16d)]
    [InlineData("TrackZSpace24", 24d)]
    [InlineData("TrackZSpace32", 32d)]
    [InlineData("TrackZPageMargin", 18d)]
    [InlineData("TrackZCardRadius", 16d)]
    [InlineData("TrackZPrimaryActionHeight", 52d)]
    [InlineData("TrackZMinimumTarget", 44d)]
    [InlineData("TrackZFieldHeight", 50d)]
    public void Spacing_and_geometry_tokens_are_exact(string key, double expected)
    {
        using var app = CreateApp();

        Assert.Equal(expected, Assert.IsType<double>(Resources(app)[key]));
    }

    [Fact]
    public void Shared_controls_meet_native_geometry()
    {
        using var app = CreateApp();

        AssertStyle<Button>(app, "TrackZPrimaryButtonStyle", ("MinimumHeightRequest", 52d), ("CornerRadius", 15));
        AssertStyle<Button>(app, "TrackZSecondaryButtonStyle", ("MinimumHeightRequest", 44d));
        AssertStyle<Button>(app, "TrackZDestructiveButtonStyle", ("MinimumHeightRequest", 44d));
        AssertStyle<Entry>(app, "TrackZFieldStyle", ("MinimumHeightRequest", 50d));
        AssertStyle<Border>(app, "TrackZCardStyle", ("StrokeShape", "RoundRectangle 16"));
    }

    [Fact]
    public void Shared_component_xaml_uses_semantic_native_resources()
    {
        var components = new[]
        {
            "TrackZStateView.xaml", "ExerciseListSkeleton.xaml", "ExercisePerformanceCard.xaml",
            "ActiveWorkoutExerciseRow.xaml", "RepsStepper.xaml", "WeightStepper.xaml", "SyncStatusPill.xaml"
        };
        var componentDirectory = Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile", "Components");

        foreach (var component in components)
        {
            var xaml = File.ReadAllText(Path.Combine(componentDirectory, component));
            Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", xaml);
            Assert.DoesNotContain("FontAutoScalingEnabled=\"False\"", xaml, StringComparison.Ordinal);
        }

        var state = File.ReadAllText(Path.Combine(componentDirectory, "TrackZStateView.xaml"));
        Assert.Contains("TrackZSecondaryButtonStyle", state, StringComparison.Ordinal);

        var skeleton = File.ReadAllText(Path.Combine(componentDirectory, "ExerciseListSkeleton.xaml"));
        Assert.Contains("TrackZSkeletonStyle", skeleton, StringComparison.Ordinal);
        Assert.True(CountOccurrences(skeleton, "TrackZSkeletonStyle") >= 3);

        foreach (var stepper in new[] { "RepsStepper.xaml", "WeightStepper.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(componentDirectory, stepper));
            Assert.Equal(1, CountOccurrences(xaml, "SemanticProperties.Description=\"{Binding DecrementLabel"));
            Assert.Equal(1, CountOccurrences(xaml, "SemanticProperties.Description=\"{Binding IncrementLabel"));
            Assert.Equal(2, CountOccurrences(xaml, "TrackZSecondaryButtonStyle"));
            Assert.DoesNotContain("CornerRadius=", xaml, StringComparison.Ordinal);
        }

        var cards = string.Concat(
            File.ReadAllText(Path.Combine(componentDirectory, "ExercisePerformanceCard.xaml")),
            File.ReadAllText(Path.Combine(componentDirectory, "ActiveWorkoutExerciseRow.xaml")));
        Assert.DoesNotContain("FontSize=\"17\"", cards, StringComparison.Ordinal);
        Assert.DoesNotContain("TextColor=\"{DynamicResource TrackZPrimary}\"", cards, StringComparison.Ordinal);
    }

    private static void AssertStyle<T>(MauiApp app, string key, params (string Property, object Expected)[] expectedSetters)
        where T : BindableObject
    {
        var style = Assert.IsType<Style>(Resources(app)[key]);
        Assert.Equal(typeof(T), style.TargetType);

        foreach (var (property, expected) in expectedSetters)
        {
            var setter = FindSetter(style, property);
            Assert.NotNull(setter);
            if (expected is "RoundRectangle 16")
            {
                var shape = Assert.IsType<RoundRectangle>(setter.Value);
                Assert.Equal(16, shape.CornerRadius.TopLeft);
            }
            else if (expected is string expectedText)
                Assert.Equal(expectedText, setter.Value?.ToString());
            else
                Assert.Equal(expected, setter.Value);
        }
    }

    private static ResourceDictionary Resources(MauiApp app) =>
        app.Services.GetRequiredService<App>().Resources;

    private static Setter? FindSetter(Style style, string property)
    {
        for (Style? candidate = style; candidate is not null; candidate = candidate.BasedOn)
        {
            var setter = candidate.Setters.FirstOrDefault(item => item.Property.PropertyName == property);
            if (setter is not null) return setter;
        }

        return null;
    }

    private static MauiApp CreateApp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-native-tokens-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton(new ProgressSnapshotCache(Path.Combine(root, "progress.json")));
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IConnectivityService>(new HeadlessConnectivity());
        });
    }

    private sealed class HeadlessThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class HeadlessConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private static string FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("TrackZ.slnx was not found from the test output directory.");
    }

    private static int CountOccurrences(string value, string match) =>
        value.Split(match, StringSplitOptions.None).Length - 1;
}
