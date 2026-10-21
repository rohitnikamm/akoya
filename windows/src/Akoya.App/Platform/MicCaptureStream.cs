using System.Runtime.InteropServices;
using Akoya.Core.Audio;
using Akoya.Core.Dictation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudioRecorder = NAudio.Wave.WasapiRecorder;

namespace Akoya.App.Platform;

/// <summary>
/// One shared-mode WASAPI capture of one endpoint, converted to a 16 kHz mono float stream.
/// Used by <see cref="WasapiRecorder"/> (keeps the samples) and <see cref="MultiMeter"/> (levels only).
/// <para>
/// Every callback runs on a NAudio capture thread, a Windows audio worker thread (device removal)
/// or, for <c>RecordingStopped</c>, the SynchronizationContext captured when the stream was opened.
/// None runs synchronously inside <see cref="Open"/>, <see cref="Start"/> or <see cref="Close"/>.
/// Once <see cref="Close"/> begins, no level or stopped callback is raised any more; samples that
/// the capture thread still delivers while Close joins it are passed to <c>onSamples</c> so the last
/// few milliseconds are not lost.
/// </para>
/// </summary>
internal sealed class MicCaptureStream : IMicStream, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDevice _device;
    private readonly NAudioRecorder _capture;
    private readonly MMDeviceNotificationClient _notifications;
    private StreamingResampler _resampler;
    private readonly RmsWindower _windower = new();
    private readonly Action<float[]>? _onSamples;
    private readonly Action<float>? _onLevel;
    private readonly Action<Exception?> _onStopped;
    private readonly string _deviceId;
    private CaptureFormat _format;
    private bool _formatChecked;
    private volatile bool _closing;
    private int _stoppedRaised;
    private bool _closed;

    private MicCaptureStream(
        MMDeviceEnumerator enumerator,
        MMDevice device,
        NAudioRecorder capture,
        MMDeviceNotificationClient notifications,
        CaptureFormat format,
        Action<float[]>? onSamples,
        Action<float>? onLevel,
        Action<Exception?> onStopped)
    {
        _enumerator = enumerator;
        _device = device;
        _capture = capture;
        _notifications = notifications;
        _onSamples = onSamples;
        _onLevel = onLevel;
        _onStopped = onStopped;
        _deviceId = device.ID;
        _format = format;
        _resampler = new StreamingResampler(format.Channels, format.SampleRate);

        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnRecordingStopped;
        _notifications.DeviceRemoved += OnDeviceRemoved;
        _notifications.DeviceStateChanged += OnDeviceStateChanged;
    }


    /// <summary>
    /// Opens (but does not start) a capture. <paramref name="deviceId"/> null means the default
    /// capture device. Throws <see cref="MicrophoneBlockedException"/> when the privacy switch or
    /// E_ACCESSDENIED blocks capture and <see cref="MicrophoneMissingException"/> when the endpoint
    /// is not an active capture device (or there is no default).
    /// </summary>
    public static MicCaptureStream Open(
        string? deviceId,
        Action<float[]>? onSamples,
        Action<float>? onLevel,
        Action<Exception?> onStopped)
    {
        if (!MicPrivacy.IsDesktopAccessAllowed())
            throw new MicrophoneBlockedException();

        var enumerator = new MMDeviceEnumerator();
        MMDevice? device = null;
        NAudioRecorder? capture = null;
        MMDeviceNotificationClient? notifications = null;
        try
        {
            device = Resolve(enumerator, deviceId);
            // Shared mode captures in the engine's mix format; checking it here makes an unsupported
            // format fail from Start, not later on the capture thread.
            CaptureFormat format;
            using (var probe = device.CreateAudioClient())
            {
                format = CaptureFormat.From(probe.MixFormat);
            }
            capture = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithSharedMode()
                .WithEventSync()
                .WithBufferLength(100)
                .Build();
            notifications = enumerator.CreateNotificationClient(useSynchronizationContext: false);
            return new MicCaptureStream(enumerator, device, capture, notifications, format, onSamples, onLevel, onStopped);
        }
        catch (Exception ex)
        {
            notifications?.Dispose();
            capture?.Dispose();
            device?.Dispose();
            enumerator.Dispose();
            throw Translate(ex);
        }
    }

    public void Start()
    {
        try
        {
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    /// <summary>Stops the capture, waits for the capture thread and releases the device.</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _closing = true;

        _notifications.DeviceRemoved -= OnDeviceRemoved;
        _notifications.DeviceStateChanged -= OnDeviceStateChanged;
        Quietly(_notifications.Dispose);
        Quietly(_capture.StopRecording);
        // Dispose releases the audio client. If NAudio's capture thread is still delivering its
        // final packet (about 10 ms), those samples may be dropped: harmless for dictation.
        Quietly(_capture.Dispose);
        Quietly(() =>
        {
            _capture.DataAvailable -= OnData;
            _capture.RecordingStopped -= OnRecordingStopped;
        });
        Quietly(_device.Dispose);
        Quietly(_enumerator.Dispose);
    }

    public void Dispose() => Close();

    private static void Quietly(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException
                                       or NullReferenceException)
        {
            // Releasing a device that is already gone can fail; there is nothing left to release.
        }
    }

    private static MMDevice Resolve(MMDeviceEnumerator enumerator, string? deviceId)
    {
        MMDevice device;
        if (deviceId is null)
        {
            if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out device!))
                throw new MicrophoneMissingException();
        }
        else
        {
            try
            {
                device = enumerator.GetDevice(deviceId);
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
                throw new MicrophoneMissingException("That microphone is not connected.", ex);
            }
        }

        if (device.DataFlow != DataFlow.Capture || device.State != DeviceState.Active)
        {
            device.Dispose();
            throw new MicrophoneMissingException("That microphone is not connected.");
        }
        return device;
    }

    private static Exception Translate(Exception ex)
    {
        if (ex is MicrophoneMissingException or MicrophoneBlockedException) return ex;
        if (CaptureErrors.IsAccessDenied(ex))
            return new MicrophoneBlockedException("Microphone access is turned off in Settings.", ex);
        if (CaptureErrors.IsDeviceGone(ex))
            return new MicrophoneMissingException("That microphone is not connected.", ex);
        return ex;
    }

    // --- Callbacks (capture / audio worker threads) -----------------------------------------

    private void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        try
        {
            if (!_formatChecked)
            {
                // The recorder reports its final format once data flows; trust it over the mix format.
                _formatChecked = true;
                var actual = _capture.WaveFormat;
                if (actual is not null)
                {
                    var format = CaptureFormat.From(actual);
                    if (format != _format)
                    {
                        _format = format;
                        _resampler = new StreamingResampler(format.Channels, format.SampleRate);
                    }
                }
            }

            var samples = PcmDecoder.ToFloat(data, _format.Bits, _format.IsFloat);
            if (flags.HasFlag(AudioClientBufferFlags.Silent)) Array.Clear(samples);
            var mono = _resampler.Process(samples);
            if (mono.Length == 0) return;

            _onSamples?.Invoke(mono);
            if (_onLevel is null) return;
            foreach (var rms in _windower.Append(mono))
            {
                if (_closing) return;
                _onLevel(rms);
            }
        }
        catch (Exception ex)
        {
            // A fault in decoding or a consumer must not take the process down on this thread.
            RaiseStopped(ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) => RaiseStopped(e.Exception);

    private void OnDeviceRemoved(object? sender, DeviceNotificationEventArgs e)
    {
        if (string.Equals(e.DeviceId, _deviceId, StringComparison.OrdinalIgnoreCase))
            RaiseStopped(new InvalidOperationException("The microphone was removed."));
    }

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        if (e.NewState != DeviceState.Active && string.Equals(e.DeviceId, _deviceId, StringComparison.OrdinalIgnoreCase))
            RaiseStopped(new InvalidOperationException("The microphone is no longer available."));
    }

    private void RaiseStopped(Exception? error)
    {
        if (_closing) return;
        if (Interlocked.Exchange(ref _stoppedRaised, 1) != 0) return;
        _onStopped(error);
    }
}

/// <summary>The sample layout of a capture buffer.</summary>
internal readonly record struct CaptureFormat(int Channels, int SampleRate, int Bits, bool IsFloat)
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>Throws <see cref="InvalidOperationException"/> when the sample type is not supported.</summary>
    public static CaptureFormat From(WaveFormat format)
    {
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible ext && ext.SubFormat == IeeeFloatSubFormat);
        if (!PcmDecoder.IsSupported(format.BitsPerSample, isFloat))
            throw new InvalidOperationException($"Unsupported capture format: {format}.");
        return new CaptureFormat(format.Channels, format.SampleRate, format.BitsPerSample, isFloat);
    }
}
