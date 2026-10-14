using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Whether a reading is in a battle (<see cref="PartyState.InCombat"/>): the rotation strip shows
/// only then. Battles in every state count; exploration, a wiped squad and screens without the
/// squad panel (login, loading, menus) don't.
/// </summary>
public class InCombatTests
{
    private static async Task<PartyState> ReadAsync(Frame frame) => await new PartyReader(new TextRecognizer()).ReadAsync(frame);

    [Theory]
    [InlineData("combat-all-ready.jpg")]
    [InlineData("combat-combos-refilling.jpg")]
    [InlineData("combat-slot1-hurt.jpg")]
    [InlineData("ultimate-charge-near-half.jpg")]
    [InlineData("battle-dimmed-squad-heal.jpg")]
    [InlineData("battle-low-hp.jpg")]
    [InlineData("battle-three-fallen.jpg")]
    public async Task Battles_are_in_combat(string file)
    {
        Assert.True((await ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(file)))).InCombat);
    }

    [Theory]
    [InlineData("exploration-dark-portraits.jpg")]
    [InlineData("exploration-levels-visible.png")]
    [InlineData("exploration-scenery-behind-ultimate.jpg")]
    [InlineData("battle-squad-wiped.jpg")]
    public async Task Exploration_and_a_wiped_squad_are_not(string file)
    {
        Assert.False((await ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(file)))).InCombat);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public async Task Screens_without_the_squad_panel_are_not(byte grey)
    {
        // Login, loading and menu screens: no squad panel at all.
        var pixels = new byte[3840 * 2160 * 4];
        Array.Fill(pixels, grey);

        Assert.False((await ReadAsync(new Frame(3840, 2160, pixels))).InCombat);
    }
}
