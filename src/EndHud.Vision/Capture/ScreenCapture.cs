using System.Runtime.InteropServices;

namespace EndHud.Vision.Capture;

/// <summary>
/// Copies a rectangle of the screen (GDI BitBlt from the desktop): what's on the screen there,
/// whichever window draws it. The fallback for a game window that Windows.Graphics.Capture
/// sends no pictures for (seen on Windows 10 22H2 with the game windowed and full screen). It
/// sees whatever covers the game, so the caller keeps its own overlay out of screen captures
/// first (WDA_EXCLUDEFROMCAPTURE) and only reads the game while it's in front.
/// </summary>
public static class ScreenCapture
{
    /// <summary>The screen pixels in <paramref name="area"/> (physical screen coordinates), fully opaque.</summary>
    public static Frame Copy(PixelRect area)
    {
        if (area.IsEmpty) throw new ArgumentException("Nothing to copy: the area is empty.", nameof(area));
        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new InvalidOperationException("The screen can't be read (GetDC failed).");
        IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            var info = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = area.Width,
                biHeight = -area.Height, // top-down rows
                biPlanes = 1,
                biBitCount = 32,
            };
            bitmap = CreateDIBSection(screen, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new InvalidOperationException("The screen can't be read (no bitmap).");
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SRCCOPY))
                throw new InvalidOperationException($"The screen can't be read (BitBlt failed, error {Marshal.GetLastWin32Error()}).");
            GdiFlush();

            var bgra = new byte[area.Width * area.Height * 4];
            Marshal.Copy(bits, bgra, 0, bgra.Length);
            for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255; // GDI leaves alpha undefined
            return new Frame(area.Width, area.Height, bgra);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>The whole desktop (every screen) in physical pixels, for checking an area lies on it.</summary>
    public static PixelRect VirtualScreen() => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    private const int SRCCOPY = 0x00CC0020;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int rop);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
