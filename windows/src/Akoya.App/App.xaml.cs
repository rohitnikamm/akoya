using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Akoya.App.Platform;

namespace Akoya.App;

public partial class App : Application
{
    /// <summary>
    /// Set to 1 by CI: start with the model folder pointed at an empty temp dir, run for <see cref="SmokeDuration"/>, then
    /// exit normally (the model download just starts and is cancelled).
    /// </summary>
    public const string SmokeVariable = "AKOYA_SMOKE";

    public static readonly TimeSpan SmokeDuration = TimeSpan.FromSeconds(5);

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

        // Before the host starts: a second launch during startup is queued and handled once the host is up.
        if (_instance is not null)
            _instance.ShowRequested += () => Dispatcher.BeginInvoke(() => _host?.ShowPanel());

        var smoke = Environment.GetEnvironmentVariable(SmokeVariable) == "1";
        string? modelsRoot = null;
        if (smoke)
        {
            modelsRoot = Path.Combine(Path.GetTempPath(), "akoya-smoke-models-" + Environment.ProcessId);
            _log.Info("Smoke run: models at " + modelsRoot);
        }

        try
        {
            // Inside OnStartup: the dispatcher and its SynchronizationContext exist, which the ports capture.
            _host = new AppHost(AppComposition.CreatePorts(Dispatcher, () => ExitApp(0), _log, modelsRoot));
            _host.Start();
        }
        catch (Exception ex)
        {
            _log.Error("Akoya could not start", ex);
            ExitApp(1);
            return;
        }

        if (smoke)
        {
            var timer = new DispatcherTimer { Interval = SmokeDuration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _log.Info("Smoke run finished");
                ExitApp(0);
            };
            timer.Start();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows is logging off or shutting down: release everything before WPF closes the windows.
        DisposeHost();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeHost();
        base.OnExit(e);
    }

    /// <summary>
    /// Every exit goes through here: the host is disposed first (tray icon removed, hook released, setup dismissed
    /// without being marked done), then WPF shuts down and closes what is left.
    /// </summary>
    private void ExitApp(int code)
    {
        DisposeHost();
        Shutdown(code);
    }

    private void DisposeHost()
    {
        var host = _host;
        _host = null;
        try
        {
            host?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error("Shutdown failed", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the hotkey alive: a failed UI handler is logged, not fatal.
        _log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }
}
