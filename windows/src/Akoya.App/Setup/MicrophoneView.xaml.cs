using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Akoya.Core.Mics;

namespace Akoya.App.Setup;

/// <summary>Setup step 2 for a <see cref="SetupViewModel"/> DataContext.</summary>
public partial class MicrophoneView : UserControl
{
    private SetupViewModel? _model;

    public MicrophoneView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as SetupViewModel);
        Refresh();
    }

    private void Attach(SetupViewModel? model)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        Refresh();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SetupViewModel.Status) or nameof(SetupViewModel.IsBlocked) or nameof(SetupViewModel.CanContinue))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var blocked = _model?.IsBlocked ?? false;
        ListLayout.Visibility = blocked ? Visibility.Collapsed : Visibility.Visible;
        BlockedLayout.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;

        // No mic to save: Continue is dimmed and inert.
        var canContinue = _model?.CanContinue ?? true;
        ContinueButton.Opacity = canContinue ? 1 : 0.4;
        ContinueButton.IsHitTestVisible = canContinue;

        var status = _model?.Status ?? MicStatusKind.None;
        Chip.Visibility = status == MicStatusKind.None ? Visibility.Hidden : Visibility.Visible;
        var hearing = status == MicStatusKind.HearingYou;
        Chip.SetResourceReference(Border.BackgroundProperty, hearing ? "Setup.ChipOkFill" : "Setup.ChipWaitFill");
        ChipDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, hearing ? "Akoya.GreenDot" : "Akoya.Amber");
        ChipText.SetResourceReference(TextBlock.ForegroundProperty, hearing ? "Akoya.GreenText" : "Setup.ChipWaitText");
    }
}
