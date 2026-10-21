using Akoya.Core.Dictation;
using Akoya.Core.Mics;
using Akoya.Core.Settings;

namespace Akoya.App.Tray;

/// <summary>The tray icon and panel as AppHost's <see cref="ITrayPresenter"/>. Create it on the UI thread.</summary>
public sealed class TrayPresenter : ITrayPresenter
{
    private readonly TrayIconHost _host;

    public TrayPresenter(TrayCommands commands, IScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(commands);
        Model = new TrayPanelViewModel(new TrayPanelActions
        {
            Copy = commands.Copy,
            SelectHotkey = commands.SelectHotkey,
            SelectMic = commands.SelectMic,
            SetLaunchAtStartup = commands.SetLaunchAtStartup,
            Retry = commands.Retry,
            OpenMicSettings = commands.OpenMicSettings,
            RestartToUpdate = commands.RestartToUpdate,
            Quit = commands.Quit,
            SelectLook = commands.SelectLook ?? (_ => { }),
            SetRedTint = commands.SetRedTint ?? (_ => { }),
        }, scheduler);
        _host = new TrayIconHost(Model);
    }

    public TrayPanelViewModel Model { get; }

    internal TrayIconHost Icon => _host;

    public void SetPhase(DictationPhase phase) => Model.SetPhase(phase);

    public void SetVoiceLevel(double level) => Model.SetVoiceLevel(level);

    public void SetModel(ModelState state, double? progress, bool loadFailed) => Model.SetModel(state, progress, loadFailed);

    public void SetMicBlocked(bool blocked) => Model.SetMicBlocked(blocked);

    public void SetHotkeyUnavailable(bool unavailable) => Model.SetHotkeyUnavailable(unavailable);

    public void SetMicrophones(IReadOnlyList<NamedMic> mics, string? savedId, string? savedName) =>
        Model.SetMicrophones(mics, savedId, savedName);

    public void SetSettings(AkoyaSettings settings) => Model.SetSettings(settings);

    public void SetLastDictation(string? text) => Model.SetLastDictation(text);

    public void SetUpdateReady(bool ready) => Model.SetUpdateReady(ready);

    public void ShowPanel() => _host.ShowPanel();

    public void ClosePanelAndReturnFocus() => _host.Panel.CloseAndReturnFocus();

    public void Dispose() => _host.Dispose();
}
