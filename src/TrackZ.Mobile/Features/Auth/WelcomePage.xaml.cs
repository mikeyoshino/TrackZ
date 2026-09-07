using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Auth;

public partial class WelcomePage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly bool _thai;
    private bool _navigating;

    public WelcomePage(IServiceProvider services, AuthTextSet text)
    {
        _services = services;
        _thai = text.EmailLabel == "อีเมล";
        InitializeComponent();
        BindingContext = this;
    }

    public string Headline => _thai ? "ฝึกอย่างมีทิศทาง" : "Train with direction";
    public string Promise => _thai ? "ไม่ต้องเดาว่าครั้งหน้าควรยกเท่าไหร่" : "Know where to start next time";
    public string RememberBenefit => _thai ? "จำให้ว่าครั้งก่อนยกเท่าไหร่\nครั้งต่อไปเริ่มได้เลย" : "Remember your last weights.\nPick up where you left off.";
    public string ProgressBenefit => _thai ? "ช่วยดูว่าเมื่อไหร่ควรเพิ่มน้ำหนัก\nหรือฝึกเท่าเดิมก่อน" : "Know when to add weight\nor keep it steady.";
    public string GuideBenefit => _thai ? "ดูภาพและวิธีฝึกสั้น ๆ\nให้ทำตามได้ง่าย" : "Follow exercise images\nand simple instructions.";
    public string TrialAction => _thai ? "ทดลองฟรี 1 เดือน" : "Try free for 1 month";
    public string ExistingAccount => _thai ? "มีบัญชีแล้ว?" : "Already have an account?";
    public string SignInAction => _thai ? "เข้าสู่ระบบ" : "Sign in";

    private async void OnTrialClicked(object? sender, EventArgs e) => await OpenAsync<CreateAccountPage>();
    private async void OnSignInClicked(object? sender, EventArgs e) => await OpenAsync<SignInPage>();

    private async Task OpenAsync<T>() where T : Page
    {
        if (_navigating) return;
        _navigating = true;
        try { await Navigation.PushAsync(_services.GetRequiredService<T>()); }
        finally { _navigating = false; }
    }
}
