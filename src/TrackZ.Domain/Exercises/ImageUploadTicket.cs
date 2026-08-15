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
    public byte[] ConcurrencyToken { get; private set; } = null!;

    public static ImageUploadTicket Create(Guid ownerId, Guid exerciseId, string stagingKey, string contentType, long length, DateTimeOffset expiresAt) => new()
    {
        Id = Guid.NewGuid(), OwnerId = ownerId, ExerciseDefinitionId = exerciseId, StagingObjectKey = stagingKey,
        DeclaredContentType = contentType, DeclaredLength = length, ExpiresAt = expiresAt.ToUniversalTime(), State = ImageUploadState.Pending, ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)
    };
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public void Begin(DateTimeOffset now) { if (State != ImageUploadState.Pending || IsExpired(now)) throw new InvalidOperationException(); State = ImageUploadState.Processing; ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16); }
    public void Complete(Guid imageId) { if (State != ImageUploadState.Processing) throw new InvalidOperationException(); State = ImageUploadState.Completed; ExerciseImageId = imageId; ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16); }
    public void Fail() { if (State == ImageUploadState.Processing) { State = ImageUploadState.Failed; ConcurrencyToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16); } }
}
