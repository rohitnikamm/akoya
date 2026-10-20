using System.Windows;

namespace Akoya.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Skeleton only: no window yet. Later tasks replace this with the tray host.
        Shutdown();
    }
}
