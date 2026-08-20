using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Sync;

public interface IWorkoutSyncTrigger
{
    void NotifyMutation();
}

public interface IWorkoutSyncLifecycle
{
    void Start();
    void Resume();
    void Stop();
}

public interface ISyncAuthenticationRecovery
{
    Task<bool> TryRecoverAsync(CancellationToken cancellationToken = default);
}

public sealed class WorkoutSyncOrchestrator(
    SyncCoordinator workouts,
    CustomExerciseImageService customExercises,
    ExerciseCache exerciseCache,
    OutboxRepository outbox,
    IAccessTokenProvider tokens,
    IConnectivityService connectivity,
    IAccountSessionBoundary sessionBoundary,
    ISyncAuthenticationRecovery authenticationRecovery,
    TimeProvider timeProvider) : IWorkoutSyncTrigger, IWorkoutSyncLifecycle, IDisposable
{
    private readonly object _lifecycleGate = new();
    private RunState? _run;
    private bool _disposed;

    public void Start()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) return;
            var run = new RunState();
            _run = run;
            connectivity.ConnectivityChanged += OnConnectivityChanged;
            sessionBoundary.SessionReset += OnSessionReset;
            run.Worker = RunAsync(run);
        }
        Signal();
    }

    public void Resume()
    {
        Start();
        Signal();
    }

    public void Stop()
    {
        RunState? run;
        lock (_lifecycleGate)
        {
            run = _run;
            if (run is null) return;
            _run = null;
            connectivity.ConnectivityChanged -= OnConnectivityChanged;
            sessionBoundary.SessionReset -= OnSessionReset;
        }
        run.Lifetime.Cancel();
        Interlocked.Exchange(ref run.SignalPending, 0);
        _ = DisposeWhenCompleteAsync(run);
    }

    public void NotifyMutation() => Signal();

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }

    private async Task RunAsync(RunState run)
    {
        var cancellationToken = run.Lifetime.Token;
        DateTimeOffset? nextAttempt = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await WaitForSignalOrDueAsync(run, nextAttempt, cancellationToken);
                Interlocked.Exchange(ref run.SignalPending, 0);
                if (!connectivity.IsOnline)
                {
                    nextAttempt = null;
                    continue;
                }
                var token = await tokens.GetAccessTokenAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(token))
                {
                    nextAttempt = null;
                    continue;
                }
                var authenticationRecoveryAvailable = true;

                var customResult = await SynchronizeCustomAsync(cancellationToken);
                if (customResult == CustomSynchronizationResult.AuthenticationRequired)
                {
                    authenticationRecoveryAvailable = false;
                    if (!await authenticationRecovery.TryRecoverAsync(cancellationToken))
                    {
                        nextAttempt = null;
                        continue;
                    }
                    customResult = await SynchronizeCustomAsync(cancellationToken);
                    if (customResult == CustomSynchronizationResult.AuthenticationRequired)
                    {
                        nextAttempt = null;
                        continue;
                    }
                }
                if (await HasUnresolvedCustomDependencyAsync(cancellationToken))
                {
                    nextAttempt = customResult == CustomSynchronizationResult.Retryable
                        ? timeProvider.GetUtcNow().AddSeconds(1)
                        : null;
                    continue;
                }

                var status = await workouts.RunOnceAsync(cancellationToken);
                if (status == SyncRunStatus.AuthenticationRequired
                    && authenticationRecoveryAvailable)
                {
                    authenticationRecoveryAvailable = false;
                    if (await authenticationRecovery.TryRecoverAsync(cancellationToken))
                        status = await workouts.RunOnceAsync(cancellationToken);
                }
                var pending = await outbox.PendingAsync(cancellationToken);
                nextAttempt = pending
                    .Where(item => item.NextAttemptAt is not null)
                    .Select(item => item.NextAttemptAt)
                    .Min();
                if ((status == SyncRunStatus.RetryScheduled
                        || customResult == CustomSynchronizationResult.Retryable
                        || status == SyncRunStatus.PermanentFailure && pending.Count != 0)
                    && nextAttempt is null)
                    nextAttempt = timeProvider.GetUtcNow().AddSeconds(1);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                nextAttempt = null;
            }
            catch (Exception exception) when (
                exception is InvalidDataException or SyncApiException)
            {
                // A surfaced permanent/protocol failure waits for the next lifecycle or mutation trigger.
                nextAttempt = null;
            }
        }
    }

    private async Task WaitForSignalOrDueAsync(
        RunState run,
        DateTimeOffset? nextAttempt,
        CancellationToken cancellationToken)
    {
        if (nextAttempt is null)
        {
            await run.Signal.WaitAsync(cancellationToken);
            return;
        }

        var delay = nextAttempt.Value - timeProvider.GetUtcNow();
        if (delay <= TimeSpan.Zero) return;
        using var dueCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signal = run.Signal.WaitAsync(dueCancellation.Token);
        var due = Task.Delay(delay, timeProvider, dueCancellation.Token);
        _ = await Task.WhenAny(signal, due);
        dueCancellation.Cancel();
        try
        {
            await Task.WhenAll(signal, due);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Signal()
    {
        lock (_lifecycleGate)
        {
            if (_disposed || _run is null) return;
            if (Interlocked.Exchange(ref _run.SignalPending, 1) == 0)
                _run.Signal.Release();
        }
    }

    private void OnConnectivityChanged(object? sender, EventArgs eventArgs)
    {
        if (connectivity.IsOnline) Signal();
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs) => Signal();

    private async Task<bool> HasUnresolvedCustomDependencyAsync(
        CancellationToken cancellationToken)
    {
        var pending = await exerciseCache.GetPendingAsync(cancellationToken);
        var failed = await exerciseCache.GetFailedAsync(cancellationToken);
        var unresolvedIds = pending.Concat(failed)
            .Where(item => item.ServerExerciseId is null)
            .Select(item => item.LocalExerciseId)
            .ToHashSet();
        if (unresolvedIds.Count == 0) return false;

        var workoutOperations = await outbox.PendingAsync(cancellationToken);
        foreach (var operation in workoutOperations)
        {
            if (operation.Type == OutboxOperationType.StartWorkout
                && operation.DeserializePayload<StartWorkoutOutboxPayload>().Exercises
                    .Any(item => unresolvedIds.Contains(item.ExerciseDefinitionId)))
                return true;
            if (operation.Type == OutboxOperationType.AddExercise
                && unresolvedIds.Contains(
                    operation.DeserializePayload<AddExerciseOutboxPayload>().ExerciseDefinitionId))
                return true;
        }
        return false;
    }

    private async Task<CustomSynchronizationResult> SynchronizeCustomAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await customExercises.SynchronizePendingAsync(cancellationToken);
            return CustomSynchronizationResult.Completed;
        }
        catch (MobileApiException exception) when (exception.IsAuthenticationRequired)
        {
            return CustomSynchronizationResult.AuthenticationRequired;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or MobileApiException)
        {
            // Custom creation remains durable and must precede workouts that reference it.
            return CustomSynchronizationResult.Retryable;
        }
    }

    private static async Task DisposeWhenCompleteAsync(RunState run)
    {
        try
        {
            if (run.Worker is not null) await run.Worker;
        }
        catch
        {
            // The lifecycle already owns failure reporting; teardown only observes the task.
        }
        finally
        {
            run.Dispose();
        }
    }

    private sealed class RunState : IDisposable
    {
        public CancellationTokenSource Lifetime { get; } = new();
        public SemaphoreSlim Signal { get; } = new(0, 1);
        public Task? Worker { get; set; }
        public int SignalPending;

        public void Dispose()
        {
            Lifetime.Dispose();
            Signal.Dispose();
        }
    }

    private enum CustomSynchronizationResult
    {
        Completed,
        Retryable,
        AuthenticationRequired
    }
}
