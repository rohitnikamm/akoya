using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Akoya.App.Controls;
using Akoya.Core.Ui;

namespace Akoya.App.Pill;

/// <summary>
/// The pill's visuals for a <see cref="PillViewModel"/> DataContext. <see cref="PopInProgress"/> poses it partway
/// through the jelly pop-in (<see cref="PillPopIn"/>); its layout size never changes, only what is drawn.
/// </summary>
public partial class PillView : UserControl
{
    public static readonly DependencyProperty PopInProgressProperty = DependencyProperty.Register(
        nameof(PopInProgress), typeof(double), typeof(PillView),
        new FrameworkPropertyMetadata(1.0, (d, _) => ((PillView)d).UpdatePopIn()));

    /// <summary>The Pasted check's glow, <c>0 0 14px rgba(47,107,255,.8)</c>.</summary>
    public static readonly IReadOnlyList<BoxShadow> CheckHalo = RimGlowRecipes.Halo(new ShadowColor(47, 107, 255, 0.8), 14);

    /// <summary>The alert badge's glow, <c>0 0 12px rgba(239,68,68,.8)</c>.</summary>
    public static readonly IReadOnlyList<BoxShadow> AlertHalo = RimGlowRecipes.Halo(new ShadowColor(239, 68, 68, 0.8), 12);

    private PillViewModel? _model;

    public PillView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as PillViewModel);
        Pill.SizeChanged += (_, _) => UpdatePopIn();
        Refresh();
    }

    /// <summary>0..1 through the pop-in; 1 (the default) is the settled pill.</summary>
    public double PopInProgress
    {
        get => (double)GetValue(PopInProgressProperty);
        set => SetValue(PopInProgressProperty, value);
    }

    /// <summary>The pop-in pose now drawn (for tests).</summary>
    internal PillPopInFrame PopInFrame { get; private set; } = new(0, 1, 1, 1, 1);

    private void UpdatePopIn()
    {
        var progress = PopInProgress;
        // Before the first layout use the measured size, so a pose set up front is right on the first frame.
        var width = Pill.ActualWidth > 0 ? Pill.ActualWidth : Pill.DesiredSize.Width;
        var height = Pill.ActualHeight > 0 ? Pill.ActualHeight : Pill.Height;
        var settled = !(progress < 1);
        // Not measured yet: pose it as a circle (invisible at the start); SizeChanged poses it again at its real width.
        var frame = settled ? new PillPopInFrame(width, 1, 1, 1, 1) : PillPopIn.At(progress, Math.Max(width, height), height);
        PopInFrame = frame;

        Pill.ShapeWidth = settled ? double.NaN : frame.Width;
        PopScale.ScaleX = frame.ScaleX;
        PopScale.ScaleY = frame.ScaleY;
        Pill.Opacity = frame.Opacity;
        Contents.Opacity = frame.ContentOpacity;
    }

    private void Attach(PillViewModel? model)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        Refresh();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PillViewModel.Mode) or nameof(PillViewModel.IsRed) or nameof(PillViewModel.AlertText)
            or nameof(PillViewModel.Tint)) Refresh();
    }

    private void Refresh()
    {
        var mode = _model?.Mode ?? PillMode.Listening;
        var red = _model?.IsRed ?? false;
        var waveform = mode is PillMode.Listening or PillMode.Transcribing;
        var check = mode is PillMode.Pasted or PillMode.Copied;
        var alertBadge = mode is PillMode.Alert && red;

        Dots.Visibility = Show(waveform);
        Timer.Visibility = Show(waveform);
        Check.Visibility = Show(check);
        AlertBadge.Visibility = Show(alertBadge);
        Label.Visibility = Show(!waveform);
        // Set directly rather than bound, so the pill's size is right the moment the model changes (PillWindow.Prewarm
        // measures it synchronously).
        Label.Text = _model?.AlertText ?? "";
        Label.Margin = check || alertBadge ? new Thickness(10, 0, 0, 0) : default;

        // Red alert: white text; everything else uses the pill's label colour.
        Label.SetResourceReference(TextBlock.ForegroundProperty, red ? "Akoya.TextPrimary" : "Akoya.PillLabel");
        Pill.SetResourceReference(RimGlowBorder.GlowColorProperty, red ? "Akoya.RedColor" : "Akoya.AccentColor");

        // Red while recording: a red waveform while listening, an amber sweep while transcribing; the glow stays blue.
        var tint = _model?.Tint ?? PillTint.Normal;
        Dots.SetResourceReference(DotGrid.DotBrushProperty, tint == PillTint.Red ? "Akoya.PillDotRed" : "Akoya.PillDot");
        if (tint == PillTint.Amber)
        {
            Dots.SetResourceReference(DotGrid.HighlightBrushProperty, "Akoya.Amber");
            Dots.HighlightAbove = PillDots.ScannerRest;
        }
        else
        {
            Dots.ClearValue(DotGrid.HighlightBrushProperty);
            Dots.ClearValue(DotGrid.HighlightAboveProperty);
        }
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
