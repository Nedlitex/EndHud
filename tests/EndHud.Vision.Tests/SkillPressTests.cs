using EndHud.Vision.Input;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Reading the player's presses is a privacy promise: what leaves the input code says only which
/// squad slot cast its battle skill and when, never which key or button it was.
/// </summary>
public sealed class SkillPressTests
{
    [Fact]
    public void A_press_says_only_which_slot_and_when()
    {
        var names = typeof(SkillPress).GetProperties().Select(p => p.Name).OrderBy(n => n);
        Assert.Equal(new[] { "At", "Slot" }, names);
    }
}
