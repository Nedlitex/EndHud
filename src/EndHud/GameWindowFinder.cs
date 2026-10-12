namespace EndHud;

/// <summary>Finds the window to overlay by enumerating top-level windows (titles and class names only).</summary>
internal static class GameWindowFinder
{
    // Endfield is a Unity game; requiring the Unity window class keeps us from latching onto
    // e.g. a browser tab or editor whose title happens to contain "Endfield".
    private const string UnityWindowClass = "UnityWndClass";
    // The English title, and the Chinese one ("Zhongmodi"), escaped to keep the source ASCII.
    private static readonly string[] GameTitles = ["Endfield", "\u7EC8\u672B\u5730"];

    public static IntPtr Find(string? targetTitle)
    {
        IntPtr found = IntPtr.Zero;
        uint ownPid = (uint)Environment.ProcessId;

        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) return true;

            string title = Native.GetTitle(hwnd);
            if (title.Length == 0) return true;

            bool match = targetTitle is not null
                ? title.Contains(targetTitle, StringComparison.OrdinalIgnoreCase)
                : Native.GetClass(hwnd) == UnityWindowClass
                  && GameTitles.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (!match) return true;

            found = hwnd;
            return false;
        }, IntPtr.Zero);

        return found;
    }
}
