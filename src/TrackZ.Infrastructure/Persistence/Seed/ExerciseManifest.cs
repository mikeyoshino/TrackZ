using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TrackZ.Domain.Exercises;

namespace TrackZ.Infrastructure.Persistence.Seed;

public sealed record ExerciseManifestItem(
    Guid Id,
    string Name,
    BodyPart BodyPart,
    TrackingMode TrackingMode,
    string Slug,
    string ImagePath,
    ExerciseImageReviewState ReviewState,
    string SourceReference);

public static partial class ExerciseManifest
{
    public const string Schema = "trackz.exercise-catalog";
    public const int Version = 1;
    public const int SystemExerciseCount = 90;
    public const int ExercisesPerBodyPart = 15;
    public const string AiGeneratedProjectOwnedDraftSourceReference = "ai-generated-project-owned-draft";

    private static readonly IReadOnlyDictionary<string, BodyPart> CanonicalBodyParts = new Dictionary<string, BodyPart>(StringComparer.Ordinal)
    {
        ["Barbell Bench Press"] = BodyPart.Chest, ["Incline Barbell Bench Press"] = BodyPart.Chest, ["Dumbbell Bench Press"] = BodyPart.Chest, ["Incline Dumbbell Press"] = BodyPart.Chest,
        ["Chest Press Machine"] = BodyPart.Chest, ["Cable Fly"] = BodyPart.Chest, ["Pec Deck Fly"] = BodyPart.Chest, ["Decline Push-Up"] = BodyPart.Chest,
        ["Push-Up"] = BodyPart.Chest, ["Chest Dip"] = BodyPart.Chest, ["Decline Barbell Bench Press"] = BodyPart.Chest, ["Smith Machine Bench Press"] = BodyPart.Chest,
        ["Dumbbell Fly"] = BodyPart.Chest, ["Low-to-High Cable Fly"] = BodyPart.Chest, ["High-to-Low Cable Fly"] = BodyPart.Chest,
        ["Lat Pulldown"] = BodyPart.Back, ["Pull-Up"] = BodyPart.Back, ["Assisted Pull-Up"] = BodyPart.Back, ["Seated Cable Row"] = BodyPart.Back,
        ["Chest-Supported Row"] = BodyPart.Back, ["Barbell Row"] = BodyPart.Back, ["One-Arm Dumbbell Row"] = BodyPart.Back, ["Straight-Arm Pulldown"] = BodyPart.Back,
        ["Conventional Deadlift"] = BodyPart.Back, ["T-Bar Row"] = BodyPart.Back, ["Inverted Row"] = BodyPart.Back, ["Neutral-Grip Lat Pulldown"] = BodyPart.Back,
        ["Wide-Grip Lat Pulldown"] = BodyPart.Back, ["Single-Arm Cable Row"] = BodyPart.Back, ["Machine High Row"] = BodyPart.Back,
        ["Overhead Press"] = BodyPart.Shoulders, ["Dumbbell Shoulder Press"] = BodyPart.Shoulders, ["Machine Shoulder Press"] = BodyPart.Shoulders, ["Lateral Raise"] = BodyPart.Shoulders,
        ["Cable Lateral Raise"] = BodyPart.Shoulders, ["Rear Delt Fly"] = BodyPart.Shoulders, ["Face Pull"] = BodyPart.Shoulders, ["Upright Row"] = BodyPart.Shoulders,
        ["Arnold Press"] = BodyPart.Shoulders, ["Dumbbell Front Raise"] = BodyPart.Shoulders, ["Cable Front Raise"] = BodyPart.Shoulders, ["Bent-Over Reverse Fly"] = BodyPart.Shoulders,
        ["Reverse Pec Deck"] = BodyPart.Shoulders, ["Landmine Press"] = BodyPart.Shoulders, ["Dumbbell Shrug"] = BodyPart.Shoulders,
        ["Barbell Curl"] = BodyPart.Arms, ["Dumbbell Curl"] = BodyPart.Arms, ["Hammer Curl"] = BodyPart.Arms, ["Preacher Curl"] = BodyPart.Arms,
        ["Triceps Pushdown"] = BodyPart.Arms, ["Overhead Triceps Extension"] = BodyPart.Arms, ["Skull Crusher"] = BodyPart.Arms, ["Close-Grip Bench Press"] = BodyPart.Arms,
        ["EZ-Bar Curl"] = BodyPart.Arms, ["Incline Dumbbell Curl"] = BodyPart.Arms, ["Cable Curl"] = BodyPart.Arms, ["Concentration Curl"] = BodyPart.Arms,
        ["Bench Dip"] = BodyPart.Arms, ["Triceps Dip"] = BodyPart.Arms, ["Single-Arm Cable Pushdown"] = BodyPart.Arms,
        ["Back Squat"] = BodyPart.Legs, ["Front Squat"] = BodyPart.Legs, ["Leg Press"] = BodyPart.Legs, ["Romanian Deadlift"] = BodyPart.Legs,
        ["Leg Extension"] = BodyPart.Legs, ["Seated Leg Curl"] = BodyPart.Legs, ["Bulgarian Split Squat"] = BodyPart.Legs, ["Standing Calf Raise"] = BodyPart.Legs,
        ["Goblet Squat"] = BodyPart.Legs, ["Hack Squat"] = BodyPart.Legs, ["Sumo Deadlift"] = BodyPart.Legs, ["Walking Lunge"] = BodyPart.Legs,
        ["Hip Thrust"] = BodyPart.Legs, ["Lying Leg Curl"] = BodyPart.Legs, ["Seated Calf Raise"] = BodyPart.Legs,
        ["Cable Crunch"] = BodyPart.Core, ["Hanging Knee Raise"] = BodyPart.Core, ["Hanging Leg Raise"] = BodyPart.Core, ["Ab Wheel Rollout"] = BodyPart.Core,
        ["Weighted Sit-Up"] = BodyPart.Core, ["Decline Sit-Up"] = BodyPart.Core, ["Reverse Crunch"] = BodyPart.Core, ["Pallof Press"] = BodyPart.Core,
        ["Plank"] = BodyPart.Core, ["Side Plank"] = BodyPart.Core, ["Dead Bug"] = BodyPart.Core, ["Bird Dog"] = BodyPart.Core,
        ["Russian Twist"] = BodyPart.Core, ["Bicycle Crunch"] = BodyPart.Core, ["Mountain Climber"] = BodyPart.Core
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<BodyPart>(allowIntegerValues: false),
            new JsonStringEnumConverter<TrackingMode>(allowIntegerValues: false),
            new JsonStringEnumConverter<ExerciseImageReviewState>(allowIntegerValues: false)
        }
    };

    public static IReadOnlyList<ExerciseManifestItem> Load(string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException("Exercise catalog manifest was not found.", catalogPath);
        }

        CatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(catalogPath), SerializerOptions)
                ?? throw new InvalidDataException("Exercise catalog manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Exercise catalog manifest is invalid JSON.", exception);
        }

        if (!string.Equals(document.Schema, Schema, StringComparison.Ordinal) || document.Version != Version)
        {
            throw new InvalidDataException($"Exercise catalog manifest must declare schema '{Schema}' version {Version}.");
        }

        var items = document.Exercises.Select(item => new ExerciseManifestItem(
            item.Id,
            Require(item.Name, "name"),
            item.BodyPart,
            item.TrackingMode,
            Require(item.Slug, "slug"),
            Require(item.ImagePath, "imagePath"),
            item.ReviewState,
            Require(item.SourceReference, "sourceReference"))).ToArray();

        Validate(items);
        return items;
    }

    public static void ValidateAssets(string catalogPath, IEnumerable<ExerciseManifestItem>? items = null)
    {
        var manifestItems = items?.ToArray() ?? Load(catalogPath).ToArray();

        foreach (var item in manifestItems)
        {
            var assetPath = ResolveAssetPath(catalogPath, item);
            if (!File.Exists(assetPath))
            {
                throw new FileNotFoundException($"Exercise artwork is missing for '{item.Name}'.", assetPath);
            }
        }
    }

    public static string ResolveAssetPath(string catalogPath, ExerciseManifestItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        ArgumentNullException.ThrowIfNull(item);
        var catalogDirectory = Path.GetDirectoryName(Path.GetFullPath(catalogPath))
            ?? throw new InvalidDataException("Exercise catalog manifest has no directory.");
        var repositoryRoot = Path.GetFullPath(Path.Combine(catalogDirectory, "..", ".."));
        var assetPath = Path.GetFullPath(Path.Combine(repositoryRoot, item.ImagePath));
        if (!assetPath.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Exercise catalog manifest has an unsafe image path for '{item.Name}'.");
        return assetPath;
    }

    private static void Validate(IReadOnlyList<ExerciseManifestItem> items)
    {
        if (items.Count != SystemExerciseCount)
        {
            throw new InvalidDataException($"Exercise catalog manifest must contain exactly {SystemExerciseCount} exercises.");
        }

        if (items.Any(item => item.Id == Guid.Empty)
            || items.Select(item => item.Id).Distinct().Count() != items.Count
            || HasCaseInsensitiveDuplicates(items.Select(item => item.Name))
            || HasCaseInsensitiveDuplicates(items.Select(item => item.Slug))
            || HasCaseInsensitiveDuplicates(items.Select(item => item.ImagePath)))
        {
            throw new InvalidDataException("Exercise catalog manifest has duplicate or empty identifiers.");
        }

        foreach (var bodyPart in Enum.GetValues<BodyPart>())
        {
            if (items.Count(item => item.BodyPart == bodyPart) != ExercisesPerBodyPart)
            {
                throw new InvalidDataException($"Exercise catalog manifest must contain exactly {ExercisesPerBodyPart} {bodyPart} exercises.");
            }
        }

        foreach (var item in items)
        {
            if (!Enum.IsDefined(item.BodyPart)
                || !Enum.IsDefined(item.TrackingMode)
                || item.ReviewState != ExerciseImageReviewState.Draft
                || !CanonicalBodyParts.TryGetValue(item.Name, out var expectedBodyPart)
                || item.BodyPart != expectedBodyPart
                || !string.Equals(item.SourceReference, AiGeneratedProjectOwnedDraftSourceReference, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Exercise catalog manifest has invalid enum values for '{item.Name}'.");
            }

            if (!SlugPattern().IsMatch(item.Slug)
                || !string.Equals(item.ImagePath, $"assets/exercises/images/{item.Slug}.png", StringComparison.Ordinal)
                || Path.IsPathFullyQualified(item.ImagePath)
                || item.ImagePath.Contains("..", StringComparison.Ordinal)
                || item.ImagePath.Contains('\\'))
            {
                throw new InvalidDataException($"Exercise catalog manifest has an unsafe image path for '{item.Name}'.");
            }
        }
    }

    private static bool HasCaseInsensitiveDuplicates(IEnumerable<string> values) =>
        values.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1);

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"Exercise catalog manifest requires '{field}'.") : value.Trim();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    private sealed class CatalogDocument
    {
        [JsonRequired]
        public string Schema { get; init; } = null!;

        [JsonRequired]
        public int Version { get; init; }

        [JsonRequired]
        public List<CatalogExercise> Exercises { get; init; } = null!;
    }

    private sealed class CatalogExercise
    {
        [JsonRequired]
        public Guid Id { get; init; }

        [JsonRequired]
        public string Name { get; init; } = null!;

        [JsonRequired]
        public BodyPart BodyPart { get; init; }

        [JsonRequired]
        public TrackingMode TrackingMode { get; init; }

        [JsonRequired]
        public string Slug { get; init; } = null!;

        [JsonRequired]
        public string ImagePath { get; init; } = null!;

        [JsonRequired]
        public ExerciseImageReviewState ReviewState { get; init; }

        [JsonRequired]
        public string SourceReference { get; init; } = null!;
    }
}
