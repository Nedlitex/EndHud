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

    /// <summary>
    /// The squad select screen before a challenge: its tab bar gives 6 of the panel marks and its
    /// strips read as combo bars and an empty skill-point bar, but every HP bar is empty, nobody is
    /// controlled and no ultimate charges. It isn't the squad panel (read as one, its floor was
    /// named as the squad and the gauges stayed up through the loading screen after it).
    /// </summary>
    [Fact]
    public async Task The_squad_select_screen_is_not_the_squad_panel()
    {
        var state = await ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("panel-or-not", "squad-select-screen.jpg"))));

        Assert.True(state.PanelMarks >= 6, $"{state.PanelMarks} marks: the case this guards against");
        Assert.False(state.PanelVisible);
        Assert.False(state.InCombat);
    }

    /// <summary>
    /// A white sky with rocks falling (respawning after a failed fight, controller recording): the
    /// sky in the combo bars' place reads as two "bars", and the red and cyan fringes around the
    /// rocks gave two HP bars 4–8% when coloured columns anywhere counted. Read as the panel, the
    /// sky was named as four Purrchena and the names stuck. An HP bar fills from its left end.
    /// </summary>
    [Fact]
    public async Task A_white_sky_with_coloured_fringes_is_not_the_squad_panel()
    {
        var state = await ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("panel-or-not", "white-sky-after-a-failed-fight.jpg"))));

        Assert.True(state.Slots.Count(s => s.Combo is not null) >= 2, "the case this guards against: the sky reads as combo bars");
        Assert.All(state.Slots, s => Assert.True(s.Hp <= 0.02, $"slot {s.Number} HP {s.Hp}"));
        Assert.False(state.PanelVisible);
        Assert.False(state.InCombat);
    }

    /// <summary>A boss's status covering every HP bar in battle: still the squad panel, still a battle.</summary>
    [Fact]
    public async Task A_battle_with_every_hp_bar_covered_is_still_in_combat()
    {
        var state = await ReadAsync(await ImageFile.LoadAsync(CombatFrameTests.Fixture(Path.Combine("panel-or-not", "battle-hp-bars-covered.jpg"))));

        Assert.All(state.Slots, s => Assert.True(s.Hp <= 0.02, $"slot {s.Number} HP {s.Hp}: the case this guards"));
        Assert.True(state.PanelVisible);
        Assert.True(state.InCombat);
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
