using Microsoft.Extensions.DependencyInjection;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Features.Profile;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Presentation;
using TrackZ.Mobile.Components;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class NativePresentationCompositionTests
{
    [Fact]
    public void Profile_page_can_be_created_before_weight_unit_bindings_are_evaluated()
    {
        using var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton<IWeightUnitPreference, KilogramPreference>();
            services.AddSingleton(new TrackZ.Mobile.Data.TrackZLocalDatabase(
                Path.Combine(Path.GetTempPath(), $"trackz-profile-composition-{Guid.NewGuid():N}.db")));
            services.AddSingleton<IProgressSnapshotSource, EmptyProgressSource>();
            services.AddSingleton<IConnectivityService, OfflineConnectivity>();
            services.AddSingleton<IMobilePrivateDataCleaner, NoopPrivateDataCleaner>();
            services.AddSingleton<IAppLanguageChanger, FixedLanguageChanger>();
            services.AddSingleton<IProfileSignOutRiskSource, ClearSignOutRiskSource>();
        });

        var page = app.Services.GetRequiredService<ProfilePage>();

        Assert.True(page.IsKilogramsSelected);
        Assert.False(page.IsPoundsSelected);
    }

    [Fact]
    public void Native_sheet_presenter_is_singleton_and_resolvable()
    {
        using var app = MauiProgram.CreateMauiApp();

        Assert.Same(
            app.Services.GetRequiredService<INativeSheetPresenter>(),
            app.Services.GetRequiredService<INativeSheetPresenter>());
    }

    [Fact]
    public void Effort_sheet_page_and_view_model_are_transient_while_presenter_stays_singleton()
    {
        using var app = MauiProgram.CreateMauiApp(services =>
            services.AddSingleton<ISetEffortRecorder, StubEffortRecorder>());

        Assert.NotSame(
            app.Services.GetRequiredService<SetEffortSheetPage>(),
            app.Services.GetRequiredService<SetEffortSheetPage>());
        Assert.NotSame(
            app.Services.GetRequiredService<ISetEffortSheet>(),
            app.Services.GetRequiredService<ISetEffortSheet>());
        Assert.NotSame(
            app.Services.GetRequiredService<SetEffortPromptViewModel>(),
            app.Services.GetRequiredService<SetEffortPromptViewModel>());
        Assert.Same(
            app.Services.GetRequiredService<INativeSheetPresenter>(),
            app.Services.GetRequiredService<INativeSheetPresenter>());
    }

    [Fact]
    public void Identity_protected_api_and_signed_media_use_distinct_registered_clients()
    {
        using var app = MauiProgram.CreateMauiApp(services =>
            services.AddSingleton<IAuthEntryPoint, StubAuthEntryPoint>());

        var identity = app.Services.GetRequiredService<IdentityHttpTransport>().HttpClient;
        var protectedApi = app.Services.GetRequiredService<HttpClient>();
        var signedMedia = app.Services.GetRequiredService<SignedMediaDownloadClient>().HttpClient;

        Assert.NotSame(identity, protectedApi);
        Assert.NotSame(identity, signedMedia);
        Assert.NotSame(protectedApi, signedMedia);
        Assert.IsType<ProtectedRequestAuthentication>(
            app.Services.GetRequiredService<IProtectedRequestAuthentication>());
    }

    [Fact]
    public void State_view_hides_an_absent_action_and_executes_a_visible_action()
    {
        var calls = 0;
        var view = new TrackZStateView();
        var action = view.FindByName<Button>("ActionButton");

        Assert.False(action.IsVisible);

        view.ActionText = "Try again";
        view.ActionCommand = new Command(() => calls++);
        action.Command.Execute(null);

        Assert.True(action.IsVisible);
        Assert.Equal("Try again", action.Text);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Skeleton_renders_three_stable_card_silhouettes_without_animation()
    {
        var skeleton = new ExerciseListSkeleton();
        var componentDirectory = Path.Combine(FindSolutionDirectory(), "src", "TrackZ.Mobile", "Components");
        var xaml = File.ReadAllText(Path.Combine(componentDirectory, "ExerciseListSkeleton.xaml"));

        Assert.NotNull(skeleton.Content);
        Assert.Equal(3, xaml.Split("TrackZExerciseCardHeight", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Animation", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Auth_entry_point_resolves_without_eagerly_constructing_the_auth_gate_graph()
    {
        using var app = MauiProgram.CreateMauiApp();

        var entryPoint = app.Services.GetRequiredService<IAuthEntryPoint>();

        Assert.IsType<MauiAuthEntryPoint>(entryPoint);
    }

    private sealed class StubAuthEntryPoint : IAuthEntryPoint
    {
        public Task RequireSignInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RequireSignInAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class KilogramPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
    }

    private sealed class EmptyProgressSource : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(null);
        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(
            int weeklyGoal,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class NoopPrivateDataCleaner : IMobilePrivateDataCleaner
    {
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ClearSignOutRiskSource : IProfileSignOutRiskSource
    {
        public Task<ProfileSignOutRisk> GetRiskAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProfileSignOutRisk.Clear);
    }

    private sealed class FixedLanguageChanger : IAppLanguageChanger
    {
        public AppLanguage Current => AppLanguage.Thai;
        public bool IsChanging => false;
        public Task ChangeAsync(AppLanguage language, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubEffortRecorder : ISetEffortRecorder
    {
        public Task<LocalSet> RecordSetEffortAsync(
            Guid exerciseDefinitionId,
            Guid setId,
            SetEffortRating effort,
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LocalWorkout?> RestoreActiveAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
}
