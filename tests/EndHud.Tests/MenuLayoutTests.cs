using System.IO;
using Xunit;

namespace EndHud.Tests;

/// <summary>Owner 2026-10-24: the menu was as tall as the screen, and the inn banners' drag frame stayed with the inn helper off.</summary>
public sealed class MenuLayoutTests
{
    [Fact]
    public void The_menu_is_at_most_two_thirds_of_a_1080_high_game_and_scrolls_the_rest()
    {
        Assert.Equal(720, OverlayWindow.MenuMaxHeight);
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "EndHud", "OverlayWindow.xaml"));
        Assert.Contains("MaxHeight=\"{x:Static local:OverlayWindow.MenuMaxHeight}\"", xaml);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    public void The_inn_banners_drag_frame_is_up_only_with_the_menu_open_and_the_inn_calls_on(bool menuOpen, bool innHelper, bool innCalls, bool shown)
    {
        var config = new HudConfig { InnHelperEnabled = innHelper, InnCallsEnabled = innCalls };
        Assert.Equal(shown, OverlayWindow.InnCallPlacing(menuOpen, config));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EndHud.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("EndHud.sln not found above the test folder");
    }
}
