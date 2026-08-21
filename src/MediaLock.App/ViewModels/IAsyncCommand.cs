using System.Windows.Input;

namespace MediaLock.App.ViewModels;

public interface IAsyncCommand : ICommand
{
    Task ExecuteAsync(object? parameter);
}

internal sealed class AsyncCommand(
    Func<object?, Task> execute,
    Predicate<object?>? canExecute = null) : IAsyncCommand
{
    private bool executing;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !executing && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter);
    }

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        executing = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute(parameter);
        }
        finally
        {
            executing = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
