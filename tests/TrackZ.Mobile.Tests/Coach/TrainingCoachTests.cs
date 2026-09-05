using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Coach;

namespace TrackZ.Mobile.Tests.Coach;

public sealed class TrainingCoachTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Two_distinct_easy_controlled_sessions_offer_one_rep_without_changing_load()
    {
        var result = TrainingCoachPolicy.Evaluate([Session(-4), Session(0)], Now);
        Assert.Equal(CoachAction.AddRep, result.Action);
        Assert.Equal(11, result.Reps);
        Assert.Equal(5m, result.WeightKg);
    }

    [Fact]
    public void Multiple_sets_from_one_session_are_not_two_training_sessions()
    {
        var session = Session(0);
        Assert.NotEqual(CoachAction.AddRep, TrainingCoachPolicy.Evaluate([session, session], Now).Action);
    }

    [Theory]
    [InlineData(null, false, 1)]
    [InlineData(false, false, 1)]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 3)]
    public void Missing_control_pain_or_hard_effort_never_increases(bool? controlled, bool pain, int effort)
    {
        var latest = Session(0) with { Controlled = controlled, Pain = pain, Effort = effort };
        var result = TrainingCoachPolicy.Evaluate([Session(-4), latest], Now);
        Assert.DoesNotContain(result.Action, new[] { CoachAction.AddRep, CoachAction.AddWeight });
    }

    [Fact]
    public void Stale_or_different_load_or_different_set_counts_are_not_comparable()
    {
        foreach (var earlier in new[] { Session(-30), Session(-4) with { WeightKg = 10m }, Session(-4) with { WorkingSets = 2 } })
            Assert.NotEqual(CoachAction.AddRep, TrainingCoachPolicy.Evaluate([earlier, Session(0)], Now).Action);
    }

    [Fact]
    public void Declining_repetitions_do_not_trigger_an_increase()
    {
        var result = TrainingCoachPolicy.Evaluate([Session(-4) with { Reps = 12 }, Session(0)], Now);
        Assert.False(result.IsIncrease);
    }

    [Fact]
    public void Upper_rep_range_requires_a_known_increment()
    {
        var sessions = new[] { Session(-4) with { Reps = 12 }, Session(0) with { Reps = 12 } };
        Assert.Equal(CoachAction.ChooseIncrement, TrainingCoachPolicy.Evaluate(sessions, Now).Action);
        var result = TrainingCoachPolicy.Evaluate(sessions, Now, 1.25m);
        Assert.Equal(CoachAction.AddWeight, result.Action);
        Assert.Equal(6.25m, result.WeightKg);
        Assert.Equal(8, result.Reps);
    }

    [Fact]
    public void Volume_warning_needs_a_personal_baseline_and_is_not_a_fixed_set_limit()
    {
        Assert.False(TrainingCoachPolicy.NeedsRecoveryCheck(30, []));
        Assert.False(TrainingCoachPolicy.NeedsRecoveryCheck(18, [18, 18, 18]));
        Assert.True(TrainingCoachPolicy.NeedsRecoveryCheck(18, [10, 10, 10]));
        Assert.False(TrainingCoachPolicy.NeedsRecoveryCheck(11, [10, 10, 10]));
    }

    private static CoachSession Session(int days) => new(
        Guid.NewGuid(), Guid.Parse("ea6fbf31-2aa0-4ef0-9380-77c7f29d81a3"), Now.AddDays(days),
        TrackingMode.Weighted, 5m, null, 10, 3, 1, true, false, false);
}
