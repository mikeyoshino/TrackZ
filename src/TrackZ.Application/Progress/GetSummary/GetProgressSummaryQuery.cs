using MediatR;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;

namespace TrackZ.Application.Progress.GetSummary;

public sealed record GetProgressSummaryQuery : IRequest<ProgressSummaryDto>;

public interface IProgressReadStore
{
    Task<ProgressSummaryDto> GetProgressSummaryAsync(Guid userId, CancellationToken cancellationToken);
    Task<GamificationProfileDto> GetGamificationProfileAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class GetProgressSummaryHandler(IProgressReadStore store, ICurrentUser currentUser)
    : IRequestHandler<GetProgressSummaryQuery, ProgressSummaryDto>
{
    public Task<ProgressSummaryDto> Handle(
        GetProgressSummaryQuery request,
        CancellationToken cancellationToken) =>
        store.GetProgressSummaryAsync(currentUser.UserId, cancellationToken);
}
