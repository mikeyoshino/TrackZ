namespace TrackZ.Domain.Exercises;

public enum ImageUploadState { Pending = 1, Uploading = 2, Uploaded = 3, Processing = 4, Completed = 5, Failed = 6, Expired = 7 }

public sealed class ImageUploadTicket
{
    private static readonly TimeSpan CleanupClaimLease = TimeSpan.FromMinutes(5);
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
    public Guid? CleanupClaimId { get; private set; }
    public DateTimeOffset? CleanupClaimedAt { get; private set; }
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
        if (IsExpired(now)
            || CleanupStagingObjectKey is not null
            || CleanupClaimId is not null
            || State is not (ImageUploadState.Pending or ImageUploadState.Uploading)) return false;
        if (State == ImageUploadState.Uploading)
        {
            if (UploadLeaseExpiresAt is null || UploadLeaseExpiresAt > now) return false;

            // Schedule the abandoned key durably, but do not allocate another key until the
            // worker has acknowledged its deletion. One slot can therefore never be overwritten
            // by repeated crashed upload attempts.
            CleanupStagingObjectKey = StagingObjectKey;
            State = ImageUploadState.Pending;
            UploadLeaseId = null;
            UploadLeaseExpiresAt = null;
            Touch();
            return false;
        }
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
        if (CleanupProcessingLeaseId is not null || CleanupStagingObjectKey is not null || IsExpired(now) || (State != ImageUploadState.Uploaded && (State != ImageUploadState.Processing || LeaseExpiresAt is null || LeaseExpiresAt > now))) return false;
        if (State == ImageUploadState.Processing)
        {
            // Do not hand an expired attempt to a new completion claim until its token-scoped
            // final-object namespace has been durably scheduled for cleanup.
            CleanupProcessingLeaseId = ProcessingLeaseId;
            CleanupStagingObjectKey = StagingObjectKey;
            State = ImageUploadState.Expired;
            ProcessingStartedAt = null; LeaseExpiresAt = null; ProcessingLeaseId = null;
            Touch();
            return false;
        }
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
    public bool ReleaseForRetry(Guid leaseId, bool retainAttemptForCleanup = false)
    {
        if (State != ImageUploadState.Processing || ProcessingLeaseId != leaseId) return false;
        if (retainAttemptForCleanup) CleanupProcessingLeaseId = ProcessingLeaseId;
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
    public bool TryClaimCleanup(DateTimeOffset now, string? expectedStagingKey, Guid? expectedProcessingLeaseId, out Guid cleanupClaimId)
    {
        cleanupClaimId = Guid.Empty;
        if (State == ImageUploadState.Completed)
        {
            // A completed ticket may retain only staging after its durable image exists.
            if (expectedStagingKey is null || expectedProcessingLeaseId is not null || CleanupStagingObjectKey != expectedStagingKey || CleanupProcessingLeaseId is not null) return false;
        }
        if (CleanupClaimId is not null)
        {
            if (CleanupClaimedAt is { } claimedAt && claimedAt.Add(CleanupClaimLease) > now) return false;
            CleanupClaimId = null;
            CleanupClaimedAt = null;
        }
        if (State != ImageUploadState.Completed && CleanupStagingObjectKey is null && CleanupProcessingLeaseId is null)
        {
            if (!IsExpired(now) || State is ImageUploadState.Failed or ImageUploadState.Expired) return false;
            CleanupStagingObjectKey = StagingObjectKey;
            if (State == ImageUploadState.Processing)
            {
                if (LeaseExpiresAt is null || LeaseExpiresAt > now) return false;
                CleanupProcessingLeaseId = ProcessingLeaseId;
                ProcessingStartedAt = null; LeaseExpiresAt = null; ProcessingLeaseId = null;
            }
            State = ImageUploadState.Expired;
        }
        if (expectedStagingKey != CleanupStagingObjectKey || expectedProcessingLeaseId != CleanupProcessingLeaseId) return false;
        CleanupClaimId = Guid.NewGuid(); CleanupClaimedAt = now; Touch();
        cleanupClaimId = CleanupClaimId.Value;
        return true;
    }
    public bool CompleteCleanupClaim(Guid cleanupClaimId)
    {
        if (CleanupClaimId != cleanupClaimId) return false;
        CleanupStagingObjectKey = null; CleanupProcessingLeaseId = null; CleanupClaimId = null; CleanupClaimedAt = null;
        Touch(); return true;
    }
    public bool ReleaseCleanupClaim(Guid cleanupClaimId)
    {
        if (CleanupClaimId != cleanupClaimId) return false;
        CleanupClaimId = null; CleanupClaimedAt = null; Touch(); return true;
    }
    private void Touch() => ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
}
