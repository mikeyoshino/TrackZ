using MediatR;
using TrackZ.Contracts.Identity;

namespace TrackZ.Application.Identity.Register;

public sealed record RegisterCommand(string Email, string Password) : IRequest<RegisteredUser>;
