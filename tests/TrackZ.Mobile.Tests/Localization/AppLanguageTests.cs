using System.Globalization;
using Microsoft.Maui.Storage;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Localization;

namespace TrackZ.Mobile.Tests.Localization;

public sealed class AppLanguageTests
{
    public static TheoryData<string?, AppLanguage> StoredLanguageCases => new()
    {
        { null, AppLanguage.Thai },
        { string.Empty, AppLanguage.Thai },
        { "ja-JP", AppLanguage.Thai },
        { "TH-th", AppLanguage.Thai },
        { "th-TH", AppLanguage.Thai },
        { "en-US", AppLanguage.English }
    };

    [Theory]
    [MemberData(nameof(StoredLanguageCases))]
    public void Store_reads_only_the_two_stable_language_tags(string? stored, AppLanguage expected)
    {
        var preferences = new MemoryPreferences();
        if (stored is not null) preferences.Set(MauiAppLanguageStore.PreferenceKey, stored);

        Assert.Equal(expected, new MauiAppLanguageStore(preferences).Read());
    }

    [Theory]
    [InlineData(AppLanguage.Thai, "th-TH")]
    [InlineData(AppLanguage.English, "en-US")]
    public void Store_writes_the_stable_culture_tag(AppLanguage language, string expectedTag)
    {
        var preferences = new MemoryPreferences();

        new MauiAppLanguageStore(preferences).Write(language);

        Assert.Equal(expectedTag, preferences.Get<string?>(MauiAppLanguageStore.PreferenceKey, null));
    }

    [Fact]
    public void Applying_Thai_updates_current_and_default_thread_cultures()
    {
        var snapshot = CultureSnapshot.Capture();
        try
        {
            AppLanguageCulture.Apply(AppLanguage.Thai);

            Assert.Equal("th-TH", CultureInfo.CurrentCulture.Name);
            Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("th-TH", CultureInfo.DefaultThreadCurrentCulture!.Name);
            Assert.Equal("th-TH", CultureInfo.DefaultThreadCurrentUICulture!.Name);
        }
        finally
        {
            snapshot.Restore();
        }
    }

    [Theory]
    [InlineData("th-TH", "ภาษา", "ไทย", "English")]
    [InlineData("en-US", "Language", "Thai", "English")]
    public void Language_resources_use_exact_requested_culture(
        string cultureName,
        string language,
        string thai,
        string english)
    {
        var text = MobileResources.ForCulture(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(language, text.Language);
        Assert.Equal(thai, text.ThaiLanguage);
        Assert.Equal(english, text.EnglishLanguage);
        Assert.False(string.IsNullOrWhiteSpace(text.LanguageSwitchFailed));
    }

    [Fact]
    public void Maui_composition_defaults_to_Thai_before_localized_services_are_resolved()
    {
        var snapshot = CultureSnapshot.Capture();
        try
        {
            var store = new MemoryLanguageStore();
            using var app = MauiProgram.CreateMauiApp(appLanguageStore: store);

            Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
            Assert.Same(store, app.Services.GetService(typeof(IAppLanguageStore)));
        }
        finally
        {
            snapshot.Restore();
        }
    }

    [Fact]
    public void Portable_composition_defaults_to_Thai_without_calling_a_platform_preference_backend()
    {
        var snapshot = CultureSnapshot.Capture();
        try
        {
            using var app = MauiProgram.CreateMauiApp();

            Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
            Assert.Equal(AppLanguage.Thai,
                app.Services.GetRequiredService<IAppLanguageStore>().Read());
        }
        finally
        {
            snapshot.Restore();
        }
    }

    private sealed class MemoryLanguageStore : IAppLanguageStore
    {
        public AppLanguage Read() => AppLanguage.Thai;
        public void Write(AppLanguage language) { }
    }

    private sealed class MemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object?> _values = [];

        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);
        public void Remove(string key, string? sharedName = null) => _values.Remove(key);
        public void Clear(string? sharedName = null) => _values.Clear();
        public void Set<T>(string key, T value, string? sharedName = null) => _values[key] = value;
        public T Get<T>(string key, T defaultValue, string? sharedName = null) =>
            _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;
    }

    private sealed record CultureSnapshot(
        CultureInfo Current,
        CultureInfo CurrentUi,
        CultureInfo? Default,
        CultureInfo? DefaultUi)
    {
        public static CultureSnapshot Capture() => new(
            CultureInfo.CurrentCulture,
            CultureInfo.CurrentUICulture,
            CultureInfo.DefaultThreadCurrentCulture,
            CultureInfo.DefaultThreadCurrentUICulture);

        public void Restore()
        {
            CultureInfo.CurrentCulture = Current;
            CultureInfo.CurrentUICulture = CurrentUi;
            CultureInfo.DefaultThreadCurrentCulture = Default;
            CultureInfo.DefaultThreadCurrentUICulture = DefaultUi;
        }
    }
}
