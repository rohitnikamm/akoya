using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Akoya.Core.Ui;

namespace Akoya.App.Controls;

/// <summary>A rasterised shadow layer and where it goes, DIP relative to the element's top-left.</summary>
public sealed record RimGlowLayer(ImageSource Image, Rect Bounds);

/// <summary>The outer (below the background) and inset (above it) shadow layers of one element size and recipe.</summary>
public sealed record RimGlowImages(RimGlowLayer? Outer, RimGlowLayer? Inset);

/// <summary>
/// Process-wide cache of rasterised rim glows, keyed by size, corner radius, raster scale and recipe (by reference,
/// so use the static recipes in <see cref="RimGlowRecipes"/>). The bitmaps are frozen, so <see cref="PrewarmAsync"/>
/// can rasterise on a worker thread ahead of time and the UI thread only looks them up.
/// </summary>
public static class RimGlowCache
{
    private const int Capacity = 64;

    private static readonly object Gate = new();
    private static readonly Dictionary<Key, Lazy<RimGlowImages>> Entries = new();
    private static readonly LinkedList<Key> Order = new(); // least recently used first

    /// <summary>The raster scale an element uses: its DPI scale, but at least 2x so 2x snapshots stay crisp.</summary>
    public static double ScaleFor(Visual visual) => Math.Max(2.0, VisualTreeHelper.GetDpi(visual).DpiScaleX);

    [ThreadStatic]
    private static int t_misses;

    /// <summary>How many <see cref="Get"/> calls on the current thread found nothing ready and had to wait for a raster.</summary>
    public static int MissesOnCurrentThread => t_misses;

    /// <summary>Returns the cached layers, rasterising them now (on the calling thread) if they aren't cached yet.</summary>
    public static RimGlowImages Get(double width, double height, double radius, double scale, IReadOnlyList<BoxShadow> recipe)
    {
        var entry = Entry(new Key(width, height, radius, scale, recipe));
        if (!entry.IsValueCreated) t_misses++;
        return entry.Value;
    }

    /// <summary>Rasterises the layers on a worker thread so a later <see cref="Get"/> finds them.</summary>
    public static Task PrewarmAsync(double width, double height, double radius, double scale, IReadOnlyList<BoxShadow> recipe)
    {
        var entry = Entry(new Key(width, height, radius, scale, recipe));
        return entry.IsValueCreated ? Task.CompletedTask : Task.Run(() => _ = entry.Value);
    }

    /// <summary>True when the layers for these parameters are rasterised and cached.</summary>
    public static bool Contains(double width, double height, double radius, double scale, IReadOnlyList<BoxShadow> recipe)
    {
        lock (Gate)
        {
            return Entries.TryGetValue(new Key(width, height, radius, scale, recipe), out var entry) && entry.IsValueCreated;
        }
    }

    private static Lazy<RimGlowImages> Entry(Key key)
    {
        ArgumentNullException.ThrowIfNull(key.Recipe);
        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var existing))
            {
                Order.Remove(key);
                Order.AddLast(key);
                return existing;
            }

            var created = new Lazy<RimGlowImages>(() => Rasterise(key), LazyThreadSafetyMode.ExecutionAndPublication);
            Entries[key] = created;
            Order.AddLast(key);
            while (Entries.Count > Capacity && Order.First is { } oldest)
            {
                Order.RemoveFirst();
                Entries.Remove(oldest.Value);
            }
            return created;
        }
    }

    private static RimGlowImages Rasterise(Key key) => new(
        ToLayer(ShadowRaster.RenderOuter(key.Width, key.Height, key.Radius, key.Recipe, key.Scale), key.Scale),
        ToLayer(ShadowRaster.RenderInset(key.Width, key.Height, key.Radius, key.Recipe, key.Scale), key.Scale));

    private static RimGlowLayer? ToLayer(ShadowImage? image, double scale)
    {
        if (image is null) return null;
        var bitmap = BitmapSource.Create(
            image.PixelWidth, image.PixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null,
            image.Pixels, image.PixelWidth * 4);
        bitmap.Freeze();
        return new RimGlowLayer(bitmap, new Rect(image.Left, image.Top, image.Width, image.Height));
    }

    /// <summary>Cache key; the recipe compares by reference.</summary>
    private readonly record struct Key(double Width, double Height, double Radius, double Scale, IReadOnlyList<BoxShadow> Recipe)
    {
        public bool Equals(Key other) =>
            Width == other.Width && Height == other.Height && Radius == other.Radius && Scale == other.Scale
            && ReferenceEquals(Recipe, other.Recipe);

        public override int GetHashCode() =>
            HashCode.Combine(Width, Height, Radius, Scale, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Recipe));
    }
}
