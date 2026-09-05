using System.Globalization;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Coach;

public static class CoachCopy
{
    public static bool Thai => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th";
    public static string T(string thai, string english) => Thai ? thai : english;
    public static string Body(BodyPart body) => body switch
    {
        BodyPart.Chest => T("อก", "Chest"), BodyPart.Back => T("หลัง", "Back"),
        BodyPart.Shoulders => T("ไหล่", "Shoulders"), BodyPart.Arms => T("แขน", "Arms"),
        BodyPart.Legs => T("ขา", "Legs"), _ => T("แกนกลางลำตัว", "Core")
    };
    public static string LocalNotice => T("คำตอบและประเภทเซ็ตเก็บเฉพาะอุปกรณ์นี้", "Check-ins and set types are stored on this device only");
    public static string Title(CoachRecommendation recommendation) => recommendation.Action switch
    {
        CoachAction.AddRep => T("น้ำหนักเดิม เพิ่มอีก 1 ครั้ง", "Same weight, one more rep"),
        CoachAction.AddWeight => T("ลองเพิ่มน้ำหนักทีละนิด", "Try a small weight increase"),
        CoachAction.ChooseIncrement => T("เพิ่มน้ำหนักได้น้อยสุดเท่าไร?", "What is your smallest weight step?"),
        CoachAction.CheckConsistency => T("ช่วงนี้ทำได้เท่าเดิม", "Your recent sessions look similar"),
        CoachAction.CheckRecovery => T("ยังล้าอยู่ไหม?", "Still feeling tired?"),
        CoachAction.Pain => T("หยุดท่านี้ก่อน", "Stop this exercise for now"),
        _ => T("ฝึกต่อในระดับที่คุมท่าได้", "Keep a level you can control")
    };
    public static string Reason(CoachRecommendation recommendation) => recommendation.Action switch
    {
        CoachAction.AddRep or CoachAction.AddWeight or CoachAction.ChooseIncrement => T(
            "สองครั้งฝึกล่าสุด คุณรายงานว่ายังทำต่อได้และคุมท่าได้ โดยใช้จำนวนเซ็ตและน้ำหนักเท่ากัน",
            "In two comparable sessions, you reported reps left and good control at the same load and set count."),
        CoachAction.CheckRecovery => T("ช่วงนี้ฝึกมากกว่าที่เคย หรือคุณรายงานว่ายังล้า เช็กความรู้สึกก่อนเพิ่มการฝึก",
            "Your volume is above your usual level, or you reported fatigue. Check how you feel before adding more."),
        CoachAction.Pain => T("อย่าฝืนทำท่าที่เจ็บ หากอาการไม่ดีขึ้นหรือรุนแรง ควรปรึกษาผู้เชี่ยวชาญด้านสุขภาพ",
            "Do not push through pain. Seek qualified health advice if pain persists or is severe."),
        CoachAction.CheckConsistency => T("น้ำหนักและจำนวนครั้งใกล้เคียงเดิมหลายครั้ง ลองเช็กความหนักและการพัก ยังไม่จำเป็นต้องเพิ่มน้ำหนัก",
            "Several sessions look similar. Check effort and rest; unchanged numbers alone do not mean you need more weight."),
        _ => T("ยังไม่มีข้อมูลที่พอให้แนะนำเพิ่ม หรือเซ็ตท้ายเริ่มหนักแล้ว คงเดิมและบันทึกต่อได้",
            "There is not enough evidence to increase, or the last set was already hard. Keep logging at a controlled level.")
    };
    public static string Target(CoachExercise exercise, WeightDisplayUnit unit)
    {
        var latest = exercise.Sessions[0];
        var recommendation = exercise.Recommendation;
        var suffix = unit == WeightDisplayUnit.Kilograms ? T("กก.", "kg") : T("ปอนด์", "lb");
        string Weight(decimal kg) => $"{WeightUnitConversion.FromKilograms(kg, unit):0.##} {suffix}";
        if (!recommendation.IsIncrease) return string.Empty;
        return recommendation.Action == CoachAction.AddWeight && recommendation.WeightKg is { } target
            ? $"{Weight(latest.WeightKg!.Value)} → {Weight(target)} × {recommendation.Reps}"
            : $"{(latest.WeightKg is { } kg ? Weight(kg) + " × " : "")}{latest.Reps} → {recommendation.Reps} {T("ครั้ง", "reps")}";
    }
}
