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
        StartLog(updatedTo, updateFailed);
        // A release EndHud that was moved keeps "start with Windows" pointing at itself; a
        // development build never touches the player's entry.
        if (ReleaseChecker.Current is not null)
        {
            try { AutoStart.ForThisExe().Refresh(); }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
            {
                DiagnosticLog.Warn($"start with Windows: couldn't check the entry ({ex.Message})");
            }
        }

        var overlay = new OverlayWindow(HudOptions.Parse(e.Args), config);
        MainWindow = overlay;
        overlay.Show();
        _updates = new UpdateService(UpdateFeed.GitHub);
        _tray = new TrayIcon(overlay, _updates, quiet: e.Args.Contains(AutoStart.Flag));
        if (updatedTo is not null) _tray.ShowUpdated(updatedTo);
        else if (updateFailed is not null) _tray.ShowUpdateFailed(updateFailed);
        _updates.Start(() => config.CheckForUpdates);
        overlay.PublishedRotations.Start(() => config.CheckForUpdates);
    }

    /// <summary>
    /// Start the diagnostic log: what this PC is, and anything that would otherwise fail unseen
    /// (an exception on a background task, a crash).
    /// </summary>
    private void StartLog(string? updatedTo, string? updateFailed)
    {
        DiagnosticLog.Info("EndHud started" + (updatedTo is not null ? $" (updated to {updatedTo})" : updateFailed is not null ? " (update failed)" : "") +
                           Environment.NewLine + DiagnosticExport.DescribeComputer().TrimEnd());
        AppDomain.CurrentDomain.UnhandledException += (_, args) => DiagnosticLog.Error("crash", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) => DiagnosticLog.Error("unhandled error on the UI thread", args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => DiagnosticLog.Error("unobserved background error", args.Exception);
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
        if (_tray is not null) DiagnosticLog.Info("EndHud exiting"); // not for a self-test or update run, which keep no user data
        _updates?.Dispose();
        (MainWindow as OverlayWindow)?.PublishedRotations.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
