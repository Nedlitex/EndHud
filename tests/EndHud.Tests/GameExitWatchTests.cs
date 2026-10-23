using Xunit;

namespace EndHud.Tests;

/// <summary>"Close EndHud when the game exits": only once the game was seen and its window has been gone 10 s.</summary>
public class GameExitWatchTests
{
    private static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_game_gone_for_ten_seconds_after_it_was_seen_has_exited()
    {
        var watch = new GameExitWatch();

        Assert.False(watch.Update(true, T0));
        Assert.False(watch.Update(false, T0.AddSeconds(1)));
        Assert.False(watch.Update(false, T0.AddSeconds(10.9)));
        Assert.True(watch.Update(false, T0.AddSeconds(11)));
    }

    /// <summary>EndHud started before the game: nothing has exited yet, however long it waits.</summary>
    [Fact]
    public void Before_the_game_was_ever_seen_nothing_has_exited()
    {
        var watch = new GameExitWatch();

        Assert.False(watch.Update(false, T0));
        Assert.False(watch.Update(false, T0.AddMinutes(30)));
    }

    /// <summary>The window made again (fullscreen to windowed): back within 10 s, so it hasn't exited.</summary>
    [Fact]
    public void A_window_back_within_ten_seconds_starts_the_wait_again()
    {
        var watch = new GameExitWatch();
        watch.Update(true, T0);
        watch.Update(false, T0.AddSeconds(1));

        Assert.False(watch.Update(true, T0.AddSeconds(5)));
        Assert.False(watch.Update(false, T0.AddSeconds(6)));
        Assert.False(watch.Update(false, T0.AddSeconds(15)));
        Assert.True(watch.Update(false, T0.AddSeconds(16)));
    }

    /// <summary>Off unless the player turns it on (owner).</summary>
    [Fact]
    public void It_is_off_by_default() => Assert.False(new HudConfig().CloseWithGame);
}
