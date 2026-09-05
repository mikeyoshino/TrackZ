using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Identity;

public enum AuthFormMode
{
    SignIn = 1,
    CreateAccount = 2
}

public sealed record AuthTextSet(
    string SignInTitle,
    string CreateAccountTitle,
    string EmailLabel,
    string PasswordLabel,
    string SignInAction,
    string CreateAccountAction,
    string CreateAccountPrompt,
    string SignInPrompt,
    string CreateAccountLink,
    string SignInLink,
    string InvalidCredentials,
    string EmailAlreadyExists,
    string PasswordPolicyViolation,
    string RetryableConnectionFailure,
    string InvalidResponse,
    string SignInWelcomeBody,
    string CreateAccountWelcomeBody,
    string CreateAccountRequirements,
    string EmailAccessibilityLabel,
    string PasswordAccessibilityLabel,
    string CheckingSessionAccessibilityLabel,
    string EmailRequired,
    string PasswordRequired,
    string InvalidEmail)
{
    public static AuthTextSet English { get; } = new(
        "Track training more easily.\nSee clearer results.", "Start training with structure.\nSee your progress clearly.", "Email", "Password", "Sign in", "Create account",
        "No account yet?", "Already have an account?", "Create account", "Sign in",
        "Email or password is incorrect.", "Use another email address.",
        "Use a password that meets the requirements.", "Check your connection and try again.",
        "The server returned an invalid response.",
        "Sign in to keep your workouts in sync.",
        "Create an account to keep your workouts in sync.",
        "Use 12+ characters with uppercase, lowercase, number, and symbol.",
        "Email", "Password", "Checking session",
        "Enter your email address.", "Enter your password.", "Enter a valid email address.");

    public static AuthTextSet Thai { get; } = new(
        "ติดตามการฝึกง่ายขึ้น\nเห็นผลลัพธ์ชัดขึ้น", "เริ่มต้นฝึกอย่างเป็นระบบ\nเห็นพัฒนาการได้ชัดขึ้น", "อีเมล", "รหัสผ่าน", "เข้าสู่ระบบ", "สร้างบัญชี",
        "ยังไม่มีบัญชี?", "มีบัญชีอยู่แล้ว?", "สร้างบัญชี", "เข้าสู่ระบบ",
        "อีเมลหรือรหัสผ่านไม่ถูกต้อง", "โปรดใช้อีเมลอื่น", "โปรดใช้รหัสผ่านที่ตรงตามข้อกำหนด",
        "ตรวจสอบการเชื่อมต่อแล้วลองอีกครั้ง", "เซิร์ฟเวอร์ตอบกลับข้อมูลไม่ถูกต้อง",
        "เข้าสู่ระบบเพื่อซิงค์การออกกำลังกายของคุณ",
        "สร้างบัญชีเพื่อซิงค์การออกกำลังกายของคุณ",
        "อย่างน้อย 12 ตัวอักษร พร้อมตัวพิมพ์ใหญ่ ตัวพิมพ์เล็ก ตัวเลข และสัญลักษณ์",
        "อีเมล", "รหัสผ่าน", "กำลังตรวจสอบเซสชัน",
        "กรุณากรอกอีเมล", "กรุณากรอกรหัสผ่าน", "กรุณากรอกอีเมลให้ถูกต้อง");

    public static AuthTextSet For(CultureInfo culture) =>
        string.Equals(culture.TwoLetterISOLanguageName, "th", StringComparison.OrdinalIgnoreCase)
            ? Thai
            : English;
}

public sealed class AuthFormViewModel : INotifyPropertyChanged
{
    private readonly AuthGateCoordinator _gate;
    private AuthFormMode _mode;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private bool _isSubmitting;
    private string? _formError;
    private string? _emailError;
    private string? _passwordError;

    public AuthFormViewModel(AuthGateCoordinator gate, AuthTextSet text, AuthFormMode mode = AuthFormMode.SignIn)
    {
        _gate = gate;
        Text = text;
        _mode = mode;
        SubmitCommand = new AsyncCommand(_ => SubmitAsync(), _ => !IsSubmitting);
    }

    public AuthTextSet Text { get; }
    public AuthFormMode Mode { get => _mode; private set => Set(ref _mode, value); }
    public string Email { get => _email; set => Set(ref _email, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public bool IsSubmitting { get => _isSubmitting; private set { if (Set(ref _isSubmitting, value)) SubmitCommand.RaiseCanExecuteChanged(); } }
    public string? FormError { get => _formError; private set => Set(ref _formError, value); }
    public string? EmailError { get => _emailError; private set => Set(ref _emailError, value); }
    public string? PasswordError { get => _passwordError; private set => Set(ref _passwordError, value); }
    public AsyncCommand SubmitCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetMode(AuthFormMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
        FormError = null;
        EmailError = null;
        PasswordError = null;
    }

    private async Task SubmitAsync()
    {
        if (IsSubmitting) return;
        IsSubmitting = true;
        FormError = null;
        EmailError = null;
        PasswordError = null;
        try
        {
            if (!ValidateInput()) return;

            if (Mode == AuthFormMode.SignIn)
                await _gate.SignInAsync(Email, Password);
            else
                await _gate.RegisterAsync(Email, Password);
        }
        catch (OperationCanceledException)
        {
            // Session teardown and navigation cancellation are intentionally silent.
        }
        catch (MobileApiException exception)
        {
            ApplyProblem(exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException)
        {
            FormError = Text.RetryableConnectionFailure;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private bool ValidateInput()
    {
        var valid = true;
        var email = Email?.Trim() ?? string.Empty;
        if (email.Length == 0)
        {
            EmailError = Text.EmailRequired;
            valid = false;
        }
        else if (Mode == AuthFormMode.CreateAccount
                 && (email.Length > 320 || !email.Contains('@', StringComparison.Ordinal)))
        {
            EmailError = Text.InvalidEmail;
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            PasswordError = Text.PasswordRequired;
            valid = false;
        }
        else if (Mode == AuthFormMode.CreateAccount && !MeetsPasswordPolicy(Password))
        {
            PasswordError = Text.PasswordPolicyViolation;
            valid = false;
        }

        return valid;
    }

    private static bool MeetsPasswordPolicy(string password) =>
        password.Length >= 12
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit)
        && password.Any(character => !char.IsLetterOrDigit(character));

    private void ApplyProblem(MobileApiException exception)
    {
        switch (exception.ErrorCode)
        {
            case BusinessErrorCode.EmailAlreadyExists:
                EmailError = Text.EmailAlreadyExists;
                break;
            case BusinessErrorCode.PasswordPolicyViolation:
                PasswordError = Text.PasswordPolicyViolation;
                break;
            case BusinessErrorCode.InvalidRegistrationInput when Mode == AuthFormMode.CreateAccount:
                EmailError = Text.InvalidEmail;
                break;
            case BusinessErrorCode.InvalidCredentials:
                FormError = Text.InvalidCredentials;
                break;
            default:
                FormError = exception.ErrorCode == BusinessErrorCode.InternalServerError
                    ? Text.InvalidResponse
                    : Text.RetryableConnectionFailure;
                break;
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
