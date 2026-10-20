namespace Akoya.Core.Mics;

/// <summary>Windows PKEY_AudioEndpoint_FormFactor, reduced to the values naming cares about.</summary>
public enum EndpointFormFactor
{
    Unknown,
    Microphone,
    Headset,
    Headphones,
    LineLevel,
    DigitalAudioDisplayDevice,
    Other,
}

/// <summary>
/// What Windows tells us about one capture endpoint. <paramref name="Enumerator"/> is the bus
/// enumerator of the parent device instance ID (USB, BTHENUM, BTHHFENUM, HDAUDIO, INTELAUDIO, SWD, ROOT...).
/// </summary>
public sealed record EndpointFacts(
    string Id,
    string FriendlyName,
    string DeviceDescription,
    EndpointFormFactor FormFactor,
    string Enumerator,
    bool HasPhysicalParent,
    bool IsDefault);

/// <summary>A microphone with the plain name shown in setup and the tray.</summary>
public sealed record NamedMic(string Id, string Name, bool InMore, bool IsDefault);
