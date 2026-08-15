namespace TrackZ.Domain.Exercises;

public enum ExerciseImageSource
{
    SystemArtwork = 1,
    UserUpload = 2
}

public sealed class ExerciseImage
{
    private ExerciseImage()
    {
    }

    public Guid Id { get; private set; }

    public Guid ExerciseDefinitionId { get; private set; }

    public Guid? OwnerId { get; private set; }

    public bool IsPrivate { get; private set; }

    public string MasterObjectKey { get; private set; } = null!;

    public string ThumbnailObjectKey { get; private set; } = null!;

    public int Version { get; private set; }

    public ExerciseImageSource Source { get; private set; }

    public string SourceReference { get; private set; } = null!;

    public string? RightsReference { get; private set; }

    public ExerciseImageReviewState ReviewState { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public bool AnatomyApproved { get; private set; }

    public bool MovementApproved { get; private set; }

    public bool RightsApproved { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ExerciseImage CreateSystem(
        Guid exerciseDefinitionId,
        string masterObjectKey,
        string thumbnailObjectKey,
        int version,
        string sourceReference,
        DateTimeOffset? createdAt = null)
    {
        return Create(
            exerciseDefinitionId,
            ownerId: null,
            isPrivate: false,
            masterObjectKey,
            thumbnailObjectKey,
            version,
            ExerciseImageSource.SystemArtwork,
            sourceReference,
            createdAt);
    }

    public static ExerciseImage CreateCustomUpload(
        Guid exerciseDefinitionId,
        Guid ownerId,
        string masterObjectKey,
        string thumbnailObjectKey,
        int version,
        string sourceReference,
        DateTimeOffset? createdAt = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerId, Guid.Empty);

        return Create(
            exerciseDefinitionId,
            ownerId,
            isPrivate: true,
            masterObjectKey,
            thumbnailObjectKey,
            version,
            ExerciseImageSource.UserUpload,
            sourceReference,
            createdAt);
    }

    public void Review(
        Guid reviewerId,
        string rightsReference,
        bool anatomyApproved,
        bool movementApproved,
        bool rightsApproved,
        DateTimeOffset reviewedAt)
    {
        if (reviewerId == Guid.Empty)
        {
            throw new ArgumentException("A reviewer is required.", nameof(reviewerId));
        }
        var normalizedRightsReference = NormalizeRequiredMetadata(rightsReference, nameof(rightsReference));

        if (ReviewState != ExerciseImageReviewState.Draft)
        {
            throw new InvalidOperationException("Only draft images can be reviewed.");
        }

        if (!anatomyApproved || !movementApproved || !rightsApproved)
        {
            throw new ArgumentException("Anatomy, movement, and rights approval are all required before review.");
        }

        ReviewedByUserId = reviewerId;
        RightsReference = normalizedRightsReference;
        AnatomyApproved = true;
        MovementApproved = true;
        RightsApproved = true;
        ReviewedAt = reviewedAt.ToUniversalTime();
        ReviewState = ExerciseImageReviewState.Reviewed;
    }

    public void Publish(DateTimeOffset publishedAt)
    {
        if (IsPrivate)
        {
            throw new InvalidOperationException("Private images cannot be published.");
        }

        if (ReviewState != ExerciseImageReviewState.Reviewed)
        {
            throw new InvalidOperationException("Only reviewed images can be published.");
        }

        var normalizedPublishedAt = publishedAt.ToUniversalTime();
        if (normalizedPublishedAt < ReviewedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(publishedAt), "An image cannot be published before its review.");
        }

        PublishedAt = normalizedPublishedAt;
        ReviewState = ExerciseImageReviewState.Published;
    }

    private static ExerciseImage Create(
        Guid exerciseDefinitionId,
        Guid? ownerId,
        bool isPrivate,
        string masterObjectKey,
        string thumbnailObjectKey,
        int version,
        ExerciseImageSource source,
        string sourceReference,
        DateTimeOffset? createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(exerciseDefinitionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        return new ExerciseImage
        {
            Id = Guid.NewGuid(),
            ExerciseDefinitionId = exerciseDefinitionId,
            OwnerId = ownerId,
            IsPrivate = isPrivate,
            MasterObjectKey = NormalizeRequiredMetadata(masterObjectKey, nameof(masterObjectKey)),
            ThumbnailObjectKey = NormalizeRequiredMetadata(thumbnailObjectKey, nameof(thumbnailObjectKey)),
            Version = version,
            Source = source,
            SourceReference = NormalizeRequiredMetadata(sourceReference, nameof(sourceReference)),
            ReviewState = ExerciseImageReviewState.Draft,
            CreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime()
        };
    }

    private static string NormalizeRequiredMetadata(string value, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 512)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Metadata must contain between 1 and 512 characters.");
        }

        return normalized;
    }
}
