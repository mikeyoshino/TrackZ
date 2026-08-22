using System.Globalization;
using System.Resources;

namespace TrackZ.Mobile.Features.Localization;

public sealed record MobileTextSet(
    string Language,
    string ThaiLanguage,
    string EnglishLanguage,
    string LanguageSwitchFailed);

public static class MobileResources
{
    private static readonly ResourceManager Manager = new(
        $"{typeof(MobileResources).Assembly.GetName().Name}.Resources.MobileStrings",
        typeof(MobileResources).Assembly);

    public static MobileTextSet ForCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return new MobileTextSet(
            Value("Language", culture),
            Value("ThaiLanguage", culture),
            Value("EnglishLanguage", culture),
            Value("LanguageSwitchFailed", culture));
    }

    private static string Value(string key, CultureInfo culture) =>
        Manager.GetString(key, culture)
        ?? throw new MissingManifestResourceException($"Mobile resource '{key}' is missing.");
}
