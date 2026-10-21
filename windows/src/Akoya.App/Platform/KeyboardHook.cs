using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Akoya.Core.Hotkey;

namespace Akoya.App.Platform;

/// <summary>The low-level keyboard hook could not be installed (or re-installed).</summary>
public sealed class HotkeyUnavailableException : Exception
{
    public HotkeyUnavailableException() : base("The keyboard hook could not be installed.") { }

    public HotkeyUnavailableException(string message) : base(message) { }

    public HotkeyUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// A <c>WH_KEYBOARD_LL</c> hook on its own thread with its own message loop, feeding a
/// <see cref="HotkeyDecoder"/>. The decoder is not thread-safe, so this class owns it: after
/// construction nothing else may touch it, and every change goes through <see cref="SetKey"/>,
/// <see cref="SetRecordingActive"/> and <see cref="Reset"/>, which run on the hook thread.
/// The hook callback only decodes and queues; push-to-talk events reach <c>onEvent</c> on the thread
/// that constructed the hook (through its <see cref="SynchronizationContext"/>), in order, each stamped
/// (<see cref="MonotonicClock"/>) in the callback, when the key moved, not when the UI thread got to it.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const uint WmRunQueued = NativeMethods.WmApp + 1;
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReinstallTimeout = TimeSpan.FromSeconds(5);

    private readonly HotkeyDecoder _decoder;
    private readonly Action<HotkeyEvent, DateTimeOffset> _onEvent;
    private readonly SynchronizationContext _context;
    private readonly ConcurrentQueue<Action> _queued = new();
    private readonly NativeMethods.HookProc _proc; // kept in a field so the delegate is never collected
    private readonly object _gate = new();

    private HookThread? _current;   // the live (or just-died) hook thread; changed under _gate
    private HookThread? _abandoned;  // a thread that timed out starting; told to unhook and exit
    private volatile bool _disposed;
    private long _lastKeyTick = Environment.TickCount64; // any key the callback saw, or the last (re-)install
    private long _keysSeen;

    /// <param name="decoder">Handed over: do not use it again after this call.</param>
    /// <param name="onEvent">Called on the creating thread's synchronization context with the event and when it happened.</param>
    public KeyboardHook(HotkeyDecoder decoder, Action<HotkeyEvent, DateTimeOffset> onEvent)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(onEvent);
        _decoder = decoder;
        _onEvent = onEvent;
        _context = SynchronizationContext.Current
                   ?? throw new InvalidOperationException("KeyboardHook must be created on a thread with a SynchronizationContext (the UI thread).");
        _proc = HookCallback;
    }

    /// <summary>
    /// How long ago the callback last saw a key (any key, passed on or not), or since the hook was (re-)installed.
    /// Silence while the user types means Windows removed the hook (it does so, without telling, when a callback
    /// once exceeds LowLevelHooksTimeout).
    /// </summary>
    public TimeSpan SinceLastKey => TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - Interlocked.Read(ref _lastKeyTick)));

    /// <summary>How many keys the callback has seen (any key, passed on or not).</summary>
    public long KeysSeen => Interlocked.Read(ref _keysSeen);

    /// <summary>True while the hook thread is alive.</summary>
    internal bool IsRunning => _current?.Thread.IsAlive == true;

    /// <summary>Test seam: ends the hook thread's message loop as if the thread had died.</summary>
    internal void SimulateThreadExit()
    {
        var id = _current?.Id ?? 0;
        if (id != 0)
        {
            NativeMethods.PostThreadMessage(id, NativeMethods.WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Starts the hook thread and installs the hook. Throws <see cref="HotkeyUnavailableException"/> on failure.</summary>
    public void Install()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is { Thread.IsAlive: true })
            {
                return;
            }

            _current = null;

            // Never run two hook threads: they would share the (single-threaded) decoder.
            if (_abandoned is { Thread.IsAlive: true })
            {
                throw new HotkeyUnavailableException("A previous keyboard hook thread has not exited yet.");
            }

            _abandoned = null;

            Interlocked.Exchange(ref _lastKeyTick, Environment.TickCount64);
            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var state = new HookThread();
            state.Thread = new Thread(() => ThreadMain(state, ready)) { IsBackground = true, Name = "Akoya keyboard hook" };
            _current = state; // before Start, so settings posted meanwhile can wake the thread
            state.Thread.Start();

            if (!ready.Task.Wait(StartTimeout))
            {
                // The thread is still starting. It checks this flag after installing and unhooks and exits;
                // if it is already in its loop, the quit message ends it.
                state.Abandoned = true;
                _abandoned = state;
                _current = null;
                if (state.Id != 0)
                {
                    NativeMethods.PostThreadMessage(state.Id, NativeMethods.WmQuit, UIntPtr.Zero, IntPtr.Zero);
                }

                throw new HotkeyUnavailableException("The keyboard hook thread did not start.");
            }

            var error = ready.Task.Result;
            if (error != 0)
            {
                state.Thread.Join();
                _current = null;
                throw new HotkeyUnavailableException("The keyboard hook could not be installed.", new Win32Exception(error));
            }
        }
    }

    /// <summary>
    /// Installs a fresh hook and drops the old one (Windows silently removes a hook that times out, e.g.
    /// across a lock screen) and forgets held-key state. Starts a new hook thread if there is none or it died.
    /// With <paramref name="onlyIfSilentFor"/> the silence is checked again on the hook thread, between key callbacks:
    /// a key that arrived after the caller looked means the hook is alive, so it is left alone (false) and a held
    /// key is not forgotten.
    /// </summary>
    public bool Reinstall(TimeSpan? onlyIfSilentFor = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var current = _current;
            if (current is null || !current.Thread.IsAlive)
            {
                _current = null;
                Install();
                return true;
            }

            var skipped = false;
            var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                if (onlyIfSilentFor is { } silence && SinceLastKey < silence)
                {
                    skipped = true;
                    done.TrySetResult(0);
                    return;
                }

                var fresh = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _proc, NativeMethods.GetModuleHandle(null), 0);
                if (fresh == IntPtr.Zero)
                {
                    done.TrySetResult(Marshal.GetLastWin32Error());
                    return;
                }

                var old = current.Hook;
                current.Hook = fresh;
                if (old != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(old);
                }

                _decoder.Reset();
                Interlocked.Exchange(ref _lastKeyTick, Environment.TickCount64);
                done.TrySetResult(0);
            });

            var deadline = DateTime.UtcNow + ReinstallTimeout;
            while (!done.Task.Wait(100))
            {
                if (!current.Thread.IsAlive)
                {
                    _current = null; // the next Reinstall or Install starts a fresh thread
                    throw new HotkeyUnavailableException("The keyboard hook thread exited.");
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new HotkeyUnavailableException("The keyboard hook thread did not respond.");
                }
            }

            var error = done.Task.Result;
            if (error != 0)
            {
                throw new HotkeyUnavailableException("The keyboard hook could not be re-installed.", new Win32Exception(error));
            }

            return !skipped;
        }
    }

    /// <summary>Changes the push-to-talk key (also resets held state). Runs on the hook thread.</summary>
    public void SetKey(HotkeyKey key) => Post(() => _decoder.Key = key);

    /// <summary>Turns Esc interception on or off. Runs on the hook thread.</summary>
    public void SetRecordingActive(bool active) => Post(() => _decoder.RecordingActive = active);

    /// <summary>Forgets held-key state. Runs on the hook thread.</summary>
    public void Reset() => Post(_decoder.Reset);

    public void Dispose()
    {
        HookThread? current;
        HookThread? abandoned;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _current;
            abandoned = _abandoned;
            _current = null;
            _abandoned = null;
        }

        foreach (var state in new[] { current, abandoned })
        {
            if (state is null)
            {
                continue;
            }

            state.Abandoned = true;
            if (state.Id != 0)
            {
                NativeMethods.PostThreadMessage(state.Id, NativeMethods.WmQuit, UIntPtr.Zero, IntPtr.Zero);
            }
        }

        current?.Thread.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>Queues work for the hook thread; before the thread exists it runs when the thread starts.</summary>
    private void Post(Action action)
    {
        if (_disposed)
        {
            return;
        }

        _queued.Enqueue(action);
        var id = _current?.Id ?? 0;
        if (id != 0)
        {
            NativeMethods.PostThreadMessage(id, WmRunQueued, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private void RunQueued()
    {
        while (_queued.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // A failed housekeeping action must not take the hook thread down.
            }
        }
    }

    private void ThreadMain(HookThread state, TaskCompletionSource<int> ready)
    {
        try
        {
            // Touching the message queue creates it, so PostThreadMessage to this thread cannot fail.
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PmNoRemove);
            state.Id = NativeMethods.GetCurrentThreadId();

            state.Hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _proc, NativeMethods.GetModuleHandle(null), 0);
            if (state.Hook == IntPtr.Zero)
            {
                ready.TrySetResult(Marshal.GetLastWin32Error());
                return;
            }

            // Install gave up waiting (or the hook was disposed) while the hook was being set up.
            if (state.Abandoned)
            {
                ready.TrySetResult(-1);
                return;
            }

            RunQueued(); // settings applied before Install
            ready.TrySetResult(0);

            while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == WmRunQueued)
                {
                    RunQueued();
                }
            }
        }
        finally
        {
            if (state.Hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(state.Hook);
                state.Hook = IntPtr.Zero;
            }

            ready.TrySetResult(-1); // never leave Install waiting on a thread that is gone
        }
    }

    private sealed class HookThread
    {
        public Thread Thread = null!;
        public volatile uint Id;
        public volatile bool Abandoned;
        public IntPtr Hook; // hook thread only
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            try
            {
                var at = MonotonicClock.Now; // first: tap or hold is measured from here
                Interlocked.Exchange(ref _lastKeyTick, Environment.TickCount64);
                Interlocked.Increment(ref _keysSeen);
                var message = (int)wParam;
                var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
                var isUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
                if (isDown || isUp)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                    var raw = new RawKey(
                        (int)info.VkCode,
                        (int)info.ScanCode,
                        (info.Flags & NativeMethods.LlkhfExtended) != 0,
                        (info.Flags & NativeMethods.LlkhfInjected) != 0,
                        isDown);

                    var result = _decoder.Decode(raw);
                    if (result.InjectMaskKey)
                    {
                        // Queued rather than sent here so the callback stays minimal; it runs as soon
                        // as the callback returns, long before Win is released.
                        Post(KeyInjector.SendMaskKey);
                    }

                    if (result.Event is { } hotkeyEvent)
                    {
                        _context.Post(
                            state =>
                            {
                                if (!_disposed)
                                {
                                    var (e, stamp) = ((HotkeyEvent, DateTimeOffset))state!;
                                    _onEvent(e, stamp);
                                }
                            },
                            (hotkeyEvent, at));
                    }

                    if (result.Swallow)
                    {
                        return (IntPtr)1;
                    }
                }
            }
            catch (Exception)
            {
                // Never let an exception escape into Windows; let the key through.
            }
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}
