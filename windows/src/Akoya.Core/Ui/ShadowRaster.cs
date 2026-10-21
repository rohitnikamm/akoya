namespace Akoya.Core.Ui;

/// <summary>An sRGB colour with straight (not premultiplied) alpha 0..1, as in CSS <c>rgba()</c>.</summary>
public readonly record struct ShadowColor(byte R, byte G, byte B, double A);

/// <summary>One CSS <c>box-shadow</c>: <c>[inset] &lt;x&gt; &lt;y&gt; &lt;blur&gt; &lt;spread&gt; &lt;color&gt;</c>, lengths in DIP.</summary>
public readonly record struct BoxShadow(double X, double Y, double Blur, double Spread, ShadowColor Color, bool Inset = false);

/// <summary>
/// A rendered shadow layer: premultiplied BGRA32 pixels covering the DIP rectangle
/// (<see cref="Left"/>, <see cref="Top"/>, <see cref="Width"/>, <see cref="Height"/>) relative to the element's top-left.
/// </summary>
public sealed record ShadowImage(int PixelWidth, int PixelHeight, byte[] Pixels, double Left, double Top, double Width, double Height);

/// <summary>
/// Paints CSS box-shadows of a rounded rectangle the way browsers do, so the WPF views can match the HTML mockups:
/// the shadow shape is the box grown (outer) or shrunk (inset) by the spread and moved by the offset, its corner
/// radius adjusted by the spread, blurred with a Gaussian of sigma = blur / 2, and clipped outside the box (outer)
/// or inside it (inset). The first shadow in the list is on top, as in CSS.
/// </summary>
public static class ShadowRaster
{
    /// <summary>Renders the inset shadows in <paramref name="shadows"/> (others are ignored), or null if there are none.</summary>
    public static ShadowImage? RenderInset(double width, double height, double radius, IReadOnlyList<BoxShadow> shadows, double scale) =>
        Render(width, height, radius, shadows, inset: true, scale);

    /// <summary>Renders the outer shadows in <paramref name="shadows"/> (others are ignored), or null if there are none.</summary>
    public static ShadowImage? RenderOuter(double width, double height, double radius, IReadOnlyList<BoxShadow> shadows, double scale) =>
        Render(width, height, radius, shadows, inset: false, scale);

    private static ShadowImage? Render(double width, double height, double radius, IReadOnlyList<BoxShadow> shadows, bool inset, double scale)
    {
        ArgumentNullException.ThrowIfNull(shadows);
        if (!(width > 0) || !(height > 0) || !(scale > 0)) return null;
        var layers = shadows.Where(s => s.Inset == inset && s.Color.A > 0).ToArray();
        if (layers.Length == 0) return null;

        var boxW = width * scale;
        var boxH = height * scale;
        var boxR = Math.Clamp(radius * scale, 0, Math.Min(boxW, boxH) / 2);

        // The image covers the box (inset) or the box plus room for the furthest-reaching outer shadow.
        var pad = inset ? 0 : layers.Max(s => (int)Math.Ceiling((Math.Max(0, s.Spread) + Math.Max(Math.Abs(s.X), Math.Abs(s.Y))) * scale + 3 * Sigma(s, scale)) + 2);
        var imageW = (int)Math.Ceiling(boxW) + 2 * pad;
        var imageH = (int)Math.Ceiling(boxH) + 2 * pad;

        // The box itself in image pixels, used to clip each layer.
        var box = Coverage(imageW, imageH, pad, pad, pad + boxW, pad + boxH, boxR);

        var r = new float[imageW * imageH];
        var g = new float[imageW * imageH];
        var b = new float[imageW * imageH];
        var a = new float[imageW * imageH];

        // CSS paints the first shadow on top, so composite from the last one up.
        for (var i = layers.Length - 1; i >= 0; i--)
        {
            var s = layers[i];
            var value = inset ? InsetLayer(s, imageW, imageH, boxW, boxH, radius, scale) : OuterLayer(s, imageW, imageH, pad, boxW, boxH, radius, scale);
            var alpha = (float)Math.Clamp(s.Color.A, 0, 1);
            var cr = s.Color.R / 255f;
            var cg = s.Color.G / 255f;
            var cb = s.Color.B / 255f;
            for (var p = 0; p < value.Length; p++)
            {
                var clip = inset ? box[p] : 1 - box[p];
                var sa = value[p] * clip * alpha;
                if (sa <= 0) continue;
                var keep = 1 - sa;
                r[p] = cr * sa + r[p] * keep;
                g[p] = cg * sa + g[p] * keep;
                b[p] = cb * sa + b[p] * keep;
                a[p] = sa + a[p] * keep;
            }
        }

        var pixels = new byte[imageW * imageH * 4];
        for (var p = 0; p < a.Length; p++)
        {
            pixels[p * 4 + 0] = ToByte(b[p]);
            pixels[p * 4 + 1] = ToByte(g[p]);
            pixels[p * 4 + 2] = ToByte(r[p]);
            pixels[p * 4 + 3] = ToByte(a[p]);
        }

        return new ShadowImage(imageW, imageH, pixels, -pad / scale, -pad / scale, imageW / scale, imageH / scale);
    }

    private static double Sigma(BoxShadow s, double scale) => Math.Max(0, s.Blur) / 2 * scale;

    /// <summary>Inset layer over the box-sized image: everything outside the shrunken, offset hole, blurred.</summary>
    private static float[] InsetLayer(BoxShadow s, int imageW, int imageH, double boxW, double boxH, double radius, double scale)
    {
        var sigma = Sigma(s, scale);
        // Work on a larger grid so the blur sees the "outside = shadow" region past the box edges.
        var margin = (int)Math.Ceiling(3 * sigma + (Math.Max(Math.Abs(s.X), Math.Abs(s.Y)) + Math.Max(0, -s.Spread)) * scale) + 2;
        var gridW = imageW + 2 * margin;
        var gridH = imageH + 2 * margin;

        var left = margin + (s.Spread + s.X) * scale;
        var top = margin + (s.Spread + s.Y) * scale;
        var right = margin + boxW + (s.X - s.Spread) * scale;
        var bottom = margin + boxH + (s.Y - s.Spread) * scale;
        var holeR = Math.Max(0, radius - s.Spread) * scale;

        var hole = right > left && bottom > top
            ? Coverage(gridW, gridH, left, top, right, bottom, holeR)
            : new float[gridW * gridH];
        for (var p = 0; p < hole.Length; p++) hole[p] = 1 - hole[p];

        Blur(hole, gridW, gridH, sigma);

        var value = new float[imageW * imageH];
        for (var y = 0; y < imageH; y++)
        {
            Array.Copy(hole, (y + margin) * gridW + margin, value, y * imageW, imageW);
        }
        return value;
    }

    /// <summary>Outer layer: the grown, offset box, blurred.</summary>
    private static float[] OuterLayer(BoxShadow s, int imageW, int imageH, int pad, double boxW, double boxH, double radius, double scale)
    {
        var left = pad + (s.X - s.Spread) * scale;
        var top = pad + (s.Y - s.Spread) * scale;
        var right = pad + boxW + (s.X + s.Spread) * scale;
        var bottom = pad + boxH + (s.Y + s.Spread) * scale;
        var shapeR = Math.Max(0, radius + s.Spread) * scale;

        var shape = right > left && bottom > top
            ? Coverage(imageW, imageH, left, top, right, bottom, shapeR)
            : new float[imageW * imageH];
        Blur(shape, imageW, imageH, Sigma(s, scale));
        return shape;
    }

    /// <summary>Anti-aliased coverage (0..1) of a rounded rectangle, sampled at pixel centres.</summary>
    private static float[] Coverage(int w, int h, double left, double top, double right, double bottom, double radius)
    {
        var cover = new float[w * h];
        var cx = (left + right) / 2;
        var cy = (top + bottom) / 2;
        var hx = (right - left) / 2;
        var hy = (bottom - top) / 2;
        var rad = Math.Clamp(radius, 0, Math.Min(hx, hy));
        for (var y = 0; y < h; y++)
        {
            var qy = Math.Abs(y + 0.5 - cy) - hy + rad;
            for (var x = 0; x < w; x++)
            {
                var qx = Math.Abs(x + 0.5 - cx) - hx + rad;
                var ox = Math.Max(qx, 0);
                var oy = Math.Max(qy, 0);
                var outside = Math.Sqrt(ox * ox + oy * oy);
                var distance = outside + Math.Min(Math.Max(qx, qy), 0) - rad;
                cover[y * w + x] = (float)Math.Clamp(0.5 - distance, 0, 1);
            }
        }
        return cover;
    }

    /// <summary>Gaussian blur approximated by three box blurs (as browsers do for shadows); edges extend.</summary>
    private static void Blur(float[] data, int w, int h, double sigma)
    {
        if (sigma < 0.25) return;
        var scratch = new float[data.Length];
        foreach (var size in BoxSizes(sigma, 3))
        {
            var radius = (size - 1) / 2;
            BoxPass(data, scratch, w, h, radius, horizontal: true);
            BoxPass(scratch, data, w, h, radius, horizontal: false);
        }
    }

    private static int[] BoxSizes(double sigma, int n)
    {
        var ideal = Math.Sqrt(12 * sigma * sigma / n + 1);
        var lower = (int)Math.Floor(ideal);
        if (lower % 2 == 0) lower--;
        var upper = lower + 2;
        var m = (int)Math.Round((12 * sigma * sigma - n * lower * lower - 4 * n * lower - 3 * n) / (-4.0 * lower - 4));
        var sizes = new int[n];
        for (var i = 0; i < n; i++) sizes[i] = i < m ? lower : upper;
        return sizes;
    }

    private static void BoxPass(float[] source, float[] target, int w, int h, int radius, bool horizontal)
    {
        var length = horizontal ? w : h;
        var lines = horizontal ? h : w;
        var stride = horizontal ? 1 : w;
        var step = horizontal ? w : 1;
        var norm = 1f / (2 * radius + 1);
        for (var line = 0; line < lines; line++)
        {
            var start = line * step;
            double sum = 0;
            for (var k = -radius; k <= radius; k++) sum += source[start + Math.Clamp(k, 0, length - 1) * stride];
            for (var i = 0; i < length; i++)
            {
                target[start + i * stride] = (float)(sum * norm);
                var add = Math.Min(i + radius + 1, length - 1);
                var drop = Math.Max(i - radius, 0);
                sum += source[start + add * stride] - source[start + drop * stride];
            }
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
}
