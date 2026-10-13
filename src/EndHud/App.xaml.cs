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
    private UpdateService? _updates;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (SelfTest.Requested(e.Args))
        {
            // No window, no tray icon, no user data; runs alongside an EndHud in the tray.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await SelfTest.RunAsync(e.Args));
            return;
        }

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
        _updates = new UpdateService();
        _tray = new TrayIcon(overlay, _updates);
        _updates.Start(() => config.CheckForUpdates);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updates?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
