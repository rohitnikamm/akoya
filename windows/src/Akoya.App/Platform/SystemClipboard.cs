using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Akoya.App.Platform;

/// <summary>The real Windows clipboard, through the raw Win32 API. Must be used from one STA thread (the UI thread).</summary>
public sealed class SystemClipboard : IClipboard, IDisposable
{
    private const uint CfUnicodeText = 13;

    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Func<uint, IntPtr, IntPtr> _setClipboardData;
    private HwndSource? _owner;

    public SystemClipboard()
        : this(null)
    {
    }

    /// <param name="setClipboardData">Replaces <c>SetClipboardData</c> in tests (to make a write fail).</param>
    internal SystemClipboard(Func<uint, IntPtr, IntPtr>? setClipboardData)
    {
        _setClipboardData = setClipboardData ?? NativeMethods.SetClipboardData;
    }

    public bool TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Allocated and filled before the clipboard is opened, so nothing can fail once its contents are gone.
        var handle = AllocateText(text + "\0");
        if (handle == IntPtr.Zero)
        {
            return false;
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
                if (!NativeMethods.EmptyClipboard() || _setClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
                {
                    return false;
                }

                handle = IntPtr.Zero; // the clipboard owns it now
                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
            }
        }
    }

    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
    }

    private static IntPtr AllocateText(string text)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(text);
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

    private IntPtr EnsureOwner()
    {
        // SetClipboardData fails for a clipboard opened with no owner window, so keep a hidden one.
        _owner ??= new HwndSource(new HwndSourceParameters("Akoya clipboard") { ParentWindow = HwndMessage });
        return _owner.Handle;
    }
}
