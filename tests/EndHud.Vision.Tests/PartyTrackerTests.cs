using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// When something can't be read, the HUD keeps showing the last value that was read rather
/// than going blank ("—" or "squad panel not visible").
/// </summary>
public class PartyTrackerTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    private static PartySlot Slot(int number, double? hp = 1, int? level = null, ComboReading? combo = null,
        UltimateReading? ultimate = null, bool dead = false) =>
        new(number, level, hp, "", NoPortrait, combo, ultimate, dead);

    private static PartyState Panel(int? ping, params PartySlot[] slots) => new(slots, ping, "", PanelMarks: 8);

    private static PartyState NoPanel() => new([Slot(1, hp: null), Slot(2, hp: null), Slot(3, hp: null), Slot(4, hp: null)], null, "");

    private static PartyState InCombat(int? ping = 120) => Panel(ping,
        Slot(1, hp: 0.8, combo: new ComboReading(0.4, false), ultimate: new UltimateReading(false, 0.3)),
        Slot(2, hp: 0.6, combo: new ComboReading(1, true), ultimate: new UltimateReading(true, 1)),
        Slot(3), Slot(4));

    [Fact]
    public void Panel_out_of_sight_keeps_the_last_reading_however_long()
    {
        var tracker = new PartyTracker();
        var seen = tracker.Track(InCombat());

        for (int i = 1; i <= 10; i++)
            Assert.Equal(seen with { ReadingsSinceSeen = i }, tracker.Track(NoPanel()));
        Assert.Equal(0, tracker.Track(InCombat()).ReadingsSinceSeen);
    }

    [Fact]
    public void Nothing_to_keep_before_the_panel_was_ever_seen()
    {
        Assert.False(new PartyTracker().Track(NoPanel()).PanelVisible);
    }

    [Fact]
    public void Ping_read_in_a_menu_is_kept_once_the_panel_appears()
    {
        var tracker = new PartyTracker();
        tracker.Track(NoPanel() with { PingMs = 165 });

        Assert.Equal(165, tracker.Track(InCombat(ping: null)).PingMs);
    }

    [Fact]
    public void Unreadable_hp_combo_ultimate_and_ping_keep_their_last_values()
    {
        var tracker = new PartyTracker();
        tracker.Track(InCombat(ping: 164));

        var state = tracker.Track(Panel(null, Slot(1, hp: null), Slot(2, hp: null), Slot(3), Slot(4)));

        Assert.Equal(164, state.PingMs);
        Assert.Equal(0.8, state.Slots[0].Hp);
        Assert.Equal(new ComboReading(0.4, false), state.Slots[0].Combo);
        Assert.Equal(new UltimateReading(false, 0.3), state.Slots[0].Ultimate);
        Assert.Equal(0.6, state.Slots[1].Hp);
        Assert.True(state.Slots[1].Combo!.Ready);
        Assert.True(state.Slots[1].Ultimate!.Ready);
    }

    [Fact]
    public void New_readings_replace_the_kept_ones()
    {
        var tracker = new PartyTracker();
        tracker.Track(InCombat(ping: 164));
        tracker.Track(Panel(null, Slot(1, hp: null), Slot(2), Slot(3), Slot(4)));

        var state = tracker.Track(Panel(200, Slot(1, hp: 0.5, combo: new ComboReading(1, true), ultimate: new UltimateReading(true, 1)),
            Slot(2), Slot(3), Slot(4)));

        Assert.Equal(200, state.PingMs);
        Assert.Equal(0.5, state.Slots[0].Hp);
        Assert.True(state.Slots[0].Combo!.Ready);
        Assert.True(state.Slots[0].Ultimate!.Ready);
    }

    [Fact]
    public void Level_on_screen_means_out_of_combat_so_no_combo_is_kept()
    {
        // The level text takes the combo bar's place outside combat: the bar is gone, not unreadable.
        var tracker = new PartyTracker();
        tracker.Track(InCombat());

        var state = tracker.Track(Panel(120, Slot(1, level: 90), Slot(2, level: 90), Slot(3, level: 90), Slot(4, level: 90)));

        Assert.All(state.Slots, s => Assert.Null(s.Combo));
    }

    [Fact]
    public void A_revived_operator_starts_fresh()
    {
        var tracker = new PartyTracker();
        tracker.Track(InCombat());
        tracker.Track(Panel(120, Slot(1, hp: 0, dead: true), Slot(2), Slot(3), Slot(4)));

        var state = tracker.Track(Panel(120, Slot(1, hp: 0.5), Slot(2), Slot(3), Slot(4)));

        Assert.False(state.Slots[0].Dead);
        Assert.Null(state.Slots[0].Combo);
        Assert.Null(state.Slots[0].Ultimate);
    }
}
