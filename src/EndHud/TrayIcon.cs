using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EndHud.Vision.Rotations;

namespace EndHud;

/// <summary>The notification-area icon: EndHud's only presence when the HUD is hidden.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly OverlayWindow _overlay;
    private readonly NotifyIcon _icon;
    private readonly Icon _iconImage;
    private AutoInstaller _autoInstaller = null!;
    private System.Windows.Threading.DispatcherTimer _autoInstallTimer = null!;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _showHud;
    private readonly ToolStripMenuItem _autoShow;
    private readonly ToolStripMenuItem _debugPanel;
    private readonly ToolStripMenuItem _innHelper;
    private readonly ToolStripMenuItem _checkUpdates;
    private readonly ToolStripMenuItem _updateNow;
    private readonly ToolStripMenuItem _releaseNotes;
    private readonly UpdateService _updates;
    private Action? _balloonClick; // what clicking the current notification does
    private bool _updating;

    /// <summary>The releases page, for updating by hand when updating in place didn't work.</summary>
    private const string ReleasesPage = $"https://github.com/{ReleaseChecker.Repository}/releases/latest";

    /// <param name="quiet">Started with Windows (<see cref="AutoStart.Flag"/>): no "EndHud is running" balloon.</param>
    public TrayIcon(OverlayWindow overlay, UpdateService updates, bool quiet = false)
    {
        _overlay = overlay;
        _updates = updates;
        _iconImage = DrawIcon();

        _status = new ToolStripMenuItem { Enabled = false };
        var version = new ToolStripMenuItem($"EndHud {ReleaseChecker.DisplayVersion}") { Enabled = false };
        _showHud = new ToolStripMenuItem(Loc.Get("Tray.ShowHud"), null, (_, _) => _overlay.ToggleHud());
        _autoShow = new ToolStripMenuItem(Loc.Get("Tray.AutoShow"), null,
            (_, _) => _overlay.SetAutoShow(!_overlay.AutoShow));
        _debugPanel = new ToolStripMenuItem(Loc.Get("Tray.DebugPanel"), null, (_, _) => _overlay.ToggleDebugPanel());
        _innHelper = new ToolStripMenuItem(Loc.Get("Tray.InnHelper"), null, (_, _) => _overlay.SetInnHelper(!_overlay.InnHelperEnabled));
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
            version,
            _status,
            new ToolStripSeparator(),
            _showHud,
            new ToolStripMenuItem(Loc.Get("Tray.OpenMenu"), null, (_, _) => _overlay.ToggleMenu()),
            _autoShow,
            _innHelper,
            _debugPanel,
            new ToolStripSeparator(),
            _updateNow,
            _releaseNotes,
            _checkUpdates,
            new ToolStripMenuItem(Loc.Get("Tray.EditRotations"), null, (_, _) => _overlay.OpenRotationEditor()),
            new ToolStripMenuItem(Loc.Get("Tray.OpenConfig"), null, (_, _) => OpenConfigFolder()),
            new ToolStripMenuItem(Loc.Get("Tray.ExportDiagnostics"), null, async (_, _) => await ExportDiagnosticsAsync()),
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

        // Found by the automatic checks (on worker threads): installed by itself once the game isn't
        // running (AutoInstaller), or offered.
        _autoInstaller = new AutoInstaller(() => _overlay.AutoInstallUpdates, () => _overlay.GameFound);
        _updates.NewVersionFound += release => OnUi(() =>
        {
            if (!_autoInstaller.Found(release))
                Notify(AvailableText(release), () => _ = UpdateNowAsync(release));
            else if (_overlay.GameFound)
                Notify(Loc.Format("Update.AutoLater", release.Version.ToString(3)), () => _ = UpdateNowAsync(release));
            InstallIfDue();
        });
        _autoInstallTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autoInstallTimer.Tick += (_, _) => InstallIfDue();
        _autoInstallTimer.Start();
        _overlay.PublishedRotations.Changed += (result, automatic) =>
        {
            // A manual check reports in its own balloon; removals alone aren't worth a notification.
            if (automatic && result.Added.Count + result.Updated.Count > 0)
                OnUi(() => Notify(PublishedText(result), _overlay.OpenRotationEditor));
        };
        // What to install for the inn helper: said here and in the log, never over the game.
        _overlay.InnProblemFound += text => OnUi(() => Notify(text, null));
        _overlay.NewOperatorsFound += added => OnUi(() =>
            Notify(Loc.Format("Operators.NewFound", string.Join("、", added.Select(o => o.NameZh))), null));

        if (!quiet)
            _icon.ShowBalloonTip(4000, "EndHud",
                Loc.Get(_overlay.AutoShow ? "Tray.Started" : "Tray.Started.Manual"), ToolTipIcon.Info);
    }

    private void RefreshItems()
    {
        _status.Text = Loc.Get(_overlay.GameFound ? "Tray.Status.Found" : "Tray.Status.NotFound");
        _showHud.Checked = _overlay.HudOn;
        _autoShow.Checked = _overlay.AutoShow;
        _debugPanel.Checked = _overlay.DebugPanel;
        _innHelper.Checked = _overlay.InnHelperEnabled;
        if (_updates.Available is { } release)
        {
            _updateNow.Text = Loc.Format(_updating ? "Tray.Updating" : "Tray.UpdateNow", release.Version.ToString(3));
            _updateNow.Enabled = !_updating;
            _releaseNotes.Text = Loc.Format("Tray.ReleaseNotes", release.Version.ToString(3));
            _updateNow.Visible = _releaseNotes.Visible = true;
        }
    }

    private void InstallIfDue()
    {
        if (!_updating && _autoInstaller.Due() is { } release) _ = UpdateNowAsync(release);
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

    private static string PublishedText(RotationSyncResult result) =>
        Loc.Format("Rotation.Published.Updated", string.Join("、", result.Added.Concat(result.Updated)));

    /// <summary>The tray's "check for updates": a newer EndHud, new operators on the wiki, and the recommended rotations.</summary>
    private async Task CheckForUpdatesAsync()
    {
        _checkUpdates.Enabled = false;
        try
        {
            var app = _updates.CheckAsync();
            var operators = _overlay.CheckOperatorsAsync();
            var rotations = _overlay.PublishedRotations.SyncAsync();
            var (update, check, published) = (await app, await operators, await rotations);

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
            string rotationLine = published switch
            {
                null => Loc.Get("Rotation.Published.Unavailable"),
                { Added.Count: > 0 } or { Updated.Count: > 0 } => PublishedText(published),
                _ => Loc.Format("Rotation.Published.UpToDate", published.Total),
            };
            Notify(appLine + Environment.NewLine + operatorLine + Environment.NewLine + rotationLine,
                update.Release is { } r && update.Status == UpdateStatus.NewVersion ? () => _ = UpdateNowAsync(r) : null);
        }
        finally
        {
            _checkUpdates.Enabled = true;
        }
    }

    /// <summary>Write the diagnostic zip to the desktop and show it in Explorer (see DiagnosticExport).</summary>
    private async Task ExportDiagnosticsAsync()
    {
        string? path = await _overlay.ExportDiagnosticsAsync();
        if (path is null)
        {
            Notify(Loc.Get("Diagnostics.ExportFailed"), OpenConfigFolder);
            return;
        }
        Notify(Loc.Format("Diagnostics.Exported", Path.GetFileName(path)), () => DiagnosticExport.Reveal(path));
        DiagnosticExport.Reveal(path);
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
