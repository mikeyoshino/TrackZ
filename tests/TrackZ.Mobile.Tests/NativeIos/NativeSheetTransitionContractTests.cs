namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class NativeSheetTransitionContractTests
{
    [Fact]
    public void Sheet_is_configured_before_the_modal_animation_begins()
    {
        var source = File.ReadAllText(Path.Combine(
            Root(),
            "src/TrackZ.Mobile/Presentation/MauiNativeSheetPresenter.cs"));

        var createController = source.IndexOf("ToUIViewController", StringComparison.Ordinal);
        var configure = source.IndexOf("NativeSheetConfiguration.Configure(controller, detent)", StringComparison.Ordinal);
        var present = source.IndexOf("PresentViewControllerAsync(controller, AnimationsEnabled)", StringComparison.Ordinal);

        Assert.True(createController >= 0, "The native sheet controller must be created explicitly.");
        Assert.True(configure > createController, "The native controller must receive its detent after creation.");
        Assert.True(present > configure, "The fully configured controller must be presented only once.");
        Assert.DoesNotContain("page.Opacity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Ios_controller_uses_page_sheet_style_before_resolving_its_sheet_controller()
    {
        var source = File.ReadAllText(Path.Combine(
            Root(),
            "src/TrackZ.Mobile/Platforms/iOS/NativeSheetConfiguration.cs"));

        var style = source.IndexOf("ModalPresentationStyle = UIModalPresentationStyle.PageSheet", StringComparison.Ordinal);
        var sheet = source.IndexOf("SheetPresentationController", StringComparison.Ordinal);

        Assert.True(style >= 0, "The native controller must explicitly use PageSheet presentation.");
        Assert.True(sheet > style, "PageSheet style must be set before requesting its sheet controller.");
        Assert.Contains("ModalInPresentation = true", source, StringComparison.Ordinal);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("TrackZ.slnx was not found.");
    }
}
