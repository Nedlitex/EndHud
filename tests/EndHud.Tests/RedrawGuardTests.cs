using Xunit;

namespace EndHud.Tests;

/// <summary>When an overlay part redraws (<see cref="RedrawGuard"/>).</summary>
public class RedrawGuardTests
{
    /// <summary>
    /// A player's report (0.5.3): after the menu was opened once out of battle, the rotation strip's
    /// sample stayed on screen. Closing the menu leaves nothing to show (a null key), and that must
    /// still redraw, or the sample is never taken down.
    /// </summary>
    [Fact]
    public void Closing_the_menu_out_of_battle_takes_the_sample_down()
    {
        var guard = new RedrawGuard();
        Assert.True(guard.Changed(null)); // the first frame, nothing to show

        guard.Invalidate(); // the menu opens
        Assert.True(guard.Changed("sample"));

        guard.Invalidate(); // the menu closes; out of battle there's nothing to show
        Assert.True(guard.Changed(null));
    }

    [Fact]
    public void The_same_view_isnt_redrawn_until_invalidated()
    {
        var guard = new RedrawGuard();
        Assert.True(guard.Changed("phase 1"));
        Assert.False(guard.Changed("phase 1"));
        Assert.True(guard.Changed("phase 2"));
        Assert.True(guard.Changed(null));
        Assert.False(guard.Changed(null));

        guard.Invalidate();
        Assert.True(guard.Changed(null));
        Assert.False(guard.Changed(null));
    }
}
