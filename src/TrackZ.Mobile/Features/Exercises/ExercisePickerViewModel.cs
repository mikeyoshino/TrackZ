using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Exercises;

public sealed class ExercisePickerViewModel : INotifyPropertyChanged
{
    private readonly ExerciseCache _cache;
    private readonly IExerciseCatalogApi _catalogApi;
    private readonly IConnectivityService _connectivity;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly IExerciseThumbnailCache? _thumbnailCache;
    private readonly IAccountSessionBoundary _boundary;
    private readonly HashSet<Guid> _selectedIds = [];
    private IReadOnlyList<CachedExercise> _catalog = [];
    private string _searchText = string.Empty;
    private BodyPart? _selectedBodyPart;
    private bool _isRefreshing;
    private BusinessErrorCode? _lastErrorCode;

    public ExercisePickerViewModel(
        ExerciseCache cache,
        IExerciseCatalogApi catalogApi,
        IConnectivityService connectivity,
        IClock clock,
        IUiDispatcher? dispatcher = null,
        IExerciseThumbnailCache? thumbnailCache = null,
        IAccountSessionBoundary? boundary = null)
    {
        _cache = cache;
        _catalogApi = catalogApi;
        _connectivity = connectivity;
        _clock = clock;
        _dispatcher = dispatcher ?? new InlineUiDispatcher();
        _thumbnailCache = thumbnailCache;
        _boundary = boundary ?? new AccountSessionBoundary();
        _boundary.SessionReset += OnSessionReset;
        ToggleSelectionCommand = new RelayCommand(ToggleSelection);
    }

    public ObservableCollection<CachedExercise> Exercises { get; } = [];
    public IReadOnlyList<BodyPart> BodyParts { get; } = Enum.GetValues<BodyPart>();
    public IReadOnlyCollection<Guid> SelectedExerciseIds => _selectedIds;
    public ICommand ToggleSelectionCommand { get; }
    public Task RefreshCompletion { get; private set; } = Task.CompletedTask;

    public string SearchText
    {
        get => _searchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (_searchText == normalized) return;
            _searchText = normalized;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public BodyPart? SelectedBodyPart
    {
        get => _selectedBodyPart;
        set
        {
            if (_selectedBodyPart == value) return;
            _selectedBodyPart = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (_isRefreshing == value) return;
            _isRefreshing = value;
            OnPropertyChanged();
        }
    }

    public BusinessErrorCode? LastErrorCode
    {
        get => _lastErrorCode;
        private set
        {
            if (_lastErrorCode == value) return;
            _lastErrorCode = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(BodyPart? bodyPart = null, CancellationToken cancellationToken = default)
    {
        var generation = _boundary.Capture();
        _selectedBodyPart = bodyPart;
        OnPropertyChanged(nameof(SelectedBodyPart));
        if (!await _boundary.TryCommitAsync(generation, async token =>
        {
            _catalog = await _cache.GetAllAsync(token);
            await _dispatcher.InvokeAsync(ApplyFilter);
        }, cancellationToken)) return;
        RefreshCompletion = _connectivity.IsOnline
            ? RefreshAsync(generation, cancellationToken)
            : Task.CompletedTask;
    }

    private async Task RefreshAsync(AccountSessionGeneration generation, CancellationToken cancellationToken)
    {
        if (!await _boundary.TryCommitAsync(generation, _ =>
            _dispatcher.InvokeAsync(() => IsRefreshing = true), cancellationToken)) return;
        try
        {
            var exercises = await _catalogApi.GetAllAsync(cancellationToken);
            var metadata = exercises.Select(WithoutRemoteThumbnail).ToArray();
            if (!await _boundary.TryCommitAsync(generation, async token =>
            {
                await _cache.ReplaceAllAsync(metadata, _clock.UtcNow, token);
                _catalog = await _cache.GetAllAsync(token);
                await _dispatcher.InvokeAsync(() =>
                {
                    LastErrorCode = null;
                    ApplyFilter();
                });
            }, cancellationToken)) return;

            if (_thumbnailCache is not null)
            {
                await Parallel.ForEachAsync(
                    exercises.Where(exercise => exercise.ThumbnailUrl is not null),
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = 4
                    },
                    (exercise, token) => CacheThumbnailBestEffortAsync(exercise, generation, token));
                await _boundary.TryCommitAsync(generation, async token =>
                {
                    _catalog = await _cache.GetAllAsync(token);
                    await _dispatcher.InvokeAsync(ApplyFilter);
                }, cancellationToken);
            }
        }
        catch (MobileApiException exception)
        {
            await _boundary.TryCommitAsync(generation, _ =>
                _dispatcher.InvokeAsync(() => LastErrorCode = exception.ErrorCode), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            await _boundary.TryCommitAsync(generation, _ =>
                _dispatcher.InvokeAsync(() => LastErrorCode = BusinessErrorCode.InternalServerError), cancellationToken);
        }
        finally
        {
            await _boundary.TryCommitAsync(generation, _ =>
                _dispatcher.InvokeAsync(() => IsRefreshing = false), cancellationToken);
        }
    }

    private static TrackZ.Contracts.Exercises.ExerciseSummaryDto WithoutRemoteThumbnail(
        TrackZ.Contracts.Exercises.ExerciseSummaryDto exercise) => new(
            exercise.Id,
            exercise.Name,
            exercise.BodyPart,
            exercise.TrackingMode,
            null,
            exercise.LastPerformedAt,
            exercise.LastBestSet,
            exercise.AllTimeBest,
            exercise.IsCustom,
            exercise.LibraryImageId);

    private async ValueTask CacheThumbnailBestEffortAsync(
        TrackZ.Contracts.Exercises.ExerciseSummaryDto exercise,
        AccountSessionGeneration generation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var sessionCancellation = _boundary.CreateCancellationLease(
                generation, cancellationToken);
            var local = await _thumbnailCache!.CacheAsync(
                exercise.ThumbnailUrl, sessionCancellation.Token);
            if (local is not null)
                await _boundary.TryCommitAsync(generation, token =>
                    _cache.SetServerThumbnailAsync(exercise.Id, local, token), sessionCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Thumbnail fills are best effort; metadata is already durable.
        }
    }

    private void ToggleSelection(object? parameter)
    {
        var id = parameter switch
        {
            CachedExercise exercise => exercise.Id,
            Guid exerciseId => exerciseId,
            _ => Guid.Empty
        };
        if (id == Guid.Empty) return;
        if (!_selectedIds.Add(id)) _selectedIds.Remove(id);
        foreach (var exercise in _catalog.Where(item => item.Id == id))
            exercise.IsSelected = _selectedIds.Contains(id);
        OnPropertyChanged(nameof(SelectedExerciseIds));
    }

    private void ApplyFilter()
    {
        var search = _searchText.Trim();
        var filtered = _catalog
            .Where(exercise => !_selectedBodyPart.HasValue || exercise.BodyPart == _selectedBodyPart.Value)
            .Where(exercise => search.Length == 0 || exercise.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(exercise => exercise.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(exercise => exercise.Id)
            .ToArray();
        Exercises.Clear();
        foreach (var exercise in filtered)
        {
            exercise.IsSelected = _selectedIds.Contains(exercise.Id);
            Exercises.Add(exercise);
        }
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        _catalog = [];
        _selectedIds.Clear();
        LastErrorCode = null;
        IsRefreshing = false;
        Exercises.Clear();
        OnPropertyChanged(nameof(SelectedExerciseIds));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
