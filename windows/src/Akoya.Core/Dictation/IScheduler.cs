namespace Akoya.Core.Dictation;

/// <summary>
/// Time and one-shot timers for the coordinator. Timer actions run on the coordinator's thread
/// (the app's UI thread). Disposing the returned handle before the action runs cancels it.
/// </summary>
public interface IScheduler
{
    DateTimeOffset Now { get; }

    IDisposable After(TimeSpan delay, Action action);
}
