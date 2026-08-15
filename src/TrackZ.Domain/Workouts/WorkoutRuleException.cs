namespace TrackZ.Domain.Workouts;

public enum WorkoutRuleViolation
{
    WorkoutAlreadyCompleted = 1,
    InvalidSetValue = 2
}

public sealed class WorkoutRuleException : InvalidOperationException
{
    public WorkoutRuleException(WorkoutRuleViolation violation, string message)
        : base(message)
    {
        Violation = violation;
    }

    public WorkoutRuleViolation Violation { get; }
}
