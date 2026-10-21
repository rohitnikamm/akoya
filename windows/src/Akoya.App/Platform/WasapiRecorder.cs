using Akoya.Core.Audio;
using Akoya.Core.Dictation;

namespace Akoya.App.Platform;

/// <summary>
/// Microphone capture through NAudio WASAPI in shared mode, delivered as 16 kHz mono float.
/// <para>
/// Threading: <see cref="Level"/> (one RMS per 40 ms window, about 25 Hz) and <see cref="Interrupted"/>
/// are always posted, never raised on an audio thread and never synchronously from
/// <see cref="Start"/>, <see cref="Stop"/>, <see cref="Snapshot"/> or <see cref="Cancel"/>. They are
/// posted to the <see cref="SynchronizationContext"/> that was current when <see cref="Start"/> was
/// called (the UI thread in the app), or, when there is none, run in order on the thread pool.
/// Handlers may call <see cref="Stop"/> or <see cref="Cancel"/>: the capture thread is not the one
/// running them, so closing the device cannot deadlock.
/// </para>
/// <para>
/// Inside the posted delegate the recording is checked again, so nothing is raised once
/// <see cref="Stop"/> or <see cref="Cancel"/> has begun, and a late event from an old recording never
/// reaches a newer one. <see cref="Interrupted"/> fires at most once per recording, when the capture
/// stops without being asked (an error, or the device was unplugged or disabled); the samples so far
/// stay available to <see cref="Stop"/> and <see cref="Snapshot"/>.
/// </para>
/// </summary>
public sealed class WasapiRecorder : IAudioRecorder, IDisposable
{
    private readonly object _gate = new();
    private readonly MicStreamFactory _open;
    private Session? _session;

    public WasapiRecorder() : this(MicCaptureStream.Open)
    {
    }

    internal WasapiRecorder(MicStreamFactory open) => _open = open;

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

            var poster = new EventPoster(SynchronizationContext.Current);
            var session = new Session();
            session.Stream = _open(
                deviceId,
                onSamples: session.Append,
                onLevel: level =>
                {
                    if (session.Ended) return;
                    poster.Post(() =>
                    {
                        if (IsLive(session)) Level?.Invoke(level);
                    });
                },
                onStopped: _ =>
                {
                    if (session.Ended) return;
                    poster.Post(() =>
                    {
                        if (IsLive(session)) Interrupted?.Invoke();
                    });
                });

            // Published before Start so the first posted events already see a live session.
            Volatile.Write(ref _session, session);
            try
            {
                session.Stream.Start();
            }
            catch
            {
                CloseSession();
                throw;
            }
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

    /// <summary>True while <paramref name="session"/> is the current, not yet stopped recording. Takes no lock.</summary>
    private bool IsLive(Session session) =>
        !session.Ended && ReferenceEquals(Volatile.Read(ref _session), session);

    private void CloseSession()
    {
        var session = _session;
        Volatile.Write(ref _session, null);
        if (session is null) return;
        session.Ended = true;
        session.Stream?.Close();
        session.Seal();
    }

    /// <summary>One recording: its stream, its samples and whether events may still be raised.</summary>
    private sealed class Session
    {
        private readonly object _lock = new();
        private readonly SampleBuffer _samples = new();
        private bool _sealed;

        public volatile bool Ended;

        public IMicStream? Stream { get; set; }

        public void Append(float[] samples)
        {
            lock (_lock)
            {
                if (!_sealed) _samples.Append(samples);
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
