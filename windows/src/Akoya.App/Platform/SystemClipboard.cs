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

    public IDataObject? Snapshot()
    {
        try
        {
            var live = Clipboard.GetDataObject();
            if (live is null)
            {
                return null;
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
                catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException or IOException)
                {
                    // This format could not be read back; keep the others.
                }
            }

            return any ? copy : null;
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            return null;
        }
    }

    public bool TrySetText(string text, bool excludeFromHistory)
    {
        ArgumentNullException.ThrowIfNull(text);

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

            var ok = SetUnicodeText(text);
            if (ok && excludeFromHistory)
            {
                // Windows only checks that the formats exist; the value of the two DWORDs is the documented 0.
                ok = SetFormat(ExcludeFromMonitorFormat, new byte[4])
                     && SetFormat(CanIncludeInHistoryFormat, new byte[4])
                     && SetFormat(CanUploadToCloudFormat, new byte[4]);
            }

            return ok;
        }
        finally
        {
            NativeMethods.CloseClipboard();
            _sequenceAfterSet = NativeMethods.GetClipboardSequenceNumber();
            _hasSet = true;
        }
    }

    public void Restore(IDataObject? data)
    {
        // The user (or another app) copied something since we wrote ours: theirs wins.
        if (!_hasSet || NativeMethods.GetClipboardSequenceNumber() != _sequenceAfterSet)
        {
            return;
        }

        try
        {
            if (data is null)
            {
                Clipboard.Clear();
            }
            else
            {
                Clipboard.SetDataObject(data, true);
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

    private static bool SetUnicodeText(string text)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
        return SetData(NativeMethods.CfUnicodeText, bytes);
    }

    private static bool SetFormat(string name, byte[] bytes)
    {
        var format = NativeMethods.RegisterClipboardFormat(name);
        return format != 0 && SetData(format, bytes);
    }

    private static bool SetData(uint format, byte[] bytes)
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

        if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle); // ownership transfers to the system only on success
            return false;
        }

        return true;
    }
}
