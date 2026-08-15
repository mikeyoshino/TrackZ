using System.Globalization;
using System.Resources;

namespace TrackZ.Mobile.Features.Workout;

public sealed record WorkoutTextSet(
    string Offline,
    string Pending,
    string Conflicted,
    string PermanentFailure,
    string Syncing,
    string Reconciling,
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
    string SavedLocally,
    string ChooseExercises,
    string SearchExercises,
    string AllBodyParts,
    string All,
    string SelectedCountFormat,
    string NoMatchingExercises,
    string CreateCustom,
    string Done,
    string BodyPartChest,
    string BodyPartBack,
    string BodyPartShoulders,
    string BodyPartArms,
    string BodyPartLegs,
    string BodyPartCore,
    string DecreaseWeight,
    string IncreaseWeight,
    string DecreaseAssistance,
    string IncreaseAssistance,
    string DecreaseReps,
    string IncreaseReps,
    string PerformanceLastFormat,
    string PerformancePrFormat,
    string Edit,
    string PendingSync,
    string WeightedPerformanceFormat,
    string AssistedPerformanceFormat,
    string BodyweightPerformanceFormat,
    string RepSingular,
    string RepPlural,
    string FinishWorkout,
    string HistoryTitle,
    string NoHistory,
    string Delete,
    string Undo,
    string Confirm,
    string Cancel,
    string DeleteSetTitle,
    string DeleteSetMessage,
    string DeleteWorkoutTitle,
    string DeleteWorkoutMessage,
    string HistoryEditFailed,
    string HistoryDeleteFailed,
    string UndoFailed,
    string KeepServer,
    string ApplyLocal,
    string HistoryConflictFailed);

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
            Value("Offline", culture), Value("Pending", culture), Value("Conflicted", culture), Value("PermanentFailure", culture),
            Value("Syncing", culture), Value("Reconciling", culture), Value("Synced", culture), Value("InvalidWeightedSet", culture),
            Value("InvalidAssistedSet", culture), Value("InvalidBodyweightSet", culture),
            Value("SaveFailed", culture), Value("LoadFailed", culture), Value("WorkoutTitle", culture), Value("SetLoggerTitle", culture),
            Value("Last", culture), Value("Today", culture), Value("MatchLast", culture),
            Value("CompleteSet", culture), Value("AddExercise", culture), Value("StartWorkout", culture),
            Value("Remove", culture), Value("MoveUp", culture), Value("MoveDown", culture),
            Value("Weight", culture), Value("Assistance", culture), Value("Reps", culture),
            Value("Kilograms", culture), Value("Pounds", culture),
            Value("Bodyweight", culture), Value("NoExercises", culture), Value("SavedLocally", culture),
            Value("ChooseExercises", culture), Value("SearchExercises", culture), Value("AllBodyParts", culture),
            Value("All", culture), Value("SelectedCountFormat", culture), Value("NoMatchingExercises", culture),
            Value("CreateCustom", culture), Value("Done", culture), Value("BodyPartChest", culture),
            Value("BodyPartBack", culture), Value("BodyPartShoulders", culture), Value("BodyPartArms", culture),
            Value("BodyPartLegs", culture), Value("BodyPartCore", culture),
            Value("DecreaseWeight", culture), Value("IncreaseWeight", culture),
            Value("DecreaseAssistance", culture), Value("IncreaseAssistance", culture),
            Value("DecreaseReps", culture), Value("IncreaseReps", culture),
            Value("PerformanceLastFormat", culture), Value("PerformancePrFormat", culture),
            Value("Edit", culture), Value("PendingSync", culture),
            Value("WeightedPerformanceFormat", culture), Value("AssistedPerformanceFormat", culture),
            Value("BodyweightPerformanceFormat", culture), Value("RepSingular", culture),
            Value("RepPlural", culture), Value("FinishWorkout", culture),
            Value("HistoryTitle", culture), Value("NoHistory", culture),
            Value("Delete", culture), Value("Undo", culture), Value("Confirm", culture),
            Value("Cancel", culture), Value("DeleteSetTitle", culture),
            Value("DeleteSetMessage", culture), Value("DeleteWorkoutTitle", culture),
            Value("DeleteWorkoutMessage", culture), Value("HistoryEditFailed", culture),
            Value("HistoryDeleteFailed", culture), Value("UndoFailed", culture),
            Value("KeepServer", culture), Value("ApplyLocal", culture),
            Value("HistoryConflictFailed", culture));
    }

    private static string Value(string key, CultureInfo culture) =>
        Manager.GetString(key, culture)
        ?? throw new MissingManifestResourceException($"Workout resource '{key}' is missing.");
}
