using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Presentation;
using TrackZ.Mobile.Components;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class NativePresentationCompositionTests
{
    [Fact]
    public void Native_sheet_presenter_is_singleton_and_resolvable()
    {
        using var app = MauiProgram.CreateMauiApp();

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
}
