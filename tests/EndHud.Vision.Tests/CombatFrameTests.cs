using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Frames from a real 4K combat recording (Fixtures/combat-*.jpg, UID blacked out). Expected
/// values were read off the recording by eye and match the readers' output:
/// <list type="bullet">
/// <item>5 s — calm: everyone at full HP, every 连携 ready, no ultimate ready.</item>
/// <item>15 s — slot 1 and slot 3 have used their 连携 and are refilling (~29%, ~46%).</item>
/// <item>20 s — both still refilling (~84%, ~74%); slot 4's ultimate has become ready.</item>
/// <item>30 s — slot 1 has taken damage (~83% HP); all 连携 ready; slot 4's ultimate still ready.</item>
/// </list>
/// </summary>
public class CombatFrameTests
{
    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static async Task<PartyState> ReadAsync(string fixture) =>
        await new PartyReader(new TextRecognizer()).ReadAsync(await ImageFile.LoadAsync(Fixture(fixture)));

    [Fact]
    public async Task Calm_moment_everything_full_and_ready()
    {
        var state = await ReadAsync("combat-5s.jpg");

        Assert.True(state.PanelVisible);
        Assert.All(state.Slots, s => Assert.InRange(s.Hp!.Value, 0.98, 1.0));
        Assert.All(state.Slots, s => Assert.True(s.Combo!.Ready));
        Assert.All(state.Slots, s => Assert.False(s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Combo_cooldowns_part_way_refilled()
    {
        var state = await ReadAsync("combat-15s.jpg");

        AssertCooling(state.Slots[0], 0.29);
        Assert.True(state.Slots[1].Combo!.Ready);
        AssertCooling(state.Slots[2], 0.46);
        Assert.True(state.Slots[3].Combo!.Ready);
    }

    [Fact]
    public async Task Ultimate_becomes_ready_while_combos_keep_refilling()
    {
        var state = await ReadAsync("combat-20s.jpg");

        AssertCooling(state.Slots[0], 0.84);
        AssertCooling(state.Slots[2], 0.74);
        Assert.Equal([false, false, false, true], state.Slots.Select(s => s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Hp_drop_on_the_damaged_operator()
    {
        var state = await ReadAsync("combat-30s.jpg");

        Assert.InRange(state.Slots[0].Hp!.Value, 0.80, 0.86);
        Assert.All(state.Slots.Skip(1), s => Assert.InRange(s.Hp!.Value, 0.98, 1.0));
        Assert.All(state.Slots, s => Assert.True(s.Combo!.Ready));
        Assert.True(state.Slots[3].Ultimate!.Ready);
    }

    [Fact]
    public async Task Exploration_screenshot_has_no_combo_bars_but_panel_is_still_visible()
    {
        // Out of combat the level text shows where the 连携 bar would be.
        var state = await ReadAsync("party-4k.png");

        Assert.True(state.PanelVisible);
        Assert.All(state.Slots, s => Assert.Null(s.Combo));
    }

    private static void AssertCooling(PartySlot slot, double expected)
    {
        Assert.False(slot.Combo!.Ready);
        Assert.InRange(slot.Combo.Progress, expected - 0.04, expected + 0.04);
    }
}
