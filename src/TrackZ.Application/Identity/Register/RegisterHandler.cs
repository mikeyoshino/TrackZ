using MediatR;
using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Identity;
using TrackZ.Domain.Identity;

namespace TrackZ.Application.Identity.Register;

public sealed class RegisterHandler(IAppDbContext db, IPasswordHasher passwordHasher)
    : IRequestHandler<RegisterCommand, RegisteredUser>
{
    public async Task<RegisteredUser> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var email = NormalizeDisplayEmail(request.Email);
        ValidateEmail(email);
        ValidatePassword(request.Password);

        var normalizedEmail = email.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw EmailAlreadyExists();
        }

        var user = User.Create(email, passwordHasher.HashPassword(request.Password));
        await db.Users.AddAsync(user, cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException?.Message.Contains("IX_users_NormalizedEmail", StringComparison.Ordinal) == true)
        {
            // The database unique index is the final authority when concurrent registrations race.
            throw EmailAlreadyExists();
        }

        return new RegisteredUser(user.Id, user.Email);
    }

    private static string NormalizeDisplayEmail(string email) => email?.Trim() ?? string.Empty;

    private static void ValidateEmail(string email)
    {
        if (email.Length is 0 or > 320 || !email.Contains('@', StringComparison.Ordinal))
        {
            throw new BusinessException(
                BusinessErrorCode.InvalidRegistrationInput,
                "A valid email address is required.",
                400);
        }
    }

    private static void ValidatePassword(string password)
    {
        var isValid = password is { Length: >= 12 }
            && password.Any(char.IsUpper)
            && password.Any(char.IsLower)
            && password.Any(char.IsDigit)
            && password.Any(character => !char.IsLetterOrDigit(character));

        if (!isValid)
        {
            throw new BusinessException(
                BusinessErrorCode.PasswordPolicyViolation,
                "Password must be at least 12 characters and contain uppercase, lowercase, digit, and symbol characters.",
                400);
        }
    }

    private static BusinessException EmailAlreadyExists() => new(
        BusinessErrorCode.EmailAlreadyExists,
        "An account with this email already exists.",
        409);
}
