using System.Windows.Input;

namespace Akoya.App.Tray;

/// <summary>An always-enabled <see cref="ICommand"/> that runs a delegate with the command parameter.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;

    public RelayCommand(Action execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
    }

    public RelayCommand(Action<object?> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
    }

    // Never raised: the commands are always enabled.
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute(parameter);
}
