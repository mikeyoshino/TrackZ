using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Media;
using TrackZ.Application.Media.CompleteUpload;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Tests.Media;

public sealed class CompleteImageUploadFailureTests
{
    [Fact]
    public async Task Declared_length_mismatch_is_terminal_and_cleans_staging()
    {
        var harness = Harness.Create(new byte[] { 1, 2, 3 });
        harness.Ticket = ImageUploadTicket.Create(harness.Owner, harness.Exercise.Id, "stage", "image/jpeg", 4, DateTimeOffset.UtcNow.AddMinutes(5));
        harness.Ticket.TryMarkUploaded(DateTimeOffset.UtcNow);
        harness.Store.Ticket = harness.Ticket;
        var error = await Assert.ThrowsAsync<BusinessException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));
        Assert.Equal(BusinessErrorCode.ImageTypeNotSupported, error.Code);
        Assert.Equal(ImageUploadState.Failed, harness.Ticket.State);
        Assert.Contains(harness.Storage.Log, item => item.StartsWith("delete:staging/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unexpected_precommit_failure_releases_retry_and_preserves_staging()
    {
        var harness = Harness.Create(new byte[] { 1, 2, 3, 4 });
        harness.Storage.GetException = new IOException("storage down");
        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));
        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.DoesNotContain(harness.Storage.Log, item => item.StartsWith("delete:staging/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Oversize_observed_bytes_has_stable_code()
    {
        var harness = Harness.Create(new byte[5_000_001]);
        harness.Ticket = ImageUploadTicket.Create(harness.Owner, harness.Exercise.Id, "stage", "image/jpeg", 5_000_001, DateTimeOffset.UtcNow.AddMinutes(5));
        harness.Ticket.TryMarkUploaded(DateTimeOffset.UtcNow);
        harness.Store.Ticket = harness.Ticket;
        var error = await Assert.ThrowsAsync<BusinessException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));
        Assert.Equal(BusinessErrorCode.ImageTooLarge, error.Code);
    }

    private sealed class Harness
    {
        public required Guid Owner { get; init; }
        public required ExerciseDefinition Exercise { get; init; }
        public required ImageUploadTicket Ticket { get; set; }
        public required Store Store { get; init; }
        public required Storage Storage { get; init; }
        public required CompleteImageUploadHandler Handler { get; init; }
        public static Harness Create(byte[] staging)
        {
            var owner = Guid.NewGuid(); var exercise = ExerciseDefinition.CreateCustom(owner, "Press", BodyPart.Chest, TrackingMode.Weighted);
            var ticket = ImageUploadTicket.Create(owner, exercise.Id, "stage", "image/jpeg", staging.Length, DateTimeOffset.UtcNow.AddMinutes(5)); ticket.TryMarkUploaded(DateTimeOffset.UtcNow);
            var store = new Store(ticket, exercise); var storage = new Storage(staging);
            return new() { Owner = owner, Exercise = exercise, Ticket = ticket, Store = store, Storage = storage, Handler = new(store, storage, new Processor(), new User(owner)) };
        }
    }
    private sealed class User(Guid id) : ICurrentUser { public Guid UserId => id; }
    private sealed class Processor : IImageProcessor
    { public Task<ProcessedExerciseImage> ProcessExerciseImageAsync(Stream source, CancellationToken ct) => Task.FromResult(new ProcessedExerciseImage([1], [2], "image/jpeg", "image/jpeg")); }
    private sealed class Storage(byte[] payload) : IObjectStorage
    {
        public List<string> Log { get; } = []; public Exception? GetException { get; set; }
        public Task<ObjectStorageObject?> GetAsync(string prefix, string key, CancellationToken ct) { Log.Add($"get:{prefix}"); if (GetException is { } e) return Task.FromException<ObjectStorageObject?>(e); return Task.FromResult<ObjectStorageObject?>(new(payload.Length, "image/jpeg", new MemoryStream(payload))); }
        public Task PutAsync(string prefix, string key, Stream content, string type, CancellationToken ct) { Log.Add($"put:{key}"); return Task.CompletedTask; }
        public Task DeleteAsync(string prefix, string key, CancellationToken ct) { Log.Add($"delete:{prefix}"); return Task.CompletedTask; }
    }
    private sealed class Store(ImageUploadTicket ticket, ExerciseDefinition exercise) : IExerciseImageUploadStore
    {
        public ImageUploadTicket Ticket { get; set; } = ticket;
        public Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid id, Guid owner, CancellationToken ct) => Task.FromResult<ExerciseDefinition?>(exercise);
        public Task AddTicketAsync(ImageUploadTicket t, CancellationToken ct) => Task.CompletedTask;
        public Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid id, Guid owner, CancellationToken ct) => Task.FromResult<ImageUploadTicket?>(Ticket);
        public Task<ExerciseImage?> FindImageAsync(Guid id, CancellationToken ct) => Task.FromResult<ExerciseImage?>(null);
        public Task<ExerciseImage?> FindOwnedImageAsync(Guid id, Guid owner, CancellationToken ct) => Task.FromResult<ExerciseImage?>(null);
        public Task<StagingUploadTransition> TryMarkUploadedAsync(Guid id, Guid owner, CancellationToken ct) => Task.FromResult(StagingUploadTransition.Uploaded);
        public Task<ExerciseImage> CommitCompletionAsync(Guid id, Guid owner, Guid lease, string master, string thumb, CancellationToken ct) { var image = ExerciseImage.CreateCustomUpload(exercise, owner, master, thumb, 1, "test"); Ticket.Complete(image.Id, lease, DateTimeOffset.UtcNow); return Task.FromResult(image); }
        public Task<bool> TryFailClaimAsync(Guid id, Guid owner, Guid lease, CancellationToken ct) => Task.FromResult(Ticket.Fail(lease));
        public Task<bool> TryReleaseClaimAsync(Guid id, Guid owner, Guid lease, CancellationToken ct) => Task.FromResult(Ticket.ReleaseForRetry(lease));
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
