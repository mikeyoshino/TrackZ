using MediatR;
using TrackZ.Application.Identity.Common;

namespace TrackZ.Application.Identity.Login;

public sealed record LoginCommand(string Email, string Password, string DeviceName) : IRequest<AuthTokenPair>;
