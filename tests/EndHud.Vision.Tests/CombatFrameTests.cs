using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Single frames from real 4K gameplay (Fixtures/, UID blacked out), named for what they show.
/// Expected values were read off the frames by eye and match the readers' output. The combat
/// frames, for example:
/// <list type="bullet">
/// <item>combat-all-ready — calm: everyone at full HP, every 连携 and 终结技 ready.</item>
/// <item>combat-combos-refilling — slot 1 has used its ultimate (charging); slots 1 and 3 have
/// used their 连携 and are refilling (~29%, ~46%).</item>
/// <item>combat-combos-nearly-refilled — both 连携 still refilling (~84%, ~74%).</item>
/// <item>combat-slot1-hurt — slot 1 has taken damage (~83% HP); all 连携 ready.</item>
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
        var state = await ReadAsync("combat-all-ready.jpg");

        Assert.True(state.PanelVisible);
        Assert.All(state.Slots, s => Assert.InRange(s.Hp!.Value, 0.98, 1.0));
        Assert.All(state.Slots, s => Assert.True(s.Combo!.Ready));
        Assert.All(state.Slots, s => Assert.True(s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Combo_cooldowns_part_way_refilled()
    {
        var state = await ReadAsync("combat-combos-refilling.jpg");

        AssertCooling(state.Slots[0], 0.29);
        Assert.True(state.Slots[1].Combo!.Ready);
        AssertCooling(state.Slots[2], 0.46);
        Assert.True(state.Slots[3].Combo!.Ready);
        Assert.Equal([false, true, true, true], state.Slots.Select(s => s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Used_ultimate_keeps_charging_while_combos_keep_refilling()
    {
        var state = await ReadAsync("combat-combos-nearly-refilled.jpg");

        AssertCooling(state.Slots[0], 0.84);
        AssertCooling(state.Slots[2], 0.74);
        Assert.Equal([false, true, true, true], state.Slots.Select(s => s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Hp_drop_on_the_damaged_operator()
    {
        var state = await ReadAsync("combat-slot1-hurt.jpg");

        Assert.InRange(state.Slots[0].Hp!.Value, 0.80, 0.86);
        Assert.All(state.Slots.Skip(1), s => Assert.InRange(s.Hp!.Value, 0.98, 1.0));
        Assert.All(state.Slots, s => Assert.True(s.Combo!.Ready));
        Assert.Equal([false, true, true, true], state.Slots.Select(s => s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Exploration_screenshot_has_no_combo_bars_but_panel_is_still_visible()
    {
        // Out of combat the level text shows where the 连携 bar would be.
        var state = await ReadAsync("exploration-levels-visible.png");

        Assert.True(state.PanelVisible);
        Assert.All(state.Slots, s => Assert.Null(s.Combo));
    }

    // ---- Frames from the ultimate-charging recording (3838×2158, UID blacked out) ----

    [Fact]
    public async Task Ultimate_charge_barely_started()
    {
        var state = await ReadAsync("ultimate-charge-just-started.jpg");

        Assert.False(state.Slots[0].Ultimate!.Ready);
        Assert.InRange(state.Slots[0].Ultimate!.Progress, 0, 0.05);
        Assert.Equal([true, true, true], state.Slots.Skip(1).Select(s => s.Ultimate!.Ready));
    }

    [Fact]
    public async Task Hurt_operators_and_a_refilling_combo_while_ultimate_charges()
    {
        var state = await ReadAsync("ultimate-charging-two-hurt.jpg");

        Assert.InRange(state.Slots[0].Hp!.Value, 0.85, 0.91);
        Assert.InRange(state.Slots[1].Hp!.Value, 0.87, 0.93);
        AssertCooling(state.Slots[3], 0.21);
        Assert.InRange(state.Slots[0].Ultimate!.Progress, 0.01, 0.08);
    }

    [Fact]
    public async Task Ultimate_charge_at_about_45_percent()
    {
        var state = await ReadAsync("ultimate-charge-near-half.jpg");

        Assert.False(state.Slots[0].Ultimate!.Ready);
        Assert.InRange(state.Slots[0].Ultimate!.Progress, 0.40, 0.48);
        AssertCooling(state.Slots[3], 0.81);
    }

    // ---- Exploration frames from the out-of-combat recording (3838×2158, UID blacked out) ----
    // Out of combat there is no 连携 bar; the rows where it would be show the portraits' dark
    // clothing and shadow, which used to read as an empty bar ("冷却中 0%").

    [Theory]
    [InlineData("exploration-dark-portraits.jpg")]
    [InlineData("exploration-scenery-behind-ultimate.jpg")]
    [InlineData("exploration-levels-visible.png")]
    public async Task Out_of_combat_no_slot_reports_a_combo_bar(string fixture)
    {
        var state = await ReadAsync(fixture);

        Assert.True(state.PanelVisible);
        Assert.All(state.Slots, s => Assert.Null(s.Combo));
        Assert.All(state.Slots, s => Assert.Equal(90, s.Level)); // "LV9" (a lost digit) must not win
    }

    [Fact]
    public async Task Ultimate_charge_reads_through_colourful_scenery_behind_the_circle()
    {
        // A copper pipe sits behind slot 1's translucent circle; it used to add ~17% to the arc.
        var state = await ReadAsync("exploration-scenery-behind-ultimate.jpg");

        Assert.False(state.Slots[0].Ultimate!.Ready);
        Assert.InRange(state.Slots[0].Ultimate!.Progress, 0.40, 0.48);
    }

    // ---- Frames from the full battle (3838×2158, UID blacked out) ----

    [Fact]
    public async Task Low_hp_pink_frames_still_show_hp_and_combo_cooldowns()
    {
        var state = await ReadAsync("battle-low-hp.jpg");

        Assert.True(state.PanelVisible);
        Assert.InRange(state.Slots[1].Hp!.Value, 0.24, 0.30);
        Assert.InRange(state.Slots[2].Hp!.Value, 0.22, 0.28);
        Assert.InRange(state.Slots[3].Hp!.Value, 0.13, 0.19);
        AssertCooling(state.Slots[1], 0.42);
        AssertCooling(state.Slots[2], 0.38);
        AssertCooling(state.Slots[3], 0.34);
    }

    [Fact]
    public async Task Nearly_dead_white_haired_operator_is_alive_and_red_ping_is_read()
    {
        var state = await ReadAsync("battle-nearly-dead-red-ping.jpg");

        Assert.All(state.Slots, s => Assert.False(s.Dead));
        Assert.InRange(state.Slots[2].Hp!.Value, 0, 0.05); // Arcane, 2%
        Assert.All(state.Slots, s => Assert.Equal(90, s.Level));
        Assert.Equal(495, state.PingMs); // red at high latency, with white icons touching it
    }

    [Fact]
    public async Task Heal_glow_over_a_dimmed_squad_does_not_fill_the_hp_bar()
    {
        var state = await ReadAsync("battle-dimmed-squad-heal.jpg");

        Assert.InRange(state.Slots[0].Hp!.Value, 0.55, 0.65); // heal glow over it
        Assert.InRange(state.Slots[1].Hp!.Value, 0.62, 0.72); // dimmed
        Assert.InRange(state.Slots[2].Hp!.Value, 0.66, 0.76);
        Assert.InRange(state.Slots[3].Hp!.Value, 0.64, 0.74);
    }

    [Fact]
    public async Task Fallen_operators_are_marked_dead()
    {
        var state = await ReadAsync("battle-three-fallen.jpg");

        Assert.Equal([false, true, true, true], state.Slots.Select(s => s.Dead));
        Assert.InRange(state.Slots[0].Hp!.Value, 0.25, 0.33);
        Assert.All(state.Slots.Skip(1), s => Assert.Null(s.Combo));
    }

    [Fact]
    public async Task Whole_squad_fallen_still_counts_as_the_panel_on_screen()
    {
        var state = await ReadAsync("battle-squad-wiped.jpg");

        Assert.True(state.PanelVisible);
        Assert.All(state.Slots, s => Assert.True(s.Dead));
        Assert.Equal(515, state.PingMs);
    }

    private static void AssertCooling(PartySlot slot, double expected)
    {
        Assert.False(slot.Combo!.Ready);
        Assert.InRange(slot.Combo.Progress, expected - 0.04, expected + 0.04);
    }
}
