namespace TrackZ.Domain.Exercises;

public enum ImageUploadState { Pending = 1, Uploaded = 2, Processing = 3, Completed = 4, Failed = 5 }

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
    public bool TryMarkUploaded(DateTimeOffset now)
    {
        if (State != ImageUploadState.Pending || IsExpired(now)) return false;
        State = ImageUploadState.Uploaded;
        Touch();
        return true;
    }
    public bool TryClaim(DateTimeOffset now, TimeSpan lease)
    {
        if (IsExpired(now) || (State != ImageUploadState.Uploaded && (State != ImageUploadState.Processing || LeaseExpiresAt is null || LeaseExpiresAt > now))) return false;
        State = ImageUploadState.Processing; ProcessingStartedAt = now; LeaseExpiresAt = now.Add(lease); ProcessingLeaseId = Guid.NewGuid(); Touch(); return true;
    }
    public bool HasActiveLease(DateTimeOffset now) => State == ImageUploadState.Processing && LeaseExpiresAt is { } expiry && expiry > now;
    public bool IsClaimHeldBy(Guid leaseId, DateTimeOffset now) => HasActiveLease(now) && ProcessingLeaseId == leaseId;
    public void Complete(Guid imageId, Guid leaseId, DateTimeOffset now)
    {
        if (!IsClaimHeldBy(leaseId, now)) throw new InvalidOperationException();
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
        State = ImageUploadState.Failed; ProcessingStartedAt = null; LeaseExpiresAt = null; ProcessingLeaseId = null; Touch(); return true;
    }
    private void Touch() => ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
}
