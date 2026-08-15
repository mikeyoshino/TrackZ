using System.Globalization;
using System.Resources;

namespace TrackZ.Mobile.Features.Workout;

public sealed record WorkoutTextSet(
    string Offline,
    string Pending,
    string Conflicted,
    string Syncing,
    string Synced,
    string InvalidWeightedSet,
    string InvalidAssistedSet,
    string InvalidBodyweightSet,
    string SaveFailed,
    string LoadFailed,
    string WorkoutTitle,
    string SetLoggerTitle,
    string Last,
    string Today,
    string MatchLast,
    string CompleteSet,
    string AddExercise,
    string StartWorkout,
    string Remove,
    string MoveUp,
    string MoveDown,
    string Weight,
    string Assistance,
    string Reps,
    string Kilograms,
    string Pounds,
    string Bodyweight,
    string NoExercises,
    string SavedLocally);

public static class WorkoutResources
{
    private static readonly ResourceManager Manager = new(
        $"{typeof(WorkoutResources).Assembly.GetName().Name}.Resources.WorkoutStrings",
        typeof(WorkoutResources).Assembly);

    public static WorkoutTextSet English { get; } = ForCulture(CultureInfo.GetCultureInfo("en-US"));
    public static WorkoutTextSet Current => ForCulture(CultureInfo.CurrentUICulture);

    public static WorkoutTextSet ForCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return new WorkoutTextSet(
            Value("Offline", culture), Value("Pending", culture), Value("Conflicted", culture),
            Value("Syncing", culture), Value("Synced", culture), Value("InvalidWeightedSet", culture),
            Value("InvalidAssistedSet", culture), Value("InvalidBodyweightSet", culture),
            Value("SaveFailed", culture), Value("LoadFailed", culture), Value("WorkoutTitle", culture), Value("SetLoggerTitle", culture),
            Value("Last", culture), Value("Today", culture), Value("MatchLast", culture),
            Value("CompleteSet", culture), Value("AddExercise", culture), Value("StartWorkout", culture),
            Value("Remove", culture), Value("MoveUp", culture), Value("MoveDown", culture),
            Value("Weight", culture), Value("Assistance", culture), Value("Reps", culture),
            Value("Kilograms", culture), Value("Pounds", culture),
            Value("Bodyweight", culture), Value("NoExercises", culture), Value("SavedLocally", culture));
    }

    private static string Value(string key, CultureInfo culture) =>
        Manager.GetString(key, culture)
        ?? throw new MissingManifestResourceException($"Workout resource '{key}' is missing.");
}
