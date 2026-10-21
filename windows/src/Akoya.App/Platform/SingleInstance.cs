using System.Security.Principal;

namespace Akoya.App.Platform;

/// <summary>
/// One Akoya per user session: the named mutex <c>Local\Akoya-&lt;SID&gt;</c>. A second launch calls
/// <see cref="SignalFirst()"/>, which sets the named event <c>Local\Akoya-&lt;SID&gt;-show</c>, and exits; the first
/// raises <see cref="ShowRequested"/> (on a thread-pool thread) and opens the tray panel.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly RegisteredWaitHandle _wait;
    private int _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
        _wait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) =>
        {
            if (Volatile.Read(ref _disposed) == 0) ShowRequested?.Invoke();
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Another launch asked this one to show itself. Raised on a thread-pool thread.</summary>
    public event Action? ShowRequested;

    /// <summary>The mutex name for this user, <c>Local\Akoya-&lt;SID&gt;</c>.</summary>
    public static string DefaultName { get; } = @"Local\Akoya-" + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

    /// <summary>True (with the handle to keep for the app's lifetime) when no other Akoya runs in this session.</summary>
    public static bool TryAcquire(out SingleInstance? handle) => TryAcquire(DefaultName, out handle);

    /// <summary>Tells the running Akoya to open its tray panel. False when there is none.</summary>
    public static bool SignalFirst() => SignalFirst(DefaultName);

    /// <summary>For tests: a name of their own, so a running Akoya doesn't interfere.</summary>
    internal static bool TryAcquire(string name, out SingleInstance? handle)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        // The event exists before the mutex, so a second launch that sees the mutex can always signal.
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-show");
        var mutex = new Mutex(false, name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            show.Dispose();
            handle = null;
            return false;
        }

        handle = new SingleInstance(mutex, show);
        return true;
    }

    internal static bool SignalFirst(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name + "-show", out var show)) return false;
        using (show) return show.Set();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _wait.Unregister(null);
        _show.Dispose();
        _mutex.Dispose();
    }
}
