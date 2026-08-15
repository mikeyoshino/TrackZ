using System.Windows.Input;

namespace TrackZ.Mobile.Features.Workout;

public interface IAsyncCommand : ICommand
{
    Task ExecuteAsync(object? parameter = null);
}

public sealed class AsyncCommand(
    Func<object?, Task> execute,
    Func<object?, bool>? canExecute = null) : IAsyncCommand
{
    private int _executing;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        Volatile.Read(ref _executing) == 0 && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter) || Interlocked.CompareExchange(ref _executing, 1, 0) != 0) return;
        RaiseCanExecuteChanged();
        try
        {
            await execute(parameter);
        }
        finally
        {
            Interlocked.Exchange(ref _executing, 0);
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
