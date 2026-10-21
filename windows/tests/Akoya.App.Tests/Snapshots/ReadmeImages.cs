using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using Akoya.App.Pill;
using Akoya.App.Setup;
using Akoya.App.Tests.Setup;
using Akoya.App.Tray;
using Akoya.Core.Dictation;
using Akoya.Core.Ui;
using Xunit;

namespace Akoya.App.Tests.Snapshots;

/// <summary>
/// The Windows images in the README, drawn by the app's own views (pill, tray panel, setup) on a staged desktop.
/// Only runs with <c>AKOYA_README_DIR</c> set:
/// <c>$env:AKOYA_README_DIR="$PWD\docs\assets"; dotnet test windows/tests/Akoya.App.Tests -c Release --filter ReadmeImages</c>
/// from the repo root (an absolute path: tests run from their build folder).
/// </summary>
public class ReadmeImages
{
    public const string DirectoryVariable = "AKOYA_README_DIR";

    private const string Dictated = "Add a retry to the upload step and log the status code when it fails.";
    private const string Earlier = "Rename the config loader to SettingsFile and update the tests.";
    private const double TaskbarHeight = 48;
    private const double GapAboveTaskbar = 52; // PillPlacement.GapAboveTaskbarDip

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");

    private static string? OutputDirectory =>
        Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } dir ? dir : null;

    /// <summary>The hero: a terminal, the pill listening above the taskbar and the tray panel open.</summary>
    [Fact]
    public void Hero()
    {
        if (OutputDirectory is not { } dir) return;
        const double w = 1200, h = 600;
        SnapshotHarness.RunSta(() =>
        {
            var pill = new PillViewModel();
            pill.Apply(DictationPhase.Recording);
            pill.SetLevels([1, 2, 3, 5, 7, 5, 3, 2, 3, 5, 7, 8, 7, 5, 3, 2, 3, 5, 7, 5, 3, 2, 1]);
            pill.SetElapsed(TimeSpan.FromSeconds(3));

            var tray = TrayPanelSnapshots.Model(vm =>
            {
                vm.SetPhase(DictationPhase.Recording);
                vm.SetVoiceLevel(0.8);
                vm.SetLastDictation(Earlier);
            });

            var desk = Desktop(w, h, out var canvas);
            var terminal = Terminal(560, 330, [Line.Prompt("claude"), Line.Banner(), Line.Blank(), Line.Sent(Earlier),
                Line.Reply("Done. SettingsFile loads and saves atomically; 12 tests updated."), Line.Blank(), Line.Input("", cursor: true)]);
            Place(canvas, terminal, 120, 70);
            var pillView = new PillView { DataContext = pill };
            Place(canvas, pillView, 0, 0);
            var panel = new TrayPanelView { DataContext = tray };
            Place(canvas, panel, 0, 0);
            Layout(desk, w, h);
            Centre(pillView, w, h);
            Canvas.SetLeft(panel, w - 12 - panel.ActualWidth);
            Canvas.SetTop(panel, h - TaskbarHeight - 12 - panel.ActualHeight);

            Save(Render(desk, w, h, 2), Path.Combine(dir, "windows-hero.png"));
        });
    }

    /// <summary>Setup's three steps side by side: Welcome, Microphone, Try it.</summary>
    [Fact]
    public void Setup()
    {
        if (OutputDirectory is not { } dir) return;
        SnapshotHarness.RunSta(() =>
        {
            var welcome = new SetupRig(Devices.Laptop(), Devices.Buds(), Devices.Webcam());

            var mic = new SetupRig(Devices.Laptop(), Devices.Buds(), Devices.Webcam());
            mic.Model.GetStarted();
            mic.Talk(0.4, ("laptop", 7), ("buds", 1), ("cam", 3));

            var tryIt = new SetupRig(Devices.Laptop(), Devices.Buds(), Devices.Webcam());
            tryIt.Model.GetStarted();
            tryIt.Model.Continue();
            tryIt.Practice.Phase(DictationPhase.Recording);
            foreach (var step in new[] { 1, 2, 3, 5, 6, 5, 3, 2, 3, 5, 6, 7, 6, 5, 3, 2, 3, 5, 6, 5, 3, 2, 1 })
                tryIt.Practice.Rms((float)Math.Pow(10, (-50 + (step - 0.5) * 40 / 7.0) / 20));
            tryIt.Practice.Text("Okay, this is actually really smooth.", false);

            const double w = 1260, h = 600;
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            foreach (var rig in new[] { welcome, mic, tryIt })
                row.Children.Add(new SetupView { DataContext = rig.Model, Margin = new Thickness(15, 0, 15, 0), VerticalAlignment = VerticalAlignment.Center });
            var page = new Grid();
            page.Resources.MergedDictionaries.Add(PillSnapshots.Theme());
            page.Children.Add(new Rectangle { Fill = DeskBrush(w, h) });
            page.Children.Add(row);

            Save(Render(page, w, h, 2), Path.Combine(dir, "windows-setup.png"));
        });
    }

    /// <summary>
    /// One dictation, animated (APNG, loops): the pill pops in, listens, sweeps, says Pasted and fades, and the words
    /// land in the terminal.
    /// </summary>
    [Fact]
    public void Dictation()
    {
        if (OutputDirectory is not { } dir) return;
        const double w = 960, h = 540, scale = 1.5, fps = 30;
        SnapshotHarness.RunSta(() =>
        {
            var pill = new PillViewModel();
            var view = new PillView { DataContext = pill };
            var fade = new ScaleTransform();
            var sink = new TranslateTransform();
            var holder = new Border
            {
                Child = view,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { fade, sink } },
                Visibility = Visibility.Hidden,
            };

            var desk = Desktop(w, h, out var canvas);
            var input = new TextBlock();
            var terminal = Terminal(700, 250, [Line.Prompt("claude"), Line.Banner(), Line.Blank(), Line.Custom(input)]);
            Place(canvas, terminal, (w - 700) / 2, 60);

            // Timeline, seconds.
            const double popAt = 0.8, stopAt = 3.6, pasteAt = 4.9, fadeAt = 5.8, end = 7.6;
            var levels = new Queue<int>(Enumerable.Repeat(1, PillViewModel.Columns));
            var random = new Random(7);
            var nextLevel = 0.0;
            var frames = new List<(byte[] Pixels, double Seconds)>();

            for (var i = 0; i * (1 / fps) < end; i++)
            {
                var t = i / fps;
                if (t >= popAt && holder.Visibility != Visibility.Visible)
                {
                    pill.Apply(DictationPhase.Recording);
                    holder.Visibility = Visibility.Visible;
                }

                if (t >= popAt && t < stopAt)
                {
                    view.PopInProgress = Math.Min(1, (t - popAt) / 0.42);
                    while (nextLevel <= t - popAt)
                    {
                        // Speech-like: syllables with short pauses, levels on the meter's 1..8 steps.
                        var syllable = Math.Abs(Math.Sin((t - popAt) * 7.3)) * (0.6 + 0.4 * random.NextDouble());
                        var pause = (int)((t - popAt) / 0.9) % 3 == 2 && (t - popAt) % 0.9 > 0.6;
                        levels.Dequeue();
                        levels.Enqueue(pause ? 1 : Math.Clamp((int)Math.Round(1 + syllable * 7), 1, 8));
                        nextLevel += 0.04;
                    }
                    pill.SetLevels([.. levels]);
                    pill.SetElapsed(TimeSpan.FromSeconds(t - popAt));
                }
                else if (t >= stopAt && t < pasteAt)
                {
                    if (pill.Mode != PillMode.Transcribing) pill.Apply(DictationPhase.Transcribing);
                    pill.Tick(t - stopAt);
                }
                else if (t >= pasteAt)
                {
                    if (pill.Mode != PillMode.Pasted) pill.Apply(DictationPhase.Pasted);
                    var p = Math.Clamp((t - fadeAt) / 0.28, 0, 1);
                    var e = CubicBezier.Evaluate(0.2, 0.8, 0.2, 1, p);
                    holder.Opacity = 1 - e;
                    sink.Y = 10 * e;
                    fade.ScaleX = fade.ScaleY = 1 - 0.06 * e;
                }

                SetInput(input, t >= pasteAt ? Dictated : "", cursorOn: (int)(t / 0.53) % 2 == 0);
                if (i == 0) Place(canvas, holder, 0, 0);
                Layout(desk, w, h);
                Centre(holder, w, h);

                var frame = Render(desk, w, h, scale);
                frames.Add((Pixels(frame), t));
                // For checking by eye: a still every half second when asked for.
                if (Environment.GetEnvironmentVariable("AKOYA_README_STILLS") is { Length: > 0 } && i % 15 == 0)
                    Save(frame, Path.Combine(dir, "stills", $"dictation-{t:0.0}s.png"));
            }

            WriteApng(Path.Combine(dir, "windows-dictation.png"), frames, (int)(w * scale), (int)(h * scale), end);
        });
    }

    // ---- Staging ----

    private static Grid Desktop(double w, double h, out Canvas canvas)
    {
        var root = new Grid { Width = w, Height = h, ClipToBounds = true };
        root.Resources.MergedDictionaries.Add(PillSnapshots.Theme());
        root.Children.Add(new Rectangle { Fill = DeskBrush(w, h) });
        canvas = new Canvas();
        root.Children.Add(canvas);
        var taskbar = Taskbar(w);
        Canvas.SetTop(taskbar, h - TaskbarHeight);
        canvas.Children.Add(taskbar);
        return root;
    }

    /// <summary>The pill mockup's desktop (docs/design/windows-pill-mockup.html .desk) stretched to the scene.</summary>
    private static Brush DeskBrush(double w, double h)
    {
        var group = new DrawingGroup();
        void Layer(Brush brush) => group.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, w, h))));
        Layer(new LinearGradientBrush(Color.FromRgb(0x05, 0x07, 0x0F), Color.FromRgb(0x0A, 0x12, 0x36), 90));
        Layer(Radial(w * 0.85, h * 0.8, w * 0.45, h * 0.55, Color.FromRgb(0x1A, 0x2C, 0x7A), 0.7));
        Layer(Radial(w * 0.2, h * 0.9, w * 0.4, h * 0.5, Color.FromRgb(0x13, 0x30, 0x8F), 0.7));
        Layer(Radial(w * 0.5, h * 1.15, w * 0.5, h * 0.6, Color.FromRgb(0x2B, 0x5C, 0xFF), 0.6));
        return new DrawingBrush(group) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h), ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, w, h) };

        static RadialGradientBrush Radial(double cx, double cy, double rx, double ry, Color color, double stop) => new()
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(cx, cy),
            GradientOrigin = new Point(cx, cy),
            RadiusX = rx,
            RadiusY = ry,
            GradientStops = { new GradientStop(color, 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), stop) },
        };
    }

    /// <summary>A plain Windows 11 taskbar: centred Start, search and a few apps, the overflow chevron and the clock.</summary>
    private static FrameworkElement Taskbar(double w)
    {
        var bar = new Grid { Width = w, Height = TaskbarHeight, Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x17, 0x22)) };
        bar.Children.Add(new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top, Fill = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)) });

        var centre = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var start = new UniformGrid2x2();
        centre.Children.Add(Tile(start, highlighted: false));
        centre.Children.Add(new Border
        {
            Width = 190, Height = 32, CornerRadius = new CornerRadius(16), Margin = new Thickness(6, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            Child = new TextBlock { Text = "Search", FontFamily = Ui, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) },
        });
        centre.Children.Add(Tile(AppIcon(Color.FromRgb(0xF5, 0xB8, 0x41), Color.FromRgb(0xE8, 0x93, 0x1C)), false)); // files
        centre.Children.Add(Tile(AppIcon(Color.FromRgb(0x3B, 0x9C, 0xFF), Color.FromRgb(0x1D, 0x6F, 0xE0)), false)); // browser
        centre.Children.Add(Tile(TerminalIcon(), highlighted: true));
        centre.Children.Add(Tile(AppIcon(Color.FromRgb(0x8B, 0x6C, 0xFF), Color.FromRgb(0x5B, 0x3F, 0xE0)), false)); // editor
        bar.Children.Add(centre);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        right.Children.Add(new TextBlock { Text = "\uE70E", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, Foreground = Brushes.White, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
        var clock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        clock.Children.Add(new TextBlock { Text = "9:41 AM", FontFamily = Ui, FontSize = 11.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right });
        clock.Children.Add(new TextBlock { Text = "10/20/2026", FontFamily = Ui, FontSize = 11.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right });
        right.Children.Add(clock);
        bar.Children.Add(right);
        return bar;

        static FrameworkElement Tile(UIElement icon, bool highlighted)
        {
            var tile = new Grid { Width = 40, Height = 40, Margin = new Thickness(2, 0, 2, 0) };
            if (highlighted)
            {
                tile.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)) });
                tile.Children.Add(new Border { Width = 16, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1), Background = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)) });
            }
            tile.Children.Add(icon);
            return tile;
        }

        static FrameworkElement AppIcon(Color top, Color bottom) => new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush(top, bottom, 90),
        };

        static FrameworkElement TerminalIcon() => new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x2E)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xBC)), BorderThickness = new Thickness(1.2),
            Child = new TextBlock { Text = ">_", FontFamily = Mono, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }

    /// <summary>The Windows logo: four blue squares.</summary>
    private sealed class UniformGrid2x2 : Canvas
    {
        public UniformGrid2x2()
        {
            Width = 20;
            Height = 20;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            var brush = new LinearGradientBrush(Color.FromRgb(0x5C, 0xC8, 0xFF), Color.FromRgb(0x1E, 0x7B, 0xE6), 45);
            foreach (var (x, y) in new[] { (0.0, 0.0), (10.5, 0.0), (0.0, 10.5), (10.5, 10.5) })
            {
                var square = new Rectangle { Width = 9.5, Height = 9.5, Fill = brush, RadiusX = 1, RadiusY = 1 };
                SetLeft(square, x);
                SetTop(square, y);
                Children.Add(square);
            }
        }
    }

    private sealed record Line(Func<UIElement> Build)
    {
        public static Line Prompt(string command) => Text(("PS ", Dim), ("C:\\dev\\akoya", Blue), ("> ", Dim), (command, Fg));

        public static Line Banner() => Text(("\u273B ", Orange), ("Welcome back. ", Fg), ("What are we building today?", Dim));

        public static Line Blank() => Text((" ", Fg));

        public static Line Sent(string text) => Text(("> ", Dim), (text, Fg));

        public static Line Reply(string text) => Text(("\u25CF ", Fg), (text, Dim));

        public static Line Input(string text, bool cursor)
        {
            var block = new TextBlock();
            SetInput(block, text, cursor);
            return new Line(() => Box(block));
        }

        public static Line Custom(TextBlock input) => new(() => Box(input));

        private static Line Text(params (string Text, Brush Brush)[] runs) => new(() =>
        {
            var block = new TextBlock { FontFamily = Mono, FontSize = 13, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
            foreach (var (text, brush) in runs) block.Inlines.Add(new System.Windows.Documents.Run(text) { Foreground = brush });
            return block;
        });

        /// <summary>Claude Code's input box.</summary>
        private static UIElement Box(TextBlock input) => new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x9A, 0xA4, 0xBC)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7), Child = input,
        };
    }

    private static readonly Brush Fg = Frozen(Color.FromRgb(0xE3, 0xE8, 0xF4));
    private static readonly Brush Dim = Frozen(Color.FromRgb(0x7A, 0x84, 0x9B));
    private static readonly Brush Blue = Frozen(Color.FromRgb(0x79, 0xC0, 0xFF));
    private static readonly Brush Orange = Frozen(Color.FromRgb(0xE8, 0x8A, 0x5B));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void SetInput(TextBlock block, string text, bool cursorOn)
    {
        block.FontFamily = Mono;
        block.FontSize = 13;
        block.TextWrapping = TextWrapping.Wrap;
        block.Inlines.Clear();
        block.Inlines.Add(new System.Windows.Documents.Run("> ") { Foreground = Dim });
        if (text.Length > 0) block.Inlines.Add(new System.Windows.Documents.Run(text) { Foreground = Fg });
        block.Inlines.Add(new System.Windows.Documents.Run("\u2588") { Foreground = cursorOn ? Fg : Brushes.Transparent });
    }

    /// <summary>A Windows Terminal window with a PowerShell tab.</summary>
    private static FrameworkElement Terminal(double w, double h, IEnumerable<Line> lines)
    {
        var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        foreach (var line in lines) body.Children.Add(line.Build());

        var tab = new Border
        {
            CornerRadius = new CornerRadius(6, 6, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0E, 0x14)), Padding = new Thickness(12, 0, 16, 0),
            Margin = new Thickness(10, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = ">_", FontFamily = Mono, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Blue, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                    new TextBlock { Text = "PowerShell", FontFamily = Ui, FontSize = 12, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        var controls = new TextBlock
        {
            Text = "\uE921      \uE922      \uE8BB", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10,
            Foreground = Fg, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0),
        };
        var title = new Grid { Height = 40, Background = new SolidColorBrush(Color.FromRgb(0x17, 0x1A, 0x24)) };
        title.Children.Add(tab);
        title.Children.Add(controls);

        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(title, Dock.Top);
        content.Children.Add(title);
        content.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0E, 0x14)), Child = body });

        return new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(8), ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0E, 0x14)),
            Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 18, Direction = 270, Opacity = 0.55, Color = Colors.Black },
            Child = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = content },
        };
    }

    private static void Place(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    /// <summary>The pill centred, its bottom edge 52 DIP above the taskbar's top, as the app places it. After a layout pass.</summary>
    private static void Centre(FrameworkElement pill, double w, double h)
    {
        Canvas.SetLeft(pill, (w - pill.ActualWidth) / 2);
        Canvas.SetTop(pill, h - TaskbarHeight - GapAboveTaskbar - 34);
    }

    private static void Layout(FrameworkElement root, double w, double h)
    {
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
    }

    // ---- Rendering ----

    private static BitmapSource Render(FrameworkElement root, double w, double h, double scale)
    {
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Round(w * scale), (int)Math.Round(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 3;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    // ---- APNG ----

    /// <summary>
    /// Writes a looping APNG: the first frame whole, each later one only as the rectangle that changed (frames with no
    /// change just lengthen the one before).
    /// </summary>
    private static void WriteApng(string path, List<(byte[] Pixels, double Seconds)> frames, int width, int height, double end)
    {
        var stride = width * 3;
        var parts = new List<(Int32Rect Rect, byte[] Png, double Start)>();
        byte[]? previous = null;
        foreach (var (pixels, start) in frames)
        {
            var rect = previous is null ? new Int32Rect(0, 0, width, height) : Changed(previous, pixels, width, height);
            previous = pixels;
            if (rect.IsEmpty) continue;
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
            var crop = new CroppedBitmap(source, rect);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(crop));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            parts.Add((rect, stream.ToArray(), start));
        }

        using var output = File.Create(path);
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var first = Chunks(parts[0].Png);
        WriteChunk(output, "IHDR", first.Single(c => c.Type == "IHDR").Data);
        var actl = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl, (uint)parts.Count);
        WriteChunk(output, "acTL", actl); // plays 0 = loop forever

        uint sequence = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var (rect, png, start) = parts[i];
            var next = i + 1 < parts.Count ? parts[i + 1].Start : end;
            var fctl = new byte[26];
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(0), sequence++);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(4), (uint)rect.Width);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(8), (uint)rect.Height);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(12), (uint)rect.X);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(16), (uint)rect.Y);
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(20), (ushort)Math.Round((next - start) * 1000));
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(22), 1000);
            // dispose_op 0 (none), blend_op 0 (source)
            WriteChunk(output, "fcTL", fctl);
            foreach (var (type, data) in Chunks(png).Where(c => c.Type == "IDAT"))
            {
                if (i == 0)
                {
                    WriteChunk(output, "IDAT", data);
                    continue;
                }
                var fdat = new byte[data.Length + 4];
                BinaryPrimitives.WriteUInt32BigEndian(fdat, sequence++);
                data.CopyTo(fdat, 4);
                WriteChunk(output, "fdAT", fdat);
            }
        }
        WriteChunk(output, "IEND", []);
    }

    private static Int32Rect Changed(byte[] a, byte[] b, int width, int height)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        var stride = width * 3;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            if (a.AsSpan(row, stride).SequenceEqual(b.AsSpan(row, stride))) continue;
            minY = Math.Min(minY, y);
            maxY = y;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 3;
                if (a[i] == b[i] && a[i + 1] == b[i + 1] && a[i + 2] == b[i + 2]) continue;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }
        }
        return maxX < 0 ? Int32Rect.Empty : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        for (var at = 8; at < png.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            chunks.Add((type, png.AsSpan(at + 8, length).ToArray()));
            at += 12 + length;
        }
        return chunks;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(header, 4);
        output.Write(header);
        output.Write(data);
        var crc = Crc32(header.AsSpan(4, 4), data);
        var tail = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        output.Write(tail);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
