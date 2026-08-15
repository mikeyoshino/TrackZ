using MediatR;
using TrackZ.Domain.Workouts;

namespace TrackZ.Application.Workouts;

public sealed class WorkoutRuleExceptionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (WorkoutRuleException exception)
        {
            throw WorkoutRuleExceptionMapper.ToBusinessException(exception);
        }
    }
}
