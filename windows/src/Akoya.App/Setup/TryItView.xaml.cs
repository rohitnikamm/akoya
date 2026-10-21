using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Akoya.App.Setup;

/// <summary>Setup step 3 for a <see cref="SetupViewModel"/> DataContext.</summary>
public partial class TryItView : UserControl
{
    private SetupViewModel? _model;

    public TryItView()
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
        if (e.PropertyName is nameof(SetupViewModel.CardKind) or nameof(SetupViewModel.CardText)
            or nameof(SetupViewModel.DoneReady) or nameof(SetupViewModel.IsHintDimmed))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var kind = _model?.CardKind ?? PracticeCardKind.Placeholder;
        CardText.SetResourceReference(TextBlock.ForegroundProperty, kind switch
        {
            PracticeCardKind.Text => "Akoya.TextPrimary",
            PracticeCardKind.RetryHint or PracticeCardKind.ModelFailed => "Setup.BodyText",
            _ => "Setup.Placeholder",
        });
        RetryButton.Visibility = kind == PracticeCardKind.ModelFailed ? Visibility.Visible : Visibility.Collapsed;
        // Quiet and inert until the first transcript, then the light primary button (as on the Mac).
        DoneButton.SetResourceReference(StyleProperty, _model?.DoneReady == true ? "Setup.ButtonLight" : "Setup.ButtonQuiet");
        Hint.Opacity = _model?.IsHintDimmed == true ? 0.4 : 1;
        // Long dictations: keep the newest words in view.
        CardScroll.ScrollToEnd();
    }
}
