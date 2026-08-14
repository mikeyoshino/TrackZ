using MediatR;
using TrackZ.Application.Identity.Common;

namespace TrackZ.Application.Identity.Refresh;

public sealed record RefreshCommand(string RefreshToken, string DeviceName) : IRequest<AuthTokenPair>;
