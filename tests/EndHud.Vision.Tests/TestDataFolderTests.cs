using System.IO;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// EndHud.Vision never picks a folder of the player's (their %APPDATA%, their %TEMP%): every store, library and
/// cache is handed its folder by the app (AppFolders) or by the test, so a test can never write the player's data.
/// And this run's test folder (<see cref="TestRoot"/>) is new, while past runs' are cleaned up after a day.
/// </summary>
public class TestDataFolderTests
{
    [Fact]
    public void The_vision_library_never_picks_a_folder_of_the_players() =>
        Assert.Empty(TestRoot.PlayerFolderLines("EndHud.Vision"));

    [Fact]
    public void This_runs_folder_is_its_own_under_the_temp_test_folder_and_not_the_players()
    {
        Assert.True(Directory.Exists(TestRoot.Folder));
        Assert.True(TestRoot.IsUnder(TestRoot.Folder, TestRoot.Parent));
        Assert.StartsWith("run-", Path.GetFileName(TestRoot.Folder));
        Assert.False(TestRoot.IsUnder(TestRoot.Folder, TestRoot.RealAppFolder));
    }

    [Fact]
    public void Past_runs_older_than_a_day_are_deleted_and_newer_ones_kept()
    {
        string parent = Path.Combine(TestRoot.Folder, "cleanup-" + Guid.NewGuid().ToString("N")[..8]);
        string old = Directory.CreateDirectory(Path.Combine(parent, "run-old")).FullName;
        File.WriteAllText(Path.Combine(old, "endhud.log"), "x");
        string recent = Directory.CreateDirectory(Path.Combine(parent, "run-recent")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(parent, "not-a-run")).FullName;
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow - TimeSpan.FromDays(2));
        Directory.SetLastWriteTimeUtc(other, DateTime.UtcNow - TimeSpan.FromDays(2));

        TestRoot.DeleteOldRuns(parent, DateTime.UtcNow - TestRoot.KeptFor);

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
        Assert.True(Directory.Exists(other)); // only run folders are this cleanup's
    }
}
