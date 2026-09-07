using System.Globalization;
using TrackZ.Domain.Exercises;

namespace TrackZ.Mobile.Features.Exercises;

public sealed record ExerciseTechnique(
    bool IsSpecific,
    IReadOnlyList<string> Steps,
    string Tip,
    string Caution);

public sealed class ExerciseTechniqueCatalog
{
    private enum Movement
    {
        Press, Fly, PushUp, Dip, VerticalPull, Row, Hinge, ShoulderPress,
        Raise, RearShoulder, Curl, Triceps, Squat, KneeMachine, SingleLeg,
        Calf, HipThrust, Crunch, LegRaise, AbWheel, AntiRotation, Plank,
        SidePlank, CoreControl, Twist, Climber
    }

    private static readonly IReadOnlyDictionary<string, Movement> Movements =
        new Dictionary<string, Movement>(StringComparer.OrdinalIgnoreCase)
        {
            ["Barbell Bench Press"] = Movement.Press,
            ["Incline Barbell Bench Press"] = Movement.Press,
            ["Dumbbell Bench Press"] = Movement.Press,
            ["Incline Dumbbell Press"] = Movement.Press,
            ["Chest Press Machine"] = Movement.Press,
            ["Decline Barbell Bench Press"] = Movement.Press,
            ["Smith Machine Bench Press"] = Movement.Press,
            ["Close-Grip Bench Press"] = Movement.Press,
            ["Cable Fly"] = Movement.Fly,
            ["Pec Deck Fly"] = Movement.Fly,
            ["Dumbbell Fly"] = Movement.Fly,
            ["Low-to-High Cable Fly"] = Movement.Fly,
            ["High-to-Low Cable Fly"] = Movement.Fly,
            ["Decline Push-Up"] = Movement.PushUp,
            ["Push-Up"] = Movement.PushUp,
            ["Chest Dip"] = Movement.Dip,
            ["Bench Dip"] = Movement.Dip,
            ["Triceps Dip"] = Movement.Dip,
            ["Lat Pulldown"] = Movement.VerticalPull,
            ["Pull-Up"] = Movement.VerticalPull,
            ["Assisted Pull-Up"] = Movement.VerticalPull,
            ["Neutral-Grip Lat Pulldown"] = Movement.VerticalPull,
            ["Wide-Grip Lat Pulldown"] = Movement.VerticalPull,
            ["Straight-Arm Pulldown"] = Movement.VerticalPull,
            ["Seated Cable Row"] = Movement.Row,
            ["Chest-Supported Row"] = Movement.Row,
            ["Barbell Row"] = Movement.Row,
            ["One-Arm Dumbbell Row"] = Movement.Row,
            ["T-Bar Row"] = Movement.Row,
            ["Inverted Row"] = Movement.Row,
            ["Single-Arm Cable Row"] = Movement.Row,
            ["Machine High Row"] = Movement.Row,
            ["Conventional Deadlift"] = Movement.Hinge,
            ["Romanian Deadlift"] = Movement.Hinge,
            ["Sumo Deadlift"] = Movement.Hinge,
            ["Overhead Press"] = Movement.ShoulderPress,
            ["Dumbbell Shoulder Press"] = Movement.ShoulderPress,
            ["Machine Shoulder Press"] = Movement.ShoulderPress,
            ["Arnold Press"] = Movement.ShoulderPress,
            ["Landmine Press"] = Movement.ShoulderPress,
            ["Lateral Raise"] = Movement.Raise,
            ["Cable Lateral Raise"] = Movement.Raise,
            ["Dumbbell Front Raise"] = Movement.Raise,
            ["Cable Front Raise"] = Movement.Raise,
            ["Rear Delt Fly"] = Movement.RearShoulder,
            ["Face Pull"] = Movement.RearShoulder,
            ["Upright Row"] = Movement.RearShoulder,
            ["Bent-Over Reverse Fly"] = Movement.RearShoulder,
            ["Reverse Pec Deck"] = Movement.RearShoulder,
            ["Dumbbell Shrug"] = Movement.RearShoulder,
            ["Barbell Curl"] = Movement.Curl,
            ["Dumbbell Curl"] = Movement.Curl,
            ["Hammer Curl"] = Movement.Curl,
            ["Preacher Curl"] = Movement.Curl,
            ["EZ-Bar Curl"] = Movement.Curl,
            ["Incline Dumbbell Curl"] = Movement.Curl,
            ["Cable Curl"] = Movement.Curl,
            ["Concentration Curl"] = Movement.Curl,
            ["Triceps Pushdown"] = Movement.Triceps,
            ["Overhead Triceps Extension"] = Movement.Triceps,
            ["Skull Crusher"] = Movement.Triceps,
            ["Single-Arm Cable Pushdown"] = Movement.Triceps,
            ["Back Squat"] = Movement.Squat,
            ["Front Squat"] = Movement.Squat,
            ["Goblet Squat"] = Movement.Squat,
            ["Hack Squat"] = Movement.Squat,
            ["Leg Press"] = Movement.KneeMachine,
            ["Leg Extension"] = Movement.KneeMachine,
            ["Seated Leg Curl"] = Movement.KneeMachine,
            ["Lying Leg Curl"] = Movement.KneeMachine,
            ["Bulgarian Split Squat"] = Movement.SingleLeg,
            ["Walking Lunge"] = Movement.SingleLeg,
            ["Standing Calf Raise"] = Movement.Calf,
            ["Seated Calf Raise"] = Movement.Calf,
            ["Hip Thrust"] = Movement.HipThrust,
            ["Cable Crunch"] = Movement.Crunch,
            ["Weighted Sit-Up"] = Movement.Crunch,
            ["Decline Sit-Up"] = Movement.Crunch,
            ["Reverse Crunch"] = Movement.Crunch,
            ["Hanging Knee Raise"] = Movement.LegRaise,
            ["Hanging Leg Raise"] = Movement.LegRaise,
            ["Ab Wheel Rollout"] = Movement.AbWheel,
            ["Pallof Press"] = Movement.AntiRotation,
            ["Plank"] = Movement.Plank,
            ["Side Plank"] = Movement.SidePlank,
            ["Dead Bug"] = Movement.CoreControl,
            ["Bird Dog"] = Movement.CoreControl,
            ["Russian Twist"] = Movement.Twist,
            ["Bicycle Crunch"] = Movement.Twist,
            ["Mountain Climber"] = Movement.Climber
        };

    public ExerciseTechnique Get(
        string name,
        BodyPart bodyPart,
        bool isCustom,
        CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(culture);
        var thai = culture.TwoLetterISOLanguageName.Equals("th", StringComparison.OrdinalIgnoreCase);
        if (!isCustom && Movements.TryGetValue(name.Trim(), out var movement))
            return Guidance(movement, thai) with { IsSpecific = true };
        return Fallback(bodyPart, thai);
    }

    private static ExerciseTechnique Guidance(Movement movement, bool thai) => thai
        ? Thai(movement)
        : English(movement);

    private static ExerciseTechnique Thai(Movement movement) => movement switch
    {
        Movement.Press => Guide("วางเท้าให้มั่นคง และเก็บหัวไหล่", "ลดน้ำหนักลงช้า ๆ ใกล้ช่วงอก", "ดันขึ้นโดยไม่ยกไหล่ตาม", "ให้ข้อมืออยู่ตรงเหนือข้อศอก", "อย่าเด้งน้ำหนักจากอก"),
        Movement.Fly => Guide("ตั้งอกและงอข้อศอกเล็กน้อย", "กางแขนออกเท่าที่ไหล่ยังสบาย", "หุบแขนกลับโดยบีบหน้าอก", "ใช้น้ำหนักที่หุบแขนได้โดยไม่สั่น", "อย่าเหยียดข้อศอกจนตึง"),
        Movement.PushUp => Guide("วางมือกว้างกว่าไหล่เล็กน้อย", "เกร็งลำตัวแล้วลดอกลง", "ดันพื้นกลับโดยให้ตัวเป็นเส้นตรง", "เริ่มจากวางเข่าหากยังคุมตัวไม่ได้", "อย่าปล่อยสะโพกตก"),
        Movement.Dip => Guide("จับที่วางให้แน่นและตั้งอก", "งอศอกลดตัวลงอย่างช้า ๆ", "ดันตัวขึ้นโดยไม่ล็อกศอกแรง", "ลงเท่าที่หัวไหล่ยังสบาย", "อย่ายกไหล่ชิดหู"),
        Movement.VerticalPull => Guide("จับอุปกรณ์แล้วตั้งอก", "ดึงศอกลงข้างลำตัว", "ค่อย ๆ ปล่อยแขนกลับจนสุด", "คิดว่าดึงด้วยศอก ไม่ใช่มือ", "อย่าเหวี่ยงตัวช่วยดึง"),
        Movement.Row => Guide("ตั้งหลังตรงและเก็บหัวไหล่", "ดึงศอกไปด้านหลังใกล้ลำตัว", "ค่อย ๆ เหยียดแขนกลับ", "หยุดสั้น ๆ ตอนศอกอยู่ด้านหลัง", "อย่ากระตุกหลังช่วยดึง"),
        Movement.Hinge => Guide("วางเท้าให้มั่นคงและจับน้ำหนักแน่น", "ดันสะโพกไปหลังโดยให้หลังตรง", "ยืนขึ้นด้วยแรงจากขาและสะโพก", "ให้น้ำหนักอยู่ใกล้ตัวตลอด", "อย่าโก่งหลังเพื่อยกให้หนักขึ้น"),
        Movement.ShoulderPress => Guide("นั่งพิงพนัก วางเท้าให้มั่นคง", "ดันดัมเบลขึ้น โดยไม่แอ่นหลัง", "ค่อย ๆ ลดกลับมาระดับไหล่", "เลือกน้ำหนักที่คุมได้ทุกครั้ง", "อย่าเหวี่ยงตัวช่วยยก"),
        Movement.Raise => Guide("ยืนมั่นคงและงอศอกเล็กน้อย", "ยกแขนถึงประมาณระดับไหล่", "ค่อย ๆ ลดแขนกลับข้างลำตัว", "ยกช้า ๆ แล้วหยุดสั้น ๆ ด้านบน", "อย่าเหวี่ยงตัวช่วยยก"),
        Movement.RearShoulder => Guide("ตั้งอกและปล่อยไหล่ลง", "ดึงหรือกางแขนออกโดยเก็บคอให้สบาย", "ค่อย ๆ กลับสู่ท่าเริ่ม", "ใช้น้ำหนักเบาพอให้รู้สึกที่หัวไหล่หลัง", "อย่าห่อไหล่หรือเชิดคอ"),
        Movement.Curl => Guide("ยืนมั่นคงและเก็บศอกข้างลำตัว", "งอแขนยกน้ำหนักขึ้น", "ค่อย ๆ ลดจนแขนเกือบตรง", "ขยับเฉพาะข้อศอกให้มากที่สุด", "อย่าโยกตัวส่งน้ำหนัก"),
        Movement.Triceps => Guide("ตั้งลำตัวนิ่งและเก็บศอก", "เหยียดแขนจนรู้สึกหลังแขนทำงาน", "ค่อย ๆ กลับโดยไม่ปล่อยน้ำหนัก", "ให้ศอกอยู่ตำแหน่งเดิมตลอด", "อย่าใช้หัวไหล่ช่วยดัน"),
        Movement.Squat => Guide("วางเท้ากว้างพอดีและตั้งอก", "นั่งสะโพกลงพร้อมงอเข่า", "ดันพื้นแล้วยืนกลับขึ้น", "ให้เข่าไปทางเดียวกับปลายเท้า", "อย่าฝืนลงลึกจนหลังงอ"),
        Movement.KneeMachine => Guide("ปรับเบาะให้ข้อพับตรงกับเครื่อง", "ออกแรงด้วยขาอย่างนุ่มนวล", "ค่อย ๆ ปล่อยกลับโดยคุมน้ำหนัก", "เริ่มเบาเพื่อหาตำแหน่งที่สบาย", "อย่ากระแทกข้อเข่าหรือล็อกเข่าแรง"),
        Movement.SingleLeg => Guide("ก้าวเท้าให้มั่นคงและตั้งตัวตรง", "ลดตัวลงโดยคุมเข่าหน้า", "ดันพื้นกลับด้วยขาหน้า", "เริ่มช่วงสั้นก่อนแล้วค่อยเพิ่ม", "อย่าปล่อยเข่าพับเข้าด้านใน"),
        Movement.Calf => Guide("วางปลายเท้าให้มั่นคง", "ยกส้นเท้าขึ้นให้สุดที่คุมได้", "ค่อย ๆ ลดส้นกลับลง", "หยุดสั้น ๆ ทั้งด้านบนและด้านล่าง", "อย่าเด้งขึ้นลงเร็วเกินไป"),
        Movement.HipThrust => Guide("พิงหลังบนม้านั่งและวางเท้าให้มั่นคง", "ดันสะโพกขึ้นพร้อมเกร็งก้น", "ค่อย ๆ ลดสะโพกกลับ", "เก็บคางเล็กน้อยเพื่อให้หลังนิ่ง", "อย่าแอ่นหลังแทนการดันสะโพก"),
        Movement.Crunch => Guide("จัดหลังและเท้าให้อยู่ในท่าสบาย", "เกร็งท้องแล้วยกลำตัวช่วงบน", "ค่อย ๆ ลดกลับโดยยังคุมท้อง", "ขยับสั้น ๆ แต่ช้าและชัด", "อย่าดึงคอช่วยขึ้น"),
        Movement.LegRaise => Guide("จับที่ยึดให้แน่นและเก็บลำตัวนิ่ง", "ยกเข่าหรือขาขึ้นด้วยหน้าท้อง", "ค่อย ๆ ลดโดยไม่แกว่งตัว", "เริ่มจากงอเข่าหากขายังหนักเกินไป", "อย่าเหวี่ยงขาเพื่อส่งแรง"),
        Movement.AbWheel => Guide("คุกเข่าและจับล้อให้แน่น", "กลิ้งออกไปโดยเกร็งท้อง", "ดึงล้อกลับด้วยลำตัวที่นิ่ง", "ไปไกลเท่าที่หลังยังตรง", "อย่าปล่อยเอวแอ่น"),
        Movement.AntiRotation => Guide("ยืนข้างสายเคเบิลและเกร็งท้อง", "ดันมือออกตรงหน้า", "ดึงมือกลับโดยไม่หมุนตัว", "ใช้แรงพอให้ต้องต้าน แต่ยังนิ่งได้", "อย่าปล่อยลำตัวบิดตามสาย"),
        Movement.Plank => Guide("วางศอกใต้หัวไหล่", "เกร็งท้องและก้นให้ตัวเป็นเส้นตรง", "หายใจปกติค้างตามเวลาที่ตั้งไว้", "หยุดก่อนที่สะโพกจะเริ่มตก", "อย่ากลั้นหายใจ"),
        Movement.SidePlank => Guide("วางศอกใต้หัวไหล่และเรียงขา", "ยกสะโพกให้ลำตัวเป็นเส้นตรง", "หายใจปกติและค้างไว้", "งอเข่าล่างได้หากยังยากเกินไป", "อย่าปล่อยสะโพกตก"),
        Movement.CoreControl => Guide("ตั้งหลังกลางและเกร็งท้องเบา ๆ", "ขยับแขนหรือขาช้า ๆ ตามท่า", "กลับจุดเริ่มโดยลำตัวยังนิ่ง", "ลดระยะขยับหากหลังเริ่มลอย", "อย่ารีบจนลำตัวโยก"),
        Movement.Twist => Guide("นั่งหรือนอนให้หลังอยู่ในท่าสบาย", "หมุนลำตัวช้า ๆ จากช่วงอก", "กลับกลางก่อนสลับอีกข้าง", "คุมจังหวะให้ท้องทำงานตลอด", "อย่ากระชากคอหรือหลัง"),
        Movement.Climber => Guide("วางมือใต้หัวไหล่และตั้งลำตัวตรง", "ดึงเข่าเข้าหาหน้าอกทีละข้าง", "สลับขาโดยคุมสะโพกให้นิ่ง", "เริ่มช้าแล้วค่อยเพิ่มความเร็ว", "อย่าปล่อยสะโพกเด้งขึ้นลง"),
        _ => throw new ArgumentOutOfRangeException(nameof(movement))
    };

    private static ExerciseTechnique English(Movement movement) => movement switch
    {
        Movement.Press => Guide("Plant your feet and set your shoulders", "Lower the weight slowly toward your chest", "Press up without shrugging", "Keep wrists stacked over elbows", "Do not bounce the weight off your chest"),
        Movement.Fly => Guide("Lift your chest with a soft bend in the elbows", "Open until your shoulders still feel comfortable", "Bring your arms together and squeeze your chest", "Use a load you can control without shaking", "Do not lock your elbows"),
        Movement.PushUp => Guide("Place hands just wider than your shoulders", "Brace your body and lower your chest", "Push the floor away in one straight line", "Use your knees if you cannot stay straight", "Do not let your hips sag"),
        Movement.Dip => Guide("Hold the bars firmly and lift your chest", "Bend your elbows and lower slowly", "Press up without snapping the elbows", "Only lower as far as your shoulders feel good", "Do not shrug toward your ears"),
        Movement.VerticalPull => Guide("Hold the bar and lift your chest", "Pull your elbows down beside your body", "Return slowly until your arms are long", "Think about moving your elbows, not your hands", "Do not swing to finish the pull"),
        Movement.Row => Guide("Keep your back steady and shoulders set", "Pull your elbows behind you", "Reach forward again with control", "Pause briefly at the back", "Do not jerk with your lower back"),
        Movement.Hinge => Guide("Plant your feet and hold the weight firmly", "Push your hips back with a steady back", "Stand using your legs and hips", "Keep the weight close to your body", "Do not round your back for a heavier lift"),
        Movement.ShoulderPress => Guide("Sit against the pad and plant your feet", "Press up without arching your back", "Lower slowly to shoulder height", "Choose a load you control every rep", "Do not swing to help the lift"),
        Movement.Raise => Guide("Stand steady with a soft elbow bend", "Raise your arms to about shoulder height", "Lower slowly to your sides", "Move slowly and pause briefly at the top", "Do not swing the weight"),
        Movement.RearShoulder => Guide("Lift your chest and relax your neck", "Pull or open your arms without shrugging", "Return slowly to the start", "Use a light load you feel behind the shoulders", "Do not round your shoulders"),
        Movement.Curl => Guide("Stand steady with elbows by your sides", "Bend your arms to raise the weight", "Lower until your arms are almost straight", "Keep your elbows in one place", "Do not rock your body"),
        Movement.Triceps => Guide("Keep your body and elbows still", "Straighten your arms and squeeze", "Return slowly without dropping the weight", "Keep your elbows in the same place", "Do not press with your shoulders"),
        Movement.Squat => Guide("Set your feet and lift your chest", "Sit down while bending your knees", "Push the floor away to stand", "Point your knees with your toes", "Do not force depth when your back rounds"),
        Movement.KneeMachine => Guide("Adjust the seat so the machine fits your joints", "Move the weight smoothly with your legs", "Return slowly under control", "Start light to find a comfortable setup", "Do not slam or snap your knees"),
        Movement.SingleLeg => Guide("Set your feet and stay tall", "Lower while controlling your front knee", "Push through the front foot to rise", "Start with a short range and build up", "Do not let the knee fall inward"),
        Movement.Calf => Guide("Set the balls of your feet firmly", "Raise your heels as high as you control", "Lower your heels slowly", "Pause briefly at the top and bottom", "Do not bounce quickly"),
        Movement.HipThrust => Guide("Set your upper back and plant your feet", "Drive your hips up and squeeze your glutes", "Lower your hips slowly", "Tuck your chin slightly to stay steady", "Do not arch your back to finish"),
        Movement.Crunch => Guide("Set your back and feet comfortably", "Tighten your stomach and lift your upper body", "Lower slowly while keeping control", "Use a short, slow movement", "Do not pull on your neck"),
        Movement.LegRaise => Guide("Hold firmly and keep your body still", "Lift your knees or legs with your stomach", "Lower slowly without swinging", "Bend your knees if straight legs are too hard", "Do not throw your legs upward"),
        Movement.AbWheel => Guide("Kneel and hold the wheel firmly", "Roll forward while bracing your stomach", "Pull back with a steady body", "Only go as far as your back stays flat", "Do not let your lower back sag"),
        Movement.AntiRotation => Guide("Stand side-on and brace your stomach", "Press your hands straight forward", "Return without turning your body", "Use enough load to challenge your balance", "Do not rotate with the cable"),
        Movement.Plank => Guide("Place elbows under shoulders", "Tighten your stomach and glutes", "Breathe normally while holding", "Stop before your hips begin to drop", "Do not hold your breath"),
        Movement.SidePlank => Guide("Place your elbow under your shoulder", "Lift your hips into one straight line", "Breathe normally while holding", "Bend the lower knee if needed", "Do not let your hips drop"),
        Movement.CoreControl => Guide("Keep a neutral back and lightly brace", "Move the arm or leg slowly", "Return while your body stays still", "Shorten the movement if your back lifts", "Do not rush and wobble"),
        Movement.Twist => Guide("Set your back in a comfortable position", "Turn slowly through your upper body", "Return to the middle before changing sides", "Stay controlled so your stomach keeps working", "Do not yank your neck or back"),
        Movement.Climber => Guide("Place hands under shoulders and stay long", "Bring one knee toward your chest", "Switch legs while keeping hips quiet", "Start slow, then build speed", "Do not bounce your hips"),
        _ => throw new ArgumentOutOfRangeException(nameof(movement))
    };

    private static ExerciseTechnique Fallback(BodyPart bodyPart, bool thai)
    {
        var body = thai
            ? bodyPart switch
            {
                BodyPart.Chest => "หน้าอก", BodyPart.Back => "หลัง", BodyPart.Shoulders => "ไหล่",
                BodyPart.Arms => "แขน", BodyPart.Legs => "ขา", BodyPart.Core => "ลำตัว",
                _ => "ร่างกาย"
            }
            : bodyPart.ToString().ToLowerInvariant();
        return thai
            ? Guide($"จัดท่าให้มั่นคงและเตรียม{body}", "ขยับช้า ๆ ในช่วงที่สบาย", "กลับท่าเริ่มโดยยังคุมน้ำหนัก", "เริ่มด้วยน้ำหนักเบาและดูว่าร่างกายรู้สึกอย่างไร", "หยุดทันทีหากรู้สึกเจ็บ")
            : Guide($"Set up steadily and prepare your {body}", "Move slowly through a comfortable range", "Return to the start under control", "Start light and notice how your body feels", "Stop if you feel pain");
    }

    private static ExerciseTechnique Guide(
        string first,
        string second,
        string third,
        string tip,
        string caution) => new(false, [first, second, third], tip, caution);
}
