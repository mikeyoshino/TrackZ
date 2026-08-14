using MediatR;

namespace TrackZ.Application.Identity.Logout;

public sealed record LogoutCommand(Guid UserId, Guid SessionId) : IRequest;
