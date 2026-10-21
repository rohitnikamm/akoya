namespace Akoya.Core.Dictation;

/// <summary>
/// Microphone capture. Events are raised on the coordinator's thread (the app's UI thread):
/// the adapter marshals them there. Events must not be raised synchronously from inside
/// <see cref="Start"/>, <see cref="Stop"/> or <see cref="Cancel"/> (the coordinator ignores them
/// there, but an adapter that does so is wrong); post them instead.
/// </summary>
public interface IAudioRecorder
{
    /// <summary>RMS of the latest window, 0...1, about 25 Hz while recording.</summary>
    event Action<float>? Level;

    /// <summary>The device was lost mid-recording.</summary>
    event Action? Interrupted;

    /// <summary>
    /// Starts capturing from <paramref name="deviceId"/> (null = the system default). Throws
    /// <see cref="MicrophoneMissingException"/> when the device (or any microphone) is absent and
    /// <see cref="MicrophoneBlockedException"/> when the privacy switch blocks capture.
    /// </summary>
    void Start(string? deviceId);

    /// <summary>Stops capturing and returns everything recorded, 16 kHz mono.</summary>
    float[] Stop();

    /// <summary>The samples recorded so far, without stopping or clearing them.</summary>
    float[] Snapshot();

    /// <summary>Stops capturing and discards the recording.</summary>
    void Cancel();
}

public interface ITranscriber
{
    Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct = default);
}

public enum InsertResult
{
    /// <summary>The text was pasted into the foreground app.</summary>
    Pasted,

    /// <summary>The text is on the clipboard but could not be pasted (e.g. elevated window).</summary>
    CopiedOnly,

    /// <summary>Nothing was pasted or copied (e.g. another app holds the clipboard).</summary>
    Failed,
}

public interface ITextInserter
{
    /// <summary>
    /// Inserts <paramref name="text"/> into the foreground app. The task completes only after the
    /// clipboard has been restored, so the caller can treat its completion as the end of the paste.
    /// </summary>
    Task<InsertResult> InsertAsync(string text);
}

public enum ModelState { Loading, Ready, Failed }

/// <summary>The chosen microphone, or every microphone, is absent.</summary>
public sealed class MicrophoneMissingException : Exception
{
    public MicrophoneMissingException() : base("Microphone not detected.") { }

    public MicrophoneMissingException(string message) : base(message) { }

    public MicrophoneMissingException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Windows' microphone privacy setting blocks capture for this app.</summary>
public sealed class MicrophoneBlockedException : Exception
{
    public MicrophoneBlockedException() : base("Microphone access is turned off in Settings.") { }

    public MicrophoneBlockedException(string message) : base(message) { }

    public MicrophoneBlockedException(string message, Exception innerException)
        : base(message, innerException) { }
}
