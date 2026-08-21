using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Auth;

public partial class CreateAccountPage : ContentPage
{
    public CreateAccountPage(AuthFormViewModel form, AuthGateCoordinator gate)
    {
        InitializeComponent();
        Gate = gate;
        Form = form;
        Form.SetMode(AuthFormMode.CreateAccount);
        BindingContext = Form;
        EmailInput.Completed += (_, _) => PasswordInput.Focus();
        PasswordInput.Completed += (_, _) => SubmitAction.Focus();
    }

    public AuthGateCoordinator Gate { get; }
    public AuthFormViewModel Form { get; }
    public Microsoft.Maui.Controls.Entry EmailEntry => EmailInput;
    public Microsoft.Maui.Controls.Entry PasswordEntry => PasswordInput;
    public Button SubmitButton => SubmitAction;
    public Button SwitchModeButton => SwitchModeAction;

    private void OnSubmitClicked(object? sender, EventArgs eventArgs) => PasswordInput.Unfocus();

    private async void OnSwitchModeClicked(object? sender, EventArgs eventArgs) => await Navigation.PopAsync();
}
