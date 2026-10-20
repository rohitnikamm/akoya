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
/// that constructed the hook (through its <see cref="SynchronizationContext"/>), in order.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const uint WmRunQueued = NativeMethods.WmApp + 1;
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReinstallTimeout = TimeSpan.FromSeconds(5);

    private readonly HotkeyDecoder _decoder;
    private readonly Action<HotkeyEvent> _onEvent;
    private readonly SynchronizationContext _context;
    private readonly ConcurrentQueue<Action> _queued = new();
    private readonly NativeMethods.HookProc _proc; // kept in a field so the delegate is never collected
    private readonly object _gate = new();

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook; // hook thread only
    private volatile bool _disposed;

    /// <param name="decoder">Handed over: do not use it again after this call.</param>
    /// <param name="onEvent">Called on the creating thread's synchronization context.</param>
    public KeyboardHook(HotkeyDecoder decoder, Action<HotkeyEvent> onEvent)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(onEvent);
        _decoder = decoder;
        _onEvent = onEvent;
        _context = SynchronizationContext.Current
                   ?? throw new InvalidOperationException("KeyboardHook must be created on a thread with a SynchronizationContext (the UI thread).");
        _proc = HookCallback;
    }

    /// <summary>Starts the hook thread and installs the hook. Throws <see cref="HotkeyUnavailableException"/> on failure.</summary>
    public void Install()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
            {
                return;
            }

            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => ThreadMain(ready)) { IsBackground = true, Name = "Akoya keyboard hook" };
            thread.Start();

            if (!ready.Task.Wait(StartTimeout))
            {
                throw new HotkeyUnavailableException("The keyboard hook thread did not start.");
            }

            var error = ready.Task.Result;
            if (error != 0)
            {
                thread.Join();
                throw new HotkeyUnavailableException("The keyboard hook could not be installed.", new Win32Exception(error));
            }

            _thread = thread;
        }
    }

    /// <summary>
    /// Installs a fresh hook and drops the old one (Windows silently removes a hook that times out, e.g.
    /// across a lock screen) and forgets held-key state. Installs for the first time if there is none.
    /// </summary>
    public void Reinstall()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is null)
            {
                Install();
                return;
            }

            var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                var fresh = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _proc, NativeMethods.GetModuleHandle(null), 0);
                if (fresh == IntPtr.Zero)
                {
                    done.TrySetResult(Marshal.GetLastWin32Error());
                    return;
                }

                var old = _hook;
                _hook = fresh;
                if (old != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(old);
                }

                _decoder.Reset();
                done.TrySetResult(0);
            });

            if (!done.Task.Wait(ReinstallTimeout))
            {
                throw new HotkeyUnavailableException("The keyboard hook thread did not respond.");
            }

            var error = done.Task.Result;
            if (error != 0)
            {
                throw new HotkeyUnavailableException("The keyboard hook could not be re-installed.", new Win32Exception(error));
            }
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
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _thread;
            _thread = null;
        }

        if (thread is null)
        {
            return;
        }

        var id = _threadId;
        if (id != 0)
        {
            NativeMethods.PostThreadMessage(id, NativeMethods.WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }

        thread.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>Queues work for the hook thread; before the thread exists it runs when the thread starts.</summary>
    private void Post(Action action)
    {
        if (_disposed)
        {
            return;
        }

        _queued.Enqueue(action);
        var id = _threadId;
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

    private void ThreadMain(TaskCompletionSource<int> ready)
    {
        // Touching the message queue creates it, so PostThreadMessage to this thread cannot fail.
        NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PmNoRemove);

        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            ready.TrySetResult(Marshal.GetLastWin32Error());
            return;
        }

        _threadId = NativeMethods.GetCurrentThreadId();
        RunQueued(); // settings applied before Install
        ready.TrySetResult(0);

        try
        {
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
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            try
            {
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
                                    _onEvent((HotkeyEvent)state!);
                                }
                            },
                            hotkeyEvent);
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
