using System.Globalization;
using TrackZ.Domain.Exercises;

namespace TrackZ.Mobile.Features.History;

public static class HistoryPresentation
{
    public static bool Thai => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th";
    public static string T(string thai, string english) => Thai ? thai : english;
    public static string Body(BodyPart body) => body switch
    {
        BodyPart.Chest => T("หน้าอก", "Chest"), BodyPart.Back => T("หลัง", "Back"),
        BodyPart.Shoulders => T("ไหล่", "Shoulders"), BodyPart.Arms => T("แขน", "Arms"),
        BodyPart.Legs => T("ขา", "Legs"), _ => T("แกนกลาง", "Core")
    };
    public static string SupportingText => Thai
        ? "ย้อนดูการฝึกของคุณ"
        : "Look back on your training";

    public static string MonthSummary(IEnumerable<HistoryWorkoutItem> workouts)
    {
        var completed = workouts.Where(workout => !workout.IsDeleted).ToArray();
        var sets = completed.Sum(workout => workout.SetCount);
        return Thai
            ? $"ฝึก {completed.Length} ครั้ง · บันทึก {sets} เซ็ต"
            : $"{completed.Length} workouts · {sets} sets logged";
    }
}
