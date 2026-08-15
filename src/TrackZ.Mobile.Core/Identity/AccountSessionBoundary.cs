using System.Runtime.ExceptionServices;

namespace TrackZ.Mobile.Identity;

public readonly record struct AccountSessionGeneration(long Value);

public sealed class AccountSessionCancellationLease : IDisposable
{
    private CancellationTokenSource? _linked;
    private Action? _release;

    internal AccountSessionCancellationLease(CancellationTokenSource linked, Action? release)
    {
        _linked = linked;
        _release = release;
        Token = linked.Token;
    }

    public CancellationToken Token { get; }

    public void Dispose()
    {
        var linked = Interlocked.Exchange(ref _linked, null);
        if (linked is null) return;
        try
        {
            linked.Dispose();
        }
        finally
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}

public interface IAccountSessionBoundary
{
    AccountSessionGeneration Capture();
    bool IsCancellationRequested(AccountSessionGeneration generation);
    AccountSessionCancellationLease CreateCancellationLease(
        AccountSessionGeneration generation,
        CancellationToken cancellationToken = default);
    bool TryStartSessionPhase(
        AccountSessionGeneration generation,
        Action phase,
        CancellationToken cancellationToken = default);
    event EventHandler? SessionReset;

    Task<bool> TryCommitAsync(
        AccountSessionGeneration generation,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the active generation and performs a serialized session reset. Caller cancellation
    /// can stop the operation before invalidation begins. Once invalidation begins, generation
    /// replacement, cleanup, and reset notification are non-abortable; a late caller cancellation is
    /// surfaced only after the boundary is fresh.
    /// </summary>
    Task ResetAsync(
        Func<CancellationToken, Task> reset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Conditionally performs the same atomic reset when <paramref name="generation"/> is still active.
    /// Returns <see langword="false"/> without mutation when another reset has already replaced it.
    /// Reset callback and notification failures are surfaced after the boundary reaches a fresh generation.
    /// </summary>
    Task<bool> TryResetAsync(
        AccountSessionGeneration generation,
        Func<CancellationToken, Task> reset,
        CancellationToken cancellationToken = default);
}

public sealed class AccountSessionBoundary : IAccountSessionBoundary
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _resetGate = new(1, 1);
    private readonly object _cancellationLock = new();
    private long _generation;
    private GenerationCancellation _generationCancellation = new();
    private bool _resetInProgress;

    public event EventHandler? SessionReset;

    public AccountSessionGeneration Capture() => new(Interlocked.Read(ref _generation));

    public bool IsCancellationRequested(AccountSessionGeneration generation)
    {
        lock (_cancellationLock)
        {
            return generation.Value != Interlocked.Read(ref _generation)
                || _resetInProgress
                || _generationCancellation.IsCancellationRequested;
        }
    }

    public AccountSessionCancellationLease CreateCancellationLease(
        AccountSessionGeneration generation,
        CancellationToken cancellationToken = default)
    {
        lock (_cancellationLock)
        {
            if (generation.Value != Interlocked.Read(ref _generation) || _resetInProgress)
                return new AccountSessionCancellationLease(
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, new CancellationToken(canceled: true)),
                    null);
            return _generationCancellation.CreateLease(cancellationToken);
        }
    }

    public bool TryStartSessionPhase(
        AccountSessionGeneration generation,
        Action phase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(phase);
        lock (_cancellationLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (generation.Value != Interlocked.Read(ref _generation)
                || _resetInProgress
                || _generationCancellation.IsCancellationRequested)
                return false;
            phase();
            return true;
        }
    }

    public async Task<bool> TryCommitAsync(
        AccountSessionGeneration generation,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            GenerationCancellation generationCancellation;
            lock (_cancellationLock)
            {
                if (generation.Value != Interlocked.Read(ref _generation)
                    || _resetInProgress
                    || _generationCancellation.IsCancellationRequested)
                    return false;
                generationCancellation = _generationCancellation;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, generationCancellation.Token);
            await mutation(linked.Token);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync(
        Func<CancellationToken, Task> reset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reset);
        _ = await ResetCoreAsync(null, reset, cancellationToken);
    }

    public async Task<bool> TryResetAsync(
        AccountSessionGeneration generation,
        Func<CancellationToken, Task> reset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reset);
        return await ResetCoreAsync(generation, reset, cancellationToken);
    }

    private async Task<bool> ResetCoreAsync(
        AccountSessionGeneration? expectedGeneration,
        Func<CancellationToken, Task> reset,
        CancellationToken cancellationToken)
    {
        await _resetGate.WaitAsync(cancellationToken);
        var failures = new List<Exception>();
        try
        {
            GenerationCancellation invalidated;
            lock (_cancellationLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (expectedGeneration is { } expected
                    && expected.Value != Interlocked.Read(ref _generation)) return false;
                _resetInProgress = true;
                invalidated = _generationCancellation;
            }

            try
            {
                invalidated.Cancel();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                await ResetUnderGateAsync(reset, failures);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            _resetGate.Release();
        }

        ThrowFailures(failures);
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private async Task ResetUnderGateAsync(
        Func<CancellationToken, Task> reset,
        List<Exception> failures)
    {
        var replacement = new GenerationCancellation();
        GenerationCancellation invalidated;
        lock (_cancellationLock)
        {
            invalidated = _generationCancellation;
            _generationCancellation = replacement;
            Interlocked.Increment(ref _generation);
            _resetInProgress = false;
        }

        var disposalFailure = invalidated.Retire();
        if (disposalFailure is not null) failures.Add(disposalFailure);

        try
        {
            await reset(CancellationToken.None);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        var handlers = SessionReset;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private static void ThrowFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 0) return;
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
            return;
        }
        throw new AggregateException("The account session reset failed.", failures);
    }

    private sealed class GenerationCancellation
    {
        private readonly object _lifetimeLock = new();
        private readonly CancellationTokenSource _source = new();
        private readonly CancellationToken _token;
        private int _leases;
        private bool _retired;
        private bool _disposed;

        public GenerationCancellation() => _token = _source.Token;

        public CancellationToken Token => _token;
        public bool IsCancellationRequested => _token.IsCancellationRequested;

        public AccountSessionCancellationLease CreateLease(CancellationToken cancellationToken)
        {
            lock (_lifetimeLock)
            {
                if (_retired) throw new InvalidOperationException("The account generation has retired.");
                _leases++;
            }
            try
            {
                return new AccountSessionCancellationLease(
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _token),
                    ReleaseLease);
            }
            catch
            {
                ReleaseLease();
                throw;
            }
        }

        public void Cancel() => _source.Cancel();

        public Exception? Retire()
        {
            CancellationTokenSource? disposable;
            lock (_lifetimeLock)
            {
                _retired = true;
                disposable = TakeDisposableSource();
            }
            if (disposable is null) return null;
            try
            {
                disposable.Dispose();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private void ReleaseLease()
        {
            CancellationTokenSource? disposable;
            lock (_lifetimeLock)
            {
                _leases--;
                disposable = TakeDisposableSource();
            }
            disposable?.Dispose();
        }

        private CancellationTokenSource? TakeDisposableSource()
        {
            if (!_retired || _leases != 0 || _disposed) return null;
            _disposed = true;
            return _source;
        }
    }
}
