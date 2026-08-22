using System.Globalization;

namespace TrackZ.Mobile.Features.Localization;

public enum AppLanguage
{
    Thai = 1,
    English = 2
}

public static class AppLanguageCulture
{
    public static CultureInfo For(AppLanguage language) => language switch
    {
        AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
        _ => CultureInfo.GetCultureInfo("th-TH")
    };

    public static void Apply(AppLanguage language)
    {
        var culture = For(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

public interface IAppLanguageStore
{
    AppLanguage Read();
    void Write(AppLanguage language);
}

public sealed record LanguageOption(
    AppLanguage Value,
    string Label,
    string AccessibilityLabel,
    bool IsSelected);

public interface IAppLanguageChanger
{
    AppLanguage Current { get; }
    bool IsChanging { get; }
    Task ChangeAsync(AppLanguage language, CancellationToken cancellationToken = default);
}

public sealed class AppLanguageChangeException : Exception
{
    public AppLanguageChangeException() : base("The localized UI replacement failed.") { }
}
