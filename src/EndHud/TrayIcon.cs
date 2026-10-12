using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EndHud;

/// <summary>The notification-area icon: EndHud's only presence when the HUD is hidden.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly OverlayWindow _overlay;
    private readonly NotifyIcon _icon;
    private readonly Icon _iconImage;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _showHud;
    private readonly ToolStripMenuItem _autoShow;
    private readonly ToolStripMenuItem _debugPanel;

    public TrayIcon(OverlayWindow overlay)
    {
        _overlay = overlay;
        _iconImage = DrawIcon();

        _status = new ToolStripMenuItem { Enabled = false };
        _showHud = new ToolStripMenuItem(Loc.Get("Tray.ShowHud"), null, (_, _) => _overlay.ToggleHud());
        _autoShow = new ToolStripMenuItem(Loc.Get("Tray.AutoShow"), null,
            (_, _) => _overlay.SetAutoShow(!_overlay.AutoShow));
        _debugPanel = new ToolStripMenuItem(Loc.Get("Tray.DebugPanel"), null, (_, _) => _overlay.ToggleDebugPanel());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _status,
            new ToolStripSeparator(),
            _showHud,
            new ToolStripMenuItem(Loc.Get("Tray.OpenMenu"), null, (_, _) => _overlay.ToggleMenu()),
            _autoShow,
            _debugPanel,
            new ToolStripSeparator(),
            new ToolStripMenuItem(Loc.Get("Tray.UpdateOperators"), null, (_, _) => _overlay.UpdateOperatorLibrary()),
            new ToolStripMenuItem(Loc.Get("Tray.OpenConfig"), null, (_, _) => OpenConfigFolder()),
            new ToolStripMenuItem(Loc.Get("Tray.Quit"), null, (_, _) => System.Windows.Application.Current.Shutdown()),
        });
        menu.Opening += (_, _) => RefreshItems();

        _icon = new NotifyIcon
        {
            Icon = _iconImage,
            Text = Loc.Get("Tray.Tooltip"),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _overlay.ToggleMenu();

        _icon.ShowBalloonTip(4000, "EndHud",
            Loc.Get(_overlay.AutoShow ? "Tray.Started" : "Tray.Started.Manual"), ToolTipIcon.Info);
    }

    private void RefreshItems()
    {
        _status.Text = Loc.Get(_overlay.GameFound ? "Tray.Status.Found" : "Tray.Status.NotFound");
        _showHud.Checked = _overlay.HudOn;
        _autoShow.Checked = _overlay.AutoShow;
        _debugPanel.Checked = _overlay.DebugPanel;
    }

    private static void OpenConfigFolder()
    {
        Directory.CreateDirectory(ConfigStore.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ConfigStore.Folder}\"") { UseShellExecute = true });
    }

    /// <summary>A small accent-coloured "E" tile, drawn at runtime so there is no binary asset to ship.</summary>
    private static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(255, 16, 20, 24));
            g.FillRectangle(background, 2, 2, 28, 28);
            using var accent = new SolidBrush(Color.FromArgb(255, 230, 194, 41));
            g.FillRectangle(accent, 2, 2, 4, 28);
            using var font = new Font("Segoe UI", 15, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("E", font, accent, 9, 5);
        }

        IntPtr handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage.Dispose();
    }
}
