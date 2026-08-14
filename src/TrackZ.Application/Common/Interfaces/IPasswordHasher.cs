namespace TrackZ.Application.Common.Interfaces;

public interface IPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string passwordHash, string password);
}
