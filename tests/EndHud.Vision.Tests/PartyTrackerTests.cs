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

    private static PartyState Panel(params PartySlot[] slots) => new(slots, PanelMarks: 8);

    private static PartyState NoPanel() => new([Slot(1, hp: null), Slot(2, hp: null), Slot(3, hp: null), Slot(4, hp: null)]);

    private static PartyState InCombat() => Panel(
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

    /// <summary>
    /// With keyboard and mouse, scenery in an empty place can pass for an operator for a reading or
    /// two (or an effect can hide one): a squad of two read as three moves every ultimate circle to
    /// the wrong operator. A new size is taken only once it repeats.
    /// </summary>
    [Fact]
    public void A_larger_squad_read_only_briefly_is_not_taken()
    {
        var tracker = new PartyTracker();
        var two = Panel(Slot(1, ultimate: new UltimateReading(true, 1)), Slot(2, ultimate: new UltimateReading(false, 0.8)), Empty(3), Empty(4));
        // Read as three: each ultimate from the circle one place to the left.
        var three = Panel(Slot(1, ultimate: new UltimateReading(false, 0)), Slot(2, ultimate: new UltimateReading(true, 1)), Slot(3, ultimate: new UltimateReading(false, 0.8)), Empty(4));
        var seen = tracker.Track(two);

        for (int i = 1; i < PartyTracker.MoreOperatorsReadings; i++)
            Assert.Equal(seen with { ReadingsSinceSeen = i }, tracker.Track(three));
        Assert.Equal(0, tracker.Track(two).ReadingsSinceSeen);

        // Interrupted, the count starts again; read enough times in a row, the new size is taken.
        for (int i = 1; i < PartyTracker.MoreOperatorsReadings; i++)
            Assert.Equal(2, tracker.Track(three).SquadSize);
        var changed = tracker.Track(three);
        Assert.Equal(3, changed.SquadSize);
        Assert.Equal(0, changed.ReadingsSinceSeen);
    }

    /// <summary>An effect hiding the last operator's marks and HP bar: a full squad read as three, for a moment.</summary>
    [Fact]
    public void A_smaller_squad_read_only_briefly_is_not_taken()
    {
        var tracker = new PartyTracker();
        var seen = tracker.Track(InCombat());
        var three = Panel(Slot(1), Slot(2), Slot(3), Empty(4));

        for (int i = 1; i < PartyTracker.FewerOperatorsReadings; i++)
            Assert.Equal(seen with { ReadingsSinceSeen = i }, tracker.Track(three));
        Assert.Equal(3, tracker.Track(three).SquadSize);
    }

    private static PartySlot Empty(int number) => new(number, null, null, "", NoPortrait) { Empty = true };

    [Fact]
    public void Nothing_to_keep_before_the_panel_was_ever_seen()
    {
        Assert.False(new PartyTracker().Track(NoPanel()).PanelVisible);
    }

    [Fact]
    public void Unreadable_hp_combo_and_ultimate_keep_their_last_values()
    {
        var tracker = new PartyTracker();
        tracker.Track(InCombat());

        var state = tracker.Track(Panel(Slot(1, hp: null), Slot(2, hp: null), Slot(3), Slot(4)));

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
        tracker.Track(InCombat());
        tracker.Track(Panel(Slot(1, hp: null), Slot(2), Slot(3), Slot(4)));

        var state = tracker.Track(Panel(Slot(1, hp: 0.5, combo: new ComboReading(1, true), ultimate: new UltimateReading(true, 1)),
            Slot(2), Slot(3), Slot(4)));

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

        var state = tracker.Track(Panel(Slot(1, level: 90), Slot(2, level: 90), Slot(3, level: 90), Slot(4, level: 90)));

        Assert.All(state.Slots, s => Assert.Null(s.Combo));
    }

    [Fact]
    public void A_revived_operator_starts_fresh()
    {
        var tracker = new PartyTracker();
        tracker.Track(InCombat());
        tracker.Track(Panel(Slot(1, hp: 0, dead: true), Slot(2), Slot(3), Slot(4)));

        var state = tracker.Track(Panel(Slot(1, hp: 0.5), Slot(2), Slot(3), Slot(4)));

        Assert.False(state.Slots[0].Dead);
        Assert.Null(state.Slots[0].Combo);
        Assert.Null(state.Slots[0].Ultimate);
    }

    private static PartyState Running(double? left, int controlled = 1) => InCombat() with { ActiveSlot = controlled, SkillPoints = 1.5, UltimateTimeLeft = left };

    /// <summary>
    /// An effect over the time bar loses a reading now and then (gaps of up to 3.5 s in the
    /// recordings): the last value is kept through it, while the same operator is controlled.
    /// </summary>
    [Fact]
    public void A_running_ultimates_time_is_kept_through_a_short_gap()
    {
        var tracker = new PartyTracker();
        tracker.Track(Running(0.6));

        for (int i = 0; i < PartyTracker.UltimateGapReadings; i++) Assert.Equal(0.6, tracker.Track(Running(null)).UltimateTimeLeft);
        Assert.Null(tracker.Track(Running(null)).UltimateTimeLeft); // longer: it ended
    }

    /// <summary>The bar's colour (whose it is) is kept with its value, and goes with it.</summary>
    [Fact]
    public void A_kept_time_keeps_its_colour()
    {
        var gold = new BarColour(0xF0, 0xD2, 0x33);
        var tracker = new PartyTracker();
        tracker.Track(Running(0.6) with { UltimateTimeColour = gold });

        Assert.Equal(gold, tracker.Track(Running(null)).UltimateTimeColour);
        for (int i = 1; i < PartyTracker.UltimateGapReadings; i++) tracker.Track(Running(null));
        Assert.Null(tracker.Track(Running(null)).UltimateTimeColour);
    }

    [Fact]
    public void A_nearly_out_ultimate_or_another_controlled_operator_ends_it_at_once()
    {
        var nearlyOut = new PartyTracker();
        nearlyOut.Track(Running(0.03));
        Assert.Null(nearlyOut.Track(Running(null)).UltimateTimeLeft);

        var switched = new PartyTracker();
        switched.Track(Running(0.6, controlled: 1));
        Assert.Null(switched.Track(Running(null, controlled: 2)).UltimateTimeLeft);
    }
}
