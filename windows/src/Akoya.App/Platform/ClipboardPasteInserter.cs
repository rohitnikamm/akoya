using System.Windows;
using Akoya.Core.Dictation;

namespace Akoya.App.Platform;

/// <summary>The clipboard operations the paste flow needs, so the flow can be tested without a real clipboard.</summary>
public interface IClipboard
{
    /// <summary>A detached copy of everything readable on the clipboard, or null when it is empty or unreadable.</summary>
    IDataObject? Snapshot();

    /// <summary>
    /// Replaces the clipboard with <paramref name="text"/> in a single attempt. False when another
    /// process holds the clipboard; the clipboard is then untouched.
    /// </summary>
    bool TrySetText(string text, bool excludeFromHistory);

    /// <summary>
    /// Puts a snapshot back (null clears the clipboard), unless something else has changed the clipboard
    /// since <see cref="TrySetText"/>: what the user copied meanwhile wins.
    /// </summary>
    void Restore(IDataObject? data);
}

/// <summary>
/// Types text into the foreground app by putting it on the clipboard, sending Ctrl+V, and restoring
/// the previous clipboard 300 ms later. Into an elevated window (UIPI) it only copies.
/// </summary>
public sealed class ClipboardPasteInserter : ITextInserter
{
    public const int MaxSetAttempts = 10;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);
    public static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(300);

    private readonly IClipboard _clipboard;
    private readonly Func<bool> _foregroundIsElevated;
    private readonly Action _sendCtrlV;
    private readonly IScheduler _scheduler;
    private readonly Func<TimeSpan, Task> _delay;

    /// <param name="sendCtrlV">Sends Ctrl+V to the foreground window; throws if the input was blocked.</param>
    /// <param name="delay">The wait between clipboard attempts; replaced in tests.</param>
    public ClipboardPasteInserter(
        IClipboard clipboard,
        Func<bool> foregroundIsElevated,
        Action sendCtrlV,
        IScheduler scheduler,
        Func<TimeSpan, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(foregroundIsElevated);
        ArgumentNullException.ThrowIfNull(sendCtrlV);
        ArgumentNullException.ThrowIfNull(scheduler);
        _clipboard = clipboard;
        _foregroundIsElevated = foregroundIsElevated;
        _sendCtrlV = sendCtrlV;
        _scheduler = scheduler;
        _delay = delay ?? Task.Delay;
    }

    public async Task<InsertResult> InsertAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var elevated = _foregroundIsElevated();

        // An elevated target cannot be pasted into, so there is nothing to restore: the user pastes by hand.
        var snapshot = elevated ? null : _clipboard.Snapshot();

        var set = false;
        for (var attempt = 1; attempt <= MaxSetAttempts; attempt++)
        {
            if (_clipboard.TrySetText(text, excludeFromHistory: true))
            {
                set = true;
                break;
            }

            if (attempt < MaxSetAttempts)
            {
                await _delay(RetryDelay);
            }
        }

        if (!set)
        {
            return InsertResult.Failed;
        }

        if (elevated)
        {
            return InsertResult.CopiedOnly;
        }

        try
        {
            _sendCtrlV();
        }
        catch (Exception)
        {
            // Input was blocked: the text is on the clipboard for a manual paste, so keep it there.
            return InsertResult.CopiedOnly;
        }

        // Give the target time to read the clipboard before the old contents come back.
        var elapsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (_scheduler.After(RestoreDelay, () => elapsed.TrySetResult()))
        {
            await elapsed.Task;
        }

        _clipboard.Restore(snapshot);
        return InsertResult.Pasted;
    }
}
