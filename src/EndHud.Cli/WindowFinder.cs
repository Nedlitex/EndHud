using System.Runtime.InteropServices;
using System.Text;

/// <summary>Finds a top-level window by part of its title (window handles only; no process access).</summary>
internal static class WindowFinder
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    public static IntPtr Find(string titlePart)
    {
        SetProcessDPIAware(); // physical pixels, like the HUD
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (!title.ToString().Contains(titlePart, StringComparison.OrdinalIgnoreCase)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
