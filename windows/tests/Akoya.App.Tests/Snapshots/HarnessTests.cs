using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace Akoya.App.Tests.Snapshots;

public class HarnessTests
{
    [Fact]
    public void Render_WritesPng()
    {
        SnapshotHarness.RunSta(() =>
        {
            var element = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F6BFF")),
            };

            SnapshotHarness.Render(element, 100, 40, "harness-check");
        });

        var directory = Environment.GetEnvironmentVariable(SnapshotHarness.DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var png = new FileInfo(Path.Combine(directory, "harness-check.png"));
        Assert.True(png.Exists, $"{png.FullName} was not written");
        Assert.True(png.Length > 100, $"{png.FullName} is only {png.Length} bytes");
    }
}
