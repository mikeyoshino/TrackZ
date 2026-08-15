namespace TrackZ.Application.Media;

/// <summary>
/// Signals that an accepted-upload transaction reached the commit phase, but the caller could
/// not determine whether PostgreSQL made the transition durable.
/// </summary>
public sealed class UploadTransitionCommitAmbiguousException(Exception innerException)
    : Exception("The accepted upload commit outcome is ambiguous.", innerException);
