using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Features.Profile;

public sealed record ProfileSignOutRisk(
    int UnsyncedWorkoutChangeCount,
    int UnsyncedCustomExerciseCount,
    bool HasActiveWorkout,
    bool InspectionFailed = false)
{
    public static ProfileSignOutRisk Clear { get; } = new(0, 0, false);
    public static ProfileSignOutRisk Unknown { get; } = new(0, 0, false, true);
    public bool HasDataAtRisk =>
        UnsyncedWorkoutChangeCount > 0
        || UnsyncedCustomExerciseCount > 0
        || HasActiveWorkout;
    public bool RequiresDataLossConfirmation => HasDataAtRisk || InspectionFailed;
}

public interface IProfileSignOutRiskSource
{
    Task<ProfileSignOutRisk> GetRiskAsync(CancellationToken cancellationToken = default);
}

public interface IProfileSignOutConfirmation
{
    Task<bool> ConfirmAsync(
        ProfileSignOutRisk risk,
        CancellationToken cancellationToken = default);
}

public sealed class ProfileSignOutRiskSource(
    OutboxRepository outbox,
    ExerciseCache exercises,
    ILocalWorkoutRepository workouts) : IProfileSignOutRiskSource
{
    public async Task<ProfileSignOutRisk> GetRiskAsync(
        CancellationToken cancellationToken = default)
    {
        var workoutChanges = await outbox.UnresolvedAsync(cancellationToken);
        var pendingExercises = await exercises.GetPendingAsync(cancellationToken);
        var failedExercises = await exercises.GetFailedAsync(cancellationToken);
        var activeWorkout = await workouts.GetActiveAsync(cancellationToken);
        return new ProfileSignOutRisk(
            workoutChanges.Count,
            pendingExercises.Count + failedExercises.Count,
            activeWorkout is not null);
    }
}

public sealed class ProfileSignOutController : INotifyPropertyChanged
{
    private readonly IProfileSignOutRiskSource _riskSource;
    private readonly IProfileSignOutConfirmation _confirmation;
    private readonly AuthGateCoordinator _authentication;
    private readonly MobileTextSet _text;
    private bool _isBusy;
    private string? _errorMessage;

    public ProfileSignOutController(
        IProfileSignOutRiskSource riskSource,
        IProfileSignOutConfirmation confirmation,
        AuthGateCoordinator authentication,
        MobileTextSet text)
    {
        _riskSource = riskSource;
        _confirmation = confirmation;
        _authentication = authentication;
        _text = text;
        SignOutCommand = new AsyncCommand(_ => SignOutAsync(), _ => !IsBusy);
    }

    public AsyncCommand SignOutCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            SignOutCommand.RaiseCanExecuteChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task SignOutAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var initialRisk = await ReadRiskAsync();
            if (!await _confirmation.ConfirmAsync(initialRisk)) return;
            var confirmedWarningStrength = WarningStrength(initialRisk);

            _ = await _authentication.TrySignOutAsync(async token =>
            {
                var latestRisk = await ReadRiskAsync(token);
                return WarningStrength(latestRisk) <= confirmedWarningStrength
                    || await _confirmation.ConfirmAsync(latestRisk, token);
            });
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = _text.SignOutFailed;
        }
        catch (Exception)
        {
            ErrorMessage = _text.SignOutFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ProfileSignOutRisk> ReadRiskAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _riskSource.GetRiskAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ProfileSignOutRisk.Unknown;
        }
        catch (Exception)
        {
            return ProfileSignOutRisk.Unknown;
        }
    }

    private static int WarningStrength(ProfileSignOutRisk risk) =>
        risk.InspectionFailed ? 2 : risk.HasDataAtRisk ? 1 : 0;

    private bool Set<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
