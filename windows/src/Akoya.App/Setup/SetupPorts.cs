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
