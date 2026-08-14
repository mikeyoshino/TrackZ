using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Identity.Common;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Identity;

namespace TrackZ.Application.Identity.Login;

public sealed class LoginHandler : IRequestHandler<LoginCommand, AuthTokenPair>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly string _unknownUserPasswordHash;

    public LoginHandler(IAppDbContext db, IPasswordHasher passwordHasher, ITokenService tokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _unknownUserPasswordHash = passwordHasher.HashPassword("TrackZ-invalid-user-timing-password-42!");
    }

    public async Task<AuthTokenPair> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = (request.Email?.Trim() ?? string.Empty).ToUpperInvariant();
        var user = await _db.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        // Both unknown-user and wrong-password paths execute an expensive password verification.
        var passwordIsValid = _passwordHasher.VerifyPassword(
            user?.PasswordHash ?? _unknownUserPasswordHash,
            request.Password ?? string.Empty);

        if (user is null || !passwordIsValid)
        {
            throw InvalidCredentials();
        }

        if (string.IsNullOrWhiteSpace(request.DeviceName))
        {
            throw new BusinessException(
                BusinessErrorCode.InvalidRegistrationInput,
                "A device name is required.",
                400);
        }

        var sessionId = Guid.NewGuid();
        var tokenPair = _tokenService.CreateTokenPair(user.Id, sessionId);
        var refreshToken = RefreshToken.Create(
            user.Id,
            _tokenService.HashRefreshToken(tokenPair.RefreshToken),
            sessionId,
            _tokenService.GetRefreshTokenExpiration(),
            deviceName: request.DeviceName);

        await _db.AddRefreshTokenAsync(refreshToken, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return tokenPair;
    }

    private static BusinessException InvalidCredentials() => new(
        BusinessErrorCode.InvalidCredentials,
        "Invalid email or password.",
        401);
}
