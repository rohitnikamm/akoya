using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Akoya.App.Setup;

/// <summary>The setup card for a <see cref="SetupViewModel"/> DataContext: shows the current step, cross-fading on change.</summary>
public partial class SetupView : UserControl
{
    private static readonly Duration StepFade = new(TimeSpan.FromMilliseconds(250));

    private SetupViewModel? _model;
    private SetupStep? _shown;

    public SetupView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as SetupViewModel);
        // Drag the window by any part of the card that isn't a control.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && Window.GetWindow(this) is { } window) window.DragMove();
        };
        Refresh(animate: false);
    }

    private void Attach(SetupViewModel? model)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        _shown = null;
        Refresh(animate: false);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SetupViewModel.Step)) Refresh(animate: true);
    }

    private void Refresh(bool animate)
    {
        var step = _model?.Step ?? SetupStep.Welcome;
        if (_shown == step) return;
        _shown = step;

        UIElement current = step switch
        {
            SetupStep.Microphone => MicrophoneStep,
            SetupStep.TryIt => TryItStep,
            _ => WelcomeStep,
        };
        foreach (var view in new UIElement[] { WelcomeStep, MicrophoneStep, TryItStep })
        {
            view.BeginAnimation(OpacityProperty, null);
            view.Opacity = 1;
            view.Visibility = view == current ? Visibility.Visible : Visibility.Collapsed;
        }
        if (animate) current.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, StepFade));

        SkipButton.Visibility = step == SetupStep.Welcome ? Visibility.Collapsed : Visibility.Visible;
        PageDots.Children.Clear();
        for (var i = 0; i < 3; i++)
        {
            var on = i == (int)step;
            var dot = new Border
            {
                Width = on ? 18 : 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
            };
            dot.SetResourceReference(Border.BackgroundProperty, on ? "Akoya.TextPrimary" : "Setup.PageDot");
            PageDots.Children.Add(dot);
        }
    }
}
