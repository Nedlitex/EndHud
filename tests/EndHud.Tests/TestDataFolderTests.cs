using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using EndHud.Vision.Tests;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The tests never write into the player's real <c>%APPDATA%\EndHud</c> (or the real update folder in %TEMP%):
/// every folder the app derives its data from points into this run's <see cref="TestRoot"/> (<see cref="TestFolders"/>).
/// Seen 2026-10-25: the update-trust tests' "refused to download … evil.example" lines in the owner's own log, and
/// so in their diagnostics zips.
/// </summary>
[Collection(WpfCollection.Name)] // the log is one for the process: DiagnosticsTests moves it while they run
public class TestDataFolderTests
{
    public static TheoryData<string, string> AppPaths() => new()
    {
        { "data folder", AppFolders.Data },
        { "temporary folder", AppFolders.Temp },
        { "settings folder", ConfigStore.Folder },
        { "settings file", ConfigStore.FilePath },
        { "log folder", DiagnosticLog.Folder },
        { "log file", DiagnosticLog.FilePath },
        { "rotations", AppFolders.Rotations },
        { "alerts", AppFolders.AlertsFile },
        { "operator library", PartyMonitor.LibraryFolder },
        { "operator package", PartyMonitor.ReferenceFolder },
        { "update work folder", Updater.WorkFolder },
    };

    [Theory]
    [MemberData(nameof(AppPaths))]
    public void Every_app_folder_is_in_this_runs_test_folder(string what, string path)
    {
        Assert.True(TestRoot.IsUnder(path, TestRoot.Folder), $"{what} is {path}, not under the test run's {TestRoot.Folder}");
        Assert.False(TestRoot.IsUnder(path, TestRoot.RealAppFolder), $"{what} is in the player's real {TestRoot.RealAppFolder}");
        Assert.False(TestRoot.IsUnder(path, TestRoot.RealUpdateFolder), $"{what} is in the player's real {TestRoot.RealUpdateFolder}");
    }

    /// <summary>
    /// No path of the player's is built anywhere but <see cref="AppFolders"/>, so a new store can't slip past the
    /// redirect. The one other line: where the MSI installs EndHud, only compared with the running exe's folder.
    /// </summary>
    [Fact]
    public void Only_AppFolders_picks_a_folder_of_the_players()
    {
        var elsewhere = TestRoot.PlayerFolderLines("EndHud")
            .Where(line => !line.StartsWith("AppFolders.cs:"))
            .Where(line => !(line.StartsWith("Updater.cs:") && line.Contains("SpecialFolder.LocalApplicationData), \"Programs\", \"EndHud\")")))
            .ToList();
        Assert.Empty(elsewhere);
    }

    [Fact]
    public void The_cli_never_reaches_the_apps_data()
    {
        // The CLI references only EndHud.Vision (which picks no folder: EndHud.Vision.Tests); its scratch folders are its own.
        string project = File.ReadAllText(Path.Combine(TestRoot.RepoRoot(), "src", "EndHud.Cli", "EndHud.Cli.csproj"));
        Assert.DoesNotContain(@"EndHud\EndHud.csproj", project);
        Assert.Empty(TestRoot.PlayerFolderLines("EndHud.Cli").Where(line => line.Contains("SpecialFolder.")));
    }

    /// <summary>The app's own default is still the player's real folder: only tests move it.</summary>
    [Fact]
    public void The_apps_real_data_folder_is_appdata_endhud() =>
        Assert.Equal(TestRoot.RealAppFolder, AppFolders.RealData, ignoreCase: true);

    /// <summary>
    /// The very refusal that reached the owner's log, run for real: it lands in the test run's log, and the
    /// player's real logs (only read, shared, never opened for writing) have no trace of it.
    /// </summary>
    [Fact]
    public async Task A_refused_update_download_is_logged_in_the_test_folder_not_the_players_log()
    {
        // Before anything is written: a log that isn't redirected is never written to by this test.
        Assert.True(TestRoot.IsUnder(DiagnosticLog.FilePath, TestRoot.Folder), $"the log is {DiagnosticLog.FilePath}: not redirected, nothing written");

        string host = $"guard-{Guid.NewGuid():N}.evil.example";
        string app = Directory.CreateDirectory(Path.Combine(TestRoot.Folder, "guard-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        string exe = Path.Combine(app, "EndHud.exe");
        File.WriteAllBytes(exe, "old"u8.ToArray());
        using var updater = new Updater(() => new HttpClient(), exe, Path.Combine(app, "work"), InstallKind.Portable, _ => true,
            UpdateFeed.GitHub, elevated: false);
        var release = new ReleaseInfo(new Version(1, 4, 0), "https://github.com/Nedlitex/EndHud/releases/tag/v1.4.0",
            [new ReleaseAsset("EndHud-1.4.0-portable.exe", $"https://{host}/EndHud-1.4.0-portable.exe",
                Convert.ToHexString(SHA256.HashData("new"u8.ToArray())).ToLowerInvariant())]);

        var outcome = await updater.InstallAsync(release, restart: false);

        Assert.Equal(UpdateFailure.NoPackage, outcome.Failure);
        Assert.Contains(host, ReadShared(DiagnosticLog.FilePath));
        foreach (string log in RealLogs())
            Assert.DoesNotContain(host, ReadShared(log));
    }

    /// <summary>The player's real log files, if any (endhud.log and its rolled-over copies).</summary>
    private static IEnumerable<string> RealLogs()
    {
        string folder = Path.Combine(TestRoot.RealAppFolder, "logs");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "endhud*.log") : [];
    }

    /// <summary>Read without locking out the running EndHud (which may be writing its own log).</summary>
    private static string ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
