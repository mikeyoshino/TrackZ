using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class LocalExerciseImageSessionRaceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"trackz-local-image-race-{Guid.NewGuid():N}.db");
    private readonly string _destination = Path.Combine(
        Path.GetTempPath(), $"trackz-local-image-race-{Guid.NewGuid():N}");
    private byte[] _png = [];
    private ExerciseCache _cache = null!;

    public async Task InitializeAsync()
    {
        _cache = new ExerciseCache(_databasePath);
        using var image = new Image<Rgba32>(32, 20);
        await using var bytes = new MemoryStream();
        await image.SaveAsPngAsync(bytes);
        _png = bytes.ToArray();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        if (Directory.Exists(_destination)) Directory.Delete(_destination, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Session_change_while_picker_is_open_discards_old_selection_without_files_or_draft()
    {
        var boundary = new AccountSessionBoundary();
        var picker = new GatedPicker(Selection(() => new MemoryStream(_png, writable: false)));
        var viewModel = ViewModel(boundary);
        var coordinator = Coordinator(picker, boundary, new InlineUiDispatcher());

        var selecting = coordinator.PickAndSelectAsync("Choose image", _destination, viewModel.SelectLocalImage);
        await picker.Entered;
        await ResetAsync(boundary);
        picker.Release();

        Assert.False(await selecting);
        await AssertCleanAsync(viewModel);
    }

    [Fact]
    public async Task Session_change_during_file_write_cancels_import_and_removes_session_files()
    {
        var boundary = new AccountSessionBoundary();
        var stream = new GatedReadStream(_png);
        var picker = new ImmediatePicker(Selection(() => stream));
        var viewModel = ViewModel(boundary);
        var coordinator = Coordinator(picker, boundary, new InlineUiDispatcher());

        var selecting = coordinator.PickAndSelectAsync("Choose image", _destination, viewModel.SelectLocalImage);
        await stream.ReadEntered;
        var resetting = ResetAsync(boundary);
        Assert.False(resetting.IsCompleted);
        stream.Release();

        Assert.False(await selecting);
        await resetting;
        await AssertCleanAsync(viewModel);
    }

    [Fact]
    public async Task Session_change_immediately_before_ui_mutation_discards_committed_image_and_draft()
    {
        var boundary = new AccountSessionBoundary();
        var dispatcher = new GatedDispatcher();
        var picker = new ImmediatePicker(Selection(() => new MemoryStream(_png, writable: false)));
        var viewModel = ViewModel(boundary);
        var coordinator = Coordinator(picker, boundary, dispatcher);

        var selecting = coordinator.PickAndSelectAsync("Choose image", _destination, viewModel.SelectLocalImage);
        await dispatcher.Entered;
        var resetting = ResetAsync(boundary);
        Assert.False(resetting.IsCompleted);
        dispatcher.Release();

        Assert.False(await selecting);
        await resetting;
        await AssertCleanAsync(viewModel);
    }

    [Fact]
    public async Task Current_session_selection_commits_original_preview_and_offline_intent()
    {
        var boundary = new AccountSessionBoundary();
        var picker = new ImmediatePicker(Selection(() => new MemoryStream(_png, writable: false)));
        var viewModel = ViewModel(boundary);
        var coordinator = Coordinator(picker, boundary, new InlineUiDispatcher());

        Assert.True(await coordinator.PickAndSelectAsync("เลือกรูป", _destination, viewModel.SelectLocalImage));
        Assert.Equal("เลือกรูป", picker.LastTitle);
        Assert.NotNull(viewModel.LocalImagePath);
        Assert.NotNull(viewModel.PreviewImagePath);
        Assert.True(File.Exists(viewModel.LocalImagePath));
        Assert.True(File.Exists(viewModel.PreviewImagePath));

        Assert.True(await viewModel.SaveAsync());
        var pending = Assert.Single(await _cache.GetPendingAsync());
        Assert.Equal(viewModel.LocalImagePath, pending.LocalImagePath);
        Assert.Equal(viewModel.PreviewImagePath, pending.LocalPreviewPath);
    }

    private LocalExerciseImageSelectionCoordinator Coordinator(
        ILocalExerciseImagePicker picker,
        IAccountSessionBoundary boundary,
        IUiDispatcher dispatcher) =>
        new(picker, new LocalExerciseImageImporter(), boundary, dispatcher);

    private CustomExerciseViewModel ViewModel(IAccountSessionBoundary boundary)
    {
        var service = new CustomExerciseImageService(
            _cache,
            new OfflineConnectivity(),
            new UnusedCustomApi(),
            new UnusedImageApi(),
            new LocalExerciseFileStore(),
            new FixedClock(),
            new NullThumbnailCache(),
            boundary);
        return new CustomExerciseViewModel(service, _cache, boundary: boundary)
        {
            Name = "Session Press",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted
        };
    }

    private LocalExerciseImageSelection Selection(Func<Stream> open) =>
        new("press.png", "image/png", _ => Task.FromResult(open()));

    private async Task ResetAsync(IAccountSessionBoundary boundary) =>
        await boundary.ResetAsync(token => _cache.ClearAllAsync(token));

    private async Task AssertCleanAsync(CustomExerciseViewModel viewModel)
    {
        Assert.Equal(string.Empty, viewModel.Name);
        Assert.Null(viewModel.LocalImagePath);
        Assert.Null(viewModel.PreviewImagePath);
        Assert.Empty(await _cache.GetAllAsync());
        Assert.Empty(await _cache.GetPendingAsync());
        Assert.Empty(Directory.Exists(_destination)
            ? Directory.EnumerateFiles(_destination, "*", SearchOption.AllDirectories)
            : []);
    }

    private sealed class GatedPicker(LocalExerciseImageSelection selection) : ILocalExerciseImagePicker
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;

        public async Task<LocalExerciseImageSelection?> PickAsync(
            string pickerTitle,
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task;
            return selection;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class ImmediatePicker(LocalExerciseImageSelection selection) : ILocalExerciseImagePicker
    {
        public string? LastTitle { get; private set; }
        public Task<LocalExerciseImageSelection?> PickAsync(
            string pickerTitle,
            CancellationToken cancellationToken = default)
        {
            LastTitle = pickerTitle;
            return
            Task.FromResult<LocalExerciseImageSelection?>(selection);
        }
    }

    private sealed class GatedReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _blocked;
        public Task ReadEntered => _entered.Task;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_blocked)
            {
                _blocked = true;
                _entered.TrySetResult();
                await _release.Task;
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class GatedDispatcher : IUiDispatcher
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;

        public async Task InvokeAsync(Action action)
        {
            _entered.TrySetResult();
            await _release.Task;
            action();
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class NullThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class UnusedCustomApi : ICustomExerciseApi
    {
        public Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Offline selection must not call the API.");

        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Offline selection must not call the API.");
    }

    private sealed class UnusedImageApi : IExerciseImageApi
    {
        public Task<ImageUploadReservation> RequestUploadAsync(
            Guid exerciseId,
            string contentType,
            long length,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Offline selection must not call the API.");

        public Task UploadContentAsync(
            Uri uploadUri,
            Stream original,
            string contentType,
            long length,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Offline selection must not call the API.");

        public Task<UploadedExerciseImage> CompleteUploadAsync(
            Guid uploadId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Offline selection must not call the API.");
    }
}
