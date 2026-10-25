using System.IO;
using EndHud.Vision.Tests;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// The tests never write into the player's real <c>%APPDATA%\EndHud</c> (or the real update folder in %TEMP%):
/// every folder the app derives its data from points into this run's <see cref="TestRoot"/>. Seen 2026-10-25:
/// the update-trust tests' "refused to download from https://evil.example" lines in the owner's own log, and so
/// in their diagnostics zips.
/// </summary>
public class TestDataFolderTests
{
    public static TheoryData<string, string> AppPaths() => new()
    {
        { "settings folder", ConfigStore.Folder },
        { "settings file", ConfigStore.FilePath },
        { "log folder", DiagnosticLog.Folder },
        { "log file", DiagnosticLog.FilePath },
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
}
