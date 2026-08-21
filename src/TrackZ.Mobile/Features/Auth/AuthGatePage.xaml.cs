using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Auth;

public partial class AuthGatePage : ContentPage
{
    public AuthGatePage(AuthTextSet text)
    {
        InitializeComponent();
        BindingContext = text;
    }

    public ActivityIndicator StatusIndicator => StatusIndicatorView;
}
