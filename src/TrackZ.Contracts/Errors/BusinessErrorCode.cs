namespace TrackZ.Contracts.Errors;

public enum BusinessErrorCode
{
    InvalidCredentials = 10001,
    EmailAlreadyExists = 10002,
    RefreshTokenInvalid = 10003,
    EmailVerificationInvalid = 10004,
    PasswordResetInvalid = 10005,
    PasswordPolicyViolation = 10006,
    InvalidRegistrationInput = 10007,
    RateLimitExceeded = 10008,
    ExerciseNotFound = 20001,
    ExerciseNameDuplicate = 20002,
    WorkoutNotFound = 30001,
    WorkoutAlreadyCompleted = 30002,
    InvalidSetValue = 30004,
    ImageTooLarge = 50002,
    ImageTypeNotSupported = 50003,
    VersionConflict = 60001
}
