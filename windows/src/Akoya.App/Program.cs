using Akoya.App.Platform;
using Velopack;

namespace Akoya.App;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        try
        {
            // First, always: Velopack's install, update and uninstall hooks run here and exit. A copy that wasn't
            // installed by Setup.exe (dotnet run from a source checkout) just carries on. The Run entry isn't
            // Velopack's, so the uninstall hook removes it.
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ => StartupRegistration.Disable())
                .Run();

            SingleInstance? instance;
            try
            {
                if (!SingleInstance.TryAcquire(out instance))
                {
                    SingleInstance.SignalFirst(); // the running Akoya opens its tray panel
                    return 0;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // An elevated Akoya owns the names; we can't open them, but it is running.
                new FileLog(FileLog.DefaultDirectory).Info("Another Akoya (elevated) is already running; exiting");
                return 0;
            }

            using (instance)
            {
                var app = new App(instance);
                app.InitializeComponent();
                return app.Run();
            }
        }
        catch (Exception ex)
        {
            // Before App's handlers exist (or after they are gone): still leave a trace.
            new FileLog(FileLog.DefaultDirectory).Error("Akoya crashed", ex);
            return 1;
        }
    }
}
