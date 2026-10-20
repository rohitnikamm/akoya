using Akoya.Core.Dictation;

namespace Akoya.Core.Tests;

/// <summary>Audio recorder whose events the test raises by hand.</summary>
public sealed class FakeRecorder : IAudioRecorder
{
    public event Action<float>? Level;
    public event Action? Interrupted;

    public Exception? StartError { get; set; }
    public float[] Samples { get; set; } = [0.1f, 0.2f, 0.3f];
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int CancelCount { get; private set; }
    public List<string?> StartedWith { get; } = [];

    public void Start(string? deviceId)
    {
        StartCount++;
        StartedWith.Add(deviceId);
        if (StartError is not null)
            throw StartError;
    }

    public float[] Stop()
    {
        StopCount++;
        return Samples;
    }

    public float[] Snapshot() => Samples;

    public void Cancel() => CancelCount++;

    public void RaiseLevel(float rms) => Level?.Invoke(rms);

    public void RaiseInterrupted() => Interrupted?.Invoke();
}

/// <summary>
/// Transcriber that answers immediately with <see cref="Result"/> (or fails with
/// <see cref="Error"/>), or, when <see cref="Gated"/>, holds each call in flight until the test
/// calls <see cref="Finish"/> or <see cref="Fail"/>.
/// </summary>
public sealed class FakeTranscriber : ITranscriber
{
    private readonly List<TaskCompletionSource<string>?> _gates = [];

    public FakeTranscriber(string result = "hello") => Result = result;

    public string Result { get; set; }
    public Exception? Error { get; set; }
    public bool Gated { get; set; }
    public List<float[]> Received { get; } = [];
    public int CallCount => Received.Count;
    public int InFlight => _gates.Count(g => g is { Task.IsCompleted: false });

    /// <summary>The most calls that were ever in flight at the same time.</summary>
    public int MaxConcurrent { get; private set; }

    public Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct = default)
    {
        Received.Add(samples16k);
        if (Gated)
        {
            var gate = new TaskCompletionSource<string>();
            _gates.Add(gate);
            MaxConcurrent = Math.Max(MaxConcurrent, InFlight);
            return gate.Task;
        }
        _gates.Add(null);
        MaxConcurrent = Math.Max(MaxConcurrent, 1);
        return Error is null ? Task.FromResult(Result) : Task.FromException<string>(Error);
    }

    public void Finish(int call, string text) => _gates[call]!.SetResult(text);

    public void Fail(int call, Exception error) => _gates[call]!.SetException(error);
}

/// <summary>
/// Text inserter that answers immediately with <see cref="Result"/> (or fails with
/// <see cref="Error"/>), or, when <see cref="Gated"/>, holds the call until <see cref="Finish"/>.
/// </summary>
public sealed class FakeInserter : ITextInserter
{
    private TaskCompletionSource<InsertResult>? _gate;

    public InsertResult Result { get; set; } = InsertResult.Pasted;
    public Exception? Error { get; set; }
    public bool Gated { get; set; }
    public List<string> Inserted { get; } = [];
    public bool Started => _gate is not null;

    public Task<InsertResult> InsertAsync(string text)
    {
        Inserted.Add(text);
        if (Error is not null)
            return Task.FromException<InsertResult>(Error);
        if (Gated)
        {
            _gate = new TaskCompletionSource<InsertResult>();
            return _gate.Task;
        }
        return Task.FromResult(Result);
    }

    public void Finish(InsertResult result) => _gate!.SetResult(result);
}

/// <summary>
/// Deterministic scheduler: time moves only in <see cref="Advance"/>, which fires due actions in
/// due-time order (ties in scheduling order), including actions scheduled while advancing.
/// </summary>
public sealed class ManualScheduler : IScheduler
{
    private readonly List<Entry> _pending = [];
    private long _sequence;

    public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public int PendingCount => _pending.Count;

    public IDisposable After(TimeSpan delay, Action action)
    {
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        var entry = new Entry(this, Now + delay, _sequence++, action);
        _pending.Add(entry);
        return entry;
    }

    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        var target = Now + by;
        while (true)
        {
            Entry? next = null;
            foreach (var entry in _pending)
            {
                if (entry.Due > target)
                    continue;
                if (next is null || entry.Due < next.Due || (entry.Due == next.Due && entry.Sequence < next.Sequence))
                    next = entry;
            }
            if (next is null)
                break;
            _pending.Remove(next);
            Now = next.Due;
            next.Action();
        }
        Now = target;
    }

    private sealed class Entry(ManualScheduler owner, DateTimeOffset due, long sequence, Action action) : IDisposable
    {
        public DateTimeOffset Due { get; } = due;
        public long Sequence { get; } = sequence;
        public Action Action { get; } = action;

        public void Dispose() => owner._pending.Remove(this);
    }
}

/// <summary>
/// Runs every posted continuation immediately on the posting thread, so a test can drive the
/// coordinator's awaits synchronously (xUnit's own context and task scheduler would defer them).
/// Stands in for the app's UI-thread context: one logical thread, deterministic order.
/// </summary>
public sealed class InlineSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Installs a new instance on this thread; dispose to restore the previous context.</summary>
    public static IDisposable Install()
    {
        var previous = Current;
        SetSynchronizationContext(new InlineSynchronizationContext());
        return new Restore(previous);
    }

    private sealed class Restore(SynchronizationContext? previous) : IDisposable
    {
        public void Dispose() => SetSynchronizationContext(previous);
    }
}
