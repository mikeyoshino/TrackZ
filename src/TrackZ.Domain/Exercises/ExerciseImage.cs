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

    public bool IsReadyForUse { get; private set; }

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
        ExerciseDefinition exerciseDefinition,
        string masterObjectKey,
        string thumbnailObjectKey,
        int version,
        string sourceReference,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(exerciseDefinition);
        if (!exerciseDefinition.IsSystem)
        {
            throw new ArgumentException("System artwork can only belong to a system exercise.", nameof(exerciseDefinition));
        }

        return Create(
            exerciseDefinition.Id,
            ownerId: null,
            isPrivate: false,
            isReadyForUse: false,
            masterObjectKey,
            thumbnailObjectKey,
            version,
            ExerciseImageSource.SystemArtwork,
            sourceReference,
            createdAt);
    }

    public static ExerciseImage CreateCustomUpload(
        ExerciseDefinition exerciseDefinition,
        Guid ownerId,
        string masterObjectKey,
        string thumbnailObjectKey,
        int version,
        string sourceReference,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(exerciseDefinition);
        if (!exerciseDefinition.IsCustom)
        {
            throw new ArgumentException("Private uploads can only belong to custom exercises.", nameof(exerciseDefinition));
        }

        if (ownerId == Guid.Empty || exerciseDefinition.OwnerId != ownerId)
        {
            throw new ArgumentException("The upload owner must match the custom exercise owner.", nameof(ownerId));
        }

        return Create(
            exerciseDefinition.Id,
            ownerId,
            isPrivate: true,
            isReadyForUse: true,
            masterObjectKey,
            thumbnailObjectKey,
            version,
            ExerciseImageSource.UserUpload,
            sourceReference,
            createdAt,
            ExerciseImageReviewState.Published);
    }

    public void Review(
        Guid reviewerId,
        string rightsReference,
        bool anatomyApproved,
        bool movementApproved,
        bool rightsApproved,
        DateTimeOffset reviewedAt)
    {
        if (IsPrivate || Source != ExerciseImageSource.SystemArtwork)
        {
            throw new InvalidOperationException("Only draft system artwork can be reviewed.");
        }

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

        var normalizedReviewedAt = reviewedAt.ToUniversalTime();
        if (normalizedReviewedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(reviewedAt), "An image cannot be reviewed before it is created.");
        }

        ReviewedByUserId = reviewerId;
        RightsReference = normalizedRightsReference;
        AnatomyApproved = true;
        MovementApproved = true;
        RightsApproved = true;
        ReviewedAt = normalizedReviewedAt;
        ReviewState = ExerciseImageReviewState.Reviewed;
    }

    public void Publish(DateTimeOffset publishedAt)
    {
        if (IsPrivate || Source != ExerciseImageSource.SystemArtwork)
        {
            throw new InvalidOperationException("Private images cannot be published.");
        }

        if (ReviewState != ExerciseImageReviewState.Reviewed)
        {
            throw new InvalidOperationException("Only reviewed images can be published.");
        }

        var reviewedAt = ReviewedAt ?? throw new InvalidOperationException("A reviewed image must record its review timestamp.");
        var normalizedPublishedAt = publishedAt.ToUniversalTime();
        if (normalizedPublishedAt < CreatedAt || normalizedPublishedAt < reviewedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(publishedAt), "An image cannot be published before its review.");
        }

        PublishedAt = normalizedPublishedAt;
        ReviewState = ExerciseImageReviewState.Published;
        IsReadyForUse = true;
    }

    private static ExerciseImage Create(
        Guid exerciseDefinitionId,
        Guid? ownerId,
        bool isPrivate,
        bool isReadyForUse,
        string masterObjectKey,
        string thumbnailObjectKey,
        int version,
        ExerciseImageSource source,
        string sourceReference,
        DateTimeOffset? createdAt,
        ExerciseImageReviewState initialReviewState = ExerciseImageReviewState.Draft)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(exerciseDefinitionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (!Enum.IsDefined(initialReviewState))
        {
            throw new ArgumentOutOfRangeException(nameof(initialReviewState));
        }

        var normalizedCreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();

        return new ExerciseImage
        {
            Id = Guid.NewGuid(),
            ExerciseDefinitionId = exerciseDefinitionId,
            OwnerId = ownerId,
            IsPrivate = isPrivate,
            IsReadyForUse = isReadyForUse,
            MasterObjectKey = NormalizeRequiredMetadata(masterObjectKey, nameof(masterObjectKey)),
            ThumbnailObjectKey = NormalizeRequiredMetadata(thumbnailObjectKey, nameof(thumbnailObjectKey)),
            Version = version,
            Source = source,
            SourceReference = NormalizeRequiredMetadata(sourceReference, nameof(sourceReference)),
            ReviewState = initialReviewState,
            CreatedAt = normalizedCreatedAt
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
