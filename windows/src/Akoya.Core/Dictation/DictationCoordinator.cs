using Akoya.Core.Audio;
using Akoya.Core.Hotkey;

namespace Akoya.Core.Dictation;

public enum DictationPhase
{
    Idle,
    Recording,
    Transcribing,
    Pasted,
    Copied,
    MicMissing,
    MicBlocked,
    ModelNotReady,
    ModelFailed,
    PasteFailed,
}

/// <summary>Where a finished dictation goes.</summary>
public abstract record Destination
{
    private Destination() { }

    /// <summary>Paste into the foreground app (the default).</summary>
    public sealed record Paste : Destination;

    /// <summary>
    /// Setup's Try it card: <c>OnText(text, isFinal)</c> receives live partials while speaking
    /// (<c>isFinal = false</c>) and the final transcript, possibly empty, on stop.
    /// </summary>
    public sealed record Practice(Action<string, bool> OnText) : Destination;
}

/// <summary>
/// The dictation state machine: turns hotkey events into recording, transcription and pasting.
/// Port of the Mac's AppCoordinator.swift.
/// </summary>
/// <remarks>
/// <para>Transitions: <c>Idle → Recording → Transcribing → Pasted | Copied | PasteFailed → Idle</c>.
/// A press that cannot record shows <c>ModelNotReady</c>, <c>ModelFailed</c>, <c>MicMissing</c> or
/// <c>MicBlocked</c> for <see cref="AlertDisplay"/>. A silent recording, an empty transcript or a
/// transcription error goes straight back to <c>Idle</c> (the pill fades). The phase stays
/// <c>Transcribing</c> until the inserter returns, so a press cannot start a recording while the
/// clipboard is still being restored.</para>
/// <para>The hotkey is hold-or-tap: releasing at least <see cref="HoldThreshold"/> after the press
/// stops (push-to-talk); releasing sooner latches the recording on and the next press stops it.
/// Esc and <see cref="SessionLocked"/> cancel either way.</para>
/// <para>Threading: one logical thread. Call every member, and raise the ports' events, on the
/// same thread (the app's UI thread); awaits resume through that thread's
/// <see cref="SynchronizationContext"/>. The class has no locks.</para>
/// </remarks>
public sealed class DictationCoordinator
{
    public static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan MaxRecording = TimeSpan.FromSeconds(300);

    /// <summary>
    /// A recording whose loudest <see cref="LevelMeter.Step"/> never rose above this is silence
    /// and is not transcribed: Parakeet invents text such as "Okay." for silent audio. Step 2 is
    /// roughly -44 to -39 dBFS.
    /// </summary>
    public const int SilenceMaxStep = 2;

    public static readonly TimeSpan PastedDisplay = TimeSpan.FromMilliseconds(900);
    public static readonly TimeSpan AlertDisplay = TimeSpan.FromMilliseconds(1600);
    public static readonly TimeSpan PreviewTick = TimeSpan.FromMilliseconds(100);

    /// <summary>Sample rate of <see cref="IAudioRecorder"/> output.</summary>
    public const int SampleRate = 16_000;

    private readonly IAudioRecorder _audio;
    private readonly ITranscriber _transcriber;
    private readonly ITextInserter _inserter;
    private readonly IScheduler _scheduler;
    private readonly Func<ModelState> _modelState;
    private readonly Func<string?> _deviceId;

    private Destination _destination = new Destination.Paste();

    /// <summary>Loudest level step seen in the current recording.</summary>
    private int _loudestStep = 1;

    /// <summary>True once a quick tap released the key: the next press stops.</summary>
    private bool _latched;

    /// <summary>The session locked while this dictation was transcribing: don't paste it.</summary>
    private bool _lockedWhileTranscribing;

    /// <summary>Bumped per recording so late timers and partials from an earlier one are ignored.</summary>
    private int _generation;

    private LivePreviewScheduler _preview = new();

    /// <summary>The transcription in flight (preview or final), if any: never two at once.</summary>
    private Task<string>? _inFlight;

    private IDisposable? _maxTimer;
    private IDisposable? _previewTimer;
    private IDisposable? _displayTimer;

    public DictationCoordinator(IAudioRecorder audio, ITranscriber transcriber, ITextInserter inserter,
        IScheduler scheduler, Func<ModelState> modelState, Func<string?> deviceId)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        _inserter = inserter ?? throw new ArgumentNullException(nameof(inserter));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _modelState = modelState ?? throw new ArgumentNullException(nameof(modelState));
        _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));

        _audio.Level += OnAudioLevel;
        _audio.Interrupted += OnAudioInterrupted;
    }

    public DictationPhase Phase { get; private set; } = DictationPhase.Idle;

    public event Action<DictationPhase>? PhaseChanged;

    /// <summary>RMS forwarded while recording; 0 once the recording stops or is cancelled.</summary>
    public event Action<float>? Level;

    /// <summary>
    /// An unexpected failure the coordinator recovered from (a start failure other than the two
    /// microphone exceptions, a transcription or insert error, an <c>OnText</c> callback that
    /// threw). For logging: the phase has already moved on as documented.
    /// </summary>
    public event Action<Exception>? Faulted;

    /// <summary>
    /// When the current recording started (from <see cref="IScheduler.Now"/>). Set while
    /// <c>Recording</c> and <c>Transcribing</c>, null otherwise.
    /// </summary>
    public DateTimeOffset? RecordingStartedAt { get; private set; }

    /// <summary>The last transcript that went to the paste destination (pasted, copied or not).</summary>
    public string? LastDictation { get; private set; }

    /// <summary>Read when a recording stops, so switching back to Paste mid-recording pastes.</summary>
    public Destination Destination
    {
        get => _destination;
        set => _destination = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void Handle(HotkeyEvent e)
    {
        switch (e, Phase)
        {
            // The result displays are cosmetic: a press starts the next dictation right away.
            case (HotkeyEvent.Pressed, DictationPhase.Idle or DictationPhase.Pasted
                or DictationPhase.Copied or DictationPhase.PasteFailed):
                BeginDictation();
                break;
            case (HotkeyEvent.Pressed, DictationPhase.Recording):
                // A press while the key is already held (no release seen) is not a stop request.
                if (_latched)
                    StopRecording();
                break;
            case (HotkeyEvent.Released, DictationPhase.Recording):
                // A latched recording ignores releases. (The release that follows the stopping
                // press arrives after the phase left Recording, so it never reaches here.)
                if (_latched)
                    break;
                if (RecordingStartedAt is { } start && _scheduler.Now - start >= HoldThreshold)
                    StopRecording();
                else
                    _latched = true;
                break;
            case (HotkeyEvent.Cancel, DictationPhase.Recording):
                CancelRecording();
                break;
            default:
                // Presses while transcribing or alerting, stray releases and Esc are ignored.
                break;
        }
    }

    /// <summary>
    /// The workstation locked: cancel a recording like Esc. A dictation already transcribing is
    /// not pasted into whatever has focus after unlock; its text is kept as
    /// <see cref="LastDictation"/>.
    /// </summary>
    public void SessionLocked()
    {
        if (Phase == DictationPhase.Recording)
            CancelRecording();
        else if (Phase == DictationPhase.Transcribing)
            _lockedWhileTranscribing = true;
    }

    // Recording

    private void BeginDictation()
    {
        DisposeTimer(ref _displayTimer);

        switch (_modelState())
        {
            case ModelState.Loading:
                ShowFor(DictationPhase.ModelNotReady, AlertDisplay);
                return;
            case ModelState.Failed:
                ShowFor(DictationPhase.ModelFailed, AlertDisplay);
                return;
        }

        _generation++;
        _loudestStep = 1;
        _latched = false;
        _lockedWhileTranscribing = false;
        _preview = new LivePreviewScheduler();

        // The hold time counts from the press, so a slow device start doesn't eat into it.
        var startedAt = _scheduler.Now;
        try
        {
            _audio.Start(_deviceId());
        }
        catch (MicrophoneBlockedException)
        {
            ShowFor(DictationPhase.MicBlocked, AlertDisplay);
            return;
        }
        catch (MicrophoneMissingException)
        {
            ShowFor(DictationPhase.MicMissing, AlertDisplay);
            return;
        }
        catch (Exception e)
        {
            // Any other start failure: the device can't be used, so it reads as not detected.
            Fault(e);
            ShowFor(DictationPhase.MicMissing, AlertDisplay);
            return;
        }

        RecordingStartedAt = startedAt;
        SetPhase(DictationPhase.Recording);

        var generation = _generation;
        _maxTimer = _scheduler.After(MaxRecording, () =>
        {
            _maxTimer = null;
            if (generation == _generation && Phase == DictationPhase.Recording)
                StopRecording();
        });
        if (_destination is Destination.Practice)
            ArmPreviewTick(generation);
    }

    // Both leave Recording before calling the port, so anything the adapter raises from inside
    // Stop/Cancel (Interrupted, Level) or a re-entrant Handle sees a finished recording.

    private void CancelRecording()
    {
        EndRecordingTimers();
        SetPhase(DictationPhase.Idle);
        _audio.Cancel();
        Level?.Invoke(0);
    }

    private void StopRecording()
    {
        EndRecordingTimers();
        var silent = _loudestStep <= SilenceMaxStep; // nothing was said: the pill just fades
        var destination = _destination;
        SetPhase(silent ? DictationPhase.Idle : DictationPhase.Transcribing);
        var samples = _audio.Stop();
        Level?.Invoke(0);
        if (!silent)
            _ = FinishAsync(samples, destination);
    }

    private async Task FinishAsync(float[] samples, Destination destination)
    {
        try
        {
            await DeliverAsync(samples, destination);
        }
        catch (Exception e)
        {
            // Only an OnText callback can get here; the dictation is over either way.
            Fault(e);
            if (Phase == DictationPhase.Transcribing)
                SetPhase(DictationPhase.Idle);
        }
    }

    private async Task DeliverAsync(float[] samples, Destination destination)
    {
        // A partial pass may still be running; never run two transcriptions at once.
        if (_inFlight is { } partial)
        {
            try
            {
                await partial;
            }
            catch (Exception)
            {
                // Already reported through Faulted by PreviewAsync.
            }
        }

        string? text;
        try
        {
            text = (await TranscribeExclusive(samples)).Trim();
        }
        catch (Exception e)
        {
            Fault(e);
            text = null; // like an empty transcript: the pill fades
        }

        if (destination is Destination.Practice practice)
        {
            practice.OnText(text ?? "", true);
            SetPhase(DictationPhase.Idle);
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            SetPhase(DictationPhase.Idle);
            return;
        }

        if (_lockedWhileTranscribing)
        {
            // Never paste into whatever has focus after the lock; keep the text for the panel.
            LastDictation = text;
            SetPhase(DictationPhase.Idle);
            return;
        }

        InsertResult result;
        try
        {
            result = await _inserter.InsertAsync(text + " ");
        }
        catch (Exception e)
        {
            Fault(e);
            result = InsertResult.Failed;
        }
        LastDictation = text;
        switch (result)
        {
            case InsertResult.Pasted:
                ShowFor(DictationPhase.Pasted, PastedDisplay);
                break;
            case InsertResult.CopiedOnly:
                ShowFor(DictationPhase.Copied, PastedDisplay);
                break;
            default:
                ShowFor(DictationPhase.PasteFailed, AlertDisplay);
                break;
        }
    }

    // Live preview (practice destination)

    private void ArmPreviewTick(int generation)
    {
        _previewTimer = _scheduler.After(PreviewTick, () =>
        {
            _previewTimer = null;
            if (generation != _generation || Phase != DictationPhase.Recording)
                return;
            TickLivePreview();
            ArmPreviewTick(generation);
        });
    }

    /// <summary>
    /// One live-preview tick: if the scheduler allows, re-transcribe the audio captured so far
    /// and report it as a partial. Does nothing once the destination is no longer Practice.
    /// </summary>
    private void TickLivePreview()
    {
        if (_destination is not Destination.Practice)
            return;
        var samples = _audio.Snapshot();
        var audio = TimeSpan.FromSeconds(samples.Length / (double)SampleRate);
        if (!_preview.ShouldRun(_scheduler.Now, audio, _loudestStep, inFlight: _inFlight is not null))
            return;
        _ = PreviewAsync(samples, _generation);
    }

    private async Task PreviewAsync(float[] samples, int generation)
    {
        string text;
        try
        {
            text = await TranscribeExclusive(samples);
        }
        catch (Exception e)
        {
            Fault(e);
            return; // a failed partial is skipped; the next tick may try again
        }
        // The final pass owns the result once the recording has stopped.
        if (generation != _generation || Phase != DictationPhase.Recording
            || _destination is not Destination.Practice practice)
            return;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return;
        try
        {
            practice.OnText(trimmed, false);
        }
        catch (Exception e)
        {
            Fault(e); // the recording carries on
        }
    }

    /// <summary>Runs one transcription, tracked in <see cref="_inFlight"/> until it settles.</summary>
    private async Task<string> TranscribeExclusive(float[] samples)
    {
        Task<string> task;
        try
        {
            task = _transcriber.TranscribeAsync(samples);
        }
        catch (Exception e)
        {
            task = Task.FromException<string>(e);
        }
        _inFlight = task;
        try
        {
            return await task;
        }
        finally
        {
            if (ReferenceEquals(_inFlight, task))
                _inFlight = null;
        }
    }

    // Audio events

    private void OnAudioLevel(float rms)
    {
        if (Phase != DictationPhase.Recording)
            return;
        _loudestStep = Math.Max(_loudestStep, LevelMeter.Step(rms));
        Level?.Invoke(rms);
    }

    private void OnAudioInterrupted()
    {
        if (Phase == DictationPhase.Recording)
            StopRecording(); // transcribe what was captured
    }

    // Phase plumbing

    /// <summary>Shows <paramref name="phase"/> for <paramref name="duration"/>, then Idle.</summary>
    private void ShowFor(DictationPhase phase, TimeSpan duration)
    {
        DisposeTimer(ref _displayTimer);
        SetPhase(phase);
        _displayTimer = _scheduler.After(duration, () =>
        {
            _displayTimer = null;
            if (Phase == phase)
                SetPhase(DictationPhase.Idle);
        });
    }

    private void Fault(Exception e) => Faulted?.Invoke(e);

    private void SetPhase(DictationPhase phase)
    {
        if (phase is not (DictationPhase.Recording or DictationPhase.Transcribing))
            RecordingStartedAt = null;
        if (Phase == phase)
            return;
        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }

    private void EndRecordingTimers()
    {
        DisposeTimer(ref _maxTimer);
        DisposeTimer(ref _previewTimer);
    }

    private static void DisposeTimer(ref IDisposable? timer)
    {
        timer?.Dispose();
        timer = null;
    }
}
