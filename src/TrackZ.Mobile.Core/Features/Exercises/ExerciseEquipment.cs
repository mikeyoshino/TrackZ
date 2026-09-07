using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises.Models;

namespace TrackZ.Mobile.Features.Exercises;

public enum ExerciseEquipment { Dumbbell, Barbell, Cable, Machine, Bodyweight, Other }

public static class ExerciseEquipmentLabels
{
    public static ExerciseEquipment Identify(CachedExercise exercise)
    {
        var name = exercise.Name.ToLowerInvariant();
        if (name.Contains("dumbbell") || name.Contains("ดัมเบล")) return ExerciseEquipment.Dumbbell;
        if (name.Contains("barbell") || name.Contains("บาร์เบล") || name.Contains("ez-bar")) return ExerciseEquipment.Barbell;
        if (name.Contains("cable") || name.Contains("เคเบิล")) return ExerciseEquipment.Cable;
        if (name.Contains("machine") || name.Contains("pec deck") || name.Contains("smith") || name.Contains("เครื่อง")) return ExerciseEquipment.Machine;
        if (exercise.TrackingMode == TrackingMode.Bodyweight) return ExerciseEquipment.Bodyweight;
        return ExerciseEquipment.Other;
    }

    public static string Label(this ExerciseEquipment value, bool thai) => (value, thai) switch
    {
        (ExerciseEquipment.Dumbbell, true) => "ดัมเบล",
        (ExerciseEquipment.Barbell, true) => "บาร์เบล",
        (ExerciseEquipment.Cable, true) => "เคเบิล",
        (ExerciseEquipment.Machine, true) => "เครื่อง",
        (ExerciseEquipment.Bodyweight, true) => "น้ำหนักตัว",
        (ExerciseEquipment.Other, true) => "อื่น ๆ",
        _ => value.ToString()
    };
}
