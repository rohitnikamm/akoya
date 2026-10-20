using System.IO;
using System.Windows;
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

    /// <summary>
    /// Renders <paramref name="element"/> at 2x scale into a PNG. WPF elements have thread affinity, so
    /// the element must be created and rendered on the same STA thread (use <see cref="RunSta"/>).
    /// </summary>
    public static void Render(FrameworkElement element, double width, double height, string name)
    {
        ArgumentNullException.ThrowIfNull(element);

        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException(
                "SnapshotHarness.Render must run on an STA thread. Wrap the call (and element creation) in SnapshotHarness.RunSta.");
        }

        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * Scale),
            (int)Math.Ceiling(height * Scale),
            BaseDpi * Scale,
            BaseDpi * Scale,
            PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
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
