using System.Runtime.InteropServices;

namespace EndHud.Vision.Capture;

/// <summary>
/// Copies a window's client area by asking Windows to draw it into a bitmap (PrintWindow with
/// PW_RENDERFULLCONTENT, which takes the picture the compositor holds for the window). Used where
/// Windows.Graphics.Capture can't hide its yellow "being captured" border (Windows 10): this copy
/// draws no border, reads the game window only (never the overlay over it), and works while
/// another window covers the game. Some windows come back black; <see cref="PrintWindowTrial"/>
/// decides when to give it up.
/// </summary>
public static class WindowPrint
{
    /// <summary>The client area of <paramref name="hwnd"/>, in physical pixels, fully opaque.</summary>
    /// <exception cref="InvalidOperationException">Windows didn't draw the window (the reason is in the message).</exception>
    public static Frame Copy(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var rect))
            throw new InvalidOperationException($"The window's size can't be read (error {Marshal.GetLastWin32Error()}).");
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) throw new InvalidOperationException($"The window's client area is empty ({width}x{height}).");

        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new InvalidOperationException("The window can't be copied (GetDC failed).");
        IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            var info = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down rows
                biPlanes = 1,
                biBitCount = 32,
            };
            bitmap = CreateDIBSection(screen, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new InvalidOperationException("The window can't be copied (no bitmap).");
            previous = SelectObject(memory, bitmap);
            if (!PrintWindow(hwnd, memory, PW_CLIENTONLY | PW_RENDERFULLCONTENT))
                throw new InvalidOperationException($"Windows didn't draw the window (PrintWindow failed, error {Marshal.GetLastWin32Error()}).");
            GdiFlush();

            var bgra = new byte[width * height * 4];
            Marshal.Copy(bits, bgra, 0, bgra.Length);
            for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255; // GDI leaves alpha undefined
            return new Frame(width, height, bgra);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private const uint PW_CLIENTONLY = 0x1, PW_RENDERFULLCONTENT = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
}

/// <summary>
/// Whether copying the game window with <see cref="WindowPrint"/> works on this PC, judged from the
/// copies as they come; one per game window. Given up (and <see cref="GaveUp"/> says why) when:
/// the copy fails <see cref="FailuresToGiveUp"/> times in a row; every copy so far is black after
/// <see cref="BlackToGiveUp"/> of them (a window that can't be copied this way comes back black;
/// once a picture has been seen, black is the game's own, a loading screen); or the last
/// <see cref="SlowWindow"/> copies took over <see cref="SlowCopy"/> on average.
/// </summary>
public sealed class PrintWindowTrial
{
    public const int FailuresToGiveUp = 3;
    public const int BlackToGiveUp = 12;
    public const int SlowWindow = 5;
    public static readonly TimeSpan SlowCopy = TimeSpan.FromMilliseconds(150);

    private readonly Queue<TimeSpan> _recent = new();
    private int _failuresInARow, _black;

    /// <summary>A copy has shown a picture (not black): the copies work on this PC.</summary>
    public bool PictureSeen { get; private set; }

    /// <summary>Why the copies were given up, or null while they're used.</summary>
    public string? GaveUp { get; private set; }

    /// <summary>Copies taken, and their mean time over the last <see cref="SlowWindow"/> (for the diagnostic log).</summary>
    public string Describe() =>
        $"{(PictureSeen ? "picture seen" : $"{_black} black")}, mean copy {(_recent.Count == 0 ? "-" : $"{Mean().TotalMilliseconds:F0} ms")}{(GaveUp is null ? "" : $", given up: {GaveUp}")}";

    /// <summary>One copy: the frame, or null with <paramref name="problem"/>, and how long it took. True while copies are still used.</summary>
    public bool Record(Frame? frame, TimeSpan took, string? problem = null)
    {
        if (GaveUp is not null) return false;
        if (frame is null)
        {
            if (++_failuresInARow >= FailuresToGiveUp) GaveUp = $"the copy failed {_failuresInARow} times in a row ({problem ?? "no reason given"})";
            return GaveUp is null;
        }
        _failuresInARow = 0;
        _recent.Enqueue(took);
        while (_recent.Count > SlowWindow) _recent.Dequeue();
        if (!PictureSeen)
        {
            if (LooksBlack(frame)) _black++;
            else PictureSeen = true;
            if (!PictureSeen && _black >= BlackToGiveUp) GaveUp = $"all {_black} copies came back black";
        }
        if (GaveUp is null && _recent.Count == SlowWindow && Mean() > SlowCopy)
            GaveUp = $"too slow: the last {SlowWindow} copies took {Mean().TotalMilliseconds:F0} ms on average";
        return GaveUp is null;
    }

    private TimeSpan Mean() => TimeSpan.FromTicks((long)_recent.Average(t => t.Ticks));

    /// <summary>No pixel on a grid over the frame is brighter than near-black.</summary>
    public static bool LooksBlack(Frame frame)
    {
        int stepX = Math.Max(1, frame.Width / 64), stepY = Math.Max(1, frame.Height / 36);
        for (int y = 0; y < frame.Height; y += stepY)
            for (int x = 0; x < frame.Width; x += stepX)
            {
                var (r, g, b) = frame.GetPixel(x, y);
                if (Math.Max(r, Math.Max(g, b)) > 8) return false;
            }
        return true;
    }
}
