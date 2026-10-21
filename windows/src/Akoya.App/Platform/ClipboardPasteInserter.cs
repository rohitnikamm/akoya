using Akoya.Core.Dictation;

namespace Akoya.App.Platform;

/// <summary>The clipboard operation the paste flow needs, so the flow can be tested without a real clipboard.</summary>
public interface IClipboard
{
    /// <summary>
    /// Replaces the clipboard with <paramref name="text"/> in a single attempt, as an ordinary copy (it shows in Win+V
    /// history like any other). False when another process holds the clipboard or the write failed.
    /// </summary>
    bool TrySetText(string text);
}

/// <summary>
/// Puts dictated text into the foreground app: copies it, like the user pressing Ctrl+C, then sends Ctrl+V. The text
/// stays on the clipboard afterwards, so it can be pasted again anywhere (what was there before is still in Win+V
/// history). Into an elevated window (UIPI) it only copies.
/// </summary>
public sealed class ClipboardPasteInserter : ITextInserter
{
    public const int MaxSetAttempts = 10;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

    private readonly IClipboard _clipboard;
    private readonly Func<bool> _foregroundIsElevated;
    private readonly Action _sendCtrlV;
    private readonly Func<TimeSpan, Task> _delay;

    /// <param name="sendCtrlV">Sends Ctrl+V to the foreground window; throws if the input was blocked.</param>
    /// <param name="delay">The wait between clipboard attempts; replaced in tests.</param>
    public ClipboardPasteInserter(
        IClipboard clipboard,
        Func<bool> foregroundIsElevated,
        Action sendCtrlV,
        Func<TimeSpan, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(foregroundIsElevated);
        ArgumentNullException.ThrowIfNull(sendCtrlV);
        _clipboard = clipboard;
        _foregroundIsElevated = foregroundIsElevated;
        _sendCtrlV = sendCtrlV;
        _delay = delay ?? Task.Delay;
    }

    public async Task<InsertResult> InsertAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var elevated = _foregroundIsElevated();

        // Another app may hold the clipboard for a moment (a clipboard manager reading it): try again briefly.
        var set = false;
        for (var attempt = 1; attempt <= MaxSetAttempts; attempt++)
        {
            if (_clipboard.TrySetText(text))
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

        // An elevated target cannot be pasted into: the user pastes by hand.
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
            // Input was blocked: the text is on the clipboard for a manual paste.
            return InsertResult.CopiedOnly;
        }

        return InsertResult.Pasted;
    }
}
