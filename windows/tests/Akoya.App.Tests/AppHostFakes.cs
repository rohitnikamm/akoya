using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Akoya.App.Pill;
using Akoya.App.Platform;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Akoya.Core.Mics;
using Akoya.Core.Settings;

namespace Akoya.App.Tests;

/// <summary>Runs a test on an STA thread with a dispatcher and its SynchronizationContext, as the app's UI thread.</summary>
internal static class UiThread
{
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60))) throw new TimeoutException("UI thread test did not finish.");
        if (failure is not null) throw new Exception("UI thread test failed: " + failure.Message, failure);
    }

    /// <summary>Runs queued dispatcher work (posted continuations) until <paramref name="done"/> or the timeout.</summary>
    public static void PumpUntil(Func<bool> done, int timeoutMs = 10_000)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not met while pumping.");
            DoEvents();
            Thread.Sleep(2);
        }
    }

    public static void Pump(TimeSpan duration)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < duration)
        {
            DoEvents();
            Thread.Sleep(5);
        }
    }

    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}

/// <summary>Time that moves only when the test says so; due actions run in order.</summary>
internal sealed class ManualScheduler : IScheduler
{
    private readonly List<(DateTimeOffset Due, long Order, Action Action, Handle Handle)> _pending = [];
    private long _order;

    public DateTimeOffset Now { get; private set; } = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public IDisposable After(TimeSpan delay, Action action)
    {
        var handle = new Handle();
        _pending.Add((Now + (delay < TimeSpan.Zero ? TimeSpan.Zero : delay), _order++, action, handle));
        return handle;
    }

    public void Advance(TimeSpan by)
    {
        var end = Now + by;
        while (true)
        {
            var next = _pending.Where(p => !p.Handle.Disposed && p.Due <= end).OrderBy(p => p.Due).ThenBy(p => p.Order)
                .Cast<(DateTimeOffset Due, long Order, Action Action, Handle Handle)?>().FirstOrDefault();
            if (next is not { } due) break;
            _pending.Remove(due);
            if (due.Due > Now) Now = due.Due;
            due.Handle.Disposed = true;
            due.Action();
        }
        Now = end;
        _pending.RemoveAll(p => p.Handle.Disposed);
    }

    private sealed class Handle : IDisposable
    {
        public bool Disposed;

        public void Dispose() => Disposed = true;
    }
}

internal sealed class FakeRecorder : IAudioRecorder, IDisposable
{
    public event Action<float>? Level;

    public event Action? Interrupted;

    /// <summary>Endpoint IDs that exist; null as Start's argument means the default, which always exists here.</summary>
    public HashSet<string> Present { get; } = ["mic-1"];

    public bool Recording { get; private set; }

    public int Starts { get; private set; }

    public int Cancels { get; private set; }

    public bool Disposed { get; private set; }

    public void Start(string? deviceId)
    {
        if (deviceId is not null && !Present.Contains(deviceId)) throw new MicrophoneMissingException();
        Starts++;
        Recording = true;
    }

    public float[] Stop()
    {
        Recording = false;
        return new float[16_000];
    }

    public float[] Snapshot() => new float[16_000];

    public void Cancel()
    {
        Recording = false;
        Cancels++;
    }

    public void RaiseLevel(float rms) => Level?.Invoke(rms);

    public void RaiseInterrupted() => Interrupted?.Invoke();

    public void Dispose() => Disposed = true;
}

internal sealed class FakeTranscriber(string text) : ITranscriber, IDisposable
{
    public bool Disposed { get; private set; }

    public Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct = default) => Task.FromResult(text);

    public void Dispose() => Disposed = true;
}

internal sealed class FakeInserter : ITextInserter
{
    public List<string> Inserted { get; } = [];

    public Func<string, InsertResult> Result { get; set; } = _ => InsertResult.Pasted;

    /// <summary>When set, every insert waits for this (a paste still restoring the clipboard).</summary>
    public Task<InsertResult>? Pending { get; set; }

    public Task<InsertResult> InsertAsync(string text)
    {
        Inserted.Add(text);
        return Pending ?? Task.FromResult(Result(text));
    }
}

internal sealed class FakeCatalog : IMicrophoneCatalog
{
    public event Action? Changed;

    public List<EndpointFacts> Endpoints { get; } =
    [
        new("mic-1", "Microphone Array (Realtek(R) Audio)", "Microphone Array", EndpointFormFactor.Microphone, "HDAUDIO", true, true),
    ];

    public IReadOnlyList<EndpointFacts> Snapshot() => Endpoints.ToArray();

    public void RaiseChanged() => Changed?.Invoke();
}

internal sealed class FakeHook : IHotkeyHook
{
    public Action<HotkeyEvent>? OnEvent { get; set; }

    public HotkeyKey CreatedWith { get; set; }

    public Func<Exception?> InstallError { get; set; } = () => null;

    public int Installs { get; private set; }

    public int Reinstalls { get; private set; }

    public int Resets { get; private set; }

    public List<HotkeyKey> Keys { get; } = [];

    public bool RecordingActive { get; private set; }

    public bool Disposed { get; private set; }

    public void Install()
    {
        Installs++;
        if (InstallError() is { } e) throw e;
    }

    public void Reinstall()
    {
        Reinstalls++;
        if (InstallError() is { } e) throw e;
    }

    public void SetKey(HotkeyKey key) => Keys.Add(key);

    public void SetRecordingActive(bool active) => RecordingActive = active;

    public void Reset() => Resets++;

    public void Dispose() => Disposed = true;

    public void Fire(HotkeyEvent e) => OnEvent!(e);
}

internal sealed class FakeModel : ISpeechModel
{
    public bool IsInstalled { get; set; } = true;

    public int Ensures { get; private set; }

    public int Loads { get; private set; }

    /// <summary>Each call's result; completes at once by default.</summary>
    public Func<IProgress<double>, Task> Ensure { get; set; } = _ => Task.CompletedTask;

    public FakeTranscriber Transcriber { get; } = new("hello world");

    public Task EnsureAsync(IProgress<double> progress, CancellationToken ct)
    {
        Ensures++;
        return Ensure(progress);
    }

    public ITranscriber Load(InferenceProvider provider)
    {
        Loads++;
        return Transcriber;
    }
}

internal sealed class FakeSettings : ISettingsFile
{
    public AkoyaSettings Stored { get; set; } = new();

    public Exception? LoadError { get; set; }

    public int Saves { get; private set; }

    public AkoyaSettings Load() => LoadError is { } e ? throw e : Stored;

    public void Save(AkoyaSettings settings)
    {
        Saves++;
        Stored = settings;
    }
}

internal sealed class FakePill : IPillPresenter
{
    public List<(PillMode Mode, string Text, bool Red)> Shows { get; } = [];

    public PillViewModel? Model { get; private set; }

    public bool Visible { get; private set; }

    public int Retargets { get; private set; }

    public bool Disposed { get; private set; }

    public void Show(PillViewModel model, bool retarget)
    {
        if (retarget && Visible) Retargets++;
        Model = model;
        Visible = true;
        Shows.Add((model.Mode, model.AlertText, model.IsRed));
    }

    public void FadeOut() => Visible = false;

    public void Dispose() => Disposed = true;
}

internal sealed class FakeTray : ITrayPresenter
{
    public TrayCommands? Commands { get; set; }

    public List<DictationPhase> Phases { get; } = [];

    public ModelState Model { get; private set; }

    public double? Progress { get; private set; }

    public bool MicBlocked { get; private set; }

    public bool HotkeyUnavailable { get; private set; }

    public IReadOnlyList<NamedMic> Mics { get; private set; } = [];

    public string? SavedMicId { get; private set; }

    public string? SavedMicName { get; private set; }

    /// <summary>What the real panel highlights yellow.</summary>
    public bool MicMissing => MicrophoneResolver.Resolve(SavedMicId, Mics).Missing;

    public AkoyaSettings? Settings { get; private set; }

    public string? LastDictation { get; private set; }

    public bool UpdateReady { get; private set; }

    public int PanelShows { get; private set; }

    public double VoiceLevel { get; private set; }

    public bool Disposed { get; private set; }

    public void SetPhase(DictationPhase phase) => Phases.Add(phase);

    public void SetVoiceLevel(double level) => VoiceLevel = level;

    public void SetModel(ModelState state, double? progress)
    {
        Model = state;
        Progress = progress;
    }

    public void SetMicBlocked(bool blocked) => MicBlocked = blocked;

    public void SetHotkeyUnavailable(bool unavailable) => HotkeyUnavailable = unavailable;

    public void SetMicrophones(IReadOnlyList<NamedMic> mics, string? savedId, string? savedName)
    {
        Mics = mics;
        SavedMicId = savedId;
        SavedMicName = savedName;
    }

    public void SetSettings(AkoyaSettings settings) => Settings = settings;

    public void SetLastDictation(string? text) => LastDictation = text;

    public void SetUpdateReady(bool ready) => UpdateReady = ready;

    public void ShowPanel() => PanelShows++;

    public void Dispose() => Disposed = true;
}

internal sealed class FakeSetup : ISetupPresenter
{
    public event Action<string>? MicChosen;

    public event Action? Finished;

    public SetupContext? Context { get; set; }

    public bool Shown { get; private set; }

    public bool Disposed { get; private set; }

    public HotkeyKey Hotkey { get; private set; }

    public ModelState Model { get; private set; }

    public List<DictationPhase> Phases { get; } = [];

    public void Show() => Shown = true;

    public void SetModel(ModelState state, double? progress) => Model = state;

    public void SetHotkey(HotkeyKey key) => Hotkey = key;

    public void SetPhase(DictationPhase phase) => Phases.Add(phase);

    public void SetLevel(float rms)
    {
    }

    public void ChooseMic(string id) => MicChosen?.Invoke(id);

    public void Finish() => Finished?.Invoke();

    public void Dispose() => Disposed = true;
}

internal sealed class FakeSession : ISessionEvents
{
    public event Action? Locked;

    public event Action? Unlocked;

    public bool Disposed { get; private set; }

    public void Lock() => Locked?.Invoke();

    public void Unlock() => Unlocked?.Invoke();

    public void Dispose() => Disposed = true;
}

internal sealed class FakeStartup : IStartupSetting
{
    public bool Enabled { get; set; }

    public bool IsEnabled() => Enabled;

    public void Set(bool enabled) => Enabled = enabled;

    public void Refresh()
    {
    }
}

internal sealed class FakeUpdates : IUpdates
{
    public Func<(bool Ready, Exception? Error)> Next { get; set; } = () => (false, null);

    public int Checks { get; private set; }

    public int Applies { get; private set; }

    public Exception? LastError { get; private set; }

    public Task<bool> CheckAndDownloadAsync()
    {
        Checks++;
        var (ready, error) = Next();
        LastError = error;
        return Task.FromResult(ready);
    }

    public void ApplyAndRestart() => Applies++;
}

internal sealed class MemoryLog : ILog
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines) return _lines.ToArray();
        }
    }

    public string Text => string.Join(Environment.NewLine, Lines);

    public IEnumerable<string> Errors => Lines.Where(l => l.StartsWith("ERROR", StringComparison.Ordinal));

    public void Info(string message)
    {
        lock (_lines) _lines.Add("INFO " + message);
    }

    public void Error(string message, Exception? exception = null)
    {
        lock (_lines) _lines.Add("ERROR " + (exception is null ? message : message + ": " + FileLog.Describe(exception)));
    }
}

/// <summary>An AppHost wired to fakes. Build it on the UI thread.</summary>
internal sealed class HostRig : IDisposable
{
    public ManualScheduler Scheduler { get; } = new();
    public FakeRecorder Recorder { get; } = new();
    public FakeCatalog Catalog { get; } = new();
    public FakeInserter Inserter { get; } = new();
    public FakeHook Hook { get; } = new();
    public FakeModel Model { get; } = new();
    public FakeSettings Settings { get; } = new() { Stored = new AkoyaSettings(SetupDone: true) };
    public FakePill Pill { get; } = new();
    public FakeTray Tray { get; } = new();
    public FakeSetup Setup { get; } = new();
    public FakeSession Session { get; } = new();
    public FakeStartup Startup { get; } = new();
    public FakeUpdates Updates { get; } = new();
    public MemoryLog Log { get; } = new();
    public List<string> Copied { get; } = [];
    public bool MicAllowed { get; set; } = true;
    public int Shutdowns { get; private set; }
    public int SetupsCreated { get; private set; }

    private AppHost? _host;

    public AppHost Host => _host ?? throw new InvalidOperationException("Call Start first.");

    public AppHost Start(bool waitForModel = true)
    {
        _host = new AppHost(new AppPorts
        {
            Recorder = Recorder,
            Microphones = Catalog,
            Inserter = Inserter,
            Scheduler = Scheduler,
            CreateHotkeyHook = (key, onEvent) =>
            {
                Hook.CreatedWith = key;
                Hook.OnEvent = onEvent;
                return Hook;
            },
            Model = Model,
            Settings = Settings,
            Pill = Pill,
            Session = Session,
            Startup = Startup,
            Log = Log,
            Shutdown = () => Shutdowns++,
            CreateTray = commands =>
            {
                Tray.Commands = commands;
                return Tray;
            },
            CreateSetup = context =>
            {
                SetupsCreated++;
                Setup.Context = context;
                return Setup;
            },
            Updates = Updates,
            MicAccessAllowed = () => MicAllowed,
            CopyToClipboard = text =>
            {
                Copied.Add(text);
                return true;
            },
            Owned = [Recorder],
        });
        _host.Start();
        if (waitForModel) UiThread.PumpUntil(() => _host.ModelState == ModelState.Ready);
        return _host;
    }

    public void Dispose() => _host?.Dispose();
}

internal static class TempDir
{
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "akoya-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
