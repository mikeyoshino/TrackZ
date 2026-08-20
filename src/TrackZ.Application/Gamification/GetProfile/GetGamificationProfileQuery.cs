using MediatR;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Contracts.Gamification;

namespace TrackZ.Application.Gamification.GetProfile;

public sealed record GetGamificationProfileQuery : IRequest<GamificationProfileDto>;

public sealed class GetGamificationProfileHandler(IProgressReadStore store, ICurrentUser currentUser)
    : IRequestHandler<GetGamificationProfileQuery, GamificationProfileDto>
{
    public Task<GamificationProfileDto> Handle(
        GetGamificationProfileQuery request,
        CancellationToken cancellationToken) =>
        store.GetGamificationProfileAsync(currentUser.UserId, cancellationToken);
}
