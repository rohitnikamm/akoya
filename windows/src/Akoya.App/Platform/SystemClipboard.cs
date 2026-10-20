using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Akoya.App.Platform;

/// <summary>
/// The real Windows clipboard. Must be used from one STA thread (the UI thread). Temporary text is
/// written with the Win32 API so a busy clipboard fails fast (one attempt per call) and can be marked
/// for exclusion from clipboard history and cloud sync; snapshot and restore use the WPF clipboard.
/// </summary>
public sealed class SystemClipboard : IClipboard, IDisposable
{
    public const string ExcludeFromMonitorFormat = "ExcludeClipboardContentFromMonitorProcessing";
    public const string CanIncludeInHistoryFormat = "CanIncludeInClipboardHistory";
    public const string CanUploadToCloudFormat = "CanUploadToCloudClipboard";

    private const int ClipbrdECantOpen = unchecked((int)0x800401D0);

    private static readonly IntPtr HwndMessage = new(-3);

    /// <summary>Formats copied out of the live clipboard, most specific first.</summary>
    private static readonly string[] SnapshotFormats =
    {
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.Rtf,
        DataFormats.Html,
        DataFormats.FileDrop,
        DataFormats.Bitmap,
    };

    private HwndSource? _owner;
    private uint _sequenceAfterSet;
    private bool _hasSet;

    public ClipboardSnapshot Snapshot()
    {
        try
        {
            var live = Clipboard.GetDataObject();
            if (live is null || live.GetFormats(false).Length == 0)
            {
                return ClipboardSnapshot.Empty;
            }

            // The object WPF returns is a live view of the clipboard; copy the data out of it.
            var copy = new DataObject();
            var any = false;
            foreach (var format in SnapshotFormats)
            {
                try
                {
                    if (!live.GetDataPresent(format, false))
                    {
                        continue;
                    }

                    var detached = Detach(live.GetData(format, false));
                    if (detached is null)
                    {
                        continue;
                    }

                    copy.SetData(format, detached, false);
                    any = true;
                }
                catch (COMException ex) when (ex.HResult == ClipbrdECantOpen)
                {
                    return ClipboardSnapshot.Unreadable; // busy, not merely unsupported
                }
                catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException or IOException)
                {
                    // This format could not be read back; keep the others.
                }
            }

            // Only formats Akoya cannot copy (private app formats): nothing to put back.
            return any ? ClipboardSnapshot.Of(copy) : ClipboardSnapshot.Empty;
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            return ClipboardSnapshot.Unreadable;
        }
    }

    public bool TrySetText(string text, bool excludeFromHistory)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Everything is allocated and filled before the clipboard is touched, so an allocation
        // failure cannot happen once the user's contents are gone.
        var items = new List<(uint Format, IntPtr Handle)>();
        try
        {
            if (!AddItem(items, NativeMethods.CfUnicodeText, System.Text.Encoding.Unicode.GetBytes(text + "\0")))
            {
                return false;
            }

            if (excludeFromHistory)
            {
                // Windows only checks that the formats exist; the value of the two DWORDs is the documented 0.
                foreach (var name in new[] { ExcludeFromMonitorFormat, CanIncludeInHistoryFormat, CanUploadToCloudFormat })
                {
                    var format = NativeMethods.RegisterClipboardFormat(name);
                    if (format == 0 || !AddItem(items, format, new byte[4]))
                    {
                        return false;
                    }
                }
            }

            var owner = EnsureOwner();
            if (!NativeMethods.OpenClipboard(owner))
            {
                return false;
            }

            try
            {
                if (!NativeMethods.EmptyClipboard())
                {
                    return false;
                }

                while (items.Count > 0)
                {
                    var (format, handle) = items[0];
                    if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
                    {
                        // Do not leave a half-written clipboard behind: empty, not partial.
                        NativeMethods.EmptyClipboard();
                        return false;
                    }

                    items.RemoveAt(0); // the system owns the handle now
                }

                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
                _sequenceAfterSet = NativeMethods.GetClipboardSequenceNumber();
                _hasSet = items.Count == 0;
            }
        }
        finally
        {
            foreach (var (_, handle) in items)
            {
                NativeMethods.GlobalFree(handle);
            }
        }
    }

    public void Restore(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Never overwrite what we could not read, and never restore over something the user (or another
        // app) copied since we wrote ours: theirs wins.
        if (snapshot.IsUnreadable || !_hasSet || NativeMethods.GetClipboardSequenceNumber() != _sequenceAfterSet)
        {
            return;
        }

        try
        {
            if (snapshot.Data is null)
            {
                Clipboard.Clear();
            }
            else
            {
                Clipboard.SetDataObject(snapshot.Data, true);
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            // Best effort: the clipboard stays busy or unavailable; nothing more to do.
        }
    }

    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
    }

    private IntPtr EnsureOwner()
    {
        // SetClipboardData fails for a clipboard opened with no owner window, so keep a hidden one.
        _owner ??= new HwndSource(new HwndSourceParameters("Akoya clipboard") { ParentWindow = HwndMessage });
        return _owner.Handle;
    }

    private static object? Detach(object? data) => data switch
    {
        string[] files => files.Clone(),
        BitmapSource bitmap => CopyBitmap(bitmap),
        _ => data,
    };

    private static BitmapSource CopyBitmap(BitmapSource source)
    {
        var copy = new WriteableBitmap(source);
        copy.Freeze();
        return copy;
    }

    private static bool AddItem(List<(uint Format, IntPtr Handle)> items, uint format, byte[] bytes)
    {
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var target = NativeMethods.GlobalLock(handle);
        if (target == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return false;
        }

        Marshal.Copy(bytes, 0, target, bytes.Length);
        NativeMethods.GlobalUnlock(handle);
        items.Add((format, handle));
        return true;
    }
}
