using System.Windows.Threading;
using Akoya.Core.Dictation;

namespace Akoya.App.Platform;

/// <summary>
/// <see cref="IScheduler"/> on a WPF dispatcher: timer actions run on the dispatcher's thread (the UI thread),
/// as <see cref="DictationCoordinator"/> requires. Create it on that thread.
/// </summary>
public sealed class DispatcherScheduler : IScheduler
{
    private readonly Dispatcher _dispatcher;

    public DispatcherScheduler() : this(Dispatcher.CurrentDispatcher)
    {
    }

    public DispatcherScheduler(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public DateTimeOffset Now => DateTimeOffset.Now;

    public IDisposable After(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = delay < TimeSpan.Zero ? TimeSpan.Zero : delay,
        };
        var handle = new Handle(timer);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (handle.Disposed) return;
            handle.Disposed = true;
            action();
        };
        timer.Start();
        return handle;
    }

    private sealed class Handle(DispatcherTimer timer) : IDisposable
    {
        public bool Disposed;

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            // DispatcherTimer.Stop may be called from the dispatcher's thread only.
            if (timer.Dispatcher.CheckAccess()) timer.Stop();
            else timer.Dispatcher.BeginInvoke(timer.Stop);
        }
    }
}
