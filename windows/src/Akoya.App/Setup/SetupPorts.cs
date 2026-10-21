using Akoya.Core.Dictation;
using Akoya.Core.Mics;

namespace Akoya.App.Setup;

/// <summary>
/// The active capture endpoints (the shape of <c>Platform.MicrophoneCatalog</c>). <see cref="Changed"/> is raised on
/// the UI thread when an input is added or removed or the Windows default changes.
/// </summary>
public interface IMicCatalog
{
    event Action? Changed;

    IReadOnlyList<EndpointFacts> Snapshot();
}

/// <summary>
/// Live level steps for several inputs at once (the shape of <c>Platform.MultiMeter</c>). <see cref="Step"/> carries an
/// endpoint id and its newest level step (1 to 8, about 25 Hz) and is raised on the thread that called <see cref="Start"/>.
/// </summary>
public interface IMicLevels
{
    event Action<string, int>? Step;

    void Start(IEnumerable<string> ids);

    void Stop();
}

/// <summary>Windows' microphone privacy switch (<c>Platform.MicPrivacy</c>).</summary>
public interface IMicPrivacy
{
    /// <summary>False when Settings blocks microphone access for desktop apps.</summary>
    bool IsDesktopAccessAllowed();

    /// <summary>Opens Settings > Privacy > Microphone (<c>ms-settings:privacy-microphone</c>).</summary>
    void OpenSettings();
}

/// <summary>
/// The dictation coordinator as setup's Try it card sees it (<see cref="CoordinatorPractice"/> wraps the real one).
/// Events are raised on the UI thread.
/// </summary>
public interface IPracticeDictation
{
    /// <summary>The coordinator's phase changed.</summary>
    event Action<DictationPhase>? PhaseChanged;

    /// <summary>RMS of the recording, 0...1, about 25 Hz while recording; 0 when it stops.</summary>
    event Action<float>? Level;

    /// <summary>
    /// Sends dictations to <paramref name="onText"/> instead of pasting them: live partials (<c>isFinal = false</c>) while
    /// recording, then the final transcript, possibly empty.
    /// </summary>
    void Begin(Action<string, bool> onText);

    /// <summary>Cancels a practice recording still running and switches dictation back to pasting.</summary>
    void End();
}
