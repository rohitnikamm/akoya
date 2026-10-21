using Akoya.App.Platform;
using Akoya.Core.Dictation;
using Xunit;
using static Akoya.App.Tests.Threads;

namespace Akoya.App.Tests;

/// <summary>A SynchronizationContext that queues posts so a test decides when they run.</summary>
internal sealed class RecordingContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();

    public int PendingCount
    {
        get { lock (_pending) return _pending.Count; }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_pending) _pending.Enqueue((d, state));
    }

    public void RunPending()
    {
        while (true)
        {
            (SendOrPostCallback Callback, object? State) item;
            lock (_pending)
            {
                if (_pending.Count == 0) return;
                item = _pending.Dequeue();
            }
            item.Callback(item.State);
        }
    }

    /// <summary>Runs <paramref name="body"/> with this as the current context, then restores the old one.</summary>
    public void Run(Action body)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try { body(); }
        finally { SetSynchronizationContext(previous); }
    }
}

internal sealed class FakeStream : IMicStream
{
    public Action<float[]>? OnSamples;
    public Action<float>? OnLevel;
    public Action<Exception?>? OnStopped;
    public bool Started;
    public int CloseCount;
    public Action? OnStart;
    public Exception? StartFailure;

    public void Start()
    {
        if (StartFailure is not null) throw StartFailure;
        Started = true;
        OnStart?.Invoke();
    }

    public void Close() => CloseCount++;
}

internal static class Threads
{
    /// <summary>Runs the action on a different thread and waits for it, like a capture thread raising an event.</summary>
    public static void OnOtherThread(Action action)
    {
        var thread = new Thread(() => action());
        thread.Start();
        thread.Join();
    }
}

public class WasapiRecorderEventTests
{
    private static (WasapiRecorder Recorder, List<FakeStream> Streams) Create()
    {
        var streams = new List<FakeStream>();
        var recorder = new WasapiRecorder((id, samples, level, stopped) =>
        {
            var fake = new FakeStream { OnSamples = samples, OnLevel = level, OnStopped = stopped };
            streams.Add(fake);
            return fake;
        });
        return (recorder, streams);
    }

    [Fact]
    public void Level_IsPostedToTheContextCapturedAtStart_NeverRaisedInline()
    {
        var context = new RecordingContext();
        var (recorder, streams) = Create();
        var levels = new List<float>();
        recorder.Level += levels.Add;

        context.Run(() => recorder.Start(null));
        // Raised from another thread, as the capture thread would.
        OnOtherThread(() => streams[0].OnLevel!(0.5f));

        Assert.Empty(levels);
        Assert.Equal(1, context.PendingCount);
        context.RunPending();
        Assert.Equal(new[] { 0.5f }, levels);
    }

    [Fact]
    public void EventsRaisedDuringStart_AreStillPosted()
    {
        var context = new RecordingContext();
        var levels = new List<float>();
        var recorder = new WasapiRecorder((id, samples, level, stopped) =>
            new FakeStream { OnStart = () => level!(0.25f) });
        recorder.Level += levels.Add;

        context.Run(() => recorder.Start(null));

        Assert.Empty(levels);
        context.RunPending();
        Assert.Equal(new[] { 0.25f }, levels);
    }

    [Fact]
    public void NoEventsAfterStop_EvenForAlreadyPostedOnes()
    {
        var context = new RecordingContext();
        var (recorder, streams) = Create();
        var levels = new List<float>();
        var interrupted = 0;
        recorder.Level += levels.Add;
        recorder.Interrupted += () => interrupted++;

        context.Run(() => recorder.Start(null));
        streams[0].OnLevel!(0.5f);
        streams[0].OnStopped!(null);
        recorder.Stop();
        streams[0].OnLevel!(0.7f);
        streams[0].OnStopped!(new InvalidOperationException());
        context.RunPending();

        Assert.Empty(levels);
        Assert.Equal(0, interrupted);
        Assert.Equal(1, streams[0].CloseCount);
    }

    [Fact]
    public void NoEventsAfterCancel()
    {
        var context = new RecordingContext();
        var (recorder, streams) = Create();
        var levels = 0;
        recorder.Level += _ => levels++;

        context.Run(() => recorder.Start(null));
        streams[0].OnLevel!(0.5f);
        recorder.Cancel();
        context.RunPending();

        Assert.Equal(0, levels);
    }

    [Fact]
    public void LateEventFromAnOldRecording_NeverReachesTheNewOne()
    {
        var context = new RecordingContext();
        var (recorder, streams) = Create();
        var levels = new List<float>();
        var interrupted = 0;
        recorder.Level += levels.Add;
        recorder.Interrupted += () => interrupted++;

        context.Run(() => recorder.Start(null));
        context.Run(() => recorder.Start(null));
        streams[0].OnLevel!(0.1f);
        streams[0].OnStopped!(null);
        streams[1].OnLevel!(0.9f);
        context.RunPending();

        Assert.Equal(new[] { 0.9f }, levels);
        Assert.Equal(0, interrupted);
        Assert.Equal(1, streams[0].CloseCount);
    }

    [Fact]
    public void Interrupted_IsDeliveredThroughTheContext()
    {
        var context = new RecordingContext();
        var (recorder, streams) = Create();
        var interrupted = 0;
        recorder.Interrupted += () => interrupted++;

        context.Run(() => recorder.Start(null));
        OnOtherThread(() => streams[0].OnStopped!(new InvalidOperationException("unplugged")));

        Assert.Equal(0, interrupted);
        context.RunPending();
        Assert.Equal(1, interrupted);
    }

    [Fact]
    public void StopFromInsideInterrupted_DoesNotDeadlock_WhenClosingJoinsTheCaptureThread()
    {
        // The fake behaves like NAudio: Close joins the capture thread, which raised the event.
        var stopRequested = new ManualResetEventSlim();
        Thread? captureThread = null;
        var raised = new ManualResetEventSlim();
        var recorder = new WasapiRecorder((id, samples, level, stopped) =>
        {
            captureThread = new Thread(() =>
            {
                stopped(new InvalidOperationException("unplugged"));
                raised.Set();
                stopRequested.Wait();
            });
            return new JoiningStream(captureThread, stopRequested);
        });
        var handled = new ManualResetEventSlim();
        float[]? result = null;
        recorder.Interrupted += () =>
        {
            result = recorder.Stop();
            handled.Set();
        };

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { recorder.Start(null); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        Assert.True(handled.Wait(TimeSpan.FromSeconds(10)), "Stop inside Interrupted hung");
        Assert.NotNull(result);
        Assert.True(raised.IsSet);
    }

    [Fact]
    public void Samples_AccumulateAcrossChunks_AndStopReturnsThem()
    {
        var (recorder, streams) = Create();
        recorder.Start("mic");
        streams[0].OnSamples!(new[] { 0.1f, 0.2f });
        streams[0].OnSamples!(new[] { 0.3f });

        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, recorder.Snapshot());
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, recorder.Stop());
        Assert.Empty(recorder.Stop());
    }

    [Fact]
    public void StartFailure_ClosesTheStreamAndRethrows()
    {
        var fake = new FakeStream { StartFailure = new MicrophoneBlockedException() };
        var recorder = new WasapiRecorder((id, samples, level, stopped) => fake);

        Assert.Throws<MicrophoneBlockedException>(() => recorder.Start(null));
        Assert.Equal(1, fake.CloseCount);
        Assert.Empty(recorder.Stop());
    }

    [Fact]
    public void OpenFailure_Propagates()
    {
        var recorder = new WasapiRecorder((id, samples, level, stopped) => throw new MicrophoneMissingException());
        Assert.Throws<MicrophoneMissingException>(() => recorder.Start("gone"));
    }

    private sealed class JoiningStream : IMicStream
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _release;

        public JoiningStream(Thread thread, ManualResetEventSlim release)
        {
            _thread = thread;
            _release = release;
        }

        public void Start() => _thread.Start();

        public void Close()
        {
            _release.Set();
            if (_thread.ThreadState != ThreadState.Unstarted) _thread.Join();
        }
    }
}

public class MultiMeterEventTests
{
    private static (MultiMeter Meter, Dictionary<string, FakeStream> Streams) Create()
    {
        var streams = new Dictionary<string, FakeStream>();
        var meter = new MultiMeter((id, samples, level, stopped) =>
        {
            var fake = new FakeStream { OnLevel = level };
            streams[id!] = fake;
            return fake;
        });
        return (meter, streams);
    }

    [Fact]
    public void Step_IsPostedToTheCapturedContext_WithTheLevelStep()
    {
        var context = new RecordingContext();
        var (meter, streams) = Create();
        var steps = new List<(string, int)>();
        meter.Step += (id, step) => steps.Add((id, step));

        context.Run(() => meter.Start(new[] { "a", "b" }));
        OnOtherThread(() => streams["a"].OnLevel!(0.1f));   // -20 dBFS
        streams["b"].OnLevel!(0f);

        Assert.Empty(steps);
        context.RunPending();
        Assert.Equal(new[] { ("a", 6), ("b", 1) }, steps);
    }

    [Fact]
    public void NoStepsAfterStop()
    {
        var context = new RecordingContext();
        var (meter, streams) = Create();
        var count = 0;
        meter.Step += (_, _) => count++;

        context.Run(() => meter.Start(new[] { "a" }));
        streams["a"].OnLevel!(0.1f);
        meter.Stop();
        streams["a"].OnLevel!(0.1f);
        context.RunPending();

        Assert.Equal(0, count);
        Assert.Equal(1, streams["a"].CloseCount);
    }

    [Fact]
    public void DuplicateIds_OpenOnce_AndFailingMicsAreSkipped()
    {
        var opened = new List<string>();
        var meter = new MultiMeter((id, samples, level, stopped) =>
        {
            opened.Add(id!);
            if (id == "bad") throw new MicrophoneMissingException();
            return new FakeStream();
        });

        meter.Start(new[] { "a", "A", "bad", "b" });

        Assert.Equal(new[] { "a", "bad", "b" }, opened);
    }

    [Fact]
    public void BlockedPrivacy_OpensNothingElse()
    {
        var opened = 0;
        var meter = new MultiMeter((id, samples, level, stopped) =>
        {
            opened++;
            throw new MicrophoneBlockedException();
        });

        meter.Start(new[] { "a", "b", "c" });

        Assert.Equal(1, opened);
    }
}

public class EventPosterTests
{
    [Fact]
    public void WithAContext_PostsToIt()
    {
        var context = new RecordingContext();
        var poster = new EventPoster(context);
        var ran = false;

        poster.Post(() => ran = true);

        Assert.False(ran);
        context.RunPending();
        Assert.True(ran);
    }

    [Fact]
    public void WithoutAContext_RunsInOrderOffTheCallingThread()
    {
        var poster = new EventPoster(null);
        var callerThread = Environment.CurrentManagedThreadId;
        var order = new List<int>();
        var threads = new List<int>();
        var done = new ManualResetEventSlim();

        for (var i = 0; i < 200; i++)
        {
            var n = i;
            poster.Post(() =>
            {
                lock (order)
                {
                    order.Add(n);
                    threads.Add(Environment.CurrentManagedThreadId);
                    if (order.Count == 200) done.Set();
                }
            });
        }

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(Enumerable.Range(0, 200), order);
        Assert.DoesNotContain(callerThread, threads);
    }
}
