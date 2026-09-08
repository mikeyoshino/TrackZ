using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Progress;

namespace TrackZ.Mobile.Tests.Coach;

public sealed class MuscleCoverageSourceTests
{
    [Fact]
    public void Canonical_exercise_keeps_coverage_when_its_cache_entry_is_missing()
    {
        var now = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        var id = Guid.Parse("56f212f7-36b4-582a-9283-2cbb8fb264ab");
        var set = new LocalSet(30, null, 10) with { CompletedAt = now, IsWarmup = false };
        var e = new LocalWorkoutExercise(Guid.NewGuid(), Guid.NewGuid(), id, TrackingMode.Weighted, 0, null, 1, 1, [set]);
        var w = new LocalWorkout(e.WorkoutId, LocalWorkoutStatus.Active, now, null, null, 1, 1, [e]);
        var facts = MuscleCoverageSource.Facts([w], [], CoachJournalData.Empty(), now);
        Assert.False(Assert.Single(facts).IsCustom);
    }
    [Fact]
    public void Deleted_workouts_exercises_and_sets_are_excluded_but_active_sets_are_included()
    {
        var now = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        var id = Guid.Parse("56f212f7-36b4-582a-9283-2cbb8fb264ab");
        var set = new LocalSet(30, null, 10) with { CompletedAt = now, IsWarmup = false };
        var e = new LocalWorkoutExercise(Guid.NewGuid(), Guid.NewGuid(), id, TrackingMode.Weighted, 0, null, 1, 1, [set]);
        var w = new LocalWorkout(e.WorkoutId, LocalWorkoutStatus.Active, now, null, null, 1, 1, [e]);
        var defs = new[] { new CachedExercise { Id = id, Name = "Barbell Bench Press", BodyPart = BodyPart.Chest, TrackingMode = TrackingMode.Weighted } };
        var facts = MuscleCoverageSource.Facts([w, w with { Id = Guid.NewGuid(), DeletedAt = now },
            w with { Id = Guid.NewGuid(), Exercises = [e with { DeletedAt = now }] },
            w with { Id = Guid.NewGuid(), Exercises = [e with { Sets = [set with { DeletedAt = now }] }] }], defs, CoachJournalData.Empty(), now);
        Assert.Single(facts);
        Assert.Equal(false, facts[0].IsWarmup);
    }
}
