namespace TrackZ.Mobile.Identity;

public readonly record struct AccountSessionGeneration(long Value);

public interface IAccountSessionBoundary
{
    AccountSessionGeneration Capture();
    CancellationToken GetCancellationToken(AccountSessionGeneration generation);
    event EventHandler? SessionReset;

    Task<bool> TryCommitAsync(
        AccountSessionGeneration generation,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default);

    Task ResetAsync(
        Func<CancellationToken, Task> reset,
        CancellationToken cancellationToken = default);

    Task<bool> TryResetAsync(
        AccountSessionGeneration generation,
        Func<CancellationToken, Task> reset,
        CancellationToken cancellationToken = default);
}

public sealed class AccountSessionBoundary : IAccountSessionBoundary
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _cancellationLock = new();
    private long _generation;
    private CancellationTokenSource _generationCancellation = new();

    public event EventHandler? SessionReset;

    public AccountSessionGeneration Capture() => new(Interlocked.Read(ref _generation));

    public CancellationToken GetCancellationToken(AccountSessionGeneration generation)
    {
        lock (_cancellationLock)
        {
            return generation.Value == Interlocked.Read(ref _generation)
                ? _generationCancellation.Token
                : new CancellationToken(canceled: true);
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
            if (generation.Value != Interlocked.Read(ref _generation)) return false;
            await mutation(cancellationToken);
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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await ResetUnderGateAsync(reset, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryResetAsync(
        AccountSessionGeneration generation,
        Func<CancellationToken, Task> reset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reset);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (generation.Value != Interlocked.Read(ref _generation)) return false;
            await ResetUnderGateAsync(reset, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ResetUnderGateAsync(
        Func<CancellationToken, Task> reset,
        CancellationToken cancellationToken)
    {
        lock (_cancellationLock)
        {
            _generationCancellation.Cancel();
            Interlocked.Increment(ref _generation);
            _generationCancellation = new CancellationTokenSource();
        }
        await reset(cancellationToken);
        SessionReset?.Invoke(this, EventArgs.Empty);
    }
}
