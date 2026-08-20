using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Exercises;

public sealed record BodyPartFilterOption(BodyPart Value, string Label);

public sealed class ExercisePickerViewModel : INotifyPropertyChanged
{
    private readonly ExerciseCache _cache;
    private readonly IExerciseCatalogApi _catalogApi;
    private readonly IConnectivityService _connectivity;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly IExerciseThumbnailCache? _thumbnailCache;
    private readonly IAccountSessionBoundary _boundary;
    private readonly WorkoutTextSet _text;
    private readonly IWeightUnitPreference? _unitPreference;
    private readonly List<Guid> _selectedIds = [];
    private readonly HashSet<Guid> _selectedIdSet = [];
    private readonly Dictionary<Guid, ExerciseArtworkState> _artworkStates = [];
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
        IAccountSessionBoundary? boundary = null,
        WorkoutTextSet? text = null,
        IWeightUnitPreference? unitPreference = null)
    {
        _cache = cache;
        _catalogApi = catalogApi;
        _connectivity = connectivity;
        _clock = clock;
        _dispatcher = dispatcher ?? new InlineUiDispatcher();
        _thumbnailCache = thumbnailCache;
        _boundary = boundary ?? new AccountSessionBoundary();
        _text = text ?? WorkoutResources.Current;
        _unitPreference = unitPreference;
        BodyPartOptions =
        [
            new(BodyPart.Chest, _text.BodyPartChest),
            new(BodyPart.Back, _text.BodyPartBack),
            new(BodyPart.Shoulders, _text.BodyPartShoulders),
            new(BodyPart.Arms, _text.BodyPartArms),
            new(BodyPart.Legs, _text.BodyPartLegs),
            new(BodyPart.Core, _text.BodyPartCore)
        ];
        _boundary.SessionReset += OnSessionReset;
        if (_unitPreference is not null) _unitPreference.Changed += OnWeightUnitChanged;
        ToggleSelectionCommand = new RelayCommand(ToggleSelection);
    }

    public ObservableCollection<ExercisePickerItem> Exercises { get; } = [];
    public IReadOnlyList<BodyPart> BodyParts { get; } = Enum.GetValues<BodyPart>();
    public IReadOnlyList<BodyPartFilterOption> BodyPartOptions { get; }
    public IReadOnlyCollection<Guid> SelectedExerciseIds => _selectedIds;
    public WorkoutTextSet Text => _text;
    public string SelectedCountText => string.Format(CultureInfo.CurrentCulture, _text.SelectedCountFormat, _selectedIds.Count);
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
            OnPropertyChanged(nameof(SelectedBodyPartOption));
            ApplyFilter();
        }
    }

    public BodyPartFilterOption? SelectedBodyPartOption
    {
        get => BodyPartOptions.SingleOrDefault(item => item.Value == SelectedBodyPart);
        set => SelectedBodyPart = value?.Value;
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
            if (!await _boundary.TryCommitAsync(generation, async token =>
            {
                await _cache.ReplaceAllAsync(exercises, _clock.UtcNow, token);
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

    private async ValueTask CacheThumbnailBestEffortAsync(
        TrackZ.Contracts.Exercises.ExerciseSummaryDto exercise,
        AccountSessionGeneration generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.InvokeAsync(() => SetVisibleArtworkState(
                exercise.Id,
                ExerciseArtworkState.Loading));
            using var sessionCancellation = _boundary.CreateCancellationLease(
                generation, cancellationToken);
            var local = await _thumbnailCache!.CacheAsync(
                exercise.ThumbnailUrl, sessionCancellation.Token);
            if (string.IsNullOrWhiteSpace(local))
            {
                await _dispatcher.InvokeAsync(() => SetVisibleArtworkState(
                    exercise.Id,
                    ExerciseArtworkState.Failed));
                return;
            }
            var committed = await _boundary.TryCommitAsync(generation, async token =>
            {
                await _cache.SetServerThumbnailAsync(exercise.Id, local, token);
                _catalog = await _cache.GetAllAsync(token);
            }, sessionCancellation.Token);
            if (committed)
                await _dispatcher.InvokeAsync(() => SetVisibleArtworkReady(exercise.Id, local));
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested
            || _boundary.IsCancellationRequested(generation))
        {
            throw;
        }
        catch (Exception)
        {
            await _dispatcher.InvokeAsync(() => SetVisibleArtworkState(
                exercise.Id,
                ExerciseArtworkState.Failed));
        }
    }

    private async Task<string?> RetryArtworkAsync(ExercisePickerItem item)
    {
        if (_thumbnailCache is null || string.IsNullOrWhiteSpace(item.RemoteThumbnailRoute)) return null;
        var generation = _boundary.Capture();
        using var lease = _boundary.CreateCancellationLease(generation);
        var local = await _thumbnailCache.CacheAsync(item.RemoteThumbnailRoute, lease.Token);
        if (string.IsNullOrWhiteSpace(local)) return null;
        var committed = await _boundary.TryCommitAsync(generation, async token =>
        {
            await _cache.SetServerThumbnailAsync(item.Id, local, token);
            _catalog = await _cache.GetAllAsync(token);
        }, lease.Token);
        return committed ? local : null;
    }

    private void ToggleSelection(object? parameter)
    {
        var id = parameter switch
        {
            ExercisePickerItem item => item.Id,
            CachedExercise exercise => exercise.Id,
            Guid exerciseId => exerciseId,
            _ => Guid.Empty
        };
        if (id == Guid.Empty) return;
        if (_selectedIdSet.Add(id))
        {
            _selectedIds.Add(id);
        }
        else
        {
            _selectedIdSet.Remove(id);
            _selectedIds.Remove(id);
        }
        foreach (var exercise in Exercises.Where(item => item.Id == id))
            exercise.IsSelected = _selectedIdSet.Contains(id);
        OnPropertyChanged(nameof(SelectedExerciseIds));
        OnPropertyChanged(nameof(SelectedCountText));
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
            exercise.IsSelected = _selectedIdSet.Contains(exercise.Id);
            var item = new ExercisePickerItem(
                exercise,
                _text,
                _unitPreference,
                RetryArtworkAsync,
                (id, state) => _artworkStates[id] = state);
            if (_artworkStates.TryGetValue(exercise.Id, out var state))
                item.RestoreArtworkState(state);
            Exercises.Add(item);
        }
    }

    private void SetVisibleArtworkState(Guid exerciseId, ExerciseArtworkState state)
    {
        _artworkStates[exerciseId] = state;
        var item = Exercises.SingleOrDefault(candidate => candidate.Id == exerciseId);
        if (item is null) return;
        if (state == ExerciseArtworkState.Loading) item.SetArtworkLoading();
        else if (state == ExerciseArtworkState.Failed) item.SetArtworkFailed();
        else item.RestoreArtworkState(state);
    }

    private void SetVisibleArtworkReady(Guid exerciseId, string local)
    {
        _artworkStates[exerciseId] = ExerciseArtworkState.Ready;
        Exercises.SingleOrDefault(candidate => candidate.Id == exerciseId)?.SetArtworkReady(local);
    }

    private void OnWeightUnitChanged(object? sender, EventArgs eventArgs) =>
        _dispatcher.InvokeAsync(() =>
        {
            foreach (var exercise in Exercises) exercise.RefreshUnit();
        }).GetAwaiter().GetResult();

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        _catalog = [];
        _selectedIds.Clear();
        _selectedIdSet.Clear();
        _artworkStates.Clear();
        LastErrorCode = null;
        IsRefreshing = false;
        Exercises.Clear();
        OnPropertyChanged(nameof(SelectedExerciseIds));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
