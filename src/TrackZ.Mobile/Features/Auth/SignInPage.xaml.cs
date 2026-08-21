using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Auth;

public partial class SignInPage : ContentPage
{
    private readonly IServiceProvider _services;

    public SignInPage(AuthFormViewModel form, AuthGateCoordinator gate, IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        Gate = gate;
        Form = form;
        Form.SetMode(AuthFormMode.SignIn);
        BindingContext = Form;
        EmailInput.Completed += (_, _) => PasswordInput.Focus();
        PasswordInput.Completed += (_, _) => SubmitAction.Focus();
    }

    public AuthGateCoordinator Gate { get; }
    public AuthFormViewModel Form { get; }
    public Label WelcomeBody => WelcomeBodyLabel;
    public Border EmailField => EmailFieldContainer;
    public Border PasswordField => PasswordFieldContainer;
    public Microsoft.Maui.Controls.Entry EmailEntry => EmailInput;
    public Microsoft.Maui.Controls.Entry PasswordEntry => PasswordInput;
    public Button SubmitButton => SubmitAction;
    public Button SwitchModeButton => SwitchModeAction;

    private void OnSubmitClicked(object? sender, EventArgs eventArgs) => PasswordInput.Unfocus();

    private async void OnSwitchModeClicked(object? sender, EventArgs eventArgs) =>
        await Navigation.PushAsync(_services.GetRequiredService<CreateAccountPage>());
}
