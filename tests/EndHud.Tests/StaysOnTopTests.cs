using System.Runtime.InteropServices;
using System.Windows.Forms;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// EndHud's windows (the open menu, the rotation and alert editors) stay on top only while the game or EndHud is in
/// front (a player, 2026-10-27: "use the editor and look at a website at the same time"). With anything else in
/// front they go just behind it, not above it. Shows two small windows for a moment.
/// </summary>
[Trait("Category", "Desktop")] // needs an interactive desktop: runs locally, not on CI runners
[Collection(WpfCollection.Name)]
public class StaysOnTopTests
{
    [Fact]
    public void On_top_while_the_game_or_EndHud_is_in_front_only() => OnSta(() =>
    {
        using var ours = Shown("EndHud window");
        IntPtr game = new(0x1234), other = GetDesktopWindow(); // the desktop belongs to another process

        Assert.True(Native.StaysOnTop(game, game));
        Assert.True(Native.StaysOnTop(ours.Handle, game));
        Assert.False(Native.StaysOnTop(other, game));
        Assert.False(Native.StaysOnTop(IntPtr.Zero, IntPtr.Zero)); // nothing in front, no game
    });

    [Fact]
    public void Taken_off_the_top_it_goes_behind_the_window_in_front_not_above_it() => OnSta(() =>
    {
        using var editor = Shown("editor");
        using var browser = Shown("browser");

        Native.SetOnTop(editor.Handle, true, browser.Handle);
        Assert.True(Native.IsTopmost(editor.Handle));
        Assert.True(Above(editor.Handle, browser.Handle));

        Native.SetOnTop(editor.Handle, false, browser.Handle);
        Assert.False(Native.IsTopmost(editor.Handle));
        Assert.True(Above(browser.Handle, editor.Handle), "the window in front should stay above the editor");
    });

    /// <summary>
    /// The open menu has a taskbar button and a place in Alt+Tab, so it can be found again from behind a browser (a
    /// player, 2026-10-27: "no taskbar icon, so if I switch to browser, I lose the menu"); closed, it has neither.
    /// </summary>
    [Fact]
    public void The_open_menu_is_in_the_taskbar_and_the_closed_one_is_not() => OnSta(() =>
    {
        using var overlay = Shown("overlay");
        long Style() => Native.GetWindowLongPtr(overlay.Handle, Native.GWL_EXSTYLE).ToInt64();
        bool InTaskbar() => (Style() & Native.WS_EX_APPWINDOW) != 0 && (Style() & Native.WS_EX_TOOLWINDOW) == 0;

        Native.SetInTaskbar(overlay.Handle, false);
        Assert.False(InTaskbar());
        Assert.True(Native.IsWindowVisible(overlay.Handle));

        Native.SetInTaskbar(overlay.Handle, true);
        Assert.True(InTaskbar());
        Assert.True(Native.IsWindowVisible(overlay.Handle), "the window should be shown again after the change");

        Native.SetInTaskbar(overlay.Handle, false);
        Assert.False(InTaskbar());
    });

    private static Form Shown(string title)
    {
        var form = new Form { Text = title, Width = 300, Height = 200, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Left = 50, Top = 50 };
        form.Show();
        Application.DoEvents();
        return form;
    }

    /// <summary>Whether <paramref name="upper"/> comes before <paramref name="lower"/> in the z-order (top first).</summary>
    private static bool Above(IntPtr upper, IntPtr lower)
    {
        for (IntPtr w = GetWindow(upper, GW_HWNDNEXT); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDNEXT))
            if (w == lower) return true;
        return false;
    }

    private static void OnSta(Action act)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { act(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private const uint GW_HWNDNEXT = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
}
