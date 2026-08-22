using Microsoft.Maui.Storage;
using TrackZ.Mobile.Features.Localization;

namespace TrackZ.Mobile.Localization;

public sealed class MauiAppLanguageStore(IPreferences preferences) : IAppLanguageStore
{
    internal const string PreferenceKey = "trackz_app_language_v1";

    public AppLanguage Read() => preferences.Get<string?>(PreferenceKey, null) switch
    {
        "en-US" => AppLanguage.English,
        "th-TH" => AppLanguage.Thai,
        _ => AppLanguage.Thai
    };

    public void Write(AppLanguage language) => preferences.Set(
        PreferenceKey,
        AppLanguageCulture.For(language).Name);
}
