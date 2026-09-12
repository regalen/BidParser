using System.Windows.Input;

namespace BidParser.Desktop.Mvvm;

public sealed class AsyncRelayCommand(
    Func<CancellationToken, Task> execute,
    Func<bool>? canExecute = null) : ICommand
{
    private CancellationTokenSource? cancellation;

    public event EventHandler? CanExecuteChanged;

    /// <summary>True while an invocation is in flight; the command refuses to re-enter.</summary>
    public bool IsRunning { get; private set; }

    public bool CanExecute(object? parameter) => !IsRunning && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        IsRunning = true;
        cancellation = new CancellationTokenSource();
        NotifyCanExecuteChanged();
        try
        {
            await execute(cancellation.Token);
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
            IsRunning = false;
            NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Requests cancellation of the in-flight invocation. Parsers are synchronous, so this is
    /// best-effort: the token is observed between stages and the eventual result is discarded.
    /// </summary>
    public void Cancel() => cancellation?.Cancel();

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
