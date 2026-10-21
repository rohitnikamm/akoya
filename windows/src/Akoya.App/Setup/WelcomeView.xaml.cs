using System.Windows.Controls;

namespace Akoya.App.Setup;

/// <summary>Setup step 1.</summary>
public partial class WelcomeView : UserControl
{
    /// <summary>The dot logo (columns 2-4-6-4-2, centred) as a 5 × 6 <see cref="Controls.DotGrid"/>, column-major.</summary>
    public static readonly double[] LogoDots = Logo([2, 4, 6, 4, 2], rows: 6);

    public WelcomeView()
    {
        InitializeComponent();
    }

    private static double[] Logo(int[] columns, int rows)
    {
        var opacities = new double[columns.Length * rows];
        for (var column = 0; column < columns.Length; column++)
        {
            var first = (rows - columns[column]) / 2;
            for (var row = first; row < first + columns[column]; row++) opacities[column * rows + row] = 1;
        }
        return opacities;
    }
}
