using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Akoya.App.Tests")]

namespace Akoya.App.Platform;

/// <summary>
/// One running capture, as <see cref="WasapiRecorder"/> and <see cref="MultiMeter"/> see it. The
/// real implementation is <see cref="MicCaptureStream"/>; tests substitute a fake.
/// </summary>
internal interface IMicStream
{
    /// <summary>Starts delivering audio. Throws the same exceptions as the factory that created it.</summary>
    void Start();

    /// <summary>Stops, waits for the capture thread and releases the device. Safe to call twice.</summary>
    void Close();
}

/// <summary>
/// Opens (without starting) a capture of <paramref name="deviceId"/> (null = default). The callbacks
/// run on capture or audio worker threads. <paramref name="onStopped"/> means the capture ended
/// without being asked to.
/// </summary>
internal delegate IMicStream MicStreamFactory(
    string? deviceId,
    Action<float[]>? onSamples,
    Action<float>? onLevel,
    Action<Exception?> onStopped);
