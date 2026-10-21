using System.Windows;
using Akoya.App.Controls;
using Akoya.App.Platform;
using Akoya.App.Tray;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;

namespace Akoya.App.Setup;

/// <summary>
/// The setup window as AppHost's <see cref="ISetupPresenter"/>. Create it on the UI thread. The card's rim glow is
/// rasterised on worker threads before the window first shows (as the pill does), so opening it doesn't hitch.
/// </summary>
public sealed class SetupPresenter : ISetupPresenter
{
    private readonly MultiMeter _meter = new();
    private readonly SetupViewModel _model;
    private readonly SetupWindow _window;
    private readonly Task _prewarm;
    private readonly Task _voicePrewarm;
    private bool _closed;
    private bool _dismissed;

    public SetupPresenter(SetupContext context, MicrophoneCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(catalog);
        _model = new SetupViewModel(new CatalogMics(catalog), new MeterLevels(_meter), new WindowsMicPrivacy(),
            new CoordinatorPractice(context.Coordinator), context.Scheduler)
        {
            HotkeyLabel = TrayPanelViewModel.Label(context.Settings.Hotkey),
        };
        // The window subscribes to Finished first, so it has closed itself before AppHost disposes this.
        _window = new SetupWindow(_model);
        _window.Closed += (_, _) => _closed = true;
        _model.MicChosen += id => MicChosen?.Invoke(id);
        _model.Finished += OnFinished;
        _model.RetryModelRequested += () => RetryModelRequested?.Invoke();

        var card = _window.Card;
        var scale = RimGlowCache.ScaleFor(_window);
        _prewarm = RimGlowCache.PrewarmAsync(card.Width, card.Height, 44, scale, SetupGlows.Card);
        // The voice glow is first drawn on Try it, two steps later: warm it without holding up the window.
        _voicePrewarm = RimGlowCache.PrewarmAsync(card.Width, card.Height, 44, scale, SetupGlows.CardVoice);
    }

    public event Action<string>? MicChosen;

    public event Action? Finished;

    public event Action? RetryModelRequested;

    /// <summary>The card glow rasters (for tests).</summary>
    internal Task PrewarmTask => _prewarm;

    /// <summary>The Try it voice glow rasters (for tests).</summary>
    internal Task VoicePrewarmTask => _voicePrewarm;

    internal SetupWindow Window => _window;

    public void Show()
    {
        if (_prewarm.IsCompleted)
        {
            ShowNow();
            return;
        }
        _prewarm.ContinueWith(_ => ShowNow(), CancellationToken.None, TaskContinuationOptions.None,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void SetModel(ModelState state, double? progress) => _model.SetModel(state, progress);

    public void SetHotkey(HotkeyKey key) => _model.HotkeyLabel = TrayPanelViewModel.Label(key);

    // The view model follows the coordinator itself (CoordinatorPractice).
    public void SetPhase(DictationPhase phase)
    {
    }

    public void SetLevel(float rms)
    {
    }

    public void Dispose()
    {
        if (_dismissed) return;
        _dismissed = true;
        if (!_closed) _window.Dismiss(); // the app quits: setup shows again next launch
        _meter.Dispose();
    }

    private void ShowNow()
    {
        if (_dismissed || _closed) return;
        _window.Show();
        _window.Activate();
    }

    private void OnFinished()
    {
        // WPF closes every window during Application.Shutdown, which the window reads as ✕: that is not the user
        // finishing setup, so it must not be marked done.
        if (_dismissed || Application.Current?.Dispatcher.HasShutdownStarted == true) return;
        Finished?.Invoke();
    }
}
