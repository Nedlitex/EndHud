using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Frames from a real 4K combat recording (Fixtures/combat-*.jpg, UID blacked out). Expected
/// values were read off the recording by eye and match the readers' output:
/// <list type="bullet">
/// <item>5 s — calm: everyone at full HP, every 连携 ready, every 终结技 ready.</item>
/// <item>15 s — slot 1 used its ultimate (at ~9 s) and it's charging; slot 1 and slot 3 have
/// used their 连携 and are refilling (~29%, ~46%).</item>
/// <item>20 s — both 连携 still refilling (~84%, ~74%); slot 1's ultimate still charging.</item>
/// <item>30 s — slot 1 has taken damage (~83% HP); all 连携 ready; slot 1's ultimate still charging.</item>
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
        Assert.All(state.Slots, s => Assert.True(s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Combo_cooldowns_part_way_refilled()
    {
        var state = await ReadAsync("combat-15s.jpg");

        AssertCooling(state.Slots[0], 0.29);
        Assert.True(state.Slots[1].Combo!.Ready);
        AssertCooling(state.Slots[2], 0.46);
        Assert.True(state.Slots[3].Combo!.Ready);
        Assert.Equal([false, true, true, true], state.Slots.Select(s => s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Used_ultimate_keeps_charging_while_combos_keep_refilling()
    {
        var state = await ReadAsync("combat-20s.jpg");

        AssertCooling(state.Slots[0], 0.84);
        AssertCooling(state.Slots[2], 0.74);
        Assert.Equal([false, true, true, true], state.Slots.Select(s => s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Hp_drop_on_the_damaged_operator()
    {
        var state = await ReadAsync("combat-30s.jpg");

        Assert.InRange(state.Slots[0].Hp!.Value, 0.80, 0.86);
        Assert.All(state.Slots.Skip(1), s => Assert.InRange(s.Hp!.Value, 0.98, 1.0));
        Assert.All(state.Slots, s => Assert.True(s.Combo!.Ready));
        Assert.Equal([false, true, true, true], state.Slots.Select(s => s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Exploration_screenshot_has_no_combo_bars_but_panel_is_still_visible()
    {
        // Out of combat the level text shows where the 连携 bar would be.
        var state = await ReadAsync("party-4k.png");

        Assert.True(state.PanelVisible);
        Assert.All(state.Slots, s => Assert.Null(s.Combo));
    }

    // ---- Frames from the ultimate-charging recording (3838×2158, UID blacked out) ----

    [Fact]
    public async Task Ultimate_charge_barely_started()
    {
        var state = await ReadAsync("charging-10s.jpg");

        Assert.False(state.Slots[0].Ultimate!.Ready);
        Assert.InRange(state.Slots[0].Ultimate!.Progress, 0, 0.05);
        Assert.Equal([true, true, true], state.Slots.Skip(1).Select(s => s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Hurt_operators_and_a_refilling_combo_while_ultimate_charges()
    {
        var state = await ReadAsync("charging-24s.jpg");

        Assert.InRange(state.Slots[0].Hp!.Value, 0.85, 0.91);
        Assert.InRange(state.Slots[1].Hp!.Value, 0.87, 0.93);
        AssertCooling(state.Slots[3], 0.21);
        Assert.InRange(state.Slots[0].Ultimate!.Progress, 0.01, 0.08);
    }

    [Fact]
    public async Task Ultimate_charge_at_about_45_percent()
    {
        var state = await ReadAsync("charging-35s.jpg");

        Assert.False(state.Slots[0].Ultimate!.Ready);
        Assert.InRange(state.Slots[0].Ultimate!.Progress, 0.40, 0.48);
        AssertCooling(state.Slots[3], 0.81);
    }

    private static void AssertCooling(PartySlot slot, double expected)
    {
        Assert.False(slot.Combo!.Ready);
        Assert.InRange(slot.Combo.Progress, expected - 0.04, expected + 0.04);
    }
}
