using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Mobile.Features.Auth;

public sealed class AuthShell : Shell
{
    public AuthShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        Items.Add(new ShellContent
        {
            Route = "welcome",
            ContentTemplate = new DataTemplate(() => services.GetRequiredService<WelcomePage>())
        });
    }
}
