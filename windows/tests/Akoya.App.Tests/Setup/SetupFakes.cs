using Akoya.App.Setup;
using Akoya.Core.Dictation;
using Akoya.Core.Mics;

namespace Akoya.App.Tests.Setup;

internal sealed class FakeCatalog(params EndpointFacts[] endpoints) : IMicCatalog
{
    public List<EndpointFacts> Endpoints { get; } = [.. endpoints];

    public event Action? Changed;

    public IReadOnlyList<EndpointFacts> Snapshot() => Endpoints.ToList();

    public void Change(params EndpointFacts[] endpoints)
    {
        Endpoints.Clear();
        Endpoints.AddRange(endpoints);
        Changed?.Invoke();
    }
}

internal sealed class FakeLevels : IMicLevels
{
    public event Action<string, int>? Step;

    public List<string>? Started { get; private set; }

    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public bool Running => Started is not null;

    public void Start(IEnumerable<string> ids)
    {
        Started = ids.ToList();
        Starts++;
    }

    public void Stop()
    {
        Started = null;
        Stops++;
    }

    public void Raise(string id, int step) => Step?.Invoke(id, step);
}

internal sealed class FakePrivacy : IMicPrivacy
{
    public bool Allowed { get; set; } = true;

    public int Opened { get; private set; }

    public bool IsDesktopAccessAllowed() => Allowed;

    public void OpenSettings() => Opened++;
}

internal sealed class FakePractice : IPracticeDictation
{
    public event Action<DictationPhase>? PhaseChanged;

    public event Action<float>? Level;

    public Action<string, bool>? OnText { get; private set; }

    public int Ends { get; private set; }

    public void Begin(Action<string, bool> onText) => OnText = onText;

    public void End()
    {
        OnText = null;
        Ends++;
    }

    public void Phase(DictationPhase phase) => PhaseChanged?.Invoke(phase);

    public void Rms(float rms) => Level?.Invoke(rms);

    public void Text(string text, bool isFinal) => OnText?.Invoke(text, isFinal);
}

/// <summary>Manual clock and timers: <see cref="Advance"/> runs every timer that comes due, in order.</summary>
internal sealed class FakeScheduler : IScheduler
{
    private readonly List<Timer> _timers = [];

    public DateTimeOffset Now { get; private set; } = new(2026, 10, 20, 9, 41, 0, TimeSpan.Zero);

    public IDisposable After(TimeSpan delay, Action action)
    {
        var timer = new Timer(this, Now + delay, action);
        _timers.Add(timer);
        return timer;
    }

    public int Pending => _timers.Count;

    public void Advance(double seconds)
    {
        var end = Now + TimeSpan.FromSeconds(seconds);
        while (_timers.Where(t => t.Due <= end).OrderBy(t => t.Due).FirstOrDefault() is { } next)
        {
            _timers.Remove(next);
            Now = next.Due;
            next.Action();
        }
        Now = end;
    }

    private sealed class Timer(FakeScheduler owner, DateTimeOffset due, Action action) : IDisposable
    {
        public DateTimeOffset Due { get; } = due;

        public Action Action { get; } = action;

        public void Dispose() => owner._timers.Remove(this);
    }
}

/// <summary>The mockup's devices.</summary>
internal static class Devices
{
    public static EndpointFacts Laptop(bool isDefault = true) =>
        new("laptop", "Microphone Array (Realtek(R) Audio)", "Microphone", EndpointFormFactor.Microphone, "HDAUDIO", true, isDefault);

    public static EndpointFacts Buds(bool isDefault = false) =>
        new("buds", "Headset (Galaxy Buds Hands-Free AG Audio)", "Galaxy Buds Hands-Free AG Audio", EndpointFormFactor.Headset,
            "BTHHFENUM", true, isDefault);

    public static EndpointFacts Webcam(string id = "cam", bool isDefault = false) =>
        new(id, "Microphone (Logitech BRIO)", "Microphone", EndpointFormFactor.Microphone, "USB", true, isDefault);

    public static EndpointFacts HyperX(bool isDefault = true) =>
        new("hyperx", "Headset Microphone (HyperX Cloud III)", "HyperX Cloud III", EndpointFormFactor.Headset, "USB", true, isDefault);

    public static EndpointFacts Nvidia(bool isDefault = false) =>
        new("nvidia", "Microphone (NVIDIA Broadcast)", "Microphone", EndpointFormFactor.Microphone, "ROOT", false, isDefault);

    public static EndpointFacts StereoMix() =>
        new("mix", "Stereo Mix (Realtek(R) Audio)", "Stereo Mix", EndpointFormFactor.LineLevel, "HDAUDIO", true, false);
}

/// <summary>A view model wired to fakes.</summary>
internal sealed class SetupRig
{
    public SetupRig(params EndpointFacts[] endpoints)
    {
        Catalog = new FakeCatalog(endpoints);
        Model = new SetupViewModel(Catalog, Levels, Privacy, Practice, Clock);
    }

    public FakeCatalog Catalog { get; }

    public FakeLevels Levels { get; } = new();

    public FakePrivacy Privacy { get; } = new();

    public FakePractice Practice { get; } = new();

    public FakeScheduler Clock { get; } = new();

    public SetupViewModel Model { get; }

    /// <summary>Every mic reports a step, then time moves on by one 40 ms meter window.</summary>
    public void Levels40ms(params (string id, int step)[] steps)
    {
        foreach (var (id, step) in steps) Levels.Raise(id, step);
        Clock.Advance(0.04);
    }

    /// <summary>Repeats <see cref="Levels40ms"/> for <paramref name="seconds"/>.</summary>
    public void Talk(double seconds, params (string id, int step)[] steps)
    {
        for (var i = 0; i < (int)Math.Round(seconds / 0.04); i++) Levels40ms(steps);
    }
}
