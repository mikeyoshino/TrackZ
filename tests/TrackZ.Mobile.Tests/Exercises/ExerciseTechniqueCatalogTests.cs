using System.Globalization;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class ExerciseTechniqueCatalogTests
{
    [Fact]
    public void Seated_barbell_press_has_specific_barbell_guidance()
    {
        var guidance = new ExerciseTechniqueCatalog().Get("Seated Barbell Shoulder Press", BodyPart.Shoulders,
            false, CultureInfo.GetCultureInfo("th-TH"));
        Assert.True(guidance.IsSpecific);
        Assert.Contains(guidance.Steps, step => step.Contains("บาร์"));
        Assert.DoesNotContain(guidance.Steps, step => step.Contains("ดัมเบล"));
    }
    private static readonly string[] SystemExerciseNames =
    [
        "Barbell Bench Press", "Incline Barbell Bench Press", "Dumbbell Bench Press", "Incline Dumbbell Press", "Chest Press Machine",
        "Cable Fly", "Pec Deck Fly", "Decline Push-Up", "Push-Up", "Chest Dip", "Decline Barbell Bench Press", "Smith Machine Bench Press",
        "Dumbbell Fly", "Low-to-High Cable Fly", "High-to-Low Cable Fly", "Lat Pulldown", "Pull-Up", "Assisted Pull-Up", "Seated Cable Row",
        "Chest-Supported Row", "Barbell Row", "One-Arm Dumbbell Row", "Straight-Arm Pulldown", "Conventional Deadlift", "T-Bar Row", "Inverted Row",
        "Neutral-Grip Lat Pulldown", "Wide-Grip Lat Pulldown", "Single-Arm Cable Row", "Machine High Row", "Overhead Press", "Dumbbell Shoulder Press",
        "Machine Shoulder Press", "Lateral Raise", "Cable Lateral Raise", "Rear Delt Fly", "Face Pull", "Upright Row", "Arnold Press",
        "Dumbbell Front Raise", "Cable Front Raise", "Bent-Over Reverse Fly", "Reverse Pec Deck", "Landmine Press", "Dumbbell Shrug",
        "Barbell Curl", "Dumbbell Curl", "Hammer Curl", "Preacher Curl", "Triceps Pushdown", "Overhead Triceps Extension", "Skull Crusher",
        "Close-Grip Bench Press", "EZ-Bar Curl", "Incline Dumbbell Curl", "Cable Curl", "Concentration Curl", "Bench Dip", "Triceps Dip",
        "Single-Arm Cable Pushdown", "Back Squat", "Front Squat", "Leg Press", "Romanian Deadlift", "Leg Extension", "Seated Leg Curl",
        "Bulgarian Split Squat", "Standing Calf Raise", "Goblet Squat", "Hack Squat", "Sumo Deadlift", "Walking Lunge", "Hip Thrust",
        "Lying Leg Curl", "Seated Calf Raise", "Cable Crunch", "Hanging Knee Raise", "Hanging Leg Raise", "Ab Wheel Rollout", "Weighted Sit-Up",
        "Decline Sit-Up", "Reverse Crunch", "Pallof Press", "Plank", "Side Plank", "Dead Bug", "Bird Dog", "Russian Twist",
        "Bicycle Crunch", "Mountain Climber"
    ];

    [Fact]
    public void Every_system_exercise_has_specific_three_step_guidance()
    {
        var catalog = new ExerciseTechniqueCatalog();

        foreach (var name in SystemExerciseNames)
        {
            var guidance = catalog.Get(name, BodyPart.Chest, isCustom: false, CultureInfo.GetCultureInfo("th-TH"));
            Assert.True(guidance.IsSpecific, name);
            Assert.Equal(3, guidance.Steps.Count);
            Assert.All(guidance.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step)));
            Assert.False(string.IsNullOrWhiteSpace(guidance.Tip));
            Assert.False(string.IsNullOrWhiteSpace(guidance.Caution));
        }
    }

    [Fact]
    public void Shoulder_press_uses_plain_Thai_copy_from_the_approved_mock()
    {
        var guidance = new ExerciseTechniqueCatalog().Get(
            "Dumbbell Shoulder Press", BodyPart.Shoulders, isCustom: false,
            CultureInfo.GetCultureInfo("th-TH"));

        Assert.Equal([
            "นั่งพิงพนัก วางเท้าให้มั่นคง",
            "ดันดัมเบลขึ้น โดยไม่แอ่นหลัง",
            "ค่อย ๆ ลดกลับมาระดับไหล่"
        ], guidance.Steps);
        Assert.Equal("เลือกน้ำหนักที่คุมได้ทุกครั้ง", guidance.Tip);
        Assert.Equal("อย่าเหวี่ยงตัวช่วยยก", guidance.Caution);
    }

    [Fact]
    public void Custom_exercise_gets_an_honest_body_part_fallback()
    {
        var guidance = new ExerciseTechniqueCatalog().Get(
            "My Gym Machine", BodyPart.Legs, isCustom: true,
            CultureInfo.GetCultureInfo("th-TH"));

        Assert.False(guidance.IsSpecific);
        Assert.Contains("ขา", guidance.Steps[0], StringComparison.Ordinal);
        Assert.Contains("น้ำหนักเบา", guidance.Tip, StringComparison.Ordinal);
    }
}
