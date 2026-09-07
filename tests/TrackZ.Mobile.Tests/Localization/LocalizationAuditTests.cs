using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Auth;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Features.Profile;
using TrackZ.Mobile.Features.Progress;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Localization;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Localization;

public sealed class LocalizationAuditTests
{
    private static readonly string[] ShippedPages =
    [
        "Features/Auth/AuthGatePage.xaml",
        "Features/Auth/SignInPage.xaml",
        "Features/Auth/CreateAccountPage.xaml",
        "Features/Train/TrainPage.xaml",
        "Features/Train/BodyAreaSheetPage.xaml",
        "Features/Exercises/ExercisePickerPage.xaml",
        "Features/Exercises/CustomExercisePage.xaml",
        "Features/Workout/WorkoutPage.xaml",
        "Features/Workout/SetLoggerPage.xaml",
        "Features/Workout/SetEffortSheetPage.xaml",
        "Features/Workout/SetEntrySheetPage.xaml",
        "Features/History/WorkoutHistoryPage.xaml",
        "Features/History/WorkoutHistoryDetailPage.xaml",
        "Features/History/HistorySetEditorSheetPage.xaml",
        "Features/History/HistoryConflictSheetPage.xaml",
        "Features/Summary/WorkoutSummaryPage.xaml",
        "Features/Progress/ExerciseProgressPage.xaml",
        "Features/Profile/ProfilePage.xaml"
    ];

    private static readonly HashSet<string> AllowedTechnicalLiterals =
        new(StringComparer.Ordinal)
        {
            "›", "—", "+", "−", "·", "↗", "×", "i", "kg", "lb", "XP", "#0",
            "JPEG, PNG, WebP", "TrackZ", "Z"
        };

    private static readonly IReadOnlyDictionary<string, (string English, string Thai)> ExpectedInsightFirstHomeCopy =
        new Dictionary<string, (string English, string Thai)>(StringComparer.Ordinal)
        {
            [nameof(WorkoutTextSet.HomeWeeklyGoalFormat)] =
                ("This week you completed {0} of {1} workouts", "สัปดาห์นี้ฝึกแล้ว {0} จากเป้าหมาย {1} ครั้ง"),
            [nameof(WorkoutTextSet.HomeWeeklyStreakFormat)] =
                ("Goal met {0} weeks in a row", "ทำถึงเป้า {0} สัปดาห์ติด"),
            [nameof(WorkoutTextSet.HomeLatestPerformance)] =
                ("Latest workout", "ฝึกล่าสุด"),
            [nameof(WorkoutTextSet.HomeLatestLabel)] = ("Latest", "ครั้งล่าสุด"),
            [nameof(WorkoutTextSet.HomeBestLabel)] = ("Best", "สถิติสูงสุด"),
            [nameof(WorkoutTextSet.HomeViewAllData)] = ("View all data", "ดูข้อมูลทั้งหมด"),
            [nameof(WorkoutTextSet.Open)] = ("Open", "เปิด")
        };

    private static readonly Rune[] ForbiddenInsightFirstHomeStatusGlyphs =
        [new(0x1F4C8), new(0x1F4AA), new(0x1F525), new(0x26A0), new(0x1F3C6), new(0x2191), new(0x2193), new(0x2192), new(0x2197)];

    [Fact]
    public void Insight_first_home_copy_audit_rejects_changed_translations_or_status_glyphs()
    {
        var en = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("en-US"));
        var th = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

        foreach (var (propertyName, expected) in ExpectedInsightFirstHomeCopy)
        {
            var property = typeof(WorkoutTextSet).GetProperty(propertyName)!;
            var english = Assert.IsType<string>(property.GetValue(en));
            var thai = Assert.IsType<string>(property.GetValue(th));

            Assert.Equal(expected.English, english);
            Assert.Equal(expected.Thai, thai);
            Assert.DoesNotContain(english.EnumerateRunes(), ForbiddenInsightFirstHomeStatusGlyphs.Contains);
            Assert.DoesNotContain(thai.EnumerateRunes(), ForbiddenInsightFirstHomeStatusGlyphs.Contains);
        }
    }

    [Fact]
    public void Previous_workout_reference_copy_is_localized_in_English_and_Thai()
    {
        var english = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("en-US"));
        var thai = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

        Assert.Equal("Previous workout reference", english.PreviousWorkoutReference);
        Assert.Equal("Heaviest set in the 8–12 rep range", english.HeaviestSetInRepRange);
        Assert.Equal("นน. ครั้งก่อน", thai.PreviousWorkoutReference);
        Assert.Equal("เซ็ตที่หนักที่สุดในช่วง 8–12 ครั้ง", thai.HeaviestSetInRepRange);
    }

    [Fact]
    public void Empty_exercise_selection_warning_copy_is_exact_in_English_and_Thai()
    {
        var english = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("en-US"));
        var thai = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

        Assert.Equal("No exercises selected", english.ExerciseSelectionRequiredTitle);
        Assert.Equal("Choose at least 1 exercise before creating your workout.", english.ExerciseSelectionRequiredMessage);
        Assert.Equal("OK", english.Okay);
        Assert.Equal("ยังไม่ได้เลือกท่า", thai.ExerciseSelectionRequiredTitle);
        Assert.Equal("เลือกอย่างน้อย 1 ท่าก่อนสร้างการฝึก", thai.ExerciseSelectionRequiredMessage);
        Assert.Equal("ตกลง", thai.Okay);
    }

    [Fact]
    public void Effort_and_guidance_copy_preserves_English_and_Thai_meaning()
    {
        var english = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("en-US"));
        var thai = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

        Assert.Equal("Too easy — many reps left", english.EffortEasyOption);
        Assert.Equal("About right — the final reps were hard, with good form", english.EffortProductiveOption);
        Assert.Equal("Too heavy — missed the range or form began to break", english.EffortTooHeavyOption);
        Assert.Equal("If you feel pain or cannot keep good form, stop this exercise.", english.EffortPainSafety);
        Assert.Equal("This set can no longer be rated. Your original set is saved.", english.EffortUnavailable);
        Assert.Equal("Less assistance makes the exercise harder.", english.GuidanceLessAssistanceReason);
        Assert.Equal("More assistance makes it easier to keep good form.", english.GuidanceMoreAssistanceReason);
        Assert.Equal("Keep this set as recorded", english.GuidanceNoSuggestion);
        Assert.Equal("A higher supported load could not be suggested.", english.GuidanceHigherLoadUnavailable);
        Assert.Equal("Less supported assistance could not be suggested.", english.GuidanceLowerAssistanceUnavailable);

        Assert.Equal("เบาไป — ยังไหวอีกหลายครั้ง", thai.EffortEasyOption);
        Assert.Equal("กำลังดี — ช่วงท้ายเริ่มหนัก แต่ฟอร์มยังดี", thai.EffortProductiveOption);
        Assert.Equal("หนักเกินไป — ทำไม่ถึงเป้าหรือฟอร์มเริ่มเสีย", thai.EffortTooHeavyOption);
        Assert.Equal("ถ้ารู้สึกเจ็บหรือรักษาฟอร์มไม่ได้ ให้หยุดท่านี้", thai.EffortPainSafety);
        Assert.Equal("ไม่สามารถบันทึกความรู้สึกของเซ็ตนี้ได้แล้ว แต่เซ็ตเดิมของคุณบันทึกไว้แล้ว", thai.EffortUnavailable);
        Assert.Equal("แรงช่วยน้อยลงทำให้ท่านี้ยากขึ้น", thai.GuidanceLessAssistanceReason);
        Assert.Equal("แรงช่วยมากขึ้นช่วยให้รักษาฟอร์มได้ง่ายขึ้น", thai.GuidanceMoreAssistanceReason);
        Assert.Equal("คงเซ็ตนี้ตามที่บันทึกไว้", thai.GuidanceNoSuggestion);
        Assert.Equal("ยังไม่สามารถแนะนำน้ำหนักที่สูงขึ้นและรองรับได้", thai.GuidanceHigherLoadUnavailable);
        Assert.Equal("ยังไม่สามารถแนะนำแรงช่วยที่น้อยลงและรองรับได้", thai.GuidanceLowerAssistanceUnavailable);
    }

    [Fact]
    public void Every_shipped_page_and_user_facing_component_uses_bound_or_resource_copy()
    {
        var mobile = MobileDirectory();
        var files = ShippedPages
            .Select(path => Path.Combine(mobile, path))
            .Concat(Directory.EnumerateFiles(Path.Combine(mobile, "Components"), "*.xaml"));
        var violations = files.SelectMany(AuditLiteralCopy).ToArray();

        Assert.True(
            violations.Length == 0,
            "Unlocalized XAML copy:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("WorkoutStrings.resx", "WorkoutStrings.th.resx")]
    [InlineData("MobileStrings.resx", "MobileStrings.th.resx")]
    public void English_and_Thai_resource_files_have_exact_non_empty_key_parity(
        string englishFile,
        string thaiFile)
    {
        var resources = Path.Combine(SolutionDirectory(), "src", "TrackZ.Mobile.Core", "Resources");
        var english = ReadResources(Path.Combine(resources, englishFile));
        var thai = ReadResources(Path.Combine(resources, thaiFile));

        Assert.Equal(english.Keys.Order(), thai.Keys.Order());
        Assert.All(english, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{englishFile}:{pair.Key}"));
        Assert.All(thai, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{thaiFile}:{pair.Key}"));
    }

    [Fact]
    public void Typed_mobile_and_workout_text_sets_are_complete_in_both_languages()
    {
        AssertTextSet(
            MobileResources.ForCulture(CultureInfo.GetCultureInfo("en-US")),
            MobileResources.ForCulture(CultureInfo.GetCultureInfo("th-TH")));
        AssertTextSet(
            WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("en-US")),
            WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH")));
    }

    [Fact]
    public void User_facing_pickers_never_bind_to_raw_enum_collections()
    {
        var violations = ShippedPages
            .Select(path => (Path: path, Document: XDocument.Load(Path.Combine(MobileDirectory(), path))))
            .SelectMany(file => AuditRawEnumPickers(file.Path, file.Document))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Localization_audits_reject_literal_and_raw_enum_mutations()
    {
        const string path = "Features/Exercises/CustomExercisePage.xaml";
        var literal = XDocument.Load(Path.Combine(MobileDirectory(), path));
        literal.Root!.SetAttributeValue("Title", "Custom exercise");
        Assert.Contains(AuditLiteralCopy(path, literal), error => error.Contains("Custom exercise", StringComparison.Ordinal));

        const string pickerPath = "Features/Exercises/ExercisePickerPage.xaml";
        var rawEnum = XDocument.Parse("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
              <Picker ItemsSource="{Binding BodyParts}" />
            </ContentPage>
            """);
        Assert.Contains(AuditRawEnumPickers(pickerPath, rawEnum), error => error.Contains("BodyParts", StringComparison.Ordinal));
    }

    [Fact]
    public void Conflict_presentations_do_not_embed_English_UI_copy()
    {
        var core = Path.Combine(SolutionDirectory(), "src", "TrackZ.Mobile.Core", "Features");
        var sources = new[]
        {
            Path.Combine(core, "Workout", "SetLoggerViewModel.cs"),
            Path.Combine(core, "History", "WorkoutHistoryViewModel.cs")
        };
        var forbidden = new[] { "\"Local ", "\"Server version", "\"exercise\"", "\"set\"" };

        Assert.All(sources, source => Assert.All(
            forbidden,
            token => Assert.DoesNotContain(token, File.ReadAllText(source), StringComparison.Ordinal)));
    }

    [Fact]
    public void Localized_UI_is_scoped_while_account_data_and_sync_remain_singleton()
    {
        ServiceDescriptor[] descriptors = [];
        using var app = MauiProgram.CreateMauiApp(services => descriptors = [.. services]);

        AssertLifetime(descriptors, typeof(AuthTextSet), ServiceLifetime.Scoped);
        AssertLifetime(descriptors, typeof(WorkoutTextSet), ServiceLifetime.Scoped);
        AssertLifetime(descriptors, typeof(GamificationTextSet), ServiceLifetime.Scoped);
        AssertLifetime(descriptors, typeof(MobileTextSet), ServiceLifetime.Scoped);
        AssertLifetime(descriptors, typeof(AppShell), ServiceLifetime.Scoped);
        AssertLifetime(descriptors, typeof(TrainPage), ServiceLifetime.Scoped);
        AssertLifetime(descriptors, typeof(ProfilePage), ServiceLifetime.Scoped);
        AssertLifetime(descriptors, typeof(TrainTodayViewModel), ServiceLifetime.Scoped);
        AssertLifetime(descriptors, typeof(ProfileViewModel), ServiceLifetime.Scoped);

        AssertLifetime(descriptors, typeof(IAccountSessionBoundary), ServiceLifetime.Singleton);
        AssertLifetime(descriptors, typeof(LocalWorkoutRepository), ServiceLifetime.Singleton);
        AssertLifetime(descriptors, typeof(SyncCoordinator), ServiceLifetime.Singleton);
        AssertLifetime(descriptors, typeof(IAppLanguageChanger), ServiceLifetime.Singleton);
    }

    private static IEnumerable<string> AuditLiteralCopy(string path)
    {
        var relative = Path.GetRelativePath(MobileDirectory(), path);
        var document = XDocument.Load(path, LoadOptions.SetLineInfo);
        return AuditLiteralCopy(relative, document);
    }

    private static IEnumerable<string> AuditLiteralCopy(string relative, XDocument document)
    {
        foreach (var attribute in document.Descendants().Attributes())
        {
            if (attribute.Name.LocalName is not (
                "Text" or "Title" or "Placeholder" or
                "SemanticProperties.Description" or "AutomationProperties.Name")) continue;
            var value = attribute.Value.Trim();
            if (value.StartsWith('{') || AllowedTechnicalLiterals.Contains(value)) continue;
            if (!value.Any(char.IsLetter)) continue;
            var line = (attribute.Parent as IXmlLineInfo)?.LineNumber ?? 0;
            yield return $"{relative}:{line} {attribute.Name.LocalName}=\"{value}\"";
        }
    }

    private static IEnumerable<string> AuditRawEnumPickers(string relative, XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "Picker")
            .Select(element => element.Attribute("ItemsSource")?.Value)
            .Where(source => source is "{Binding BodyParts}" or "{Binding TrackingModes}")
            .Select(source => $"{relative}: Picker ItemsSource={source}");

    private static Dictionary<string, string> ReadResources(string path) =>
        XDocument.Load(path).Descendants("data").ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element.Element("value")?.Value ?? string.Empty,
            StringComparer.Ordinal);

    private static void AssertTextSet<T>(T english, T thai)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Assert.NotEmpty(properties);
        var different = 0;
        foreach (var property in properties)
        {
            var englishValue = Assert.IsType<string>(property.GetValue(english));
            var thaiValue = Assert.IsType<string>(property.GetValue(thai));
            Assert.False(string.IsNullOrWhiteSpace(englishValue), $"English {typeof(T).Name}.{property.Name}");
            Assert.False(string.IsNullOrWhiteSpace(thaiValue), $"Thai {typeof(T).Name}.{property.Name}");
            if (!string.Equals(englishValue, thaiValue, StringComparison.Ordinal)) different++;
        }
        Assert.True(different >= properties.Length * 0.8, $"{typeof(T).Name} does not meaningfully differ by language.");
    }

    private static void AssertLifetime(
        IEnumerable<ServiceDescriptor> descriptors,
        Type serviceType,
        ServiceLifetime expected)
    {
        var descriptor = Assert.Single(descriptors, item => item.ServiceType == serviceType);
        Assert.Equal(expected, descriptor.Lifetime);
    }

    private static string MobileDirectory() =>
        Path.Combine(SolutionDirectory(), "src", "TrackZ.Mobile");

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("TrackZ.slnx was not found.");
    }
}
