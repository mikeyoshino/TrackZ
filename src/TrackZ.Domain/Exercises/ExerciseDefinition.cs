namespace TrackZ.Domain.Exercises;

public sealed class ExerciseDefinition
{
    public const int MaximumNameLength = 100;

    private ExerciseDefinition()
    {
    }

    public Guid Id { get; private set; }

    public Guid? OwnerId { get; private set; }

    public Guid? ClientOperationId { get; private set; }

    public Guid? LibraryImageId { get; private set; }

    public string Name { get; private set; } = null!;

    public string NormalizedName { get; private set; } = null!;

    public BodyPart BodyPart { get; private set; }

    public TrackingMode TrackingMode { get; private set; }

    public bool IsArchived { get; private set; }

    public bool HasSetHistory { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsSystem => OwnerId is null;

    public bool IsCustom => OwnerId is not null;

    public static ExerciseDefinition CreateSystem(
        string name,
        BodyPart bodyPart,
        TrackingMode trackingMode,
        DateTimeOffset? createdAt = null)
    {
        return Create(null, name, bodyPart, trackingMode, createdAt);
    }

    public static ExerciseDefinition CreateSystem(
        Guid id,
        string name,
        BodyPart bodyPart,
        TrackingMode trackingMode,
        DateTimeOffset? createdAt = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        var exercise = Create(null, name, bodyPart, trackingMode, createdAt);
        exercise.Id = id;
        return exercise;
    }

    public static ExerciseDefinition CreateCustom(
        Guid ownerId,
        string name,
        BodyPart bodyPart,
        TrackingMode trackingMode,
        DateTimeOffset? createdAt = null,
        Guid? clientOperationId = null)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A custom exercise owner is required.", nameof(ownerId));
        }

        if (clientOperationId == Guid.Empty)
        {
            throw new ArgumentException("A client operation identifier cannot be empty.", nameof(clientOperationId));
        }

        var exercise = Create(ownerId, name, bodyPart, trackingMode, createdAt);
        exercise.ClientOperationId = clientOperationId;
        return exercise;
    }

    public void Archive()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("System exercises cannot be archived.");
        }

        IsArchived = true;
    }

    public void RecordSetHistory()
    {
        HasSetHistory = true;
    }

    public void SelectLibraryImage(Guid? libraryImageId)
    {
        if (libraryImageId == Guid.Empty)
        {
            throw new ArgumentException("A library image identifier cannot be empty.", nameof(libraryImageId));
        }

        LibraryImageId = libraryImageId;
    }

    public void UpdateDetails(string name, BodyPart bodyPart)
    {
        var normalizedName = NormalizeName(name);
        ValidateBodyPart(bodyPart);
        Name = normalizedName;
        NormalizedName = normalizedName.ToUpperInvariant();
        BodyPart = bodyPart;
    }

    public void ChangeTrackingMode(TrackingMode trackingMode)
    {
        ValidateTrackingMode(trackingMode);

        if (TrackingMode == trackingMode)
        {
            return;
        }

        if (HasSetHistory)
        {
            throw new InvalidOperationException("The tracking mode cannot change after set history exists.");
        }

        TrackingMode = trackingMode;
    }

    private static ExerciseDefinition Create(
        Guid? ownerId,
        string name,
        BodyPart bodyPart,
        TrackingMode trackingMode,
        DateTimeOffset? createdAt)
    {
        var normalizedName = NormalizeName(name);
        ValidateBodyPart(bodyPart);
        ValidateTrackingMode(trackingMode);

        return new ExerciseDefinition
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = normalizedName,
            NormalizedName = normalizedName.ToUpperInvariant(),
            BodyPart = bodyPart,
            TrackingMode = trackingMode,
            CreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime()
        };
    }

    private static string NormalizeName(string name)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is 0 or > MaximumNameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(name), $"An exercise name between 1 and {MaximumNameLength} characters is required.");
        }

        return normalizedName;
    }

    private static void ValidateBodyPart(BodyPart bodyPart)
    {
        if (!Enum.IsDefined(bodyPart))
        {
            throw new ArgumentOutOfRangeException(nameof(bodyPart));
        }
    }

    private static void ValidateTrackingMode(TrackingMode trackingMode)
    {
        if (!Enum.IsDefined(trackingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(trackingMode));
        }
    }
}
