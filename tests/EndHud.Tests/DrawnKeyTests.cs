using Xunit;

namespace EndHud.Tests;

/// <summary>
/// When a strip is redrawn (<see cref="DrawnKey"/>). The regression: the rotation strip's sample,
/// drawn while the menu was open, stayed on screen after it closed, because closing the menu reset
/// the key to null and "nothing to show" is also null, so the strip was never collapsed.
/// </summary>
public class DrawnKeyTests
{
    [Fact]
    public void After_invalidating_nothing_to_show_still_redraws()
    {
        var drawn = new DrawnKey();
        Assert.True(drawn.Changed("sample")); // the menu is open: the sample is drawn
        drawn.Invalidate();                    // the menu closes
        Assert.True(drawn.Changed(null));      // out of battle: nothing to show, the strip must collapse
    }

    [Fact]
    public void The_same_key_is_not_redrawn()
    {
        var drawn = new DrawnKey();
        Assert.True(drawn.Changed("a"));
        Assert.False(drawn.Changed("a"));
        Assert.True(drawn.Changed(null));
        Assert.False(drawn.Changed(null));
    }

    [Fact]
    public void A_fresh_strip_with_nothing_to_show_is_collapsed_once()
    {
        // The strip starts collapsed in XAML; drawing "nothing" first is harmless either way.
        var drawn = new DrawnKey();
        drawn.Invalidate();
        Assert.True(drawn.Changed(null));
    }
}
