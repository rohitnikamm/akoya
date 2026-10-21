using Akoya.Core.Dictation;

namespace Akoya.App.Platform;

/// <summary>One clipboard format, copied byte for byte (an enhanced metafile as its bits).</summary>
public sealed record ClipboardItem(uint Format, byte[] Data);

/// <summary>
/// What was on the clipboard before Akoya borrowed it. <see cref="IsUnreadable"/> means the clipboard could not be
/// opened (another process holds it), which is different from an empty clipboard: restoring an empty snapshot
/// clears the clipboard, while an unreadable one must never be overwritten. <see cref="IsIncomplete"/> means some
/// format could not be copied (or the contents are too large): the clipboard must not be borrowed at all.
/// </summary>
public sealed class ClipboardSnapshot
{
    private ClipboardSnapshot(IReadOnlyList<ClipboardItem> items, bool isUnreadable, bool isIncomplete, string? reason = null)
    {
        Items = items;
        IsUnreadable = isUnreadable;
        IsIncomplete = isIncomplete;
        IncompleteReason = reason;
    }

    /// <summary>For the log: which format made the snapshot incomplete (never clipboard contents).</summary>
    public string? IncompleteReason { get; }

    /// <summary>The detached copies of every format; empty when empty, unreadable or incomplete.</summary>
    public IReadOnlyList<ClipboardItem> Items { get; }

    public bool IsUnreadable { get; }

    public bool IsIncomplete { get; }

    public bool IsEmpty => Items.Count == 0 && !IsUnreadable && !IsIncomplete;

    public static ClipboardSnapshot Empty { get; } = new([], false, false);

    public static ClipboardSnapshot Unreadable { get; } = new([], true, false);

    public static ClipboardSnapshot Incomplete { get; } = new([], false, true);

    public static ClipboardSnapshot IncompleteBecause(string reason) => new([], false, true, reason);

    public static ClipboardSnapshot Of(IReadOnlyList<ClipboardItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new ClipboardSnapshot(items.ToArray(), false, false);
    }
}

/// <summary>The clipboard operations the paste flow needs, so the flow can be tested without a real clipboard.</summary>
public interface IClipboard
{
    /// <summary>A detached copy of everything on the clipboard; distinguishes empty, unreadable and incomplete.</summary>
    ClipboardSnapshot Snapshot();

    /// <summary>
    /// Replaces the clipboard with <paramref name="text"/> in a single attempt. False when another process holds
    /// the clipboard (the clipboard is then untouched) or the write failed after the clipboard was emptied: then
    /// <paramref name="restoreOnFailure"/> is put back at once (or the clipboard is left empty without one).
    /// </summary>
    bool TrySetText(string text, bool excludeFromHistory, ClipboardSnapshot? restoreOnFailure = null);

    /// <summary>
    /// Puts a snapshot back (an empty snapshot clears the clipboard) in a single attempt, unless something else has
    /// changed the clipboard since <see cref="TrySetText"/>: what the user copied meanwhile wins. False only when
    /// another process holds the clipboard, so the caller can try again.
    /// </summary>
    bool TryRestore(ClipboardSnapshot snapshot);
}

/// <summary>
/// Types text into the foreground app by putting it on the clipboard, sending Ctrl+V, and restoring
/// the previous clipboard 300 ms later. Into an elevated window (UIPI) it only copies. When the clipboard
/// holds something that cannot be copied back exactly, it is not borrowed: the text is typed instead.
/// </summary>
public sealed class ClipboardPasteInserter : ITextInserter
{
    public const int MaxSetAttempts = 10;

    /// <summary>About a second of tries, <see cref="RetryDelay"/> apart.</summary>
    public const int MaxRestoreAttempts = 51;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);
    public static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(300);

    private readonly IClipboard _clipboard;
    private readonly Func<bool> _foregroundIsElevated;
    private readonly Action _sendCtrlV;
    private readonly Action<string> _typeText;
    private readonly IScheduler _scheduler;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly Action<string> _log;

    /// <param name="sendCtrlV">Sends Ctrl+V to the foreground window; throws if the input was blocked.</param>
    /// <param name="typeText">Types text into the foreground window as Unicode key input; throws if it was blocked.</param>
    /// <param name="delay">The wait between clipboard attempts; replaced in tests.</param>
    /// <param name="log">Says why the text was typed instead of pasted (never the text).</param>
    public ClipboardPasteInserter(
        IClipboard clipboard,
        Func<bool> foregroundIsElevated,
        Action sendCtrlV,
        Action<string> typeText,
        IScheduler scheduler,
        Func<TimeSpan, Task>? delay = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(foregroundIsElevated);
        ArgumentNullException.ThrowIfNull(sendCtrlV);
        ArgumentNullException.ThrowIfNull(typeText);
        ArgumentNullException.ThrowIfNull(scheduler);
        _clipboard = clipboard;
        _foregroundIsElevated = foregroundIsElevated;
        _sendCtrlV = sendCtrlV;
        _typeText = typeText;
        _scheduler = scheduler;
        _delay = delay ?? Task.Delay;
        _log = log ?? (_ => { });
    }

    public async Task<InsertResult> InsertAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var elevated = _foregroundIsElevated();

        // An elevated target cannot be pasted into, so there is nothing to restore: the user pastes by hand.
        var snapshot = elevated ? ClipboardSnapshot.Empty : _clipboard.Snapshot();

        // If we cannot read what is there we could not put it back: leave the clipboard alone.
        if (snapshot.IsUnreadable)
        {
            return InsertResult.Failed;
        }

        // Something on the clipboard could not be copied (a format we can't read, or too much): borrowing it
        // would lose it, so type the text instead and never touch the clipboard.
        if (snapshot.IsIncomplete)
        {
            _log($"Typing instead of pasting: the clipboard can't be copied ({snapshot.IncompleteReason ?? "unknown"})");
            try
            {
                _typeText(text);
                return InsertResult.Pasted;
            }
            catch (Exception)
            {
                return InsertResult.Failed; // input was blocked; the text stays as Last dictation
            }
        }

        var set = false;
        for (var attempt = 1; attempt <= MaxSetAttempts; attempt++)
        {
            if (_clipboard.TrySetText(text, excludeFromHistory: true, restoreOnFailure: snapshot))
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

        // Another app may hold the clipboard (a clipboard manager reading our text): keep trying for about a second.
        for (var attempt = 1; !_clipboard.TryRestore(snapshot); attempt++)
        {
            if (attempt >= MaxRestoreAttempts)
            {
                _log("Clipboard busy: the previous contents could not be put back");
                break;
            }

            await _delay(RetryDelay);
        }

        return InsertResult.Pasted;
    }
}
