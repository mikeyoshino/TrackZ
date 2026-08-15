using TrackZ.Domain.Workouts;

namespace TrackZ.Domain.Tests.Architecture;

public sealed class WorkoutDomainArchitectureTests
{
    [Fact]
    public void Workout_rule_failures_are_owned_by_the_domain_assembly()
    {
        Assert.Equal(typeof(WorkoutSession).Assembly, typeof(WorkoutRuleException).Assembly);
        Assert.Equal(typeof(WorkoutSession).Assembly, typeof(WorkoutRuleViolation).Assembly);
        Assert.Equal("TrackZ.Domain", typeof(WorkoutRuleException).Assembly.GetName().Name);
    }
}
