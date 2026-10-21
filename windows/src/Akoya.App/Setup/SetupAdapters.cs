using Akoya.App.Platform;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Akoya.Core.Mics;

namespace Akoya.App.Setup;

/// <summary><see cref="MicrophoneCatalog"/> as an <see cref="IMicCatalog"/>.</summary>
public sealed class CatalogMics(MicrophoneCatalog catalog) : IMicCatalog
{
    public event Action? Changed
    {
        add => catalog.Changed += value;
        remove => catalog.Changed -= value;
    }

    public IReadOnlyList<EndpointFacts> Snapshot() => catalog.Snapshot();
}

/// <summary><see cref="MultiMeter"/> as an <see cref="IMicLevels"/>.</summary>
public sealed class MeterLevels(MultiMeter meter) : IMicLevels
{
    public event Action<string, int>? Step
    {
        add => meter.Step += value;
        remove => meter.Step -= value;
    }

    public void Start(IEnumerable<string> ids) => meter.Start(ids);

    public void Stop() => meter.Stop();
}

/// <summary><see cref="MicPrivacy"/> as an <see cref="IMicPrivacy"/>.</summary>
public sealed class WindowsMicPrivacy : IMicPrivacy
{
    public bool IsDesktopAccessAllowed() => MicPrivacy.IsDesktopAccessAllowed();

    public void OpenSettings() => MicPrivacy.OpenSettings();
}

/// <summary><see cref="DictationCoordinator"/> as setup's <see cref="IPracticeDictation"/>.</summary>
public sealed class CoordinatorPractice(DictationCoordinator coordinator) : IPracticeDictation
{
    public event Action<DictationPhase>? PhaseChanged
    {
        add => coordinator.PhaseChanged += value;
        remove => coordinator.PhaseChanged -= value;
    }

    public event Action<float>? Level
    {
        add => coordinator.Level += value;
        remove => coordinator.Level -= value;
    }

    public void Begin(Action<string, bool> onText) => coordinator.Destination = new Destination.Practice(onText);

    public void End()
    {
        // The coordinator reads the destination when a recording stops, so a practice recording still running would
        // otherwise be pasted into whatever app is in front next. A final pass already running keeps the practice
        // destination it captured at stop time, so it is never pasted.
        if (coordinator.Phase == DictationPhase.Recording) coordinator.Handle(HotkeyEvent.Cancel);
        coordinator.Destination = new Destination.Paste();
    }
}
