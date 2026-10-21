using System.Collections.Concurrent;

namespace Akoya.App.Platform;

/// <summary>
/// Moves events off the audio threads. With a <see cref="SynchronizationContext"/> (the UI thread's)
/// every action is posted to it; without one, actions run in order on the thread pool, one at a time.
/// Either way the action never runs inside the posting call, so a handler that closes the capture
/// (which joins the capture thread) cannot deadlock against the thread that raised the event.
/// </summary>
internal sealed class EventPoster
{
    private readonly SynchronizationContext? _context;
    private readonly ConcurrentQueue<Action> _queue = new();
    private int _draining;

    public EventPoster(SynchronizationContext? context) => _context = context;

    public void Post(Action action)
    {
        if (_context is not null)
        {
            _context.Post(static state => ((Action)state!)(), action);
            return;
        }

        _queue.Enqueue(action);
        ScheduleDrain();
    }

    private void ScheduleDrain()
    {
        if (Interlocked.CompareExchange(ref _draining, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(static poster => poster.Drain(), this, preferLocal: false);
    }

    private void Drain()
    {
        try
        {
            while (_queue.TryDequeue(out var action)) action();
        }
        finally
        {
            Volatile.Write(ref _draining, 0);
            // An action may have been enqueued between the last TryDequeue and the reset.
            if (!_queue.IsEmpty) ScheduleDrain();
        }
    }
}
