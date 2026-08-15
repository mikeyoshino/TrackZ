namespace TrackZ.Domain.Exercises;

public enum ImageUploadState { Pending = 1, Processing = 2, Completed = 3, Failed = 4 }

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
    public byte[] ConcurrencyToken { get; private set; } = null!;

    public static ImageUploadTicket Create(Guid ownerId, Guid exerciseId, string stagingKey, string contentType, long length, DateTimeOffset expiresAt) => new()
    {
        Id = Guid.NewGuid(), OwnerId = ownerId, ExerciseDefinitionId = exerciseId, StagingObjectKey = stagingKey,
        DeclaredContentType = contentType, DeclaredLength = length, ExpiresAt = expiresAt.ToUniversalTime(), State = ImageUploadState.Pending, ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)
    };
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public bool TryClaim(DateTimeOffset now, TimeSpan lease)
    {
        if (IsExpired(now) || (State != ImageUploadState.Pending && (State != ImageUploadState.Processing || LeaseExpiresAt is null || LeaseExpiresAt > now))) return false;
        State = ImageUploadState.Processing; ProcessingStartedAt = now; LeaseExpiresAt = now.Add(lease); Touch(); return true;
    }
    public bool HasActiveLease(DateTimeOffset now) => State == ImageUploadState.Processing && LeaseExpiresAt is { } expiry && expiry > now;
    public void Complete(Guid imageId)
    {
        if (State != ImageUploadState.Processing) throw new InvalidOperationException();
        State = ImageUploadState.Completed; ExerciseImageId = imageId; LeaseExpiresAt = null; Touch();
    }
    public void ReleaseForRetry()
    {
        if (State == ImageUploadState.Processing) { State = ImageUploadState.Pending; ProcessingStartedAt = null; LeaseExpiresAt = null; Touch(); }
    }
    public void Fail() { if (State == ImageUploadState.Processing) { State = ImageUploadState.Failed; LeaseExpiresAt = null; Touch(); } }
    private void Touch() => ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
}
