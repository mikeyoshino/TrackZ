using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using System.Net;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class CustomExerciseViewModelTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"trackz-custom-{Guid.NewGuid():N}.db");
    private readonly string _imagePath = Path.Combine(Path.GetTempPath(), $"trackz-original-{Guid.NewGuid():N}.png");
    private ExerciseCache _cache = null!;

    public async Task InitializeAsync()
    {
        _cache = new ExerciseCache(_databasePath);
        await File.WriteAllBytesAsync(_imagePath, [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4]);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        if (File.Exists(_imagePath)) File.Delete(_imagePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Offline_save_persists_pending_exercise_and_local_thumbnail()
    {
        var connectivity = new MutableConnectivity(false);
        var customApi = new RecordingCustomApi();
        var mediaApi = new RecordingMediaApi();
        using var service = Service(connectivity, customApi, mediaApi);
        var sut = new CustomExerciseViewModel(service)
        {
            Name = "My Press",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted
        };
        sut.SelectLocalImage(new ImportedExerciseImage(_imagePath, "/local/preview.jpg", "image/png"));

        var saved = await sut.SaveAsync();

        Assert.True(saved);
        Assert.Equal(1, await _cache.CountPendingAsync());
        var cached = Assert.Single(await new ExerciseCache(_databasePath).GetAllAsync());
        Assert.True(cached.IsPendingSync);
        Assert.Equal("/local/preview.jpg", cached.ThumbnailUri);
        Assert.Equal("Pending Sync", cached.SyncLabel);
        Assert.Empty(customApi.Saved);
    }

    [Fact]
    public async Task Reconnect_creates_exercise_and_uploads_the_unchanged_original_in_two_phases()
    {
        var connectivity = new MutableConnectivity(false);
        var customApi = new RecordingCustomApi();
        var mediaApi = new RecordingMediaApi();
        using var service = Service(connectivity, customApi, mediaApi);
        var sut = new CustomExerciseViewModel(service)
        {
            Name = "My Press",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted,
            LocalImagePath = _imagePath,
            LocalImageContentType = "image/png"
        };
        await sut.SaveAsync();
        var original = await File.ReadAllBytesAsync(_imagePath);

        connectivity.SetOnline(true);
        await service.PendingSynchronization;

        Assert.Equal(original, mediaApi.UploadedBytes);
        Assert.Equal(["request", "content", "complete"], mediaApi.Calls);
        Assert.Equal(0, await new ExerciseCache(_databasePath).CountPendingAsync());
        var cached = Assert.Single(await _cache.GetAllAsync());
        Assert.Equal(RecordingCustomApi.ServerId, cached.Id);
        Assert.Equal($"/api/v1/media/exercise-images/{RecordingMediaApi.ImageId:D}/thumbnail", cached.ThumbnailUri);
        Assert.False(cached.IsPendingSync);
    }

    [Fact]
    public async Task Invalid_form_uses_stable_invalid_request_code_without_writing_outbox()
    {
        using var service = Service(new MutableConnectivity(false), new RecordingCustomApi(), new RecordingMediaApi());
        var sut = new CustomExerciseViewModel(service)
        {
            Name = " ",
            BodyPart = (BodyPart)99,
            TrackingMode = null
        };

        var saved = await sut.SaveAsync();

        Assert.False(saved);
        Assert.Equal(BusinessErrorCode.InvalidRequest, sut.LastErrorCode);
        Assert.Equal(["name", "bodyPart", "trackingMode"], sut.ValidationErrors.Keys.Order().OrderByFieldOrder());
        Assert.Equal(0, await _cache.CountPendingAsync());
    }

    [Fact]
    public async Task Library_and_local_images_are_mutually_exclusive()
    {
        using var service = Service(new MutableConnectivity(false), new RecordingCustomApi(), new RecordingMediaApi());
        var sut = new CustomExerciseViewModel(service)
        {
            Name = "My Press",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted,
            LibraryImageId = Guid.NewGuid(),
            LocalImagePath = _imagePath,
            LocalImageContentType = "image/png"
        };

        Assert.False(await sut.SaveAsync());
        Assert.Equal(BusinessErrorCode.InvalidRequest, sut.LastErrorCode);
        Assert.Contains("image", sut.ValidationErrors.Keys);
    }

    [Fact]
    public async Task Failed_upload_persists_server_id_so_retry_does_not_create_a_duplicate()
    {
        var connectivity = new MutableConnectivity(false);
        var customApi = new RecordingCustomApi();
        var mediaApi = new FailFirstUploadMediaApi();
        using var service = new CustomExerciseImageService(
            _cache, connectivity, customApi, mediaApi, new LocalExerciseFileStore(), new FixedClock());
        var sut = new CustomExerciseViewModel(service)
        {
            Name = "Retry Press",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted,
            LocalImagePath = _imagePath,
            LocalImageContentType = "image/png"
        };
        await sut.SaveAsync();

        connectivity.SetOnline(true);
        await service.PendingSynchronization;
        Assert.Equal(BusinessErrorCode.InternalServerError, service.LastSynchronizationError);
        connectivity.SetOnline(false);
        connectivity.SetOnline(true);
        await service.PendingSynchronization;

        Assert.Equal(1, customApi.CreateCount);
        Assert.Equal(1, customApi.UpdateCount);
        Assert.Null(service.LastSynchronizationError);
        Assert.Equal(0, await _cache.CountPendingAsync());
    }

    [Fact]
    public async Task Api_problem_preserves_stable_business_error_code()
    {
        var handler = new SingleHttpHandler(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"type":"https://api.trackz.app/problems/business-rule-violation","title":"Business rule violation","status":409,"errorCode":20002,"message":"Duplicate","traceId":"trace","fieldErrors":null}""",
                Encoding.UTF8,
                "application/problem+json")
        });
        var client = new TrackZExerciseApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") });

        var error = await Assert.ThrowsAsync<MobileApiException>(() => client.CreateAsync(
            new CustomExerciseDraft("Duplicate", BodyPart.Chest, TrackingMode.Weighted, null, null, null)));

        Assert.Equal(BusinessErrorCode.ExerciseNameDuplicate, error.ErrorCode);
        Assert.Equal("Duplicate", error.Message);
    }

    [Fact]
    public async Task Image_import_keeps_original_bytes_and_only_resizes_the_preview()
    {
        using (var source = new Image<Rgba32>(800, 400)) await source.SaveAsPngAsync(_imagePath);
        var expectedOriginal = await File.ReadAllBytesAsync(_imagePath);
        var destination = Path.Combine(Path.GetTempPath(), $"trackz-import-{Guid.NewGuid():N}");
        try
        {
            var result = await new LocalExerciseImageImporter().ImportAsync(_imagePath, "image/png", destination);

            Assert.Equal(expectedOriginal, await File.ReadAllBytesAsync(result.OriginalPath));
            using var preview = await Image.LoadAsync(result.PreviewPath);
            Assert.Equal(512, preview.Width);
            Assert.Equal(256, preview.Height);
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public async Task Connection_loss_during_online_image_upload_turns_into_pending_sync()
    {
        var connectivity = new MutableConnectivity(true);
        var customApi = new RecordingCustomApi();
        using var service = new CustomExerciseImageService(
            _cache, connectivity, customApi, new FailFirstUploadMediaApi(), new LocalExerciseFileStore(), new FixedClock());
        var sut = new CustomExerciseViewModel(service)
        {
            Name = "Interrupted Press",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted,
            LocalImagePath = _imagePath,
            LocalImageContentType = "image/png"
        };

        var saved = await sut.SaveAsync();

        Assert.True(saved);
        Assert.Equal(1, customApi.CreateCount);
        Assert.Equal(1, await _cache.CountPendingAsync());
        Assert.True(Assert.Single(await _cache.GetAllAsync()).IsPendingSync);
    }

    [Fact]
    public async Task Connection_loss_during_online_create_keeps_durable_pending_intent()
    {
        var connectivity = new MutableConnectivity(true);
        using var service = new CustomExerciseImageService(
            _cache, connectivity, new FailingCustomApi(), new RecordingMediaApi(), new LocalExerciseFileStore(), new FixedClock());
        var sut = new CustomExerciseViewModel(service)
        {
            Name = "Queued Press",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted
        };

        Assert.True(await sut.SaveAsync());
        Assert.Equal(1, await _cache.CountPendingAsync());
        Assert.True(Assert.Single(await _cache.GetAllAsync()).IsPendingSync);
    }

    [Fact]
    public async Task Image_import_rejects_content_type_that_does_not_match_detected_bytes()
    {
        using (var source = new Image<Rgba32>(16, 16)) await source.SaveAsPngAsync(_imagePath);
        var destination = Path.Combine(Path.GetTempPath(), $"trackz-mismatch-{Guid.NewGuid():N}");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LocalExerciseImageImporter().ImportAsync(_imagePath, "image/jpeg", destination));
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public async Task Image_import_rejects_files_over_server_limit_before_persisting()
    {
        await File.WriteAllBytesAsync(_imagePath, new byte[5_000_001]);
        var destination = Path.Combine(Path.GetTempPath(), $"trackz-large-{Guid.NewGuid():N}");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LocalExerciseImageImporter().ImportAsync(_imagePath, "image/png", destination));
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public async Task Online_edit_updates_existing_exercise_and_keeps_its_thumbnail()
    {
        var existingId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var customApi = new RecordingCustomApi();
        using var service = Service(new MutableConnectivity(true), customApi, new RecordingMediaApi());
        var sut = new CustomExerciseViewModel(service)
        {
            ExistingExerciseId = existingId,
            Name = "Edited Press",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted,
            PreviewImagePath = "/cached/private-thumbnail.jpg"
        };

        Assert.True(await sut.SaveAsync());

        Assert.Equal(0, customApi.CreateCount);
        Assert.Equal(1, customApi.UpdateCount);
        var cached = Assert.Single(await _cache.GetAllAsync());
        Assert.Equal(existingId, cached.Id);
        Assert.Equal("/cached/private-thumbnail.jpg", cached.ThumbnailUri);
    }

    private CustomExerciseImageService Service(
        MutableConnectivity connectivity,
        RecordingCustomApi customApi,
        RecordingMediaApi mediaApi) =>
        new(_cache, connectivity, customApi, mediaApi, new LocalExerciseFileStore(), new FixedClock());

    private sealed class MutableConnectivity(bool isOnline) : IConnectivityService
    {
        public bool IsOnline { get; private set; } = isOnline;
        public event EventHandler? ConnectivityChanged;
        public void SetOnline(bool value)
        {
            IsOnline = value;
            ConnectivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class RecordingCustomApi : ICustomExerciseApi
    {
        public static readonly Guid ServerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public List<CustomExerciseDraft> Saved { get; } = [];
        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }

        public Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            Saved.Add(exercise);
            return Task.FromResult(ServerId);
        }

        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            Saved.Add(exercise);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingCustomApi : ICustomExerciseApi
    {
        public Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("connection lost");

        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("connection lost");
    }

    private sealed class FailFirstUploadMediaApi : IExerciseImageApi
    {
        private bool _failed;

        public Task<ImageUploadReservation> RequestUploadAsync(Guid exerciseId, string contentType, long length, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImageUploadReservation(Guid.NewGuid(), new Uri("/upload", UriKind.Relative)));

        public Task UploadContentAsync(Uri uploadUri, Stream original, string contentType, long length, CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw new IOException("Simulated connection loss after server exercise creation.");
            }
            return Task.CompletedTask;
        }

        public Task<UploadedExerciseImage> CompleteUploadAsync(Guid uploadId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UploadedExerciseImage(RecordingMediaApi.ImageId, "/master", "/thumbnail"));
    }

    private sealed class RecordingMediaApi : IExerciseImageApi
    {
        public static readonly Guid ImageId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public List<string> Calls { get; } = [];
        public byte[] UploadedBytes { get; private set; } = [];

        public Task<ImageUploadReservation> RequestUploadAsync(Guid exerciseId, string contentType, long length, CancellationToken cancellationToken = default)
        {
            Calls.Add("request");
            return Task.FromResult(new ImageUploadReservation(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                new Uri("/api/v1/media/exercise-images/uploads/44444444-4444-4444-4444-444444444444/content", UriKind.Relative)));
        }

        public async Task UploadContentAsync(Uri uploadUri, Stream original, string contentType, long length, CancellationToken cancellationToken = default)
        {
            Calls.Add("content");
            using var buffer = new MemoryStream();
            await original.CopyToAsync(buffer, cancellationToken);
            UploadedBytes = buffer.ToArray();
        }

        public Task<UploadedExerciseImage> CompleteUploadAsync(Guid uploadId, CancellationToken cancellationToken = default)
        {
            Calls.Add("complete");
            return Task.FromResult(new UploadedExerciseImage(
                ImageId,
                $"/api/v1/media/exercise-images/{ImageId:D}/master",
                $"/api/v1/media/exercise-images/{ImageId:D}/thumbnail"));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 15, 3, 4, 5, TimeSpan.Zero);
    }

    private sealed class SingleHttpHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}

internal static class FieldOrderExtensions
{
    private static readonly string[] FieldOrder = ["name", "bodyPart", "trackingMode"];
    public static string[] OrderByFieldOrder(this IOrderedEnumerable<string> fields) =>
        fields.OrderBy(field => Array.IndexOf(FieldOrder, field)).ToArray();
}
