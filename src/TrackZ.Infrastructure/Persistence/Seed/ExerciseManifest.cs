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
        var catalogDirectory = Path.GetDirectoryName(Path.GetFullPath(catalogPath))
            ?? throw new InvalidDataException("Exercise catalog manifest has no directory.");
        var repositoryRoot = Path.GetFullPath(Path.Combine(catalogDirectory, "..", ".."));

        foreach (var item in manifestItems)
        {
            var assetPath = Path.GetFullPath(Path.Combine(repositoryRoot, item.ImagePath));
            if (!assetPath.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || !File.Exists(assetPath))
            {
                throw new FileNotFoundException($"Exercise artwork is missing for '{item.Name}'.", assetPath);
            }
        }
    }

    private static void Validate(IReadOnlyList<ExerciseManifestItem> items)
    {
        if (items.Count != 48)
        {
            throw new InvalidDataException("Exercise catalog manifest must contain exactly 48 exercises.");
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
            if (items.Count(item => item.BodyPart == bodyPart) != 8)
            {
                throw new InvalidDataException($"Exercise catalog manifest must contain eight {bodyPart} exercises.");
            }
        }

        foreach (var item in items)
        {
            if (!Enum.IsDefined(item.BodyPart) || !Enum.IsDefined(item.TrackingMode) || item.ReviewState != ExerciseImageReviewState.Draft)
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
