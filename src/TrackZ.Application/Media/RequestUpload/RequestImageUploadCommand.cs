using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Media;
using TrackZ.Domain.Exercises;
using System.Security.Cryptography;

namespace TrackZ.Application.Media.RequestUpload;

public sealed record RequestImageUploadCommand(Guid ExerciseId, string? ContentType, long DeclaredLength) : IRequest<UploadRequestDto>;
public sealed record UploadRequestDto(Guid UploadId, Uri UploadUri, DateTimeOffset ExpiresAt);

public sealed class RequestImageUploadHandler : IRequestHandler<RequestImageUploadCommand, UploadRequestDto>
{
    private readonly IExerciseImageUploadStore? _store;
    private readonly IObjectStorage? _storage;
    private readonly ICurrentUser? _currentUser;
    public RequestImageUploadHandler() { }
    public RequestImageUploadHandler(IExerciseImageUploadStore store, IObjectStorage storage, ICurrentUser currentUser) => (_store, _storage, _currentUser) = (store, storage, currentUser);
    public Task<UploadRequestDto> Handle(RequestImageUploadCommand request, CancellationToken cancellationToken)
    {
        if (request.DeclaredLength <= 0) throw Invalid();
        if (request.DeclaredLength > 5_000_000) throw new BusinessException(BusinessErrorCode.ImageTooLarge, "The image is too large.", 400);
        if (request.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new BusinessException(BusinessErrorCode.ImageTypeNotSupported, "The image type is not supported.", 400);
        return HandleValidAsync(request, cancellationToken);
    }

    private async Task<UploadRequestDto> HandleValidAsync(RequestImageUploadCommand request, CancellationToken cancellationToken)
    {
        if (_store is null || _storage is null || _currentUser is null) throw new InvalidOperationException("Upload services are required.");
        var ownerId = _currentUser.UserId;
        var exercise = await _store.FindOwnedActiveExerciseAsync(request.ExerciseId, ownerId, cancellationToken);
        if (exercise is null) throw new BusinessException(BusinessErrorCode.ExerciseNotFound, "The exercise was not found.", 404);
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        var key = $"staging/{ownerId:D}/{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";
        var ticket = ImageUploadTicket.Create(ownerId, exercise.Id, key, request.ContentType!, request.DeclaredLength, expires);
        var uri = await _storage.CreateUploadUriAsync($"staging/{ownerId:D}/", key, request.ContentType!, request.DeclaredLength, expires, cancellationToken);
        await _store.AddTicketAsync(ticket, cancellationToken);
        await _store.SaveAsync(cancellationToken);
        return new UploadRequestDto(ticket.Id, uri, expires);
    }

    private static BusinessException Invalid() => new(BusinessErrorCode.InvalidRequest, "Request data is invalid.", 400);
}
