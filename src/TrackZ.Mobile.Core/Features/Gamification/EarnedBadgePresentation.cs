using System.Globalization;
using TrackZ.Contracts.Gamification;

namespace TrackZ.Mobile.Features.Gamification;

public sealed record EarnedBadgePresentation(
    EarnedBadgeDto Source,
    string Name,
    string Description,
    string IconGlyph,
    string EarnedText)
{
    private static readonly IReadOnlyDictionary<string, (string Name, string Description)> English =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["Badge_FirstWorkout_Name"] = ("First workout", "Complete your first workout."),
            ["Badge_Workouts10_Name"] = ("10 workouts", "Complete 10 workouts."),
            ["Badge_Workouts25_Name"] = ("25 workouts", "Complete 25 workouts."),
            ["Badge_Workouts50_Name"] = ("50 workouts", "Complete 50 workouts."),
            ["Badge_Streak4_Name"] = ("4-week streak", "Train in four consecutive weeks."),
            ["Badge_Streak8_Name"] = ("8-week streak", "Train in eight consecutive weeks."),
            ["Badge_Streak12_Name"] = ("12-week streak", "Train in twelve consecutive weeks."),
            ["Badge_Exercises10_Name"] = ("10 exercises", "Log 10 different exercises."),
            ["Badge_Exercises25_Name"] = ("25 exercises", "Log 25 different exercises."),
            ["Badge_FirstPr_Name"] = ("First personal record", "Set your first personal record."),
            ["Badge_Prs10_Name"] = ("10 personal records", "Set 10 personal records.")
        };

    private static readonly IReadOnlyDictionary<string, (string Name, string Description)> Thai =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["Badge_FirstWorkout_Name"] = ("ออกกำลังกายครั้งแรก", "ออกกำลังกายให้เสร็จครั้งแรก"),
            ["Badge_Workouts10_Name"] = ("ออกกำลังกาย 10 ครั้ง", "ออกกำลังกายให้เสร็จ 10 ครั้ง"),
            ["Badge_Workouts25_Name"] = ("ออกกำลังกาย 25 ครั้ง", "ออกกำลังกายให้เสร็จ 25 ครั้ง"),
            ["Badge_Workouts50_Name"] = ("ออกกำลังกาย 50 ครั้ง", "ออกกำลังกายให้เสร็จ 50 ครั้ง"),
            ["Badge_Streak4_Name"] = ("ต่อเนื่อง 4 สัปดาห์", "ฝึกติดต่อกันสี่สัปดาห์"),
            ["Badge_Streak8_Name"] = ("ต่อเนื่อง 8 สัปดาห์", "ฝึกติดต่อกันแปดสัปดาห์"),
            ["Badge_Streak12_Name"] = ("ต่อเนื่อง 12 สัปดาห์", "ฝึกติดต่อกันสิบสองสัปดาห์"),
            ["Badge_Exercises10_Name"] = ("ท่าออกกำลังกาย 10 ท่า", "บันทึกท่าออกกำลังกายที่ต่างกัน 10 ท่า"),
            ["Badge_Exercises25_Name"] = ("ท่าออกกำลังกาย 25 ท่า", "บันทึกท่าออกกำลังกายที่ต่างกัน 25 ท่า"),
            ["Badge_FirstPr_Name"] = ("สถิติส่วนตัวครั้งแรก", "สร้างสถิติส่วนตัวครั้งแรก"),
            ["Badge_Prs10_Name"] = ("สถิติส่วนตัว 10 ครั้ง", "สร้างสถิติส่วนตัว 10 ครั้ง")
        };

    private static readonly IReadOnlyDictionary<string, string> Icons =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["badge-first-workout"] = "✓",
            ["badge-workouts-10"] = "10",
            ["badge-workouts-25"] = "25",
            ["badge-workouts-50"] = "50",
            ["badge-streak-4"] = "4",
            ["badge-streak-8"] = "8",
            ["badge-streak-12"] = "12",
            ["badge-exercises-10"] = "+10",
            ["badge-exercises-25"] = "+25",
            ["badge-first-pr"] = "↑",
            ["badge-prs-10"] = "↑10"
        };

    private static readonly IReadOnlyDictionary<string, string> DescriptionToNameKey =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Badge_FirstWorkout_Description"] = "Badge_FirstWorkout_Name",
            ["Badge_Workouts10_Description"] = "Badge_Workouts10_Name",
            ["Badge_Workouts25_Description"] = "Badge_Workouts25_Name",
            ["Badge_Workouts50_Description"] = "Badge_Workouts50_Name",
            ["Badge_Streak4_Description"] = "Badge_Streak4_Name",
            ["Badge_Streak8_Description"] = "Badge_Streak8_Name",
            ["Badge_Streak12_Description"] = "Badge_Streak12_Name",
            ["Badge_Exercises10_Description"] = "Badge_Exercises10_Name",
            ["Badge_Exercises25_Description"] = "Badge_Exercises25_Name",
            ["Badge_FirstPr_Description"] = "Badge_FirstPr_Name",
            ["Badge_Prs10_Description"] = "Badge_Prs10_Name"
        };

    public static EarnedBadgePresentation From(EarnedBadgeDto source, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(culture);
        var resources = culture.TwoLetterISOLanguageName == "th" ? Thai : English;
        var name = resources.TryGetValue(source.NameResourceKey, out var localized)
            ? localized.Item1
            : Humanize(source.Key);
        var description = DescriptionToNameKey.TryGetValue(source.DescriptionResourceKey, out var descriptionNameKey)
            && resources.TryGetValue(descriptionNameKey, out var described)
            ? described.Item2
            : string.Empty;
        var icon = Icons.GetValueOrDefault(source.IconKey, string.Empty);
        var earned = culture.TwoLetterISOLanguageName == "th"
            ? $"ได้รับเมื่อ {source.EarnedAt.ToString("d", culture)}"
            : $"Earned {source.EarnedAt.ToString("d", culture)}";
        return new EarnedBadgePresentation(source, name, description, icon, earned);
    }

    private static string Humanize(string key) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace('-', ' '));
}
