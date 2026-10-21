using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Akoya.App.Tests.Snapshots;

public class HarnessTests
{
    private static readonly Color Blue = Color.FromRgb(0x2F, 0x6B, 0xFF);

    [Fact]
    public void Render_WritesPng()
    {
        SnapshotHarness.Render(
            () => new Border { Background = new SolidColorBrush(Blue) },
            100, 40, "harness-check");

        var directory = Environment.GetEnvironmentVariable(SnapshotHarness.DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var png = new FileInfo(Path.Combine(directory, "harness-check.png"));
        Assert.True(png.Exists, $"{png.FullName} was not written");
        Assert.True(png.Length > 100, $"{png.FullName} is only {png.Length} bytes");
        Assert.Equal((200, 80), ReadSize(png.FullName));
    }

    [Fact]
    public void Render_ResolvesMergedResources_AndHonorsLayoutMargin()
    {
        SnapshotHarness.Render(
            () =>
            {
                var border = new Border { Margin = new Thickness(10) };
                border.SetResourceReference(Border.BackgroundProperty, "Accent");
                return border;
            },
            100, 40, "harness-resources",
            () => new ResourceDictionary { ["Accent"] = new SolidColorBrush(Blue) });

        var directory = Environment.GetEnvironmentVariable(SnapshotHarness.DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var path = Path.Combine(directory, "harness-resources.png");
        Assert.Equal((Blue.R, Blue.G, Blue.B, (byte)255), ReadPixel(path, 100, 40)); // center, from the resource
        Assert.Equal(0, ReadPixel(path, 2, 2).A);                                    // inside the 10px margin
    }

    private static (int Width, int Height) ReadSize(string path)
    {
        var size = (0, 0);
        SnapshotHarness.RunSta(() =>
        {
            var frame = Decode(path);
            size = (frame.PixelWidth, frame.PixelHeight);
        });
        return size;
    }

    private static (byte R, byte G, byte B, byte A) ReadPixel(string path, int x, int y)
    {
        var pixel = (R: (byte)0, G: (byte)0, B: (byte)0, A: (byte)0);
        SnapshotHarness.RunSta(() =>
        {
            var frame = new FormatConvertedBitmap(Decode(path), PixelFormats.Bgra32, null, 0);
            var bgra = new byte[4];
            frame.CopyPixels(new Int32Rect(x, y, 1, 1), bgra, 4, 0);
            pixel = (bgra[2], bgra[1], bgra[0], bgra[3]);
        });
        return pixel;
    }

    private static BitmapSource Decode(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }
}
