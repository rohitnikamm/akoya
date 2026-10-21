using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Akoya.App.Controls;
using Akoya.Core.Ui;

namespace Akoya.App.Pill;

/// <summary>The pill's visuals for a <see cref="PillViewModel"/> DataContext.</summary>
public partial class PillView : UserControl
{
    /// <summary>The Pasted check's glow, <c>0 0 14px rgba(47,107,255,.8)</c>.</summary>
    public static readonly IReadOnlyList<BoxShadow> CheckHalo = RimGlowRecipes.Halo(new ShadowColor(47, 107, 255, 0.8), 14);

    /// <summary>The alert badge's glow, <c>0 0 12px rgba(239,68,68,.8)</c>.</summary>
    public static readonly IReadOnlyList<BoxShadow> AlertHalo = RimGlowRecipes.Halo(new ShadowColor(239, 68, 68, 0.8), 12);

    private PillViewModel? _model;

    public PillView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as PillViewModel);
        Refresh();
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
        if (e.PropertyName is nameof(PillViewModel.Mode) or nameof(PillViewModel.IsRed) or nameof(PillViewModel.AlertText)) Refresh();
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
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
