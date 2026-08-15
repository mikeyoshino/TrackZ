namespace TrackZ.Domain.Exercises;

public enum ImageUploadState { Pending = 1, Uploading = 2, Uploaded = 3, Processing = 4, Completed = 5, Failed = 6 }

public sealed class ImageUploadTicket
{
    private ImageUploadTicket() { }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid ExerciseDefinitionId { get; private set; }
    public string StagingObjectKey { get; private set; } = null!;
    public string DeclaredContentType { get; private set; } = null!;
    public long DeclaredLength { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public ImageUploadState State { get; private set; }
    public Guid? ExerciseImageId { get; private set; }
    public Guid? UploadLeaseId { get; private set; }
    public DateTimeOffset? UploadLeaseExpiresAt { get; private set; }
    public string? CleanupStagingObjectKey { get; private set; }
    public Guid? CleanupProcessingLeaseId { get; private set; }
    public DateTimeOffset? ProcessingStartedAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public Guid? ProcessingLeaseId { get; private set; }
    public byte[] ConcurrencyToken { get; private set; } = null!;

    public static ImageUploadTicket Create(Guid ownerId, Guid exerciseId, string stagingKey, string contentType, long length, DateTimeOffset expiresAt) => new()
    {
        Id = Guid.NewGuid(), OwnerId = ownerId, ExerciseDefinitionId = exerciseId, StagingObjectKey = stagingKey,
        DeclaredContentType = contentType, DeclaredLength = length, ExpiresAt = expiresAt.ToUniversalTime(), State = ImageUploadState.Pending, ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)
    };
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public bool TryClaimUpload(DateTimeOffset now, TimeSpan lease, out Guid uploadLeaseId, out string stagingKey)
    {
        uploadLeaseId = Guid.Empty;
        stagingKey = string.Empty;
        if (IsExpired(now) || (State != ImageUploadState.Pending && (State != ImageUploadState.Uploading || UploadLeaseExpiresAt is null || UploadLeaseExpiresAt > now))) return false;
        if (State == ImageUploadState.Uploading) CleanupStagingObjectKey = StagingObjectKey;
        uploadLeaseId = Guid.NewGuid();
        stagingKey = $"staging/{OwnerId:D}/{Id:D}/{uploadLeaseId:D}";
        State = ImageUploadState.Uploading;
        UploadLeaseId = uploadLeaseId;
        UploadLeaseExpiresAt = now.Add(lease);
        StagingObjectKey = stagingKey;
        Touch();
        return true;
    }
    // Kept for the domain's legacy transition tests; network callers must use the lease overload.
    public bool TryMarkUploaded(DateTimeOffset now)
    {
        if (State == ImageUploadState.Pending && !IsExpired(now))
        {
            State = ImageUploadState.Uploaded;
            Touch();
            return true;
        }
        return TryMarkUploaded(UploadLeaseId ?? Guid.Empty, now);
    }
    public bool TryMarkUploaded(Guid uploadLeaseId, DateTimeOffset now)
    {
        if (State != ImageUploadState.Uploading || IsExpired(now) || UploadLeaseId != uploadLeaseId || UploadLeaseExpiresAt <= now) return false;
        State = ImageUploadState.Uploaded;
        UploadLeaseId = null;
        UploadLeaseExpiresAt = null;
        Touch();
        return true;
    }
    public bool TryClaim(DateTimeOffset now, TimeSpan lease)
    {
        if (IsExpired(now) || (State != ImageUploadState.Uploaded && (State != ImageUploadState.Processing || LeaseExpiresAt is null || LeaseExpiresAt > now))) return false;
        if (State == ImageUploadState.Processing) CleanupProcessingLeaseId = ProcessingLeaseId;
        State = ImageUploadState.Processing; ProcessingStartedAt = now; LeaseExpiresAt = now.Add(lease); ProcessingLeaseId = Guid.NewGuid(); Touch(); return true;
    }
    public bool HasActiveLease(DateTimeOffset now) => State == ImageUploadState.Processing && LeaseExpiresAt is { } expiry && expiry > now;
    public bool IsClaimHeldBy(Guid leaseId, DateTimeOffset now) => HasActiveLease(now) && ProcessingLeaseId == leaseId;
    public void Complete(Guid imageId, Guid leaseId, DateTimeOffset now)
    {
        if (!IsClaimHeldBy(leaseId, now)) throw new InvalidOperationException();
        CleanupStagingObjectKey = StagingObjectKey;
        State = ImageUploadState.Completed; ExerciseImageId = imageId; ProcessingStartedAt = null; LeaseExpiresAt = null; ProcessingLeaseId = null; Touch();
    }
    public bool ReleaseForRetry(Guid leaseId)
    {
        if (State != ImageUploadState.Processing || ProcessingLeaseId != leaseId) return false;
        State = ImageUploadState.Uploaded; ProcessingStartedAt = null; LeaseExpiresAt = null; ProcessingLeaseId = null; Touch(); return true;
    }
    public bool Fail(Guid leaseId)
    {
        if (State != ImageUploadState.Processing || ProcessingLeaseId != leaseId) return false;
        CleanupStagingObjectKey = StagingObjectKey;
        CleanupProcessingLeaseId = ProcessingLeaseId;
        State = ImageUploadState.Failed; ProcessingStartedAt = null; LeaseExpiresAt = null; ProcessingLeaseId = null; Touch(); return true;
    }
    public void MarkCleanupComplete(string? stagingKey, Guid? processingLeaseId)
    {
        if (stagingKey is not null && CleanupStagingObjectKey == stagingKey) CleanupStagingObjectKey = null;
        if (processingLeaseId is not null && CleanupProcessingLeaseId == processingLeaseId) CleanupProcessingLeaseId = null;
        if (processingLeaseId is not null && State == ImageUploadState.Processing && ProcessingLeaseId == processingLeaseId)
        {
            // The worker only sees an expired processing lease. Once its token-scoped finals
            // are gone the staged bytes are retryable again under a fresh processing token.
            State = ImageUploadState.Uploaded;
            ProcessingStartedAt = null;
            LeaseExpiresAt = null;
            ProcessingLeaseId = null;
        }
        Touch();
    }
    private void Touch() => ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
}
