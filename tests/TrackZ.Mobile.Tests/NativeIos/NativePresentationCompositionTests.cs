using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Presentation;

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
}
