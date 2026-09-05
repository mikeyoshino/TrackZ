using System.Globalization;
using TrackZ.Domain.Exercises;

namespace TrackZ.Mobile.Features.Train;

public sealed record HomeCopy(
    string Today,
    string ReadyHeadline,
    string TodaysWorkout,
    string ChooseWorkoutSupporting,
    string ViewSummary,
    string ViewAll,
    string LatestWorkoutHeading,
    string Yesterday,
    string ExerciseCountFormat,
    string SetCountFormat,
    string Loading,
    CultureInfo Culture)
{
    public static HomeCopy Current => ForCulture(CultureInfo.CurrentUICulture);

    internal static HomeCopy ForCulture(CultureInfo culture) =>
        string.Equals(culture.TwoLetterISOLanguageName, "th", StringComparison.OrdinalIgnoreCase)
            ? new HomeCopy(
                "วันนี้",
                "พร้อมฝึกกันไหม?",
                "การฝึกวันนี้",
                "เลือกกล้ามเนื้อและท่าที่จะฝึก",
                "ดูสรุป ›",
                "ดูทั้งหมด ›",
                "ฝึกครั้งล่าสุด",
                "เมื่อวาน",
                "{0} ท่า",
                "{0} เซ็ต",
                "กำลังโหลดการฝึกวันนี้",
                culture)
            : new HomeCopy(
                "Today",
                "Ready to train?",
                "Today's workout",
                "Choose muscles and exercises to train",
                "View summary ›",
                "View all ›",
                "Latest workout",
                "Yesterday",
                "{0} exercises",
                "{0} sets",
                "Loading today's training",
                culture);

    public string FormatLatestWorkoutTitle(RepeatWorkoutShortcut? workout) =>
        workout is null
            ? string.Empty
            : string.Join(" · ", workout.BodyParts.Select(BodyPartName));

    public string FormatLatestWorkoutMeta(RepeatWorkoutShortcut? workout, DateTimeOffset now)
    {
        if (workout is null)
            return string.Empty;

        var localNow = now.ToLocalTime();
        var completed = workout.CompletedAt.ToLocalTime();
        var day = completed.Date == localNow.Date
            ? Today
            : completed.Date == localNow.Date.AddDays(-1)
                ? Yesterday
                : completed.ToString("d MMM", Culture);

        return string.Join(
            " · ",
            day,
            string.Format(Culture, ExerciseCountFormat, workout.ExerciseCount),
            string.Format(Culture, SetCountFormat, workout.LoggedSetCount));
    }

    private string BodyPartName(BodyPart bodyPart) =>
        (Culture.TwoLetterISOLanguageName, bodyPart) switch
        {
            ("th", BodyPart.Chest) => "อก",
            ("th", BodyPart.Back) => "หลัง",
            ("th", BodyPart.Shoulders) => "ไหล่",
            ("th", BodyPart.Arms) => "แขน",
            ("th", BodyPart.Legs) => "ขา",
            ("th", BodyPart.Core) => "แกนกลาง",
            (_, BodyPart.Chest) => "Chest",
            (_, BodyPart.Back) => "Back",
            (_, BodyPart.Shoulders) => "Shoulders",
            (_, BodyPart.Arms) => "Arms",
            (_, BodyPart.Legs) => "Legs",
            (_, BodyPart.Core) => "Core",
            _ => throw new ArgumentOutOfRangeException(nameof(bodyPart))
        };
}
