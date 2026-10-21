using System.Windows;
using System.Windows.Input;

namespace Akoya.App.Setup;

/// <summary>
/// Hosts setup. Only the user's ✕, Skip or Done end setup (<see cref="SetupViewModel.Finished"/>, which closes the
/// window). Every other close (Alt+F4, the taskbar's Close, the host's <see cref="Window.Close"/> or
/// <see cref="Dismiss"/>, Application.Shutdown, Windows signing out) is a dismissal: setup is not marked done and shows
/// again next launch. Enter presses the step's primary button. The view model is disposed when the window closes.
/// </summary>
public partial class SetupWindow : Window
{
    /// <summary>Room around the card for its drop shadow (<c>0 30px 80px</c>): 80 to the sides, 50 above, 110 below.</summary>
    public static readonly Thickness ShadowPadding = new(80, 50, 80, 110);

    private readonly SetupViewModel _model;
    private bool _closing;

    public SetupWindow(SetupViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        InitializeComponent();
        Card.DataContext = model;
        model.Finished += OnFinished;
        CentreOnPrimaryWorkArea();
    }

    /// <summary>Closes the window without ending setup (it shows again next launch). Same as <see cref="Window.Close"/>.</summary>
    public void Dismiss() => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Enter && !e.IsRepeat)
        {
            _model.PrimaryAction();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        _model.Finished -= OnFinished;
        // Tears down meters and practice without raising Finished: a close that wasn't ✕/Skip/Done never marks setup done.
        _model.Dispose();
        base.OnClosed(e);
    }

    private void OnFinished()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void CentreOnPrimaryWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Card.Width) / 2 - ShadowPadding.Left;
        Top = area.Top + (area.Height - Card.Height) / 2 - ShadowPadding.Top;
    }
}
