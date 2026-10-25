using System.IO;

namespace EndHud;

/// <summary>
/// <c>EndHud.exe --update-now [--from &lt;release api&gt;]</c>: the tray's "update now", without a
/// window and without restarting EndHud afterwards. For scripts, and for release.yml, which
/// installs an older build and updates it from a stand-in for GitHub (<c>--from</c>, HTTP allowed
/// only on this computer). The steps go to <see cref="Updater.WorkFolder"/>\update-now.log; exit
/// code 0 when the update was started (an MSI install finishes in the background: the installer's
/// exit code then appears in install-result.txt), 3 when already up to date, 2 when <c>--from</c> is refused
/// (anything but a stand-in on this computer: <see cref="UpdateFeed.Local"/>), 1 otherwise.
/// </summary>
internal static class UpdateNow
{
    public const string Flag = "--update-now";

    public static bool Requested(string[] args) => args.Contains(Flag);

    public static Task<int> RunAsync(string[] args) =>
        RunAsync(args, Updater.WorkFolder, feed => new UpdateService(feed), feed => new Updater(feed));

    /// <param name="work">Where update-now.log goes.</param>
    /// <param name="service">The update check against this feed (tests: with a stand-in handler).</param>
    /// <param name="updater">The updater for this feed (tests: one that records what it would start).</param>
    internal static async Task<int> RunAsync(string[] args, string work, Func<UpdateFeed, UpdateService> service, Func<UpdateFeed, Updater> updater)
    {
        Directory.CreateDirectory(work);
        using var log = new StreamWriter(Path.Combine(work, "update-now.log"), append: false) { AutoFlush = true };
        void Log(string line) => log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");

        var feed = UpdateFeed.GitHub;
        int from = Array.IndexOf(args, "--from");
        if (from >= 0)
        {
            // Only release.yml's stand-in on this computer: otherwise any program could make EndHud fetch and run a file of its choosing.
            if (UpdateFeed.Local(from + 1 < args.Length ? args[from + 1] : null, out string refusal) is not { } local)
            {
                Log(refusal);
                return 2;
            }
            feed = local;
        }
        using var updates = service(feed);
        var install = updater(feed);
        Log($"EndHud {ReleaseChecker.Current?.ToString(3) ?? "dev"} ({install.Kind}) at {Environment.ProcessPath}; asking {feed.Api}");

        var check = await updates.CheckAsync();
        Log($"check: {check.Status} {check.Release?.Version.ToString(3)}");
        if (check.Status == UpdateStatus.UpToDate) return 3;
        if (check.Status != UpdateStatus.NewVersion) return 1;

        var outcome = await install.InstallAsync(check.Release!, restart: false);
        Log(outcome.Started ? "update started" : $"update failed: {outcome.Failure} {outcome.Detail}");
        return outcome.Started ? 0 : 1;
    }
}
