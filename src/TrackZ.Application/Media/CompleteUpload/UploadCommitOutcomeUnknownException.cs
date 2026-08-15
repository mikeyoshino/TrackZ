namespace TrackZ.Application.Media.CompleteUpload;

/// <summary>Signals that a database commit may have succeeded but its durable outcome could not be read.</summary>
public sealed class UploadCommitOutcomeUnknownException(Exception innerException)
    : Exception("The upload completion outcome is unknown; final objects were preserved for reconciliation.", innerException);
