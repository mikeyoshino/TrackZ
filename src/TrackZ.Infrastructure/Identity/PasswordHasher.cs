using Microsoft.AspNetCore.Identity;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Infrastructure.Identity;

public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _hasher.HashPassword(new object(), password);
    }

    public bool VerifyPassword(string passwordHash, string password)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || password is null)
        {
            return false;
        }

        var result = _hasher.VerifyHashedPassword(new object(), passwordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
