using System.Windows;

namespace EndHud;

/// <summary>
/// A system-tray app: no main window, just the tray icon plus a transparent overlay that
/// shows the HUD over the game.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\EndHud.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            // Already running in the tray; a second copy would fight over the hotkeys.
            Shutdown();
            return;
        }

        var config = ConfigStore.Load();
        Loc.Load(config.Language);

        var overlay = new OverlayWindow(HudOptions.Parse(e.Args), config);
        MainWindow = overlay;
        overlay.Show();
        _tray = new TrayIcon(overlay);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
