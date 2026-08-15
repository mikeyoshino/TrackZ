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
    public async Task Claim_persistence_failure_propagates_and_leaves_the_durable_ticket_retryable()
    {
        var harness = Harness.Create();
        harness.Store.SaveException = new IOException("database down");

        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
        Assert.Equal(2, harness.Storage.FinalKeys.Count);
    }

    [Fact]
    public async Task Staging_get_failure_propagates_and_preserves_the_retryable_upload()
    {
        var harness = Harness.Create();
        harness.Storage.GetException = new IOException("storage down");

        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
    }

    [Fact]
    public async Task Staging_read_failure_propagates_and_preserves_the_retryable_upload()
    {
        var harness = Harness.Create();
        harness.Storage.ReadException = new IOException("read failed");

        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
    }

    [Fact]
    public async Task Master_write_failure_releases_the_ticket_without_removing_staging()
    {
        var harness = Harness.Create();
        harness.Storage.MasterPutException = new IOException("master unavailable");

        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
        Assert.Equal(2, harness.Storage.FinalKeys.Count);
    }

    [Fact]
    public async Task Thumbnail_write_failure_removes_only_the_partial_attempt_and_keeps_staging()
    {
        var harness = Harness.Create();
        harness.Storage.ThumbnailPutException = new IOException("thumbnail unavailable");

        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
        Assert.Equal(2, harness.Storage.FinalKeys.Count);
    }

    [Fact]
    public async Task Cleanup_delete_failure_does_not_mask_the_thumbnail_write_failure()
    {
        var harness = Harness.Create();
        harness.Storage.ThumbnailPutException = new IOException("thumbnail unavailable");
        harness.Storage.DeleteException = new IOException("cleanup unavailable");

        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
        Assert.Contains(harness.Storage.FinalKeys, key => key.EndsWith("master.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Commit_failure_removes_both_renditions_and_keeps_staging_for_retry()
    {
        var harness = Harness.Create();
        harness.Store.CommitException = new IOException("commit unavailable");

        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
        Assert.Equal(2, harness.Storage.FinalKeys.Count);
    }

    [Fact]
    public async Task Post_commit_acknowledgement_failure_is_reconciled_without_deleting_durable_finals()
    {
        var harness = Harness.Create();
        harness.Store.ThrowAfterDurableCommit = true;

        var dto = await harness.Handler.Handle(new(harness.Ticket.Id), default);

        Assert.Equal(ImageUploadState.Completed, harness.Ticket.State);
        Assert.Equal(harness.Store.CommittedImageId, dto.Id);
        Assert.Contains(harness.Storage.FinalKeys, key => key.EndsWith("master.jpg", StringComparison.Ordinal));
        Assert.Contains(harness.Storage.FinalKeys, key => key.EndsWith("thumbnail.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_reported_after_durable_commit_is_reconciled_as_completed()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = Harness.Create(cancellation: cancellation);
        harness.Store.CancelAfterDurableCommit = true;

        var dto = await harness.Handler.Handle(new(harness.Ticket.Id), cancellation.Token);

        Assert.Equal(harness.Store.CommittedImageId, dto.Id);
        Assert.Equal(ImageUploadState.Completed, harness.Ticket.State);
        Assert.Contains(harness.Storage.FinalKeys, key => key.EndsWith("master.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completed_upload_stays_completed_when_postcommit_staging_delete_fails()
    {
        var harness = Harness.Create();
        harness.Storage.StagingDeleteException = new IOException("delete unavailable");

        var dto = await harness.Handler.Handle(new(harness.Ticket.Id), default);

        Assert.Equal(ImageUploadState.Completed, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
        Assert.Equal(harness.Store.CommittedImageId, dto.Id);
        Assert.Equal(4, harness.Storage.FinalKeys.Count);
    }

    [Fact]
    public async Task Terminal_invalid_image_stays_failed_when_cleanup_deletes_fail()
    {
        var harness = Harness.Create();
        harness.Processor.Exception = new InvalidDataException("invalid image");
        harness.Storage.DeleteException = new IOException("cleanup unavailable");

        var error = await Assert.ThrowsAsync<BusinessException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(BusinessErrorCode.ImageTypeNotSupported, error.Code);
        Assert.Equal(ImageUploadState.Failed, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
    }

    [Fact]
    public async Task Unexpected_processor_failure_propagates_and_preserves_the_prior_ready_image()
    {
        var harness = Harness.Create();
        harness.Processor.Exception = new IOException("processor unavailable");

        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.Contains("private/prior/master.jpg", harness.Storage.FinalKeys);
        Assert.True(harness.Storage.StagingPresent);
    }

    [Theory]
    [InlineData(FailureBoundary.Storage)]
    [InlineData(FailureBoundary.Processor)]
    [InlineData(FailureBoundary.Commit)]
    public async Task Cancellation_at_each_precommit_boundary_propagates_and_releases_for_retry(FailureBoundary boundary)
    {
        using var cancellation = new CancellationTokenSource();
        var harness = Harness.Create(cancellation: cancellation);
        switch (boundary)
        {
            case FailureBoundary.Storage: harness.Storage.CancelOnGet = true; break;
            case FailureBoundary.Processor: harness.Processor.CancelOnProcess = true; break;
            case FailureBoundary.Commit: harness.Store.CancelOnCommit = true; break;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Handler.Handle(new(harness.Ticket.Id), cancellation.Token));

        Assert.Equal(ImageUploadState.Uploaded, harness.Ticket.State);
        Assert.True(harness.Storage.StagingPresent);
        Assert.Equal(2, harness.Storage.FinalKeys.Count);
    }

    [Fact]
    public async Task Completed_upload_returns_the_same_opaque_dto_without_reprocessing()
    {
        var harness = Harness.Create();

        var first = await harness.Handler.Handle(new(harness.Ticket.Id), default);
        var operationCount = harness.Storage.Log.Count;
        var second = await harness.Handler.Handle(new(harness.Ticket.Id), default);

        Assert.Equal(first, second);
        Assert.Equal(operationCount, harness.Storage.Log.Count);
        Assert.Equal(ImageUploadState.Completed, harness.Ticket.State);
    }

    [Fact]
    public async Task Stale_lease_loser_deletes_only_its_own_tokenized_renditions()
    {
        var harness = Harness.Create();
        harness.Store.LoseLeaseDuringCommit = true;

        await Assert.ThrowsAsync<IOException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.NotNull(harness.Store.WinnerLeaseId);
        Assert.Equal(ImageUploadState.Processing, harness.Ticket.State);
        Assert.All(harness.Storage.DeletedKeys, key => Assert.DoesNotContain(harness.Store.WinnerLeaseId!.Value.ToString("D"), key, StringComparison.Ordinal));
        Assert.Contains(harness.Storage.FinalKeys, key => key.Contains(harness.Store.WinnerLeaseId!.Value.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Declared_length_mismatch_is_terminal_and_cleans_staging()
    {
        var harness = Harness.Create([1, 2, 3], 4);

        var error = await Assert.ThrowsAsync<BusinessException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(BusinessErrorCode.ImageTypeNotSupported, error.Code);
        Assert.Equal(ImageUploadState.Failed, harness.Ticket.State);
        Assert.False(harness.Storage.StagingPresent);
    }

    [Fact]
    public async Task Oversize_observed_bytes_has_stable_code()
    {
        var harness = Harness.Create(new byte[5_000_001], 5_000_001);

        var error = await Assert.ThrowsAsync<BusinessException>(() => harness.Handler.Handle(new(harness.Ticket.Id), default));

        Assert.Equal(BusinessErrorCode.ImageTooLarge, error.Code);
        Assert.Equal(ImageUploadState.Failed, harness.Ticket.State);
    }

    public enum FailureBoundary { Storage, Processor, Commit }

    private sealed class Harness
    {
        public required ImageUploadTicket Ticket { get; init; }
        public required Store Store { get; init; }
        public required Storage Storage { get; init; }
        public required Processor Processor { get; init; }
        public required CompleteImageUploadHandler Handler { get; init; }

        public static Harness Create(byte[]? staging = null, int? declaredLength = null, CancellationTokenSource? cancellation = null)
        {
            var owner = Guid.NewGuid();
            var exercise = ExerciseDefinition.CreateCustom(owner, "Press", BodyPart.Chest, TrackingMode.Weighted);
            var ticket = ImageUploadTicket.Create(owner, exercise.Id, "stage", "image/jpeg", declaredLength ?? (staging?.Length ?? 4), DateTimeOffset.UtcNow.AddMinutes(5));
            ticket.TryMarkUploaded(DateTimeOffset.UtcNow);
            var storage = new Storage(staging ?? [1, 2, 3, 4], cancellation);
            storage.FinalKeys.Add("private/prior/master.jpg");
            storage.FinalKeys.Add("private/prior/thumbnail.jpg");
            var store = new Store(ticket, exercise, storage, cancellation);
            var processor = new Processor(cancellation);
            return new() { Ticket = ticket, Store = store, Storage = storage, Processor = processor, Handler = new(store, storage, processor, new User(owner)) };
        }
    }

    private sealed class User(Guid id) : ICurrentUser { public Guid UserId => id; }

    private sealed class Processor(CancellationTokenSource? cancellation) : IImageProcessor
    {
        public Exception? Exception { get; set; }
        public bool CancelOnProcess { get; set; }
        public Task<ProcessedExerciseImage> ProcessExerciseImageAsync(Stream source, CancellationToken cancellationToken)
        {
            if (CancelOnProcess)
            {
                cancellation!.Cancel();
                return Task.FromCanceled<ProcessedExerciseImage>(cancellationToken);
            }
            return Exception is { } exception
                ? Task.FromException<ProcessedExerciseImage>(exception)
                : Task.FromResult(new ProcessedExerciseImage([1], [2], "image/jpeg", "image/jpeg"));
        }
    }

    private sealed class Storage(byte[] payload, CancellationTokenSource? cancellation) : IObjectStorage
    {
        public List<string> Log { get; } = [];
        public HashSet<string> FinalKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public bool StagingPresent { get; private set; } = true;
        public Exception? GetException { get; set; }
        public Exception? ReadException { get; set; }
        public Exception? MasterPutException { get; set; }
        public Exception? ThumbnailPutException { get; set; }
        public Exception? DeleteException { get; set; }
        public Exception? StagingDeleteException { get; set; }
        public bool CancelOnGet { get; set; }

        public Task<ObjectStorageObject?> GetAsync(string prefix, string key, CancellationToken cancellationToken)
        {
            Log.Add($"get:{prefix}{key}");
            if (CancelOnGet)
            {
                cancellation!.Cancel();
                return Task.FromCanceled<ObjectStorageObject?>(cancellationToken);
            }
            if (GetException is { } exception) return Task.FromException<ObjectStorageObject?>(exception);
            return Task.FromResult<ObjectStorageObject?>(StagingPresent ? new(payload.Length, "image/jpeg", ReadException is { } read ? new ThrowingReadStream(payload, read) : new MemoryStream(payload)) : null);
        }

        public async Task PutAsync(string prefix, string key, Stream content, string contentType, CancellationToken cancellationToken)
        {
            Log.Add($"put:{key}");
            if (key.EndsWith("master.jpg", StringComparison.Ordinal) && MasterPutException is { } master) throw master;
            if (key.EndsWith("thumbnail.jpg", StringComparison.Ordinal) && ThumbnailPutException is { } thumbnail) throw thumbnail;
            await content.CopyToAsync(Stream.Null, cancellationToken);
            FinalKeys.Add(key);
        }

        public Task DeleteAsync(string prefix, string key, CancellationToken cancellationToken)
        {
            Log.Add($"delete:{prefix}{key}");
            DeletedKeys.Add(key);
            if (prefix.StartsWith("staging/", StringComparison.Ordinal))
            {
                if (StagingDeleteException is { } staging) return Task.FromException(staging);
                if (DeleteException is { } delete) return Task.FromException(delete);
                StagingPresent = false;
                return Task.CompletedTask;
            }
            if (DeleteException is { } exception) return Task.FromException(exception);
            FinalKeys.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingReadStream(byte[] payload, Exception exception) : MemoryStream(payload)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(exception);
    }

    private sealed class Store(ImageUploadTicket ticket, ExerciseDefinition exercise, Storage storage, CancellationTokenSource? cancellation) : IExerciseImageUploadStore
    {
        public ImageUploadTicket Ticket { get; } = ticket;
        public Exception? SaveException { get; set; }
        public Exception? CommitException { get; set; }
        public bool CancelOnCommit { get; set; }
        public bool LoseLeaseDuringCommit { get; set; }
        public bool ThrowAfterDurableCommit { get; set; }
        public bool CancelAfterDurableCommit { get; set; }
        public Guid? CommittedImageId { get; private set; }
        public ExerciseImage? DurablyCompletedImage { get; private set; }
        public Guid? WinnerLeaseId { get; private set; }

        public Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid id, Guid owner, CancellationToken ct) => Task.FromResult<ExerciseDefinition?>(exercise);
        public Task AddTicketAsync(ImageUploadTicket value, CancellationToken ct) => Task.CompletedTask;
        public Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid id, Guid owner, CancellationToken ct) => Task.FromResult<ImageUploadTicket?>(Ticket);
        public Task<ExerciseImage?> FindImageAsync(Guid id, CancellationToken ct) => Task.FromResult<ExerciseImage?>(null);
        public Task<ExerciseImage?> FindOwnedImageAsync(Guid id, Guid owner, CancellationToken ct) => Task.FromResult<ExerciseImage?>(null);
        public Task<ExerciseImage?> FindReadableImageAsync(Guid id, Guid owner, CancellationToken ct) => Task.FromResult<ExerciseImage?>(null);
        public Task<StagingUploadTransition> TryMarkUploadedAsync(Guid id, Guid owner, CancellationToken ct) => Task.FromResult(StagingUploadTransition.Uploaded);
        public Task<UploadClaim> TryClaimUploadAsync(Guid id, Guid owner, TimeSpan lease, CancellationToken ct) => Task.FromResult(new UploadClaim(Guid.NewGuid(), Ticket.StagingObjectKey, DateTimeOffset.UtcNow.Add(lease)));
        public Task<StagingUploadTransition> TryMarkUploadedAsync(Guid id, Guid owner, Guid uploadLease, CancellationToken ct) => Task.FromResult(StagingUploadTransition.Uploaded);

        public Task<ExerciseImage> CommitCompletionAsync(Guid id, Guid owner, Guid lease, string master, string thumb, CancellationToken ct)
        {
            if (CancelOnCommit)
            {
                cancellation!.Cancel();
                return Task.FromCanceled<ExerciseImage>(cancellation.Token);
            }
            if (LoseLeaseDuringCommit)
            {
                Ticket.ReleaseForRetry(lease);
                Ticket.TryClaim(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
                WinnerLeaseId = Ticket.ProcessingLeaseId;
                storage.FinalKeys.Add($"private/winner/{WinnerLeaseId:D}/master.jpg");
                storage.FinalKeys.Add($"private/winner/{WinnerLeaseId:D}/thumbnail.jpg");
                return Task.FromException<ExerciseImage>(new IOException("stale lease"));
            }
            if (CommitException is { } exception) return Task.FromException<ExerciseImage>(exception);
            var image = ExerciseImage.CreateCustomUpload(exercise, owner, master, thumb, 1, "test");
            Ticket.Complete(image.Id, lease, DateTimeOffset.UtcNow);
            CommittedImageId = image.Id;
            DurablyCompletedImage = image;
            if (CancelAfterDurableCommit)
            {
                cancellation!.Cancel();
                return Task.FromCanceled<ExerciseImage>(cancellation.Token);
            }
            if (ThrowAfterDurableCommit) return Task.FromException<ExerciseImage>(new IOException("commit acknowledgement lost"));
            return Task.FromResult(image);
        }

        public Task<ExerciseImage?> FindCompletedByAttemptAsync(Guid id, Guid owner, Guid lease, string master, string thumb, CancellationToken ct)
        {
            return Task.FromResult(DurablyCompletedImage);
        }
        public Task<IReadOnlyList<ImageUploadCleanupCandidate>> ListCleanupCandidatesAsync(DateTimeOffset now, CancellationToken ct) => Task.FromResult<IReadOnlyList<ImageUploadCleanupCandidate>>([]);
        public Task<ImageUploadCleanupCandidate?> TryClaimCleanupAsync(ImageUploadCleanupCandidate candidate, DateTimeOffset now, CancellationToken ct) => Task.FromResult<ImageUploadCleanupCandidate?>(null);
        public Task<bool> CompleteCleanupClaimAsync(Guid id, Guid claim, CancellationToken ct) => Task.FromResult(false);
        public Task ReleaseCleanupClaimAsync(Guid id, Guid claim, CancellationToken ct) => Task.CompletedTask;
        public Task MarkCleanupCompleteAsync(Guid id, string? staging, Guid? lease, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> TryFailClaimAsync(Guid id, Guid owner, Guid lease, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Ticket.Fail(lease));
        }

        public Task<bool> TryReleaseClaimAsync(Guid id, Guid owner, Guid lease, CancellationToken ct, bool retainAttemptForCleanup = false)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Ticket.ReleaseForRetry(lease, retainAttemptForCleanup));
        }

        public Task SaveAsync(CancellationToken ct)
        {
            if (SaveException is { } exception)
            {
                Ticket.ReleaseForRetry(Ticket.ProcessingLeaseId!.Value);
                return Task.FromException(exception);
            }
            return Task.CompletedTask;
        }
    }
}
