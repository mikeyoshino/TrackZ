using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Workouts;

namespace TrackZ.Application.Workouts;

public static class WorkoutRuleExceptionMapper
{
    public static BusinessException ToBusinessException(WorkoutRuleException exception) => exception.Violation switch
    {
        WorkoutRuleViolation.WorkoutAlreadyCompleted => new BusinessException(
            BusinessErrorCode.WorkoutAlreadyCompleted,
            exception.Message,
            409),
        WorkoutRuleViolation.InvalidSetValue => new BusinessException(
            BusinessErrorCode.InvalidSetValue,
            exception.Message,
            400),
        _ => throw new ArgumentOutOfRangeException(nameof(exception), exception.Violation, "Unknown workout rule violation.")
    };
}
