using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Akoya.App.Pill;
using Akoya.Core.Dictation;
using Xunit;

namespace Akoya.App.Tests.Snapshots;

/// <summary>
/// The pill in each state, on a 420×120 crop of the mockup's desktop (docs/design/windows-pill-mockup.html, the
/// area around the pill with the taskbar hidden), for side-by-side comparison with the mockup.
/// </summary>
public class PillSnapshots
{
    private const double Width = 420;
    private const double Height = 120;

    private static readonly int[] Steps = [1, 2, 3, 5, 7, 5, 3, 2, 3, 5, 7, 8, 7, 5, 3, 2, 3, 5, 7, 5, 3, 2, 1];

    internal static ResourceDictionary Theme() =>
        new() { Source = new Uri("pack://application:,,,/Akoya.App;component/Theme/Theme.xaml") };

    [Fact]
    public void Listening()
    {
        Render("pill-listening", () =>
        {
            var vm = new PillViewModel();
            vm.Apply(DictationPhase.Recording);
            vm.SetLevels(Steps);
            vm.SetElapsed(TimeSpan.FromSeconds(3));
            return vm;
        });
    }

    [Fact]
    public void Transcribing()
    {
        Render("pill-transcribing", () =>
        {
            var vm = new PillViewModel();
            vm.Apply(DictationPhase.Recording);
            vm.SetElapsed(TimeSpan.FromSeconds(4.2));
            vm.Apply(DictationPhase.Transcribing);
            vm.SetElapsed(TimeSpan.FromSeconds(5)); // frozen while transcribing
            vm.Tick(0.3);
            return vm;
        });
    }

    [Fact]
    public void Pasted() => Render("pill-pasted", () => Make(DictationPhase.Pasted));

    [Fact]
    public void Copied() => Render("pill-copied", () => Make(DictationPhase.Copied));

    [Fact]
    public void AlertMic() => Render("pill-alert-mic", () => Make(DictationPhase.MicMissing));

    [Fact]
    public void AlertModelNotReady() => Render("pill-alert-model-not-ready", () => Make(DictationPhase.ModelNotReady));

    private static PillViewModel Make(DictationPhase phase)
    {
        var vm = new PillViewModel();
        vm.Apply(phase);
        return vm;
    }

    private static void Render(string name, Func<PillViewModel> model)
    {
        SnapshotHarness.Render(
            () =>
            {
                var scene = new Grid { ClipToBounds = true };
                scene.Children.Add(Desk());
                scene.Children.Add(new PillView
                {
                    DataContext = model(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                return scene;
            },
            Width, Height, name, Theme);
    }

    /// <summary>
    /// The mockup's <c>.desk</c> background (1200×675), shifted so the 420×120 window shows the area around the pill
    /// (pill centre at desk (600, 592)).
    /// </summary>
    internal static FrameworkElement Desk()
    {
        var canvas = new Canvas();
        void Layer(Brush brush) => canvas.Children.Add(Shift(new Rectangle { Width = 1200, Height = 675, Fill = brush }));

        Layer(new LinearGradientBrush(Rgb(0x05, 0x07, 0x0F), Rgb(0x0A, 0x12, 0x36), 90));
        Layer(Radial(1020, 540, 540, 371.25, Rgb(0x1A, 0x2C, 0x7A), 0.7));
        Layer(Radial(240, 607.5, 480, 337.5, Rgb(0x13, 0x30, 0x8F), 0.7));
        Layer(Radial(600, 776.25, 600, 405, Rgb(0x2B, 0x5C, 0xFF), 0.6));
        return canvas;

        static UIElement Shift(UIElement element)
        {
            Canvas.SetLeft(element, -(600 - Width / 2));
            Canvas.SetTop(element, -(592 - Height / 2));
            return element;
        }
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>CSS <c>radial-gradient(ellipse ... at x y, color 0%, transparent stop)</c>.</summary>
    private static RadialGradientBrush Radial(double cx, double cy, double rx, double ry, Color color, double stop) => new()
    {
        MappingMode = BrushMappingMode.Absolute,
        Center = new Point(cx, cy),
        GradientOrigin = new Point(cx, cy),
        RadiusX = rx,
        RadiusY = ry,
        // CSS interpolates in premultiplied space, so fade to the same colour at alpha 0, not to transparent black.
        GradientStops = { new GradientStop(color, 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), stop) },
    };
}
