using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Akoya.App.Platform;

/// <summary>
/// The real Windows clipboard, through the raw Win32 API. Must be used from one STA thread (the UI thread).
/// <para>
/// A snapshot copies every format on the clipboard byte for byte (registered private formats, Explorer's
/// <c>Preferred DropEffect</c>, password managers' history markers…), so the restore puts back exactly what was
/// there. Formats Windows synthesises from others (bitmaps from DIBs) or that only work inside their owner are
/// skipped. If anything else cannot be copied, the snapshot is <see cref="ClipboardSnapshot.IsIncomplete"/> and
/// the clipboard must not be borrowed at all.
/// </para>
/// </summary>
public sealed class SystemClipboard : IClipboard, IDisposable
{
    public const string ExcludeFromMonitorFormat = "ExcludeClipboardContentFromMonitorProcessing";
    public const string CanIncludeInHistoryFormat = "CanIncludeInClipboardHistory";
    public const string CanUploadToCloudFormat = "CanUploadToCloudClipboard";

    /// <summary>Larger clipboards are not borrowed (the text is typed instead).</summary>
    public const long DefaultMaxSnapshotBytes = 64L * 1024 * 1024;

    private const uint CfText = 1;
    private const uint CfBitmap = 2;
    private const uint CfMetaFilePict = 3;
    private const uint CfOemText = 7;
    private const uint CfDib = 8;
    private const uint CfPalette = 9;
    private const uint CfUnicodeText = 13;
    private const uint CfEnhMetaFile = 14;
    private const uint CfLocale = 16;
    private const uint CfDibV5 = 17;
    private const uint CfOwnerDisplay = 0x0080;
    private const uint CfDspText = 0x0081;
    private const uint CfDspBitmap = 0x0082;
    private const uint CfDspMetaFilePict = 0x0083;
    private const uint CfDspEnhMetaFile = 0x008E;
    private const uint CfPrivateFirst = 0x0200;
    private const uint CfGdiObjLast = 0x03FF; // CF_PRIVATEFIRST..CF_PRIVATELAST, then CF_GDIOBJFIRST..CF_GDIOBJLAST

    private const int OpenAttempts = 4;
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(10);

    private static readonly IntPtr HwndMessage = new(-3);

    private readonly long _maxSnapshotBytes;
    private readonly Func<uint, IntPtr, IntPtr> _setClipboardData;
    private HwndSource? _owner;
    private uint _sequenceAfterSet;
    private bool _hasSet;

    public SystemClipboard()
        : this(DefaultMaxSnapshotBytes)
    {
    }

    /// <param name="setClipboardData">Replaces <c>SetClipboardData</c> in tests (to make a write fail).</param>
    internal SystemClipboard(long maxSnapshotBytes, Func<uint, IntPtr, IntPtr>? setClipboardData = null)
    {
        _maxSnapshotBytes = maxSnapshotBytes;
        _setClipboardData = setClipboardData ?? NativeMethods.SetClipboardData;
    }

    public ClipboardSnapshot Snapshot()
    {
        if (!Open())
        {
            return ClipboardSnapshot.Unreadable; // busy, not merely unsupported
        }

        try
        {
            return Capture();
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public bool TrySetText(string text, bool excludeFromHistory, ClipboardSnapshot? restoreOnFailure = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var items = new List<ClipboardItem> { new(CfUnicodeText, System.Text.Encoding.Unicode.GetBytes(text + "\0")) };
        if (excludeFromHistory)
        {
            // Windows only checks that the formats exist; the value of the two DWORDs is the documented 0.
            foreach (var name in new[] { ExcludeFromMonitorFormat, CanIncludeInHistoryFormat, CanUploadToCloudFormat })
            {
                var format = NativeMethods.RegisterClipboardFormat(name);
                if (format == 0)
                {
                    return false;
                }

                items.Add(new ClipboardItem(format, new byte[4]));
            }
        }

        // Everything is allocated and filled before the clipboard is emptied, so an allocation failure cannot
        // happen once the user's contents are gone. Only one attempt to open it: the caller retries without
        // blocking the UI thread.
        var ours = Allocate(items);
        if (ours is null)
        {
            return false;
        }

        List<Allocated>? fallback = null;
        var success = false;
        try
        {
            if (!NativeMethods.OpenClipboard(EnsureOwner()))
            {
                return false;
            }

            try
            {
                if (restoreOnFailure is { IsUnreadable: false, IsIncomplete: false } snapshot)
                {
                    fallback = Allocate(WithMonitorExclusion(snapshot.Items));
                    if (fallback is null)
                    {
                        return false;
                    }
                }

                if (!NativeMethods.EmptyClipboard())
                {
                    return false;
                }

                if (Put(ours, stopAtFailure: true))
                {
                    success = true;
                    return true;
                }

                // Do not leave a half-written clipboard behind: what was there, or empty.
                NativeMethods.EmptyClipboard();
                if (fallback is not null)
                {
                    Put(fallback, stopAtFailure: false);
                }

                return false;
            }
            finally
            {
                NativeMethods.CloseClipboard();
                _sequenceAfterSet = NativeMethods.GetClipboardSequenceNumber();
                _hasSet = success;
            }
        }
        finally
        {
            Free(ours);
            Free(fallback);
        }
    }

    public bool TryRestore(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Never overwrite what we could not read (or only partly read).
        if (snapshot.IsUnreadable || snapshot.IsIncomplete || !_hasSet)
        {
            return true;
        }

        // Marked so clipboard managers and Win+V don't record the restore as a new copy.
        var prepared = snapshot.IsEmpty ? new List<Allocated>() : Allocate(WithMonitorExclusion(snapshot.Items));
        if (prepared is null)
        {
            return true; // out of memory: our temporary text stays (it is excluded from history)
        }

        try
        {
            // One attempt: the caller retries without blocking the UI thread.
            if (!NativeMethods.OpenClipboard(EnsureOwner()))
            {
                return false;
            }

            try
            {
                // What the user (or another app) copied since we wrote ours wins. Checked with the clipboard
                // open, so nothing can change it in between.
                if (NativeMethods.GetClipboardSequenceNumber() != _sequenceAfterSet)
                {
                    return true;
                }

                NativeMethods.EmptyClipboard();
                Put(prepared, stopAtFailure: false);
                _hasSet = false;
                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }
        finally
        {
            Free(prepared);
        }
    }

    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
    }

    /// <summary>Formats Windows re-synthesises from another one, or that are meaningless outside their owner.</summary>
    internal static bool IsSkipped(uint format) =>
        format is CfBitmap or CfMetaFilePict or CfPalette
            or CfOwnerDisplay or CfDspText or CfDspBitmap or CfDspMetaFilePict or CfDspEnhMetaFile
            || format is >= CfPrivateFirst and <= CfGdiObjLast;

    /// <summary>Windows synthesises each member of a family from any other member that is present.</summary>
    private static uint[]? FamilyOf(uint format) => format switch
    {
        CfText or CfOemText or CfUnicodeText or CfLocale => [CfText, CfOemText, CfUnicodeText],
        CfDib or CfDibV5 => [CfDib, CfDibV5],
        _ => null,
    };

    private ClipboardSnapshot Capture()
    {
        var items = new List<ClipboardItem>();
        var failed = new List<uint>();
        long total = 0;
        uint format = 0;
        while (true)
        {
            format = NativeMethods.EnumClipboardFormats(format);
            if (format == 0)
            {
                // Zero with an error is a failed enumeration, not the end of the list.
                if (Marshal.GetLastWin32Error() is var error and not 0)
                {
                    return ClipboardSnapshot.IncompleteBecause($"listing the formats failed, error {error}");
                }

                break;
            }

            if (IsSkipped(format))
            {
                continue;
            }

            // Formats come in the order they were set, the synthesised ones last, so once one member of a family is
            // copied the rest are Windows' conversions of it: it makes them again after the restore. Copying them too
            // would count a 4K screenshot two or three times against the cap.
            if (FamilyOf(format) is { } siblings && siblings.Contains(format) && items.Any(i => siblings.Contains(i.Format)))
            {
                continue;
            }

            byte[]? data;
            try
            {
                data = Read(format, _maxSnapshotBytes - total);
            }
            catch (OverflowException)
            {
                return ClipboardSnapshot.IncompleteBecause($"over the {_maxSnapshotBytes / (1024 * 1024)} MB cap at {Describe(format)}");
            }
            catch (OutOfMemoryException)
            {
                return ClipboardSnapshot.IncompleteBecause($"out of memory copying {Describe(format)}");
            }

            if (data is null)
            {
                failed.Add(format);
                continue;
            }

            total += data.Length;
            items.Add(new ClipboardItem(format, data));
        }

        // A synthesised format that fails to render is fine when the format it comes from was copied.
        foreach (var missing in failed)
        {
            var family = FamilyOf(missing);
            if (family is null || !items.Any(i => family.Contains(i.Format)))
            {
                return ClipboardSnapshot.IncompleteBecause($"{Describe(missing)} could not be read");
            }
        }

        return items.Count == 0 ? ClipboardSnapshot.Empty : ClipboardSnapshot.Of(items);
    }

    /// <summary>A format's number and, for a registered one, its name (for the log).</summary>
    private static string Describe(uint format)
    {
        var name = new System.Text.StringBuilder(128);
        return NativeMethods.GetClipboardFormatName(format, name, name.Capacity) > 0
            ? $"format {format} \"{name}\""
            : $"format {format}";
    }

    /// <summary>The bytes of one format; null when it cannot be read. Throws <see cref="OverflowException"/> past <paramref name="budget"/>.</summary>
    private static byte[]? Read(uint format, long budget)
    {
        var handle = NativeMethods.GetClipboardData(format);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        if (format == CfEnhMetaFile)
        {
            var bits = NativeMethods.GetEnhMetaFileBits(handle, 0, null);
            if (bits == 0)
            {
                return null;
            }

            if (bits > budget)
            {
                throw new OverflowException();
            }

            var emf = new byte[bits];
            return NativeMethods.GetEnhMetaFileBits(handle, bits, emf) == bits ? emf : null;
        }

        var size = (ulong)NativeMethods.GlobalSize(handle);
        if (size == 0)
        {
            return null; // not an HGLOBAL (or a discarded one)
        }

        if (size > (ulong)Math.Max(0, budget))
        {
            throw new OverflowException();
        }

        var source = NativeMethods.GlobalLock(handle);
        if (source == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bytes = new byte[size];
            Marshal.Copy(source, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    private static IReadOnlyList<ClipboardItem> WithMonitorExclusion(IReadOnlyList<ClipboardItem> items)
    {
        var exclude = NativeMethods.RegisterClipboardFormat(ExcludeFromMonitorFormat);
        if (exclude == 0 || items.Any(i => i.Format == exclude))
        {
            return items;
        }

        return [.. items, new ClipboardItem(exclude, new byte[4])];
    }

    /// <summary>A handle ready for SetClipboardData (an HGLOBAL, or an HENHMETAFILE for CF_ENHMETAFILE).</summary>
    private readonly record struct Allocated(uint Format, IntPtr Handle);

    /// <summary>Copies the items into new handles; null (with nothing leaked) if any allocation fails.</summary>
    private static List<Allocated>? Allocate(IReadOnlyList<ClipboardItem> items)
    {
        var allocated = new List<Allocated>(items.Count);
        foreach (var item in items)
        {
            var handle = item.Format == CfEnhMetaFile ? AllocateMetaFile(item.Data) : AllocateGlobal(item.Data);
            if (handle == IntPtr.Zero)
            {
                Free(allocated);
                return null;
            }

            allocated.Add(new Allocated(item.Format, handle));
        }

        return allocated;
    }

    private static IntPtr AllocateMetaFile(byte[] bits) =>
        NativeMethods.SetEnhMetaFileBits((uint)bits.Length, bits);

    private static IntPtr AllocateGlobal(byte[] bytes)
    {
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var target = NativeMethods.GlobalLock(handle);
        if (target == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return IntPtr.Zero;
        }

        Marshal.Copy(bytes, 0, target, bytes.Length);
        NativeMethods.GlobalUnlock(handle);
        return handle;
    }

    /// <summary>
    /// Hands the handles to the open clipboard; each one the system accepts leaves the list (it owns it now).
    /// False if any was refused.
    /// </summary>
    private bool Put(List<Allocated> allocated, bool stopAtFailure)
    {
        var all = true;
        for (var i = 0; i < allocated.Count;)
        {
            var (format, handle) = allocated[i];
            if (_setClipboardData(format, handle) != IntPtr.Zero)
            {
                allocated.RemoveAt(i);
                continue;
            }

            all = false;
            if (stopAtFailure)
            {
                return false;
            }

            i++;
        }

        return all;
    }

    private static void Free(List<Allocated>? allocated)
    {
        if (allocated is null)
        {
            return;
        }

        foreach (var (format, handle) in allocated)
        {
            if (format == CfEnhMetaFile)
            {
                NativeMethods.DeleteEnhMetaFile(handle);
            }
            else
            {
                NativeMethods.GlobalFree(handle);
            }
        }

        allocated.Clear();
    }

    /// <summary>Opens the clipboard with a few short retries (about 30 ms at most on the UI thread).</summary>
    private bool Open()
    {
        var owner = EnsureOwner();
        for (var attempt = 1; attempt <= OpenAttempts; attempt++)
        {
            if (NativeMethods.OpenClipboard(owner))
            {
                return true;
            }

            if (attempt < OpenAttempts)
            {
                Thread.Sleep(OpenRetryDelay);
            }
        }

        return false;
    }

    private IntPtr EnsureOwner()
    {
        // SetClipboardData fails for a clipboard opened with no owner window, so keep a hidden one.
        _owner ??= new HwndSource(new HwndSourceParameters("Akoya clipboard") { ParentWindow = HwndMessage });
        return _owner.Handle;
    }
}
