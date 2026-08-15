using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Media.CompleteUpload;
public sealed record CompleteImageUploadCommand(Guid UploadId) : IRequest<ExerciseImageDto>;
public sealed record ExerciseImageDto(Guid Id, string MasterUrl, string ThumbnailUrl);

public sealed class CompleteImageUploadHandler(IExerciseImageUploadStore store, IObjectStorage storage, IImageProcessor processor, ICurrentUser currentUser) : IRequestHandler<CompleteImageUploadCommand, ExerciseImageDto>
{
    public async Task<ExerciseImageDto> Handle(CompleteImageUploadCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;
        var ticket = await store.FindOwnedTicketAsync(request.UploadId, owner, cancellationToken) ?? throw Missing();
        if (ticket.State == ImageUploadState.Completed && ticket.ExerciseImageId is { } existingId)
        {
            var existing = await store.FindImageAsync(existingId, cancellationToken) ?? throw Missing();
            return await Dto(existing, owner, cancellationToken);
        }
        if (ticket.IsExpired(DateTimeOffset.UtcNow) || ticket.State != ImageUploadState.Pending) throw Missing();
        try { ticket.Begin(DateTimeOffset.UtcNow); await store.SaveAsync(cancellationToken); }
        catch { throw Missing(); }
        string? masterKey = null;
        string? thumbKey = null;
        try
        {
            var staged = await storage.GetAsync($"staging/{owner:D}/", ticket.StagingObjectKey, cancellationToken);
            if (staged is null || staged.Length <= 0 || staged.Length > 5_000_000 || staged.ContentType != ticket.DeclaredContentType) throw InvalidImage();
            await using var stream = staged.Content;
            var rendered = await processor.ProcessExerciseImageAsync(stream, cancellationToken);
            var version = await store.NextImageVersionAsync(ticket.ExerciseDefinitionId, cancellationToken);
            var root = $"private/{owner:D}/{ticket.ExerciseDefinitionId:D}/{ticket.Id:D}";
            masterKey = root + "/master.jpg"; thumbKey = root + "/thumbnail.jpg";
            await using var master = new MemoryStream(rendered.Master); await using var thumbnail = new MemoryStream(rendered.Thumbnail);
            await storage.PutAsync($"private/{owner:D}/", masterKey, master, rendered.ContentType, cancellationToken);
            await storage.PutAsync($"private/{owner:D}/", thumbKey, thumbnail, rendered.ContentType, cancellationToken);
            var exercise = await store.FindOwnedActiveExerciseAsync(ticket.ExerciseDefinitionId, owner, cancellationToken) ?? throw Missing();
            var image = ExerciseImage.CreateCustomUpload(exercise, owner, masterKey, thumbKey, version, "validated-upload");
            await store.AddImageAsync(image, cancellationToken); ticket.Complete(image.Id); await store.SaveAsync(cancellationToken);
            await storage.DeleteAsync($"staging/{owner:D}/", ticket.StagingObjectKey, cancellationToken);
            return await Dto(image, owner, cancellationToken);
        }
        catch (BusinessException) { await FailAndCleanAsync(ticket, owner, masterKey, thumbKey, cancellationToken); throw; }
        catch { await FailAndCleanAsync(ticket, owner, masterKey, thumbKey, cancellationToken); throw InvalidImage(); }
    }
    private async Task<ExerciseImageDto> Dto(ExerciseImage image, Guid owner, CancellationToken cancellationToken) => new(image.Id,
        (await storage.CreateReadUriAsync($"private/{owner:D}/", image.MasterObjectKey, DateTimeOffset.UtcNow.AddMinutes(5), cancellationToken)).ToString(),
        (await storage.CreateReadUriAsync($"private/{owner:D}/", image.ThumbnailObjectKey, DateTimeOffset.UtcNow.AddMinutes(5), cancellationToken)).ToString());
    private static BusinessException Missing() => new(BusinessErrorCode.ExerciseNotFound, "The exercise was not found.", 404);
    private static BusinessException InvalidImage() => new(BusinessErrorCode.ImageTypeNotSupported, "The image type is not supported.", 400);
    private async Task FailAndCleanAsync(ImageUploadTicket ticket, Guid owner, string? master, string? thumbnail, CancellationToken cancellationToken)
    {
        ticket.Fail();
        try { await store.SaveAsync(cancellationToken); } finally
        {
            try { if (master is not null) await storage.DeleteAsync($"private/{owner:D}/", master, cancellationToken); } catch { }
            try { if (thumbnail is not null) await storage.DeleteAsync($"private/{owner:D}/", thumbnail, cancellationToken); } catch { }
            try { await storage.DeleteAsync($"staging/{owner:D}/", ticket.StagingObjectKey, cancellationToken); } catch { }
        }
    }
}
