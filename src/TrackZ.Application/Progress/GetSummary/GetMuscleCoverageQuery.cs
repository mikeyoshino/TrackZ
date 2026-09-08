using MediatR;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Domain.Muscles;

namespace TrackZ.Application.Progress.GetSummary;

public sealed record GetMuscleCoverageQuery(DateOnly? Week = null) : IRequest<MuscleCoverageReport>;
public interface IMuscleCoverageReadStore
{
    Task<MuscleCoverageReport> GetAsync(Guid userId, DateOnly? week, CancellationToken cancellationToken);
}
public sealed class GetMuscleCoverageHandler(IMuscleCoverageReadStore store, ICurrentUser currentUser)
    : IRequestHandler<GetMuscleCoverageQuery, MuscleCoverageReport>
{
    public Task<MuscleCoverageReport> Handle(GetMuscleCoverageQuery request, CancellationToken cancellationToken) =>
        store.GetAsync(currentUser.UserId, request.Week, cancellationToken);
}
