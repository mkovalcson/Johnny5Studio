using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ServoAnimator;

/// <summary>GPU-rendered pixels of a window's client area, read with PrintWindow(PW_RENDERFULLCONTENT) so WPF's
/// hardware-drawn 3D is captured (RenderTargetBitmap renders 3D in software, far too slowly for a heavy model).</summary>
internal static class WindowCapture
{
    private const uint PW_CLIENTONLY = 1, PW_RENDERFULLCONTENT = 2;

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
    }

    /// <summary>Client-area size of the window in device pixels.</summary>
    internal static (int Width, int Height) ClientSize(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        GetClientRect(hwnd, out var r);
        return (r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>The window's client area as top-down BGRA (alpha unused), or null when nothing could be captured.</summary>
    internal static byte[] CaptureClient(Window window, out int width, out int height)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        (width, height) = ClientSize(window);
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen);
        var info = new BITMAPINFO { Size = Marshal.SizeOf<BITMAPINFO>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        IntPtr dib = CreateDIBSection(memory, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr old = SelectObject(memory, dib);
        try
        {
            if (!PrintWindow(hwnd, memory, PW_CLIENTONLY | PW_RENDERFULLCONTENT)) return null;
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally { SelectObject(memory, old); DeleteObject(dib); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }

    /// <summary>Premultiplied BGRA from the same picture captured over black and over white: where the background shows
    /// through, the white capture is brighter by (1 − alpha) × 255. Exact for WPF's (sRGB) blending.</summary>
    internal static byte[] Matte(byte[] overBlack, byte[] overWhite)
    {
        var result = new byte[overBlack.Length];
        for (int i = 0; i < result.Length; i += 4)
        {
            int show = Math.Max(overWhite[i] - overBlack[i], Math.Max(overWhite[i + 1] - overBlack[i + 1], overWhite[i + 2] - overBlack[i + 2]));
            int alpha = Math.Clamp(255 - show, 0, 255);
            result[i] = (byte)Math.Min(overBlack[i], alpha); result[i + 1] = (byte)Math.Min(overBlack[i + 1], alpha); result[i + 2] = (byte)Math.Min(overBlack[i + 2], alpha);
            result[i + 3] = (byte)alpha;
        }
        return result;
    }
}
