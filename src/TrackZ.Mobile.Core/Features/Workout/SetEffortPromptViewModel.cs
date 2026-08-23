using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Workout;

public enum SetEffortPromptState
{
    Asking = 1,
    SavingEffort = 2,
    NeedsIncrement = 3,
    Recommendation = 4,
    SaveFailed = 5,
    Unavailable = 6
}

public sealed record ProgressionIncrementOption(decimal DisplayValue, string Label);

public sealed class SetEffortPromptViewModel : INotifyPropertyChanged
{
    private readonly ISetEffortRecorder _recorder;
    private readonly IExerciseGuidancePreferenceStore _preferences;
    private readonly IWeightUnitPreference _unitPreference;
    private readonly IAccountSessionBoundary _boundary;
    private readonly AsyncCommand _chooseEasyCommand;
    private readonly AsyncCommand _chooseProductiveCommand;
    private readonly AsyncCommand _chooseTooHeavyCommand;
    private readonly AsyncCommand _selectIncrementCommand;
    private readonly AsyncCommand _editIncrementCommand;
    private readonly AsyncCommand _saveIncrementCommand;
    private readonly AsyncCommand _retryCommand;
    private readonly AsyncCommand _useSuggestionCommand;
    private readonly AsyncCommand _skipCommand;
    private readonly AsyncCommand _notNowCommand;
    private SetEffortPromptRequest? _request;
    private Func<HypertrophyGuidanceResult, bool>? _applyToDraft;
    private CancellationTokenSource? _lifetime;
    private AccountSessionCancellationLease? _sessionLease;
    private AccountSessionGeneration _generation;
    private SetEffortRating? _selectedEffort;
    private HypertrophyGuidanceSet? _ratedSet;
    private IReadOnlyList<HypertrophyGuidanceSet> _priorCandidates = [];
    private IReadOnlyList<ProgressionIncrementOption> _incrementOptions = [];
    private SetEffortPromptState _state;
    private HypertrophyGuidanceResult? _guidance;
    private string _incrementInput = string.Empty;
    private string _incrementValidationMessage = string.Empty;
    private string _recommendationTitle = string.Empty;
    private string _recommendationReason = string.Empty;
    private string _useActionText = string.Empty;
    private bool _confirmsOriginalSetSaved;
    private bool _hasStoredIncrement;
    private bool _subscribed;
    private bool _dismissRaised;

    public SetEffortPromptViewModel(
        ISetEffortRecorder recorder,
        IExerciseGuidancePreferenceStore preferences,
        IWeightUnitPreference unitPreference,
        IAccountSessionBoundary boundary,
        WorkoutTextSet text)
    {
        _recorder = recorder;
        _preferences = preferences;
        _unitPreference = unitPreference;
        _boundary = boundary;
        Text = text;
        _chooseEasyCommand = new AsyncCommand(
            _ => ChooseEffortAsync(SetEffortRating.Easy),
            _ => IsActive && IsAsking);
        _chooseProductiveCommand = new AsyncCommand(
            _ => ChooseEffortAsync(SetEffortRating.Productive),
            _ => IsActive && IsAsking);
        _chooseTooHeavyCommand = new AsyncCommand(
            _ => ChooseEffortAsync(SetEffortRating.TooHeavy),
            _ => IsActive && IsAsking);
        _selectIncrementCommand = new AsyncCommand(
            SelectIncrementFromCommandAsync, _ => IsActive && NeedsIncrement);
        _editIncrementCommand = new AsyncCommand(
            _ => { EditIncrement(); return Task.CompletedTask; },
            _ => IsActive && CanEditIncrement);
        _saveIncrementCommand = new AsyncCommand(
            _ => SaveIncrementAsync(), _ => IsActive && NeedsIncrement);
        _retryCommand = new AsyncCommand(
            _ => RetryAsync(), _ => IsActive && HasSaveError);
        _useSuggestionCommand = new AsyncCommand(
            _ => { UseSuggestion(); return Task.CompletedTask; },
            _ => IsActive && HasUseAction);
        _skipCommand = new AsyncCommand(
            _ => { Skip(); return Task.CompletedTask; },
            _ => IsActive && IsAsking);
        _notNowCommand = new AsyncCommand(
            _ => { NotNow(); return Task.CompletedTask; },
            _ => IsActive);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DismissRequested;

    public WorkoutTextSet Text { get; }
    public SetEffortPromptState State => _state;
    public HypertrophyGuidanceResult? Guidance => _guidance;
    public IReadOnlyList<ProgressionIncrementOption> IncrementOptions =>
        _incrementOptions;
    public string IncrementUnitLabel => _unitPreference.Current == WeightDisplayUnit.Kilograms
        ? Text.Kilograms
        : Text.Pounds;
    public string IncrementInput
    {
        get => _incrementInput;
        set
        {
            if (!Set(ref _incrementInput, value ?? string.Empty)) return;
            if (_incrementValidationMessage.Length == 0) return;
            _incrementValidationMessage = string.Empty;
            OnPropertyChanged(nameof(IncrementValidationMessage));
            OnPropertyChanged(nameof(HasIncrementValidation));
        }
    }
    public string IncrementValidationMessage => _incrementValidationMessage;
    public bool HasIncrementValidation => _incrementValidationMessage.Length > 0;
    public string RecommendationTitle => _recommendationTitle;
    public string RecommendationReason => _recommendationReason;
    public string UseActionText => _useActionText;
    public bool IsAsking => State == SetEffortPromptState.Asking;
    public bool IsSavingEffort => State == SetEffortPromptState.SavingEffort;
    public bool NeedsIncrement => State == SetEffortPromptState.NeedsIncrement;
    public bool ShowsRecommendation => State == SetEffortPromptState.Recommendation;
    public bool HasSaveError => State == SetEffortPromptState.SaveFailed;
    public bool HasUnavailable => State == SetEffortPromptState.Unavailable;
    public bool IsBusy => IsSavingEffort;
    public bool ConfirmsOriginalSetSaved => _confirmsOriginalSetSaved;
    public bool HasUseAction => IsActive
        && ShowsRecommendation
        && _applyToDraft is not null
        && Guidance is
        {
            SuggestedWeightKg: not null
        } or
        {
            SuggestedAssistedKg: not null
        } or
        {
            SuggestedReps: not null
        };
    public bool CanEditIncrement => IsActive
        && ShowsRecommendation
        && _hasStoredIncrement
        && _request?.TrackingMode is TrackingMode.Weighted or TrackingMode.Assisted;

    public IAsyncCommand ChooseEasyCommand => _chooseEasyCommand;
    public IAsyncCommand ChooseProductiveCommand => _chooseProductiveCommand;
    public IAsyncCommand ChooseTooHeavyCommand => _chooseTooHeavyCommand;
    public IAsyncCommand SelectIncrementCommand => _selectIncrementCommand;
    public IAsyncCommand EditIncrementCommand => _editIncrementCommand;
    public IAsyncCommand SaveIncrementCommand => _saveIncrementCommand;
    public IAsyncCommand RetryCommand => _retryCommand;
    public IAsyncCommand UseSuggestionCommand => _useSuggestionCommand;
    public IAsyncCommand SkipCommand => _skipCommand;
    public IAsyncCommand NotNowCommand => _notNowCommand;

    public void Initialize(
        SetEffortPromptRequest request,
        Func<HypertrophyGuidanceResult, bool> applyToDraft)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(applyToDraft);
        Deactivate();
        _request = request;
        _applyToDraft = applyToDraft;
        _generation = _boundary.Capture();
        _lifetime = new CancellationTokenSource();
        _sessionLease = _boundary.CreateCancellationLease(
            _generation, _lifetime.Token);
        _selectedEffort = null;
        _ratedSet = null;
        _priorCandidates = [];
        _guidance = null;
        _confirmsOriginalSetSaved = false;
        _hasStoredIncrement = false;
        _incrementInput = string.Empty;
        _incrementValidationMessage = string.Empty;
        _recommendationTitle = string.Empty;
        _recommendationReason = string.Empty;
        _useActionText = string.Empty;
        _dismissRaised = false;
        if (!_subscribed)
        {
            _boundary.SessionReset += OnSessionReset;
            _unitPreference.Changed += OnUnitPreferenceChanged;
            _subscribed = true;
        }
        RefreshIncrementOptions();
        SetState(SetEffortPromptState.Asking);
        PublishAllPresentation();
        RaiseCommandStates();
    }

    public async Task ChooseEffortAsync(SetEffortRating effort)
    {
        if (!Enum.IsDefined(effort))
            throw new ArgumentOutOfRangeException(nameof(effort));
        if (_request is null)
            throw new InvalidOperationException("Initialize the effort prompt first.");
        if (!IsAsking) return;
        _selectedEffort = effort;
        await RecordAndEvaluateAsync();
    }

    public Task RetryAsync() => HasSaveError && _selectedEffort is not null
        ? RecordAndEvaluateAsync()
        : Task.CompletedTask;

    public async Task SelectIncrementAsync(decimal displayValue)
    {
        if (!NeedsIncrement) return;
        IncrementInput = FormatDisplayValue(displayValue);
        await SaveIncrementAsync();
    }

    public async Task SaveIncrementAsync()
    {
        if (!NeedsIncrement || _request is not { } request || _ratedSet is null) return;
        if (!decimal.TryParse(
                IncrementInput,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var displayValue)
            || displayValue <= 0m)
        {
            ShowIncrementValidation();
            return;
        }

        try
        {
            var canonical = WeightUnitConversion.ToKilograms(
                displayValue, _unitPreference.Current);
            var token = _sessionLease?.Token
                ?? new CancellationToken(canceled: true);
            var started = _boundary.TryStartSessionPhase(
                _generation,
                () =>
                {
                    _preferences.SetIncrementKg(
                        request.ExerciseDefinitionId, canonical);
                    _hasStoredIncrement = true;
                    EvaluateCached(canonical);
                },
                token);
            if (!started) DeactivateAndDismiss();
        }
        catch (ArgumentOutOfRangeException)
        {
            if (IsCurrent(request)) ShowIncrementValidation();
        }
        catch (OverflowException)
        {
            if (IsCurrent(request)) ShowIncrementValidation();
        }
        catch (OperationCanceledException)
        {
            DeactivateAndDismiss();
        }

        await Task.CompletedTask;
    }

    public void EditIncrement()
    {
        if (!CanEditIncrement || _request is null) return;
        var canonical = _preferences.GetIncrementKg(_request.ExerciseDefinitionId);
        if (canonical is null) return;
        IncrementInput = FormatDisplayValue(WeightUnitConversion.FromKilograms(
            canonical.Value, _unitPreference.Current));
        SetState(SetEffortPromptState.NeedsIncrement);
    }

    public void UseSuggestion()
    {
        if (!HasUseAction || Guidance is null) return;
        var callback = Interlocked.Exchange(ref _applyToDraft, null);
        OnPropertyChanged(nameof(HasUseAction));
        RaiseCommandStates();
        if (callback is null) return;
        bool applied;
        try
        {
            applied = callback(Guidance);
        }
        catch
        {
            applied = false;
        }
        if (applied) RequestDismiss();
    }

    public void Skip() => RequestDismiss();
    public void NotNow() => RequestDismiss();

    public void Deactivate()
    {
        if (_subscribed)
        {
            _boundary.SessionReset -= OnSessionReset;
            _unitPreference.Changed -= OnUnitPreferenceChanged;
            _subscribed = false;
        }
        var lease = Interlocked.Exchange(ref _sessionLease, null);
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        try
        {
            try { lifetime?.Cancel(); }
            catch { }
            lease?.Dispose();
        }
        finally
        {
            lifetime?.Dispose();
        }
        _request = null;
        _applyToDraft = null;
        _selectedEffort = null;
        _ratedSet = null;
        _priorCandidates = [];
        _guidance = null;
        RaiseCommandStates();
    }

    private async Task SelectIncrementFromCommandAsync(object? parameter)
    {
        if (parameter is decimal displayValue)
            await SelectIncrementAsync(displayValue);
    }

    private async Task RecordAndEvaluateAsync()
    {
        var request = _request
            ?? throw new InvalidOperationException("Initialize the effort prompt first.");
        var effort = _selectedEffort
            ?? throw new InvalidOperationException("Choose an effort before saving.");
        var token = _sessionLease?.Token
            ?? new CancellationToken(canceled: true);
        SetGuidance(null);
        SetConfirmation(false);
        SetState(SetEffortPromptState.SavingEffort);

        try
        {
            await _recorder.RecordSetEffortAsync(
                request.ExerciseDefinitionId,
                request.SavedSet.Id,
                effort,
                request.EffortOperationId,
                token);
        }
        catch (SetEffortRecordingUnavailableException)
        {
            if (IsCurrent(request)) SetUnavailable();
            return;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            if (IsCurrent(request)) SetSaveFailed();
            return;
        }

        if (!IsCurrent(request) || token.IsCancellationRequested) return;

        LocalWorkout? active;
        try
        {
            active = await _recorder.RestoreActiveAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            if (IsCurrent(request)) SetSaveFailed();
            return;
        }

        if (!IsCurrent(request) || token.IsCancellationRequested) return;
        if (active is null)
        {
            SetUnavailable();
            return;
        }

        var exercise = active.Exercises.SingleOrDefault(candidate =>
            candidate.DeletedAt is null
            && candidate.Id == request.SavedSet.WorkoutExerciseId
            && candidate.ExerciseDefinitionId == request.ExerciseDefinitionId
            && candidate.TrackingMode == request.TrackingMode);
        var saved = exercise?.Sets.SingleOrDefault(candidate =>
            candidate.DeletedAt is null
            && candidate.Id == request.SavedSet.Id);
        if (exercise is null || saved is null)
        {
            SetUnavailable();
            return;
        }

        _ratedSet = FromLocal(exercise.TrackingMode, saved);
        var currentCandidates = exercise.Sets
            .Where(candidate => candidate.DeletedAt is null
                && candidate.Id != saved.Id
                && (candidate.CompletedAt < saved.CompletedAt
                    || candidate.CompletedAt == saved.CompletedAt
                    && candidate.Order < saved.Order))
            .OrderByDescending(candidate => candidate.CompletedAt)
            .ThenByDescending(candidate => candidate.Order)
            .Select(candidate => FromLocal(exercise.TrackingMode, candidate));
        var previousCandidates = request.PreviousSession is { } previous
            && previous.TrackingMode == request.TrackingMode
            ? previous.Sets
                .OrderByDescending(candidate => candidate.CompletedAt)
                .ThenByDescending(candidate => candidate.Order)
                .Select(candidate => FromHistory(previous.TrackingMode, candidate))
            : Enumerable.Empty<HypertrophyGuidanceSet>();
        _priorCandidates = currentCandidates.Concat(previousCandidates).ToArray();
        var increment = _preferences.GetIncrementKg(request.ExerciseDefinitionId);
        _hasStoredIncrement = increment is not null;
        EvaluateCached(increment);
    }

    private void EvaluateCached(decimal? incrementKg)
    {
        if (_ratedSet is null) return;
        var result = HypertrophyLoadGuidancePolicy.Evaluate(
            new HypertrophyGuidanceRequest(
                _ratedSet,
                _priorCandidates,
                incrementKg));
        SetGuidance(result);
        SetConfirmation(true);
        if (result.RequiresIncrement)
        {
            RefreshIncrementOptions();
            SetState(SetEffortPromptState.NeedsIncrement);
            return;
        }
        MapRecommendation(result);
        SetState(SetEffortPromptState.Recommendation);
    }

    private void MapRecommendation(HypertrophyGuidanceResult result)
    {
        var request = _request
            ?? throw new InvalidOperationException("The effort prompt is inactive.");
        var rated = _ratedSet
            ?? throw new InvalidOperationException("The rated set is unavailable.");
        var titleAndReason = (result.Action, result.Reason, request.TrackingMode) switch
        {
            (HypertrophyGuidanceAction.Increase,
                HypertrophyGuidanceReason.TwoQualifyingSets,
                TrackingMode.Weighted) when result.SuggestedWeightKg is { } weight =>
                (FormatMeasurement(Text.GuidanceTryWeightNextSetFormat, weight),
                    Text.GuidanceTwoSetsReason),
            (HypertrophyGuidanceAction.Increase,
                HypertrophyGuidanceReason.TwoQualifyingSets,
                TrackingMode.Assisted) when result.SuggestedAssistedKg is { } assistance =>
                (FormatMeasurement(Text.GuidanceTryAssistanceNextSetFormat, assistance),
                    Text.GuidanceLessAssistanceReason),
            (HypertrophyGuidanceAction.Reduce, _, TrackingMode.Weighted)
                when result.SuggestedWeightKg is { } weight =>
                (FormatMeasurement(Text.GuidanceTryWeightNextSetFormat, weight),
                    result.Reason == HypertrophyGuidanceReason.BelowRepRange
                        ? Text.GuidanceBelowRangeReason
                        : Text.GuidanceReduceReason),
            (HypertrophyGuidanceAction.Reduce, _, TrackingMode.Assisted)
                when result.SuggestedAssistedKg is { } assistance =>
                (FormatMeasurement(Text.GuidanceTryAssistanceNextSetFormat, assistance),
                    Text.GuidanceMoreAssistanceReason),
            (HypertrophyGuidanceAction.Reduce, _, TrackingMode.Bodyweight)
                when result.SuggestedReps is { } reps =>
                (string.Format(CultureInfo.CurrentCulture,
                    Text.GuidanceTryRepsFormat, reps),
                    Text.GuidanceReduceReason),
            (HypertrophyGuidanceAction.Reduce, _, _) =>
                (Text.GuidanceReduceDifficulty,
                    result.Reason == HypertrophyGuidanceReason.BelowRepRange
                        ? Text.GuidanceBelowRangeReason
                        : Text.GuidanceReduceReason),
            (HypertrophyGuidanceAction.IncreaseRepetitions,
                HypertrophyGuidanceReason.EasyWithinRange,
                _) when result.SuggestedReps is { } reps =>
                (string.Format(CultureInfo.CurrentCulture,
                    Text.GuidanceTryRepsFormat, reps),
                    Text.GuidanceKeepReason),
            (HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange,
                TrackingMode.Weighted) when rated.WeightKg is { } weight =>
                (FormatMeasurement(Text.GuidanceKeepLoadFormat, weight),
                    Text.GuidanceKeepReason),
            (HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange,
                TrackingMode.Assisted) when rated.AssistedKg is { } assistance =>
                (FormatMeasurement(Text.GuidanceKeepLoadFormat, assistance),
                    Text.GuidanceKeepReason),
            (HypertrophyGuidanceAction.Keep,
                HypertrophyGuidanceReason.ProductiveWithinRange,
                TrackingMode.Bodyweight) when result.SuggestedReps is { } reps =>
                (string.Format(CultureInfo.CurrentCulture,
                    Text.GuidanceTryRepsFormat, reps),
                    Text.GuidanceKeepReason),
            (HypertrophyGuidanceAction.CollectMoreData,
                HypertrophyGuidanceReason.OneQualifyingSet,
                _) =>
                (Text.GuidanceAlmostReady, Text.GuidanceNeedAnotherReason),
            (HypertrophyGuidanceAction.None,
                HypertrophyGuidanceReason.BodyweightRangeCompleted,
                TrackingMode.Bodyweight) =>
                (Text.GuidanceBodyweightComplete, string.Empty),
            (HypertrophyGuidanceAction.Increase,
                HypertrophyGuidanceReason.InvalidSuggestedMeasurement,
                TrackingMode.Weighted) =>
                (Text.GuidanceKeepCurrentLoad,
                    Text.GuidanceHigherLoadUnavailable),
            (HypertrophyGuidanceAction.Increase,
                HypertrophyGuidanceReason.InvalidSuggestedMeasurement,
                TrackingMode.Assisted) =>
                (Text.GuidanceKeepCurrentAssistance,
                    Text.GuidanceLowerAssistanceUnavailable),
            (HypertrophyGuidanceAction.None,
                HypertrophyGuidanceReason.InvalidInput
                    or HypertrophyGuidanceReason.MissingEffort,
                _) =>
                (Text.GuidanceNoSuggestion, Text.GuidanceNoSuggestionReason),
            _ =>
                (Text.GuidanceNoSuggestion, Text.GuidanceNoSuggestionReason)
        };
        Set(ref _recommendationTitle, titleAndReason.Item1,
            nameof(RecommendationTitle));
        Set(ref _recommendationReason, titleAndReason.Item2,
            nameof(RecommendationReason));
        var useText = result switch
        {
            { SuggestedWeightKg: { } weight } =>
                FormatMeasurement(Text.GuidanceUseWeightFormat, weight),
            { SuggestedAssistedKg: { } assistance } =>
                FormatMeasurement(Text.GuidanceUseAssistanceFormat, assistance),
            { SuggestedReps: { } reps } => string.Format(
                CultureInfo.CurrentCulture, Text.GuidanceUseRepsFormat, reps),
            _ => string.Empty
        };
        Set(ref _useActionText, useText, nameof(UseActionText));
        OnPropertyChanged(nameof(HasUseAction));
        OnPropertyChanged(nameof(CanEditIncrement));
        RaiseCommandStates();
    }

    private void RefreshIncrementOptions()
    {
        var values = _unitPreference.Current == WeightDisplayUnit.Kilograms
            ? new[] { 1.25m, 2.5m, 5m }
            : new[] { 2.5m, 5m, 10m };
        _incrementOptions = values.Select(value =>
            new ProgressionIncrementOption(
                value,
                $"{FormatDisplayValue(value)} {IncrementUnitLabel}"))
            .ToArray();
        OnPropertyChanged(nameof(IncrementOptions));
        OnPropertyChanged(nameof(IncrementUnitLabel));
    }

    private string FormatMeasurement(string format, decimal kilograms)
    {
        var display = WeightUnitConversion.FromKilograms(
            kilograms, _unitPreference.Current);
        return string.Format(
            CultureInfo.CurrentCulture,
            format,
            FormatDisplayValue(display),
            IncrementUnitLabel);
    }

    private string FormatDisplayValue(decimal value) => value.ToString(
        _unitPreference.Current == WeightDisplayUnit.Kilograms ? "0.###" : "0.00",
        CultureInfo.CurrentCulture);

    private static HypertrophyGuidanceSet FromLocal(
        TrackingMode mode,
        LocalSet set) =>
        new(set.Id, mode, set.WeightKg, set.AssistedKg, set.Reps,
            set.Effort, set.CompletedAt, set.Order);

    private static HypertrophyGuidanceSet FromHistory(
        TrackingMode mode,
        WorkoutSetDto set) =>
        new(set.Id, mode, set.WeightKg, set.AssistedKg, set.Reps,
            set.Effort, set.CompletedAt, set.Order);

    private void ShowIncrementValidation()
    {
        Set(ref _incrementValidationMessage,
            Text.GuidanceIncrementInvalid,
            nameof(IncrementValidationMessage));
        OnPropertyChanged(nameof(HasIncrementValidation));
        SetState(SetEffortPromptState.NeedsIncrement);
    }

    private void SetSaveFailed()
    {
        SetGuidance(null);
        SetConfirmation(true);
        SetState(SetEffortPromptState.SaveFailed);
    }

    private void SetUnavailable()
    {
        _selectedEffort = null;
        _ratedSet = null;
        _priorCandidates = [];
        SetGuidance(null);
        SetConfirmation(true);
        SetState(SetEffortPromptState.Unavailable);
    }

    private void SetGuidance(HypertrophyGuidanceResult? value)
    {
        if (Equals(_guidance, value)) return;
        _guidance = value;
        OnPropertyChanged(nameof(Guidance));
        OnPropertyChanged(nameof(HasUseAction));
        RaiseCommandStates();
    }

    private void SetConfirmation(bool value)
    {
        if (_confirmsOriginalSetSaved == value) return;
        _confirmsOriginalSetSaved = value;
        OnPropertyChanged(nameof(ConfirmsOriginalSetSaved));
    }

    private void SetState(SetEffortPromptState value)
    {
        if (_state == value) return;
        _state = value;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsAsking));
        OnPropertyChanged(nameof(IsSavingEffort));
        OnPropertyChanged(nameof(NeedsIncrement));
        OnPropertyChanged(nameof(ShowsRecommendation));
        OnPropertyChanged(nameof(HasSaveError));
        OnPropertyChanged(nameof(HasUnavailable));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(HasUseAction));
        OnPropertyChanged(nameof(CanEditIncrement));
        RaiseCommandStates();
    }

    private void OnUnitPreferenceChanged(object? sender, EventArgs eventArgs)
    {
        RefreshIncrementOptions();
        if (_request is { } request && NeedsIncrement)
        {
            var canonical = _preferences.GetIncrementKg(request.ExerciseDefinitionId);
            IncrementInput = canonical is { } stored
                ? FormatDisplayValue(WeightUnitConversion.FromKilograms(
                    stored, _unitPreference.Current))
                : string.Empty;
        }
        if (Guidance is { } guidance && ShowsRecommendation)
            MapRecommendation(guidance);
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        if (_request is null) return;
        Deactivate();
        RequestDismiss();
    }

    private bool IsCurrent(SetEffortPromptRequest request) =>
        ReferenceEquals(_request, request)
        && !_boundary.IsCancellationRequested(_generation);

    private bool IsActive =>
        _request is not null
        && _lifetime is not null
        && _sessionLease is not null
        && !_boundary.IsCancellationRequested(_generation);

    private void DeactivateAndDismiss()
    {
        Deactivate();
        RequestDismiss();
    }

    private void RequestDismiss()
    {
        if (_dismissRaised) return;
        _dismissRaised = true;
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseCommandStates()
    {
        _chooseEasyCommand.RaiseCanExecuteChanged();
        _chooseProductiveCommand.RaiseCanExecuteChanged();
        _chooseTooHeavyCommand.RaiseCanExecuteChanged();
        _selectIncrementCommand.RaiseCanExecuteChanged();
        _editIncrementCommand.RaiseCanExecuteChanged();
        _saveIncrementCommand.RaiseCanExecuteChanged();
        _retryCommand.RaiseCanExecuteChanged();
        _useSuggestionCommand.RaiseCanExecuteChanged();
        _skipCommand.RaiseCanExecuteChanged();
        _notNowCommand.RaiseCanExecuteChanged();
    }

    private void PublishAllPresentation()
    {
        OnPropertyChanged(nameof(Guidance));
        OnPropertyChanged(nameof(IncrementInput));
        OnPropertyChanged(nameof(IncrementValidationMessage));
        OnPropertyChanged(nameof(HasIncrementValidation));
        OnPropertyChanged(nameof(RecommendationTitle));
        OnPropertyChanged(nameof(RecommendationReason));
        OnPropertyChanged(nameof(UseActionText));
        OnPropertyChanged(nameof(ConfirmsOriginalSetSaved));
    }

    private bool Set<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
