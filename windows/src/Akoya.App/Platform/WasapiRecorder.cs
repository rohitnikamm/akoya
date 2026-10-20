using Akoya.Core.Dictation;

namespace Akoya.App.Platform;

/// <summary>
/// Microphone capture through NAudio WASAPI in shared mode, delivered as 16 kHz mono float.
/// <para>
/// Threading: <see cref="Level"/> (one RMS per 40 ms window, about 25 Hz) and <see cref="Interrupted"/>
/// are raised only from capture or audio worker threads (NAudio's DataAvailable / RecordingStopped
/// callbacks and the device-removal notification), never synchronously from <see cref="Start"/>,
/// <see cref="Stop"/>, <see cref="Snapshot"/> or <see cref="Cancel"/>. The App layer must marshal
/// them to the UI thread asynchronously (Dispatcher.BeginInvoke), never block in a handler. After
/// <see cref="Stop"/> or <see cref="Cancel"/> begins, no further event is raised for that
/// recording; each recording has its own session object, so a late callback from an old recording
/// cannot reach a new one.
/// </para>
/// <para>
/// <see cref="Interrupted"/> fires at most once per recording, when the capture stops without us
/// asking (an error, or the device was unplugged or disabled). The samples recorded so far stay
/// available to <see cref="Stop"/> and <see cref="Snapshot"/>.
/// </para>
/// </summary>
public sealed class WasapiRecorder : IAudioRecorder, IDisposable
{
    private readonly object _gate = new();
    private Session? _session;

    public event Action<float>? Level;

    public event Action? Interrupted;

    /// <summary>
    /// Starts capturing from <paramref name="deviceId"/> (null = the system default capture device).
    /// Starting while a recording is running discards that recording first.
    /// </summary>
    public void Start(string? deviceId)
    {
        lock (_gate)
        {
            CloseSession();

            var session = new Session();
            session.Stream = MicCaptureStream.Open(
                deviceId,
                onSamples: session.Append,
                onLevel: level =>
                {
                    if (!session.Ended) Level?.Invoke(level);
                },
                onStopped: _ =>
                {
                    if (!session.Ended) Interrupted?.Invoke();
                });
            try
            {
                session.Stream.Start();
            }
            catch
            {
                session.Ended = true;
                session.Stream.Close();
                throw;
            }
            _session = session;
        }
    }

    public float[] Stop()
    {
        lock (_gate)
        {
            var session = _session;
            if (session is null) return Array.Empty<float>();
            CloseSession();
            return session.ToArray();
        }
    }

    public float[] Snapshot()
    {
        lock (_gate)
        {
            return _session?.ToArray() ?? Array.Empty<float>();
        }
    }

    public void Cancel()
    {
        lock (_gate) CloseSession();
    }

    public void Dispose() => Cancel();

    private void CloseSession()
    {
        var session = _session;
        _session = null;
        if (session is null) return;
        session.Ended = true;
        session.Stream?.Close();
        session.Seal();
    }

    /// <summary>One recording: its stream, its samples and whether events may still be raised.</summary>
    private sealed class Session
    {
        private readonly object _lock = new();
        private readonly List<float> _samples = new();
        private bool _sealed;

        public volatile bool Ended;

        public MicCaptureStream? Stream { get; set; }

        public void Append(float[] samples)
        {
            lock (_lock)
            {
                if (!_sealed) _samples.AddRange(samples);
            }
        }

        public void Seal()
        {
            lock (_lock) _sealed = true;
        }

        public float[] ToArray()
        {
            lock (_lock) return _samples.ToArray();
        }
    }
}
