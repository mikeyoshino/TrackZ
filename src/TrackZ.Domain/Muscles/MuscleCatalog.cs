using System.Collections.Frozen;
using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Muscles;

public sealed record ExerciseMuscles(Guid ExerciseId, IReadOnlySet<string> Primary, IReadOnlySet<string> Secondary);

/// <summary>
/// Versioned, conservative exercise-region attribution for canonical catalogue IDs only.
/// Chest subdivisions are training regions, not three separate muscles. Secondary is never a fractional primary set.
/// No free-text inference for custom exercises. Changes require catalogue/coverage regression tests and content review.
/// </summary>
public static class MuscleCatalog
{
    public const int Version = 1;
    public static IReadOnlyList<MuscleRegion> Regions { get; } = Array.AsReadOnly<MuscleRegion>([
        new("chest-upper", BodyPart.Chest, "อกบน", "Upper chest"),
        new("chest-middle", BodyPart.Chest, "อกกลาง", "Mid chest"),
        new("chest-lower", BodyPart.Chest, "อกล่าง", "Lower chest"),
        new("lats", BodyPart.Back, "ปีกหลัง", "Lats"),
        new("upper-back", BodyPart.Back, "หลังส่วนบน", "Upper back"),
        new("lower-back", BodyPart.Back, "หลังส่วนล่าง", "Lower back"),
        new("shoulder-front", BodyPart.Shoulders, "ไหล่หน้า", "Front shoulders"),
        new("shoulder-side", BodyPart.Shoulders, "ไหล่ข้าง", "Side shoulders"),
        new("shoulder-rear", BodyPart.Shoulders, "ไหล่หลัง", "Rear shoulders"),
        new("biceps", BodyPart.Arms, "ต้นแขนด้านหน้า", "Front upper arms"),
        new("triceps", BodyPart.Arms, "ต้นแขนด้านหลัง", "Triceps"),
        new("forearms", BodyPart.Arms, "ปลายแขน", "Forearms"),
        new("quads", BodyPart.Legs, "ต้นขาด้านหน้า", "Front thighs"),
        new("hamstrings", BodyPart.Legs, "ต้นขาด้านหลัง", "Back thighs"),
        new("adductors", BodyPart.Legs, "ต้นขาด้านใน", "Inner thighs"),
        new("glutes", BodyPart.Legs, "ก้น", "Glutes"),
        new("lateral-hips", BodyPart.Legs, "สะโพกด้านข้าง", "Side hips"),
        new("calves", BodyPart.Legs, "น่อง", "Calves"),
        new("shins", BodyPart.Legs, "หน้าแข้ง", "Shins"),
        new("abs", BodyPart.Core, "หน้าท้อง", "Abdominals"),
        new("obliques", BodyPart.Core, "หน้าท้องด้านข้าง", "Obliques"),
        new("deep-core", BodyPart.Core, "แกนกลางลำตัว", "Deep core")
    ]);
    private static readonly FrozenDictionary<Guid, ExerciseMuscles> Exercises = Build();
    public static ExerciseMuscles? Find(Guid exerciseId) => Exercises.GetValueOrDefault(exerciseId);
    public static IEnumerable<Guid> Recommendations(string regionId) => Exercises.Values
        .Where(m => m.Primary.Contains(regionId)).Select(m => m.ExerciseId).Order();

    private static FrozenDictionary<Guid, ExerciseMuscles> Build()
    {
        var result = new Dictionary<Guid, ExerciseMuscles>();
        void Add(string primary, string secondary, string ids)
        {
            var p = primary.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet();
            var s = secondary.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet();
            if (p.Overlaps(s) || p.Concat(s).Any(id => !Regions.Any(r => r.Id == id)))
                throw new InvalidOperationException("Invalid muscle catalogue mapping.");
            foreach (var id in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse))
                result.Add(id, new(id, p, s));
        }
        // Chest: region emphasis is intentionally conservative; other chest regions can still participate.
        Add("chest-middle", "chest-upper chest-lower shoulder-front triceps",
            "56f212f7-36b4-582a-9283-2cbb8fb264ab 33fa9f22-14e1-594f-895d-fcfe787f2ecb 3986189b-a15a-5edd-a75b-9858ab92036a 6fee6f1f-edcb-471e-8ab9-49855f3aa4b6 2d2a2222-3149-4510-a823-008aad084be8");
        Add("chest-upper", "chest-middle chest-lower shoulder-front triceps",
            "e29389d6-410c-54e8-ab5c-3cc7d46c16a6 da586c59-a2a3-5f0e-bc5b-88a1d87995ec 0db22351-22f7-572f-bf15-f350cf6ac3be");
        Add("chest-lower", "chest-upper chest-middle shoulder-front triceps",
            "f42f83b8-c38a-489b-808c-01f0ad3dfb45 b7952b0f-7427-49ac-8464-6144a4dcceb3");
        Add("chest-middle", "chest-upper chest-lower shoulder-front",
            "6aa13394-7524-54b5-9592-a069d2e5d546 c98582ee-ea95-5952-93cd-7bb5fbbac045 b1b38714-7d84-4044-a318-e0507ecc2a9b");
        Add("chest-upper", "chest-middle chest-lower shoulder-front", "dc85a3f4-6737-4c91-9746-822c6335fc69");
        Add("chest-lower", "chest-middle chest-upper shoulder-front", "107d60c7-5436-4fa4-bd46-d282c8f9a3ef");
        Add("lats", "biceps upper-back forearms", "80cf4c4d-420b-55b1-b534-28508a4a9cec e4839715-3681-5114-bae5-c1869fade857 ab9756e0-108e-5173-9b6d-f490c4af0c2b 21d3b334-30a3-4ff7-bd65-2b209c1e1021 086b42f9-7055-4c88-a96a-f30aeaab2ec3");
        Add("lats upper-back", "biceps shoulder-rear forearms", "b02f9864-83b8-5153-9211-a5f2e5d40c07 0ee82fa3-79ac-5462-911c-541d34d4be3c 4d4f3adc-6375-5d31-8e22-09e5a1125886 c2b2ade3-0aef-5d20-9345-ce529ee998c3 3c629c78-4993-41a0-8489-13496c803109 997da94a-ead9-455f-a33e-401cf4c58c65 40046edc-3551-4d86-9b48-5240a1798e01 ad9c1476-16c1-4577-8eb8-f611c068cb0a");
        Add("lats", "triceps abs", "a395dbbb-110a-5736-96fa-ac4a298b4eaf");
        Add("glutes hamstrings", "quads lower-back forearms adductors", "bac8064d-c02c-4989-8ed6-61f1a0264cc2");
        Add("shoulder-front shoulder-side", "triceps upper-back", "50c26394-d234-5035-acad-ec5070b5e3f3 ba22ebb8-4b56-50d5-b519-83d73fb1c5db 2a79202c-13a2-5948-9000-bbff06a0d46e 1f6193b6-c9b2-49d5-855c-b200be184005");
        Add("shoulder-front shoulder-side", "triceps upper-back", SupplementalExercises.SeatedBarbellShoulderPressId.ToString());
        Add("shoulder-side", "upper-back", "b7ff1daf-8cd1-513b-9d3e-de5c5f42b0aa f591f5c1-ce2f-5daf-8c1e-06225f9f7c1f c3a059eb-dcd8-5eeb-a720-61f567fe276a");
        Add("shoulder-rear", "upper-back", "abfa9b25-fec6-515e-8345-f64ae736084c 59278fde-da50-5c36-8f98-e68b172bbe43 f5e26454-3182-4b22-93e0-ebf9eddb1197 7778206a-d9fd-47b1-86ff-54f9dbfb956e");
        Add("shoulder-front", "chest-upper", "9a1467c5-6d4b-48ab-ae55-0f55d656388f 66452088-fada-4505-8efe-8e1f40697244");
        Add("shoulder-front", "chest-upper triceps deep-core", "ad64162d-cea5-4634-9a4f-56516c35a8f5");
        Add("upper-back", "forearms", "bb7d4225-6d07-48fa-ab6a-7086e9ca98c1");
        Add("biceps", "forearms", "2af9cf0e-7441-58bf-9cea-da3b9e9611d1 3810d1d8-a57a-5803-a110-fdaa4f6641af bb745a4b-5f47-5b42-afff-31116187ce98 422ff241-deb5-583b-93c1-81e21745baac f47534bf-95b5-4b2d-88e6-57c82ab21dc2 1ed6c327-00bf-4246-8d63-44174a07886d 29eac18e-b075-4695-ad07-217377dc49f3 96b626cf-45fc-4d23-8f97-150c9c21626d");
        Add("triceps", "", "5151ef3b-cc64-54f6-a880-7f343fe4c997 1dd57614-9adc-5fec-ac8d-f7aaa324cd58 56c72ada-593c-584b-979e-0250ef0ad5a6 f31fdd3c-78f9-41e4-ba2c-fa893fb2870f");
        Add("triceps", "chest-middle chest-lower shoulder-front", "e4ac3fe2-a2e4-5de1-9134-10d56159788e 5129fab6-7a4f-4942-bf87-336a9af33331 71528cbf-4420-41f4-953d-ea042e6f21ad");
        Add("quads glutes", "adductors deep-core", "6adac5f8-1e71-5aeb-83ce-4105c7a00b8a 0e50233c-482c-58ff-a58d-5a8516590ba9 dc104673-de77-536b-9284-d707e13aa4fb b3127b81-a08c-4310-862a-063398f96457 66ba962e-bd16-4b50-a7de-d6675a956603");
        Add("quads glutes", "adductors lateral-hips hamstrings", "84473551-deb5-542d-afea-11b17a01b7f4 0e798fcf-5477-445c-a014-cc8956a4f839");
        Add("hamstrings glutes", "lower-back adductors forearms", "884b9d13-1577-5fe1-bdc4-2b69cd281380");
        Add("quads", "", "b11d97ff-e793-50b3-b8a7-4ade5e74b11c");
        Add("hamstrings", "calves", "5f70df5c-ebd2-5414-947d-b98e4e23c95e 15f2871e-b705-4865-9950-8e0fef86b97a");
        Add("calves", "", "cb8a6255-4e72-5fa1-8fe7-6d89ae60544d fb06d513-49ed-4bd7-80c0-e77a6942859f");
        Add("glutes quads adductors", "hamstrings lower-back forearms", "470f531a-7530-483a-b3fb-6c2c8fbc34ae");
        Add("glutes", "hamstrings adductors", "1edfb74a-5cdb-4994-a3d8-e6f21f930123");
        Add("abs", "obliques", "915932ce-b948-5d5f-becd-de0bf5f4a9a2 f8a24551-0cbe-5509-af38-05ff3131db94 e272e64a-bd32-5287-b207-155bd1dea34f f321c70a-023a-51e8-a85d-cdaa87d51f20 41ff5bf8-2a64-5d50-96ae-b9c5216b7126 73bfc117-c21e-5789-be65-073126c57180");
        Add("abs deep-core", "shoulder-front", "1cd9d8ae-cb7b-57b7-9445-db5952712354 21af3244-02ae-4b55-95de-38691b470ba0 44a13be4-4567-4571-a023-3dbf206e36fa");
        Add("obliques deep-core", "abs", "33a08f1f-4dbf-592b-97e3-901d7f006cec c283aac1-6aa3-499e-b654-f7e6d989c821");
        Add("deep-core", "glutes lower-back", "c5f1b776-f16a-4c88-96aa-ec1f4728980b");
        Add("obliques abs", "deep-core", "86ddc383-11df-4a33-bff2-0b9eadd21c86 09faa319-d24b-4df4-a53b-024ade496a61");
        Add("abs deep-core", "shoulder-front quads", "a7eabd25-cb93-4aa3-9134-f59f7022ba6b");
        return result.ToFrozenDictionary();
    }
}
