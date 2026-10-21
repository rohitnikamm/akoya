using Akoya.Core.Audio;
using Akoya.Core.Dictation;

namespace Akoya.App.Platform;

/// <summary>
/// Opens several microphones at once (setup only) and reports each one's level step, 1...8, about
/// 25 Hz (one per 40 ms window). A microphone that cannot be opened is skipped silently: its row
/// just never moves. When the Windows privacy switch blocks the microphone nothing is opened.
/// <para>
/// <see cref="Step"/> is posted to the <see cref="SynchronizationContext"/> current when
/// <see cref="Start"/> was called (the UI thread), or run in order on the thread pool when there is
/// none; it is never raised on an audio thread. A step that was posted before <see cref="Stop"/> but
/// not yet delivered is dropped, so after <see cref="Stop"/> returns no further step arrives.
/// </para>
/// </summary>
public sealed class MultiMeter : IDisposable
{
    private readonly object _gate = new();
    private readonly List<IMicStream> _streams = new();
    private readonly MicStreamFactory _open;
    private int _generation;

    public MultiMeter() : this(MicCaptureStream.Open)
    {
    }

    internal MultiMeter(MicStreamFactory open) => _open = open;

    /// <summary>Endpoint id and the newest level step.</summary>
    public event Action<string, int>? Step;

    public void Start(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        lock (_gate)
        {
            StopLocked();
            var generation = _generation;
            var poster = new EventPoster(SynchronizationContext.Current);
            foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var endpoint = id;
                try
                {
                    var stream = _open(
                        endpoint,
                        onSamples: null,
                        onLevel: rms =>
                        {
                            if (Volatile.Read(ref _generation) != generation) return;
                            var step = LevelMeter.Step(rms);
                            poster.Post(() =>
                            {
                                if (Volatile.Read(ref _generation) == generation) Step?.Invoke(endpoint, step);
                            });
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
        Interlocked.Increment(ref _generation);
        foreach (var stream in _streams) stream.Close();
        _streams.Clear();
    }
}
