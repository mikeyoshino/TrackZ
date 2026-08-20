using System.Globalization;
using TrackZ.Contracts.Errors;

namespace TrackZ.Mobile.Features.Shared;

public enum BusinessErrorAction
{
    Dismiss = 1,
    Retry = 2,
    SignIn = 3,
    ResolveConflict = 4
}

public sealed record BusinessErrorPresentation(string ResourceKey, BusinessErrorAction Action);

public static class BusinessErrorPresenter
{
    public static BusinessErrorPresentation Map(BusinessErrorCode code) => code switch
    {
        BusinessErrorCode.WorkoutNotFound => new("WorkoutNotFound", BusinessErrorAction.Dismiss),
        BusinessErrorCode.InvalidSetValue => new("InvalidSetValue", BusinessErrorAction.Dismiss),
        BusinessErrorCode.VersionConflict => new("SyncConflict", BusinessErrorAction.ResolveConflict),
        BusinessErrorCode.InvalidCredentials => new("InvalidCredentials", BusinessErrorAction.SignIn),
        BusinessErrorCode.RateLimitExceeded => new("RateLimited", BusinessErrorAction.Retry),
        BusinessErrorCode.InternalServerError => new("TryAgain", BusinessErrorAction.Retry),
        _ => new("RequestFailed", BusinessErrorAction.Dismiss)
    };
}

public static class BusinessErrorText
{
    private static readonly IReadOnlyDictionary<string, (string English, string Thai)> Text =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["WorkoutNotFound"] = ("This workout is no longer available.", "ไม่พบการออกกำลังกายนี้แล้ว"),
            ["InvalidSetValue"] = ("Check the set values and try again.", "ตรวจสอบค่าน้ำหนักและจำนวนครั้งแล้วลองอีกครั้ง"),
            ["SyncConflict"] = ("Review the local and server versions.", "ตรวจสอบข้อมูลในเครื่องและบนเซิร์ฟเวอร์"),
            ["InvalidCredentials"] = ("Sign in details are incorrect.", "อีเมลหรือรหัสผ่านไม่ถูกต้อง"),
            ["RateLimited"] = ("Please wait a moment and try again.", "กรุณารอสักครู่แล้วลองอีกครั้ง"),
            ["TryAgain"] = ("Something went wrong. Try again.", "เกิดข้อผิดพลาด กรุณาลองอีกครั้ง"),
            ["RequestFailed"] = ("The request could not be completed.", "ไม่สามารถดำเนินการได้")
        };

    public static string Resolve(string resourceKey, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        ArgumentNullException.ThrowIfNull(culture);
        var copy = Text.GetValueOrDefault(resourceKey, Text["RequestFailed"]);
        return culture.TwoLetterISOLanguageName == "th" ? copy.Thai : copy.English;
    }
}

public static class NativeAccessibility
{
    public const double MinimumActionTarget = 44;
}
