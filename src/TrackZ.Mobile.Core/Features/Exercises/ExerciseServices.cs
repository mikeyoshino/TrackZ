using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;

namespace TrackZ.Mobile.Features.Exercises;

public interface IConnectivityService
{
    bool IsOnline { get; }
    event EventHandler? ConnectivityChanged;
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}

public sealed class InlineUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}

public interface IExerciseCatalogApi
{
    Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default);
}

public interface IExerciseThumbnailCache
{
    Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed record CustomExerciseDraft(
    string Name,
    BodyPart BodyPart,
    TrackingMode TrackingMode,
    Guid? LibraryImageId,
    string? LocalImagePath,
    string? LocalImageContentType,
    Guid? ExistingExerciseId = null,
    string? LocalPreviewPath = null,
    Guid? OperationId = null);

public interface ICustomExerciseApi
{
    Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default);
}

public sealed record ImageUploadReservation(Guid UploadId, Uri UploadUri);

public sealed record UploadedExerciseImage(Guid Id, string MasterUrl, string ThumbnailUrl);

public interface IExerciseImageApi
{
    Task<ImageUploadReservation> RequestUploadAsync(
        Guid exerciseId,
        string contentType,
        long length,
        CancellationToken cancellationToken = default);

    Task UploadContentAsync(
        Uri uploadUri,
        Stream original,
        string contentType,
        long length,
        CancellationToken cancellationToken = default);

    Task<UploadedExerciseImage> CompleteUploadAsync(Guid uploadId, CancellationToken cancellationToken = default);
}

public interface IExerciseFileStore
{
    long GetLength(string path);
    Stream OpenRead(string path);
}

public sealed class LocalExerciseFileStore : IExerciseFileStore
{
    public long GetLength(string path) => new FileInfo(path).Length;
    public Stream OpenRead(string path) => File.OpenRead(path);
}

public sealed class MobileApiException(
    BusinessErrorCode errorCode,
    string message,
    IReadOnlyDictionary<string, string[]>? fieldErrors = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public BusinessErrorCode ErrorCode { get; } = errorCode;
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; } = fieldErrors;
}
