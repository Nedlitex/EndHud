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
    private readonly ToolStripMenuItem _checkUpdates;
    private readonly ToolStripMenuItem _updateNow;
    private readonly ToolStripMenuItem _releaseNotes;
    private readonly UpdateService _updates;
    private Action? _balloonClick; // what clicking the current notification does
    private bool _updating;

    /// <summary>The releases page, for updating by hand when updating in place didn't work.</summary>
    private const string ReleasesPage = $"https://github.com/{ReleaseChecker.Repository}/releases/latest";

    public TrayIcon(OverlayWindow overlay, UpdateService updates)
    {
        _overlay = overlay;
        _updates = updates;
        _iconImage = DrawIcon();

        _status = new ToolStripMenuItem { Enabled = false };
        _showHud = new ToolStripMenuItem(Loc.Get("Tray.ShowHud"), null, (_, _) => _overlay.ToggleHud());
        _autoShow = new ToolStripMenuItem(Loc.Get("Tray.AutoShow"), null,
            (_, _) => _overlay.SetAutoShow(!_overlay.AutoShow));
        _debugPanel = new ToolStripMenuItem(Loc.Get("Tray.DebugPanel"), null, (_, _) => _overlay.ToggleDebugPanel());
        _checkUpdates = new ToolStripMenuItem(Loc.Get("Tray.CheckUpdates"), null, async (_, _) => await CheckForUpdatesAsync());
        // Shown only when a newer release has been found.
        _updateNow = new ToolStripMenuItem("", null, async (_, _) =>
        {
            if (_updates.Available is { } release) await UpdateNowAsync(release);
        }) { Visible = false };
        _releaseNotes = new ToolStripMenuItem("", null, (_, _) => OpenLink(_updates.Available?.PageUrl)) { Visible = false };

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
            _updateNow,
            _releaseNotes,
            _checkUpdates,
            new ToolStripMenuItem(Loc.Get("Tray.EditRotations"), null, (_, _) => _overlay.OpenRotationEditor()),
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
        _icon.BalloonTipClicked += (_, _) => _balloonClick?.Invoke();

        // Found by the automatic checks (on worker threads).
        _updates.NewVersionFound += release => OnUi(() => Notify(AvailableText(release), () => _ = UpdateNowAsync(release)));
        _overlay.NewOperatorsFound += added => OnUi(() =>
            Notify(Loc.Format("Operators.NewFound", string.Join("、", added.Select(o => o.NameZh))), null));

        _icon.ShowBalloonTip(4000, "EndHud",
            Loc.Get(_overlay.AutoShow ? "Tray.Started" : "Tray.Started.Manual"), ToolTipIcon.Info);
    }

    private void RefreshItems()
    {
        _status.Text = Loc.Get(_overlay.GameFound ? "Tray.Status.Found" : "Tray.Status.NotFound");
        _showHud.Checked = _overlay.HudOn;
        _autoShow.Checked = _overlay.AutoShow;
        _debugPanel.Checked = _overlay.DebugPanel;
        if (_updates.Available is { } release)
        {
            _updateNow.Text = Loc.Format(_updating ? "Tray.Updating" : "Tray.UpdateNow", release.Version.ToString(3));
            _updateNow.Enabled = !_updating;
            _releaseNotes.Text = Loc.Format("Tray.ReleaseNotes", release.Version.ToString(3));
            _updateNow.Visible = _releaseNotes.Visible = true;
        }
    }

    private static string AvailableText(ReleaseInfo release) =>
        Loc.Format("Update.Available", release.Version.ToString(3), ReleaseChecker.Current?.ToString(3) ?? "?");

    /// <summary>
    /// "Update now": download, check and install <paramref name="release"/> in place, then EndHud
    /// exits and the new version starts (see <see cref="Updater"/>). On failure nothing has changed;
    /// the notification says why and links to the releases page.
    /// </summary>
    private async Task UpdateNowAsync(ReleaseInfo release)
    {
        if (_updating) return;
        _updating = true;
        string version = release.Version.ToString(3);
        Notify(Loc.Format("Update.Downloading", version), null);
        try
        {
            var outcome = await new Updater().InstallAsync(release, restart: true);
            if (outcome.Started)
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }
            string reason = outcome.Failure switch
            {
                UpdateFailure.Download => Loc.Format("Update.Failed.Download", version),
                UpdateFailure.Checksum => Loc.Format("Update.Failed.Checksum", version),
                UpdateFailure.Replace => Loc.Format("Update.Failed.Replace", version, outcome.Detail ?? ""),
                _ => Loc.Format("Update.Failed.NoPackage", version),
            };
            Notify(reason, () => OpenLink(release.PageUrl));
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Started by the updater after installing <paramref name="version"/>.</summary>
    public void ShowUpdated(string version) => Notify(Loc.Format("Update.Done", version), null);

    /// <summary>Started by the updater after the installer failed: this is still the old version.</summary>
    public void ShowUpdateFailed(string version) =>
        Notify(Loc.Format("Update.Failed.Install", version, ReleaseChecker.Current?.ToString(3) ?? "?"), () => OpenLink(ReleasesPage));

    /// <summary>The tray's "check for updates": a newer EndHud, and new operators on the wiki.</summary>
    private async Task CheckForUpdatesAsync()
    {
        _checkUpdates.Enabled = false;
        try
        {
            var app = _updates.CheckAsync();
            var operators = _overlay.CheckOperatorsAsync();
            var (update, check) = (await app, await operators);

            string appLine = update.Status switch
            {
                UpdateStatus.NewVersion => Loc.Format("Update.Available", update.Release!.Version.ToString(3), ReleaseChecker.Current!.ToString(3)),
                UpdateStatus.UpToDate => Loc.Format("Update.UpToDate", ReleaseChecker.Current!.ToString(3)),
                UpdateStatus.DevelopmentBuild => Loc.Get("Update.DevelopmentBuild"),
                _ => Loc.Get("Update.Unavailable"),
            };
            string operatorLine = check.Status switch
            {
                OperatorCheckStatus.Checked when check.Added.Count > 0 =>
                    Loc.Format("Operators.NewFound", string.Join("、", check.Added.Select(o => o.NameZh))),
                OperatorCheckStatus.Checked => Loc.Format("Operators.NoneNew", check.Total),
                OperatorCheckStatus.CheckedRecently => Loc.Format("Operators.CheckedRecently", Math.Max(1, (int)check.Since.TotalMinutes)),
                OperatorCheckStatus.AlreadyChecking => Loc.Get("Operators.AlreadyChecking"),
                _ => Loc.Get("Operators.UpdateFailed"),
            };
            Notify(appLine + Environment.NewLine + operatorLine,
                update.Release is { } r && update.Status == UpdateStatus.NewVersion ? () => _ = UpdateNowAsync(r) : null);
        }
        finally
        {
            _checkUpdates.Enabled = true;
        }
    }

    private void Notify(string text, Action? onClick)
    {
        _balloonClick = onClick;
        _icon.ShowBalloonTip(8000, Loc.Get("Update.Title"), text, ToolTipIcon.Info);
    }

    private static void OnUi(Action action) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);

    private static void OpenLink(string? url)
    {
        if (url is not null) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
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
