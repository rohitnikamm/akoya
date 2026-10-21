using Akoya.App.Platform;
using Velopack;

namespace Akoya.App;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        // First, always: Velopack's install, update and uninstall hooks run here and exit. A copy that wasn't installed
        // by Setup.exe (dotnet run from a source checkout) just carries on. The Run entry isn't Velopack's, so the
        // uninstall hook removes it.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.Disable())
            .Run();

        if (!SingleInstance.TryAcquire(out var instance))
        {
            SingleInstance.SignalFirst(); // the running Akoya opens its tray panel
            return 0;
        }

        using (instance)
        {
            var app = new App(instance);
            app.InitializeComponent();
            return app.Run();
        }
    }
}
