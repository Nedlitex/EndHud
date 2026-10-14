using Xunit;

namespace EndHud.Tests;

/// <summary>A newer EndHud installs by itself, but never while the game is running, and each version is tried once.</summary>
public class AutoInstallerTests
{
    private static ReleaseInfo Release(string version) =>
        new(new Version(version), "https://example/releases/" + version, []);

    [Fact]
    public void Installs_right_away_when_the_game_isnt_running()
    {
        var installer = new AutoInstaller(() => true, () => false);

        Assert.True(installer.Found(Release("9.0.0")));
        Assert.Equal(new Version("9.0.0"), installer.Due()!.Version);
        Assert.Null(installer.Due()); // taken
    }

    [Fact]
    public void Waits_until_the_game_is_closed()
    {
        bool playing = true;
        var installer = new AutoInstaller(() => true, () => playing);
        installer.Found(Release("9.0.0"));

        Assert.Null(installer.Due());
        Assert.Null(installer.Due());
        playing = false;
        Assert.NotNull(installer.Due());
    }

    [Fact]
    public void Off_means_never()
    {
        var installer = new AutoInstaller(() => false, () => false);

        Assert.False(installer.Found(Release("9.0.0")));
        Assert.Null(installer.Due());
    }

    [Fact]
    public void A_version_is_tried_once_per_run()
    {
        var installer = new AutoInstaller(() => true, () => false);
        installer.Found(Release("9.0.0"));
        installer.Due(); // tried (and, say, failed)

        Assert.False(installer.Found(Release("9.0.0")));
        Assert.Null(installer.Due());
        Assert.True(installer.Found(Release("9.0.1"))); // a newer one still is
    }
}
