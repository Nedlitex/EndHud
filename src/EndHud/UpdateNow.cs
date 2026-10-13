using System.IO;

namespace EndHud;

/// <summary>
/// <c>EndHud.exe --update-now [--from &lt;release api&gt;]</c>: the tray's "update now", without a
/// window and without restarting EndHud afterwards. For scripts, and for release.yml, which
/// installs an older build and updates it from a stand-in for GitHub (<c>--from</c>, HTTP allowed
/// only on this computer). The steps go to <see cref="Updater.WorkFolder"/>\update-now.log; exit
/// code 0 when the update was started (an MSI install finishes in the background: the installer's
/// exit code then appears in install-result.txt), 3 when already up to date, 1 otherwise.
/// </summary>
internal static class UpdateNow
{
    public const string Flag = "--update-now";

    public static bool Requested(string[] args) => args.Contains(Flag);

    public static async Task<int> RunAsync(string[] args)
    {
        Directory.CreateDirectory(Updater.WorkFolder);
        using var log = new StreamWriter(Path.Combine(Updater.WorkFolder, "update-now.log"), append: false) { AutoFlush = true };
        void Log(string line) => log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");

        int from = Array.IndexOf(args, "--from");
        string api = from >= 0 && from + 1 < args.Length ? args[from + 1] : ReleaseChecker.LatestReleaseApi;
        using var updates = new UpdateService(api);
        var updater = new Updater();
        Log($"EndHud {ReleaseChecker.Current?.ToString(3) ?? "dev"} ({updater.Kind}) at {Environment.ProcessPath}; asking {api}");

        var check = await updates.CheckAsync();
        Log($"check: {check.Status} {check.Release?.Version.ToString(3)}");
        if (check.Status == UpdateStatus.UpToDate) return 3;
        if (check.Status != UpdateStatus.NewVersion) return 1;

        var outcome = await updater.InstallAsync(check.Release!, restart: false);
        Log(outcome.Started ? "update started" : $"update failed: {outcome.Failure} {outcome.Detail}");
        return outcome.Started ? 0 : 1;
    }
}
