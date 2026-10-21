using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Akoya.App.Tests.Snapshots;

/// <summary>
/// Renders WPF elements to PNG so UI can be reviewed from CI artifacts.
/// Output goes to <c>$AKOYA_SNAPSHOT_DIR/&lt;name&gt;.png</c>; with the variable unset, rendering is skipped.
/// </summary>
public static class SnapshotHarness
{
    public const string DirectoryVariable = "AKOYA_SNAPSHOT_DIR";

    private const double Scale = 2.0;
    private const double BaseDpi = 96.0;

    static SnapshotHarness()
    {
        // Register the pack:// URI scheme (normally done by System.Windows.Application) so tests can load
        // pack://application:,,,/Akoya.App;component/... resources, e.g. the theme, without an Application.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        // Run Application's static initialisation once, up front: it (re)registers the application:/// resource
        // package, and doing that lazily while another test thread loads a pack:// resource races ("The URI prefix
        // is not recognized").
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(System.Windows.Application).TypeHandle);
        if (typeof(System.Windows.Application).Assembly.GetType("System.Windows.Navigation.BaseUriHelper") is { } baseUriHelper)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(baseUriHelper.TypeHandle);
        }
    }

    /// <summary>
    /// Builds the element with <paramref name="factory"/> and renders it at 2x scale into a PNG, all on a
    /// dedicated STA thread (WPF elements have thread affinity, so creation must happen there too).
    /// The element is hosted in a container of exactly <paramref name="width"/> x <paramref name="height"/>,
    /// which is measured and arranged at that size. <paramref name="resources"/>, if given, builds a dictionary
    /// (also on the STA thread) that is merged into the host so the element can resolve theme resources
    /// without a running Application.
    /// </summary>
    public static void Render(
        Func<FrameworkElement> factory,
        double width,
        double height,
        string name,
        Func<ResourceDictionary>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        RunSta(() =>
        {
            var host = new Grid { Width = width, Height = height };
            if (resources is not null)
            {
                host.Resources.MergedDictionaries.Add(resources());
            }

            host.Children.Add(factory());
            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();

            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(width * Scale),
                (int)Math.Ceiling(height * Scale),
                BaseDpi * Scale,
                BaseDpi * Scale,
                PixelFormats.Pbgra32);
            bitmap.Render(host);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(stream);
        });
    }

    /// <summary>Runs <paramref name="action"/> on a new STA thread and rethrows any exception on the caller.</summary>
    public static void RunSta(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
