using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Akoya.App.Platform;

namespace Akoya.App;

public partial class App : Application
{
    private readonly SingleInstance? _instance;
    private readonly FileLog _log = new(FileLog.DefaultDirectory);
    private AppHost? _host;

    public App() : this(null)
    {
    }

    internal App(SingleInstance? instance)
    {
        _instance = instance;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
            // Inside OnStartup: the dispatcher and its SynchronizationContext exist, which the ports capture.
            _host = new AppHost(AppComposition.CreatePorts(Dispatcher, Shutdown, _log));
            _host.Start();
        }
        catch (Exception ex)
        {
            _log.Error("Akoya could not start", ex);
            _host?.Dispose();
            _host = null;
            Shutdown(1);
            return;
        }

        if (_instance is not null)
            _instance.ShowRequested += () => Dispatcher.BeginInvoke(() => _host?.ShowPanel());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _host = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the hotkey alive: a failed UI handler is logged, not fatal.
        _log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }
}
