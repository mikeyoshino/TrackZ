namespace TrackZ.Contracts.Exercises;

public sealed record SignedMediaAccessDto(string Url, DateTimeOffset ExpiresAt);
