using TrackZ.Domain.Exercises;
using TrackZ.Infrastructure.Persistence.Seed;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TrackZ.Infrastructure.Tests.Seed;

public sealed class ExerciseCatalogManifestTests
{
    [Fact]
    public void Catalog_contains_90_unique_exercises_with_15_in_every_body_part()
    {
        var items = ExerciseManifest.Load(CatalogPath);

        Assert.Equal(90, items.Count);
        Assert.Equal(90, items.Select(item => item.Id).Distinct().Count());
        Assert.All(Enum.GetValues<BodyPart>(), part => Assert.Equal(15, items.Count(item => item.BodyPart == part)));
    }

    [Fact]
    public void Catalog_preserves_the_canonical_order_names_groups_modes_and_uuid_snapshot()
    {
        var items = ExerciseManifest.Load(CatalogPath);

        Assert.Equal(ExpectedNames, items.Select(item => item.Name));
        Assert.Equal(ExpectedIds, items.Select(item => item.Id));
        Assert.Equal(ExpectedBodyParts, items.Select(item => item.BodyPart));
        Assert.Equal(BodyweightNames.Count, items.Count(item => item.TrackingMode == TrackingMode.Bodyweight));
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
        "Push-Up", "Chest Dip", "Decline Barbell Bench Press", "Smith Machine Bench Press", "Dumbbell Fly", "Low-to-High Cable Fly", "High-to-Low Cable Fly",
        "Lat Pulldown", "Pull-Up", "Assisted Pull-Up", "Seated Cable Row", "Chest-Supported Row", "Barbell Row", "One-Arm Dumbbell Row", "Straight-Arm Pulldown",
        "Conventional Deadlift", "T-Bar Row", "Inverted Row", "Neutral-Grip Lat Pulldown", "Wide-Grip Lat Pulldown", "Single-Arm Cable Row", "Machine High Row",
        "Overhead Press", "Dumbbell Shoulder Press", "Machine Shoulder Press", "Lateral Raise", "Cable Lateral Raise", "Rear Delt Fly", "Face Pull", "Upright Row",
        "Arnold Press", "Dumbbell Front Raise", "Cable Front Raise", "Bent-Over Reverse Fly", "Reverse Pec Deck", "Landmine Press", "Dumbbell Shrug",
        "Barbell Curl", "Dumbbell Curl", "Hammer Curl", "Preacher Curl", "Triceps Pushdown", "Overhead Triceps Extension", "Skull Crusher", "Close-Grip Bench Press",
        "EZ-Bar Curl", "Incline Dumbbell Curl", "Cable Curl", "Concentration Curl", "Bench Dip", "Triceps Dip", "Single-Arm Cable Pushdown",
        "Back Squat", "Front Squat", "Leg Press", "Romanian Deadlift", "Leg Extension", "Seated Leg Curl", "Bulgarian Split Squat", "Standing Calf Raise",
        "Goblet Squat", "Hack Squat", "Sumo Deadlift", "Walking Lunge", "Hip Thrust", "Lying Leg Curl", "Seated Calf Raise",
        "Cable Crunch", "Hanging Knee Raise", "Hanging Leg Raise", "Ab Wheel Rollout", "Weighted Sit-Up", "Decline Sit-Up", "Reverse Crunch", "Pallof Press",
        "Plank", "Side Plank", "Dead Bug", "Bird Dog", "Russian Twist", "Bicycle Crunch", "Mountain Climber"
    ];

    private static readonly Guid[] ExpectedIds =
    [
        Guid.Parse("56f212f7-36b4-582a-9283-2cbb8fb264ab"), Guid.Parse("e29389d6-410c-54e8-ab5c-3cc7d46c16a6"), Guid.Parse("33fa9f22-14e1-594f-895d-fcfe787f2ecb"), Guid.Parse("da586c59-a2a3-5f0e-bc5b-88a1d87995ec"), Guid.Parse("3986189b-a15a-5edd-a75b-9858ab92036a"), Guid.Parse("6aa13394-7524-54b5-9592-a069d2e5d546"), Guid.Parse("c98582ee-ea95-5952-93cd-7bb5fbbac045"), Guid.Parse("0db22351-22f7-572f-bf15-f350cf6ac3be"),
        Guid.Parse("6fee6f1f-edcb-471e-8ab9-49855f3aa4b6"), Guid.Parse("f42f83b8-c38a-489b-808c-01f0ad3dfb45"), Guid.Parse("b7952b0f-7427-49ac-8464-6144a4dcceb3"), Guid.Parse("2d2a2222-3149-4510-a823-008aad084be8"), Guid.Parse("b1b38714-7d84-4044-a318-e0507ecc2a9b"), Guid.Parse("dc85a3f4-6737-4c91-9746-822c6335fc69"), Guid.Parse("107d60c7-5436-4fa4-bd46-d282c8f9a3ef"),
        Guid.Parse("80cf4c4d-420b-55b1-b534-28508a4a9cec"), Guid.Parse("e4839715-3681-5114-bae5-c1869fade857"), Guid.Parse("ab9756e0-108e-5173-9b6d-f490c4af0c2b"), Guid.Parse("b02f9864-83b8-5153-9211-a5f2e5d40c07"), Guid.Parse("0ee82fa3-79ac-5462-911c-541d34d4be3c"), Guid.Parse("4d4f3adc-6375-5d31-8e22-09e5a1125886"), Guid.Parse("c2b2ade3-0aef-5d20-9345-ce529ee998c3"), Guid.Parse("a395dbbb-110a-5736-96fa-ac4a298b4eaf"),
        Guid.Parse("bac8064d-c02c-4989-8ed6-61f1a0264cc2"), Guid.Parse("3c629c78-4993-41a0-8489-13496c803109"), Guid.Parse("997da94a-ead9-455f-a33e-401cf4c58c65"), Guid.Parse("21d3b334-30a3-4ff7-bd65-2b209c1e1021"), Guid.Parse("086b42f9-7055-4c88-a96a-f30aeaab2ec3"), Guid.Parse("40046edc-3551-4d86-9b48-5240a1798e01"), Guid.Parse("ad9c1476-16c1-4577-8eb8-f611c068cb0a"),
        Guid.Parse("50c26394-d234-5035-acad-ec5070b5e3f3"), Guid.Parse("ba22ebb8-4b56-50d5-b519-83d73fb1c5db"), Guid.Parse("2a79202c-13a2-5948-9000-bbff06a0d46e"), Guid.Parse("b7ff1daf-8cd1-513b-9d3e-de5c5f42b0aa"), Guid.Parse("f591f5c1-ce2f-5daf-8c1e-06225f9f7c1f"), Guid.Parse("abfa9b25-fec6-515e-8345-f64ae736084c"), Guid.Parse("59278fde-da50-5c36-8f98-e68b172bbe43"), Guid.Parse("c3a059eb-dcd8-5eeb-a720-61f567fe276a"),
        Guid.Parse("1f6193b6-c9b2-49d5-855c-b200be184005"), Guid.Parse("9a1467c5-6d4b-48ab-ae55-0f55d656388f"), Guid.Parse("66452088-fada-4505-8efe-8e1f40697244"), Guid.Parse("f5e26454-3182-4b22-93e0-ebf9eddb1197"), Guid.Parse("7778206a-d9fd-47b1-86ff-54f9dbfb956e"), Guid.Parse("ad64162d-cea5-4634-9a4f-56516c35a8f5"), Guid.Parse("bb7d4225-6d07-48fa-ab6a-7086e9ca98c1"),
        Guid.Parse("2af9cf0e-7441-58bf-9cea-da3b9e9611d1"), Guid.Parse("3810d1d8-a57a-5803-a110-fdaa4f6641af"), Guid.Parse("bb745a4b-5f47-5b42-afff-31116187ce98"), Guid.Parse("422ff241-deb5-583b-93c1-81e21745baac"), Guid.Parse("5151ef3b-cc64-54f6-a880-7f343fe4c997"), Guid.Parse("1dd57614-9adc-5fec-ac8d-f7aaa324cd58"), Guid.Parse("56c72ada-593c-584b-979e-0250ef0ad5a6"), Guid.Parse("e4ac3fe2-a2e4-5de1-9134-10d56159788e"),
        Guid.Parse("f47534bf-95b5-4b2d-88e6-57c82ab21dc2"), Guid.Parse("1ed6c327-00bf-4246-8d63-44174a07886d"), Guid.Parse("29eac18e-b075-4695-ad07-217377dc49f3"), Guid.Parse("96b626cf-45fc-4d23-8f97-150c9c21626d"), Guid.Parse("5129fab6-7a4f-4942-bf87-336a9af33331"), Guid.Parse("71528cbf-4420-41f4-953d-ea042e6f21ad"), Guid.Parse("f31fdd3c-78f9-41e4-ba2c-fa893fb2870f"),
        Guid.Parse("6adac5f8-1e71-5aeb-83ce-4105c7a00b8a"), Guid.Parse("0e50233c-482c-58ff-a58d-5a8516590ba9"), Guid.Parse("dc104673-de77-536b-9284-d707e13aa4fb"), Guid.Parse("884b9d13-1577-5fe1-bdc4-2b69cd281380"), Guid.Parse("b11d97ff-e793-50b3-b8a7-4ade5e74b11c"), Guid.Parse("5f70df5c-ebd2-5414-947d-b98e4e23c95e"), Guid.Parse("84473551-deb5-542d-afea-11b17a01b7f4"), Guid.Parse("cb8a6255-4e72-5fa1-8fe7-6d89ae60544d"),
        Guid.Parse("b3127b81-a08c-4310-862a-063398f96457"), Guid.Parse("66ba962e-bd16-4b50-a7de-d6675a956603"), Guid.Parse("470f531a-7530-483a-b3fb-6c2c8fbc34ae"), Guid.Parse("0e798fcf-5477-445c-a014-cc8956a4f839"), Guid.Parse("1edfb74a-5cdb-4994-a3d8-e6f21f930123"), Guid.Parse("15f2871e-b705-4865-9950-8e0fef86b97a"), Guid.Parse("fb06d513-49ed-4bd7-80c0-e77a6942859f"),
        Guid.Parse("915932ce-b948-5d5f-becd-de0bf5f4a9a2"), Guid.Parse("f8a24551-0cbe-5509-af38-05ff3131db94"), Guid.Parse("e272e64a-bd32-5287-b207-155bd1dea34f"), Guid.Parse("1cd9d8ae-cb7b-57b7-9445-db5952712354"), Guid.Parse("f321c70a-023a-51e8-a85d-cdaa87d51f20"), Guid.Parse("41ff5bf8-2a64-5d50-96ae-b9c5216b7126"), Guid.Parse("73bfc117-c21e-5789-be65-073126c57180"), Guid.Parse("33a08f1f-4dbf-592b-97e3-901d7f006cec"),
        Guid.Parse("21af3244-02ae-4b55-95de-38691b470ba0"), Guid.Parse("c283aac1-6aa3-499e-b654-f7e6d989c821"), Guid.Parse("44a13be4-4567-4571-a023-3dbf206e36fa"), Guid.Parse("c5f1b776-f16a-4c88-96aa-ec1f4728980b"), Guid.Parse("86ddc383-11df-4a33-bff2-0b9eadd21c86"), Guid.Parse("09faa319-d24b-4df4-a53b-024ade496a61"), Guid.Parse("a7eabd25-cb93-4aa3-9134-f59f7022ba6b")
    ];

    private static readonly BodyPart[] ExpectedBodyParts =
    [
        BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest,
        BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest, BodyPart.Chest,
        BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back,
        BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back, BodyPart.Back,
        BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders,
        BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders, BodyPart.Shoulders,
        BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms,
        BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms, BodyPart.Arms,
        BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs,
        BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs, BodyPart.Legs,
        BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core,
        BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core, BodyPart.Core
    ];

    private static readonly HashSet<string> BodyweightNames = new(StringComparer.Ordinal)
    {
        "Pull-Up", "Decline Push-Up", "Hanging Knee Raise", "Hanging Leg Raise", "Ab Wheel Rollout", "Decline Sit-Up", "Reverse Crunch",
        "Push-Up", "Chest Dip", "Inverted Row", "Bench Dip", "Triceps Dip", "Plank", "Side Plank", "Dead Bug", "Bird Dog", "Bicycle Crunch", "Mountain Climber"
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
