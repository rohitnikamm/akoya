using Akoya.Core.Audio;
using Akoya.Core.Dictation;

namespace Akoya.App.Platform;

/// <summary>
/// Opens several microphones at once (setup only) and reports each one's level step, 1...8, about
/// 25 Hz (one per 40 ms window). A microphone that cannot be opened is skipped silently: its row
/// just never moves. When the Windows privacy switch blocks the microphone nothing is opened.
/// <para>
/// <see cref="Step"/> is raised on a NAudio capture thread, concurrently for different ids. The
/// App layer marshals to the UI thread and must not block in the handler. After <see cref="Stop"/>
/// returns, no more steps are raised.
/// </para>
/// </summary>
public sealed class MultiMeter : IDisposable
{
    private readonly object _gate = new();
    private readonly List<MicCaptureStream> _streams = new();
    private volatile bool _running;

    /// <summary>Endpoint id and the newest level step.</summary>
    public event Action<string, int>? Step;

    public void Start(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        lock (_gate)
        {
            StopLocked();
            _running = true;
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                var endpoint = id;
                try
                {
                    var stream = MicCaptureStream.Open(
                        endpoint,
                        onSamples: null,
                        onLevel: rms =>
                        {
                            if (_running) Step?.Invoke(endpoint, LevelMeter.Step(rms));
                        },
                        onStopped: _ => { });
                    try
                    {
                        stream.Start();
                    }
                    catch
                    {
                        stream.Close();
                        throw;
                    }
                    _streams.Add(stream);
                }
                catch (MicrophoneBlockedException)
                {
                    return;
                }
                catch (Exception ex) when (ex is MicrophoneMissingException or System.Runtime.InteropServices.COMException
                                               or InvalidOperationException)
                {
                    // Skip this microphone.
                }
            }
        }
    }

    public void Stop()
    {
        lock (_gate) StopLocked();
    }

    public void Dispose() => Stop();

    private void StopLocked()
    {
        _running = false;
        foreach (var stream in _streams) stream.Close();
        _streams.Clear();
    }
}
