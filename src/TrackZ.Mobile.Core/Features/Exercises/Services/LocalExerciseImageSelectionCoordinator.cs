using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed record LocalExerciseImageSelection(
    string FileName,
    string? ReportedContentType,
    Func<CancellationToken, Task<Stream>> OpenReadAsync);

public interface ILocalExerciseImagePicker
{
    Task<LocalExerciseImageSelection?> PickAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalExerciseImageSelectionCoordinator
{
    private readonly ILocalExerciseImagePicker _picker;
    private readonly LocalExerciseImageImporter _importer;
    private readonly IAccountSessionBoundary _boundary;
    private readonly IUiDispatcher _dispatcher;
    private readonly object _managedFilesLock = new();
    private readonly HashSet<ImportedExerciseImage> _managedFiles = [];

    public LocalExerciseImageSelectionCoordinator(
        ILocalExerciseImagePicker picker,
        LocalExerciseImageImporter importer,
        IAccountSessionBoundary boundary,
        IUiDispatcher dispatcher)
    {
        _picker = picker;
        _importer = importer;
        _boundary = boundary;
        _dispatcher = dispatcher;
        _boundary.SessionReset += OnSessionReset;
    }

    public async Task<bool> PickAndSelectAsync(
        string destinationDirectory,
        Action<ImportedExerciseImage> select,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(select);
        var generation = _boundary.Capture();
        var selected = await _picker.PickAsync(cancellationToken);
        if (selected is null) return false;

        ImportedExerciseImage? imported = null;
        try
        {
            return await _boundary.TryCommitAsync(generation, async token =>
            {
                var contentType = ResolveContentType(selected.FileName, selected.ReportedContentType)
                    ?? throw new InvalidDataException("Choose a JPEG, PNG, or WebP image.");
                token.ThrowIfCancellationRequested();
                await using var source = await selected.OpenReadAsync(token);
                token.ThrowIfCancellationRequested();
                imported = await _importer.ImportAsync(
                    source,
                    contentType,
                    destinationDirectory,
                    $"session-{generation.Value}",
                    token);
                Register(imported);
                try
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        select(imported);
                    });
                }
                catch
                {
                    UnregisterAndDelete(imported);
                    imported = null;
                    throw;
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && _boundary.IsCancellationRequested(generation))
        {
            if (imported is not null) UnregisterAndDelete(imported);
            return false;
        }
    }

    private static string? ResolveContentType(string fileName, string? reported) =>
        reported is "image/jpeg" or "image/png" or "image/webp"
            ? reported
            : Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => null
            };

    private void Register(ImportedExerciseImage image)
    {
        lock (_managedFilesLock) _managedFiles.Add(image);
    }

    private void UnregisterAndDelete(ImportedExerciseImage image)
    {
        lock (_managedFilesLock) _managedFiles.Remove(image);
        Delete(image);
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        ImportedExerciseImage[] files;
        lock (_managedFilesLock)
        {
            files = [.. _managedFiles];
            _managedFiles.Clear();
        }
        foreach (var image in files) Delete(image);
    }

    private static void Delete(ImportedExerciseImage image)
    {
        if (File.Exists(image.OriginalPath)) File.Delete(image.OriginalPath);
        if (File.Exists(image.PreviewPath)) File.Delete(image.PreviewPath);
    }
}
