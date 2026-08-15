using TrackZ.Domain.Exercises;
using TrackZ.Infrastructure.Persistence.Seed;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TrackZ.Infrastructure.Tests.Seed;

public sealed class ExerciseCatalogManifestTests
{
    [Fact]
    public void Catalog_contains_48_unique_exercises_across_all_body_parts()
    {
        var items = ExerciseManifest.Load(CatalogPath);

        Assert.Equal(48, items.Count);
        Assert.Equal(48, items.Select(item => item.Id).Distinct().Count());
        Assert.All(Enum.GetValues<BodyPart>(), part => Assert.Equal(8, items.Count(item => item.BodyPart == part)));
    }

    [Fact]
    public void Catalog_preserves_the_canonical_order_names_groups_modes_and_uuid_snapshot()
    {
        var items = ExerciseManifest.Load(CatalogPath);

        Assert.Equal(ExpectedNames, items.Select(item => item.Name));
        Assert.Equal(ExpectedIds, items.Select(item => item.Id));
        Assert.Equal(ExpectedBodyParts, items.Select(item => item.BodyPart));
        Assert.Equal(7, items.Count(item => item.TrackingMode == TrackingMode.Bodyweight));
        Assert.Equal(TrackingMode.Assisted, Assert.Single(items, item => item.Name == "Assisted Pull-Up").TrackingMode);
        Assert.All(items.Where(item => item.Name != "Assisted Pull-Up" && !BodyweightNames.Contains(item.Name)), item => Assert.Equal(TrackingMode.Weighted, item.TrackingMode));
        Assert.All(items, item => Assert.Equal(ExerciseImageReviewState.Draft, item.ReviewState));
        Assert.All(items, item => Assert.Equal(ExerciseManifest.AiGeneratedProjectOwnedDraftSourceReference, item.SourceReference));
    }

    [Theory]
    [InlineData("\"version\": 1", "\"version\": 2")]
    [InlineData("\"trackingMode\": \"Weighted\"", "\"trackingMode\": 1")]
    [InlineData("\"imagePath\": \"assets/exercises/images/barbell-bench-press.png\"", "\"imagePath\": \"../barbell-bench-press.png\"")]
    [InlineData("\"version\": 1,", "\"version\": 1, \"unexpected\": true,")]
    public void Loader_rejects_bad_schema_enums_paths_and_unknown_fields(string oldValue, string newValue)
    {
        var path = WriteModifiedCatalog(source => source.Replace(oldValue, newValue, StringComparison.Ordinal));
        try
        {
            Assert.Throws<InvalidDataException>(() => ExerciseManifest.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loader_rejects_case_insensitive_slug_and_path_collisions()
    {
        var path = WriteModifiedCatalog(source => source.Replace(
            "\"slug\": \"incline-barbell-bench-press\", \"imagePath\": \"assets/exercises/images/incline-barbell-bench-press.png\"",
            "\"slug\": \"barbell-bench-press\", \"imagePath\": \"assets/exercises/images/barbell-bench-press.png\"",
            StringComparison.Ordinal));
        try
        {
            Assert.Throws<InvalidDataException>(() => ExerciseManifest.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loader_rejects_a_name_to_body_part_swap_that_preserves_group_counts()
    {
        var path = WriteModifiedCatalog(source => source
            .Replace("\"name\": \"Barbell Bench Press\", \"bodyPart\": \"Chest\"", "\"name\": \"Barbell Bench Press\", \"bodyPart\": \"Back\"", StringComparison.Ordinal)
            .Replace("\"name\": \"Lat Pulldown\", \"bodyPart\": \"Back\"", "\"name\": \"Lat Pulldown\", \"bodyPart\": \"Chest\"", StringComparison.Ordinal));
        try
        {
            Assert.Throws<InvalidDataException>(() => ExerciseManifest.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loader_rejects_an_unsupported_artwork_provenance()
    {
        var path = WriteModifiedCatalog(source => source.Replace(
            "\"sourceReference\": \"ai-generated-project-owned-draft\"",
            "\"sourceReference\": \"unverified-third-party-image\"",
            StringComparison.Ordinal));
        try
        {
            Assert.Throws<InvalidDataException>(() => ExerciseManifest.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Prompt_and_review_artifacts_name_each_draft_exercise_and_expose_required_human_fields()
    {
        var items = ExerciseManifest.Load(CatalogPath);
        var promptTemplate = File.ReadAllText(Path.Combine(RepositoryRoot, "assets", "exercises", "prompt-template.md"));
        var checklist = File.ReadAllText(Path.Combine(RepositoryRoot, "assets", "exercises", "review-checklist.md"));

        Assert.Contains("polished grayscale scientific anatomy illustration", promptTemplate, StringComparison.Ordinal);
        Assert.Contains("no labels/text/numbers/logos/watermarks", promptTemplate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Equipment", promptTemplate, StringComparison.Ordinal);
        Assert.Contains("Grip / stance", promptTemplate, StringComparison.Ordinal);
        Assert.Contains("Joint path", promptTemplate, StringComparison.Ordinal);
        Assert.Contains("Target muscle", promptTemplate, StringComparison.Ordinal);
        Assert.Contains("Failure to avoid", promptTemplate, StringComparison.Ordinal);
        Assert.Contains("Anatomy (owner)", checklist, StringComparison.Ordinal);
        Assert.Contains("Movement (owner)", checklist, StringComparison.Ordinal);
        Assert.Contains("Equipment/grip (owner)", checklist, StringComparison.Ordinal);
        Assert.Contains("Target muscle (owner)", checklist, StringComparison.Ordinal);
        Assert.Contains("Originality (owner)", checklist, StringComparison.Ordinal);
        Assert.Contains("Rights (owner)", checklist, StringComparison.Ordinal);
        Assert.Contains("Pass/fail", checklist, StringComparison.Ordinal);
        Assert.Contains("Reviewer", checklist, StringComparison.Ordinal);
        Assert.Contains("Reviewed-at", checklist, StringComparison.Ordinal);
        Assert.Contains("Correction notes", checklist, StringComparison.Ordinal);
        Assert.All(items, item =>
        {
            Assert.Contains(item.Name, promptTemplate, StringComparison.Ordinal);
            Assert.Contains($"| {item.Name} |", checklist, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Category", "Asset")]
    public void Catalog_assets_are_dedicated_square_rgba_pngs_with_unique_content()
    {
        var items = ExerciseManifest.Load(CatalogPath);
        ExerciseManifest.ValidateAssets(CatalogPath, items);
        var hashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var path = Path.Combine(RepositoryRoot, item.ImagePath);
            using var image = Image.Load<Rgba32>(path);
            Assert.Equal("PNG", image.Metadata.DecodedImageFormat?.Name);
            Assert.Equal(1024, image.Width);
            Assert.Equal(1024, image.Height);
            Assert.Equal(32, image.PixelType.BitsPerPixel);
            Assert.True(hashes.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))), $"Artwork is reused for '{item.Name}'.");
        }
    }

    private static string CatalogPath => Path.Combine(RepositoryRoot, "assets", "exercises", "catalog.json");

    private static readonly string[] ExpectedNames =
    [
        "Barbell Bench Press", "Incline Barbell Bench Press", "Dumbbell Bench Press", "Incline Dumbbell Press", "Chest Press Machine", "Cable Fly", "Pec Deck Fly", "Decline Push-Up",
        "Lat Pulldown", "Pull-Up", "Assisted Pull-Up", "Seated Cable Row", "Chest-Supported Row", "Barbell Row", "One-Arm Dumbbell Row", "Straight-Arm Pulldown",
        "Overhead Press", "Dumbbell Shoulder Press", "Machine Shoulder Press", "Lateral Raise", "Cable Lateral Raise", "Rear Delt Fly", "Face Pull", "Upright Row",
        "Barbell Curl", "Dumbbell Curl", "Hammer Curl", "Preacher Curl", "Triceps Pushdown", "Overhead Triceps Extension", "Skull Crusher", "Close-Grip Bench Press",
        "Back Squat", "Front Squat", "Leg Press", "Romanian Deadlift", "Leg Extension", "Seated Leg Curl", "Bulgarian Split Squat", "Standing Calf Raise",
        "Cable Crunch", "Hanging Knee Raise", "Hanging Leg Raise", "Ab Wheel Rollout", "Weighted Sit-Up", "Decline Sit-Up", "Reverse Crunch", "Pallof Press"
    ];

    private static readonly Guid[] ExpectedIds =
    [
        Guid.Parse("56f212f7-36b4-582a-9283-2cbb8fb264ab"), Guid.Parse("e29389d6-410c-54e8-ab5c-3cc7d46c16a6"), Guid.Parse("33fa9f22-14e1-594f-895d-fcfe787f2ecb"), Guid.Parse("da586c59-a2a3-5f0e-bc5b-88a1d87995ec"), Guid.Parse("3986189b-a15a-5edd-a75b-9858ab92036a"), Guid.Parse("6aa13394-7524-54b5-9592-a069d2e5d546"), Guid.Parse("c98582ee-ea95-5952-93cd-7bb5fbbac045"), Guid.Parse("0db22351-22f7-572f-bf15-f350cf6ac3be"),
        Guid.Parse("80cf4c4d-420b-55b1-b534-28508a4a9cec"), Guid.Parse("e4839715-3681-5114-bae5-c1869fade857"), Guid.Parse("ab9756e0-108e-5173-9b6d-f490c4af0c2b"), Guid.Parse("b02f9864-83b8-5153-9211-a5f2e5d40c07"), Guid.Parse("0ee82fa3-79ac-5462-911c-541d34d4be3c"), Guid.Parse("4d4f3adc-6375-5d31-8e22-09e5a1125886"), Guid.Parse("c2b2ade3-0aef-5d20-9345-ce529ee998c3"), Guid.Parse("a395dbbb-110a-5736-96fa-ac4a298b4eaf"),
        Guid.Parse("50c26394-d234-5035-acad-ec5070b5e3f3"), Guid.Parse("ba22ebb8-4b56-50d5-b519-83d73fb1c5db"), Guid.Parse("2a79202c-13a2-5948-9000-bbff06a0d46e"), Guid.Parse("b7ff1daf-8cd1-513b-9d3e-de5c5f42b0aa"), Guid.Parse("f591f5c1-ce2f-5daf-8c1e-06225f9f7c1f"), Guid.Parse("abfa9b25-fec6-515e-8345-f64ae736084c"), Guid.Parse("59278fde-da50-5c36-8f98-e68b172bbe43"), Guid.Parse("c3a059eb-dcd8-5eeb-a720-61f567fe276a"),
        Guid.Parse("2af9cf0e-7441-58bf-9cea-da3b9e9611d1"), Guid.Parse("3810d1d8-a57a-5803-a110-fdaa4f6641af"), Guid.Parse("bb745a4b-5f47-5b42-afff-31116187ce98"), Guid.Parse("422ff241-deb5-583b-93c1-81e21745baac"), Guid.Parse("5151ef3b-cc64-54f6-a880-7f343fe4c997"), Guid.Parse("1dd57614-9adc-5fec-ac8d-f7aaa324cd58"), Guid.Parse("56c72ada-593c-584b-979e-0250ef0ad5a6"), Guid.Parse("e4ac3fe2-a2e4-5de1-9134-10d56159788e"),
        Guid.Parse("6adac5f8-1e71-5aeb-83ce-4105c7a00b8a"), Guid.Parse("0e50233c-482c-58ff-a58d-5a8516590ba9"), Guid.Parse("dc104673-de77-536b-9284-d707e13aa4fb"), Guid.Parse("884b9d13-1577-5fe1-bdc4-2b69cd281380"), Guid.Parse("b11d97ff-e793-50b3-b8a7-4ade5e74b11c"), Guid.Parse("5f70df5c-ebd2-5414-947d-b98e4e23c95e"), Guid.Parse("84473551-deb5-542d-afea-11b17a01b7f4"), Guid.Parse("cb8a6255-4e72-5fa1-8fe7-6d89ae60544d"),
        Guid.Parse("915932ce-b948-5d5f-becd-de0bf5f4a9a2"), Guid.Parse("f8a24551-0cbe-5509-af38-05ff3131db94"), Guid.Parse("e272e64a-bd32-5287-b207-155bd1dea34f"), Guid.Parse("1cd9d8ae-cb7b-57b7-9445-db5952712354"), Guid.Parse("f321c70a-023a-51e8-a85d-cdaa87d51f20"), Guid.Parse("41ff5bf8-2a64-5d50-96ae-b9c5216b7126"), Guid.Parse("73bfc117-c21e-5789-be65-073126c57180"), Guid.Parse("33a08f1f-4dbf-592b-97e3-901d7f006cec")
    ];

    private static readonly BodyPart[] ExpectedBodyParts =
    [
        BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest,
        BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back,
        BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders,
        BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms,
        BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs,
        BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core
    ];

    private static readonly HashSet<string> BodyweightNames = new(StringComparer.Ordinal)
    {
        "Pull-Up", "Decline Push-Up", "Hanging Knee Raise", "Hanging Leg Raise", "Ab Wheel Rollout", "Decline Sit-Up", "Reverse Crunch"
    };

    private static string WriteModifiedCatalog(Func<string, string> modify)
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackz-catalog-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, modify(File.ReadAllText(CatalogPath)));
        return path;
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the TrackZ repository root.");
        }
    }
}
