using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Mobile.Features.Auth;

public sealed class AuthShell : Shell
{
    public AuthShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        Items.Add(new ShellContent
        {
            Route = "sign-in",
            ContentTemplate = new DataTemplate(() => services.GetRequiredService<SignInPage>())
        });
    }
}
