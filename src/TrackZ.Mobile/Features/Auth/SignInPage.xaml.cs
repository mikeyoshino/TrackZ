using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
#if IOS
using UIKit;
#endif

namespace TrackZ.Mobile.Features.Auth;

public partial class SignInPage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly IReduceMotionPreference _reduceMotion;
    private CancellationTokenSource? _ambientMotion;

    public SignInPage(
        AuthFormViewModel form,
        AuthGateCoordinator gate,
        IServiceProvider services,
        IReduceMotionPreference reduceMotion)
    {
        InitializeComponent();
        _services = services;
        _reduceMotion = reduceMotion;
        Gate = gate;
        Form = form;
        Form.SetMode(AuthFormMode.SignIn);
        BindingContext = Form;
        BackContours.Drawable = new AuthContourDrawable(isForeground: false);
        FrontContours.Drawable = new AuthContourDrawable(isForeground: true);
        EmailInput.Completed += (_, _) => PasswordInput.Focus();
        PasswordInput.Completed += (_, _) => SubmitAction.Focus();
#if IOS
        EmailInput.HandlerChanged += ConfigureIosAutofill;
        PasswordInput.HandlerChanged += ConfigureIosAutofill;
#endif
    }

    public AuthGateCoordinator Gate { get; }
    public AuthFormViewModel Form { get; }
    public Border EmailField => EmailFieldContainer;
    public Border PasswordField => PasswordFieldContainer;
    public Microsoft.Maui.Controls.Entry EmailEntry => EmailInput;
    public Microsoft.Maui.Controls.Entry PasswordEntry => PasswordInput;
    public Button SubmitButton => SubmitAction;
    public Button SwitchModeButton => SwitchModeAction;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartAmbientMotion();
    }

    protected override void OnDisappearing()
    {
        StopAmbientMotion();
        base.OnDisappearing();
    }

    private void StartAmbientMotion()
    {
        StopAmbientMotion();
        ResetAmbientBackground();
        if (_reduceMotion.IsEnabled)
            return;

        _ambientMotion = new CancellationTokenSource();
        _ = RunAmbientMotionAsync(_ambientMotion.Token);
    }

    private async Task RunAmbientMotionAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.WhenAll(
                    BackContours.TranslateToAsync(10, -8, 7000, Easing.SinInOut),
                    FrontContours.TranslateToAsync(-8, 10, 8000, Easing.SinInOut),
                    AmbientGlow.ScaleToAsync(1.05, 8000, Easing.SinInOut),
                    AmbientGlow.FadeToAsync(0.34, 8000, Easing.SinInOut));
                cancellationToken.ThrowIfCancellationRequested();
                await Task.WhenAll(
                    BackContours.TranslateToAsync(-6, 6, 7000, Easing.SinInOut),
                    FrontContours.TranslateToAsync(7, -7, 8000, Easing.SinInOut),
                    AmbientGlow.ScaleToAsync(0.96, 8000, Easing.SinInOut),
                    AmbientGlow.FadeToAsync(0.22, 8000, Easing.SinInOut));
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopAmbientMotion()
    {
        _ambientMotion?.Cancel();
        _ambientMotion?.Dispose();
        _ambientMotion = null;
        BackContours.CancelAnimations();
        FrontContours.CancelAnimations();
        AmbientGlow.CancelAnimations();
    }

    private void ResetAmbientBackground()
    {
        BackContours.TranslationX = 0;
        BackContours.TranslationY = 0;
        FrontContours.TranslationX = 0;
        FrontContours.TranslationY = 0;
        AmbientGlow.Scale = 1;
        AmbientGlow.Opacity = 0.28;
    }

    private void OnSubmitClicked(object? sender, EventArgs eventArgs) => PasswordInput.Unfocus();

#if IOS
    private void ConfigureIosAutofill(object? sender, EventArgs eventArgs)
    {
        if (EmailInput.Handler?.PlatformView is UITextField emailField)
        {
            emailField.TextContentType = UITextContentType.EmailAddress;
            emailField.BorderStyle = UITextBorderStyle.None;
            emailField.BackgroundColor = UIColor.Clear;
        }
        if (PasswordInput.Handler?.PlatformView is UITextField passwordField)
        {
            passwordField.TextContentType = UITextContentType.Password;
            passwordField.BorderStyle = UITextBorderStyle.None;
            passwordField.BackgroundColor = UIColor.Clear;
        }
    }
#endif

    private async void OnSwitchModeClicked(object? sender, EventArgs eventArgs) =>
        await Navigation.PushAsync(_services.GetRequiredService<CreateAccountPage>());
}
