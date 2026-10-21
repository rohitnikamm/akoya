using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Akoya.App.Setup;

/// <summary>
/// Hosts setup. Closes itself when <see cref="SetupViewModel.Finished"/> fires (Done, Skip, ✕); Alt+F4 counts as ✕.
/// Enter presses the step's primary button. <see cref="Dismiss"/> closes it without finishing (e.g. the app quits).
/// The view model is disposed when the window closes.
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

    /// <summary>Closes the window without ending setup (it shows again next launch).</summary>
    public void Dismiss()
    {
        _closing = true;
        Close();
    }

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

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        // Alt+F4 or the taskbar's Close: same as ✕.
        if (!_closing && !e.Cancel)
        {
            _closing = true;
            _model.Skip();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.Finished -= OnFinished;
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
        var card = new Size(Card.Width, Card.Height);
        Left = area.Left + (area.Width - card.Width) / 2 - ShadowPadding.Left;
        Top = area.Top + (area.Height - card.Height) / 2 - ShadowPadding.Top;
    }
}
