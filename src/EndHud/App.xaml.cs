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

        if (SelfTest.Requested(e.Args) || UpdateNow.Requested(e.Args))
        {
            // No window, no tray icon, no user data; runs alongside an EndHud in the tray.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(SelfTest.Requested(e.Args) ? await SelfTest.RunAsync(e.Args) : await UpdateNow.RunAsync(e.Args));
            return;
        }

        string? updatedTo = Option(e.Args, Updater.UpdatedFlag);
        string? updateFailed = Option(e.Args, Updater.UpdateFailedFlag);

        _singleInstance = new Mutex(true, @"Local\EndHud.SingleInstance", out bool isFirst);
        if (!isFirst && !(updatedTo is not null && WaitForPreviousCopy(_singleInstance)))
        {
            // Already running in the tray; a second copy would fight over the hotkeys.
            Shutdown();
            return;
        }

        // A finished update leaves the old exe and the download behind; a failed one keeps the
        // installer's log for whoever looks into it.
        if (updateFailed is null) Updater.CleanUp();

        var config = ConfigStore.Load();
        Loc.Load(config.Language);

        var overlay = new OverlayWindow(HudOptions.Parse(e.Args), config);
        MainWindow = overlay;
        overlay.Show();
        _updates = new UpdateService();
        _tray = new TrayIcon(overlay, _updates);
        if (updatedTo is not null) _tray.ShowUpdated(updatedTo);
        else if (updateFailed is not null) _tray.ShowUpdateFailed(updateFailed);
        _updates.Start(() => config.CheckForUpdates);
        overlay.PublishedRotations.Start(() => config.CheckForUpdates);
    }

    /// <summary>Started by the version being replaced (portable update): wait for it to exit.</summary>
    private static bool WaitForPreviousCopy(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.FromSeconds(30));
        }
        catch (AbandonedMutexException)
        {
            return true; // it exited without releasing: ours now
        }
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updates?.Dispose();
        (MainWindow as OverlayWindow)?.PublishedRotations.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
