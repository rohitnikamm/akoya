using System.Runtime.InteropServices;
using System.Windows.Interop;
using Akoya.App.Platform;

namespace Akoya.App.Tests;

/// <summary>Byte-level access to the real clipboard for tests. Call on an STA thread.</summary>
internal static class RawClipboard
{
    public const uint CfText = 1;
    public const uint CfDib = 8;
    public const uint CfUnicodeText = 13;
    public const uint CfEnhMetaFile = 14;
    public const uint CfHdrop = 15;

    public static uint Format(string name)
    {
        var format = NativeMethods.RegisterClipboardFormat(name);
        if (format == 0) throw new InvalidOperationException($"could not register {name}");
        return format;
    }

    /// <summary>Replaces the clipboard with these HGLOBAL formats (and an optional enhanced metafile).</summary>
    public static void Set(IEnumerable<(uint Format, byte[] Data)> items, IntPtr enhMetaFile = default)
    {
        using var owner = new HwndSource(new HwndSourceParameters("Akoya test clipboard") { ParentWindow = new IntPtr(-3) });
        Open(owner.Handle);
        try
        {
            if (!NativeMethods.EmptyClipboard()) throw new InvalidOperationException("EmptyClipboard failed");
            foreach (var (format, data) in items)
            {
                var handle = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable, (UIntPtr)data.Length);
                var target = NativeMethods.GlobalLock(handle);
                Marshal.Copy(data, 0, target, data.Length);
                NativeMethods.GlobalUnlock(handle);
                if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(handle);
                    throw new InvalidOperationException($"SetClipboardData({format}) failed");
                }
            }

            if (enhMetaFile != IntPtr.Zero && NativeMethods.SetClipboardData(CfEnhMetaFile, enhMetaFile) == IntPtr.Zero)
            {
                throw new InvalidOperationException("SetClipboardData(CF_ENHMETAFILE) failed");
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>The bytes of an HGLOBAL format, or null when it is not on the clipboard.</summary>
    public static byte[]? Get(uint format)
    {
        Open(IntPtr.Zero);
        try
        {
            var handle = NativeMethods.GetClipboardData(format);
            if (handle == IntPtr.Zero) return null;
            var size = (int)NativeMethods.GlobalSize(handle);
            var source = NativeMethods.GlobalLock(handle);
            try
            {
                var bytes = new byte[size];
                Marshal.Copy(source, bytes, 0, size);
                return bytes;
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>The bits of the enhanced metafile on the clipboard, or null.</summary>
    public static byte[]? GetEnhMetaFileBits()
    {
        Open(IntPtr.Zero);
        try
        {
            var handle = NativeMethods.GetClipboardData(CfEnhMetaFile);
            if (handle == IntPtr.Zero) return null;
            var size = NativeMethods.GetEnhMetaFileBits(handle, 0, null);
            var bytes = new byte[size];
            NativeMethods.GetEnhMetaFileBits(handle, size, bytes);
            return bytes;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public static bool Has(uint format) => NativeMethods.IsClipboardFormatAvailable(format);

    /// <summary>Whether <paramref name="actual"/> starts with <paramref name="expected"/> (GlobalSize may round up).</summary>
    public static bool StartsWith(byte[]? actual, byte[] expected) =>
        actual is not null && actual.Length >= expected.Length && actual.AsSpan(0, expected.Length).SequenceEqual(expected);

    public static byte[] Utf16(string text) => System.Text.Encoding.Unicode.GetBytes(text + "\0");

    public static byte[] Ascii(string text) => System.Text.Encoding.ASCII.GetBytes(text + "\0");

    private static void Open(IntPtr owner)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (NativeMethods.OpenClipboard(owner)) return;
            Thread.Sleep(20);
        }

        throw new InvalidOperationException("the clipboard stayed busy");
    }
}

/// <summary>Sample clipboard payloads.</summary>
internal static class ClipboardSamples
{
    /// <summary>A valid 2×2 32-bit bottom-up DIB.</summary>
    public static byte[] Dib()
    {
        var bytes = new byte[40 + 16];
        BitConverter.GetBytes(40).CopyTo(bytes, 0);      // biSize
        BitConverter.GetBytes(2).CopyTo(bytes, 4);       // biWidth
        BitConverter.GetBytes(2).CopyTo(bytes, 8);       // biHeight
        BitConverter.GetBytes((short)1).CopyTo(bytes, 12); // biPlanes
        BitConverter.GetBytes((short)32).CopyTo(bytes, 14); // biBitCount
        BitConverter.GetBytes(16).CopyTo(bytes, 20);     // biSizeImage
        for (var i = 40; i < bytes.Length; i++) bytes[i] = (byte)(i * 7);
        return bytes;
    }

    /// <summary>A CF_DIB no decoder accepts: 7 bits per pixel, 3 planes, an absurd width, truncated pixels.</summary>
    public static byte[] OddDib()
    {
        var bytes = new byte[44];
        BitConverter.GetBytes(40).CopyTo(bytes, 0);
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 4);
        BitConverter.GetBytes(-3).CopyTo(bytes, 8);
        BitConverter.GetBytes((short)3).CopyTo(bytes, 12);
        BitConverter.GetBytes((short)7).CopyTo(bytes, 14);
        BitConverter.GetBytes(99).CopyTo(bytes, 16);     // unknown compression
        bytes[40] = 0xAB;
        return bytes;
    }

    /// <summary>CF_HDROP (DROPFILES, wide) for the given paths.</summary>
    public static byte[] FileDrop(params string[] paths)
    {
        var names = System.Text.Encoding.Unicode.GetBytes(string.Join("\0", paths) + "\0\0");
        var bytes = new byte[20 + names.Length];
        BitConverter.GetBytes(20).CopyTo(bytes, 0); // pFiles
        BitConverter.GetBytes(1).CopyTo(bytes, 16); // fWide
        names.CopyTo(bytes, 20);
        return bytes;
    }

    public static string Html(string fragment) =>
        "Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\nStartFragment:0000000000\r\nEndFragment:0000000000\r\n"
        + "<html><body><!--StartFragment-->" + fragment + "<!--EndFragment--></body></html>";

    /// <summary>A small enhanced metafile (one rectangle). The caller hands it to the clipboard.</summary>
    public static IntPtr EnhMetaFile()
    {
        var dc = CreateEnhMetaFile(IntPtr.Zero, null, IntPtr.Zero, "Akoya test");
        Rectangle(dc, 1, 2, 30, 40);
        return CloseEnhMetaFile(dc);
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateEnhMetaFileW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEnhMetaFile(IntPtr hdcRef, string? fileName, IntPtr rect, string? description);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CloseEnhMetaFile(IntPtr hdc);
}
