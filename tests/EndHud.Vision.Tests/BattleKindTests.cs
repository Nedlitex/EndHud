using EndHud.Vision;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Which kind of battle it is (owner, 2026-10-25: "look at the left top for difference, if there is 撤离 that is
/// battle"), kept by <see cref="PartyTracker"/> from the top left: the evacuate bar (撤离, <see cref="EvacuateBar"/>)
/// is an instance, the mode switch (探索 / 工业, <see cref="ModePill"/>) the open world. The last one seen holds while
/// neither is (an ultimate's cutscene, an on-screen display over the corner, an open-world event's own buttons
/// there), and a new one needs two readings in a row.
/// </summary>
public class BattleKindTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    /// <summary>The squad panel as in a battle, and what the top left shows.</summary>
    private static PartyState Battle(bool evacuate = false, ModePillReading pill = ModePillReading.NotSeen) => new(
        Enumerable.Range(1, 4).Select(n => new PartySlot(n, null, 1, "", NoPortrait, new ComboReading(1, true))).ToList(), PanelMarks: 8)
    { SkillPoints = 3, ModePill = pill, EvacuateBar = evacuate };

    [Fact]
    public void Unknown_until_either_sign_is_seen()
    {
        var tracker = new PartyTracker();
        Assert.Equal(BattleKind.Unknown, tracker.Track(Battle()).BattleKind);
        Assert.Equal(BattleKind.Unknown, tracker.Track(Battle()).BattleKind);
    }

    [Fact]
    public void The_evacuate_bar_twice_in_a_row_is_an_instance()
    {
        var tracker = new PartyTracker();
        Assert.Equal(BattleKind.Unknown, tracker.Track(Battle(evacuate: true)).BattleKind);
        Assert.Equal(BattleKind.Instance, tracker.Track(Battle(evacuate: true)).BattleKind);
    }

    [Theory]
    [InlineData(ModePillReading.OutOfBattle)]
    [InlineData(ModePillReading.Fighting)]
    public void The_mode_switch_twice_in_a_row_is_the_open_world(ModePillReading pill)
    {
        var tracker = new PartyTracker();
        tracker.Track(Battle(pill: pill));
        Assert.Equal(BattleKind.OpenWorld, tracker.Track(Battle(pill: pill)).BattleKind);
    }

    [Fact]
    public void An_instance_stays_one_while_the_corner_is_covered_or_empty()
    {
        var tracker = new PartyTracker();
        tracker.Track(Battle(evacuate: true));
        tracker.Track(Battle(evacuate: true));
        // A minute of readings without it (an on-screen display over the corner, ultimates' cutscenes).
        for (int i = 0; i < 240; i++) Assert.Equal(BattleKind.Instance, tracker.Track(Battle()).BattleKind);
    }

    [Fact]
    public void The_open_world_stays_so_through_an_open_world_event_without_the_switch()
    {
        // An open-world challenge draws its own buttons at the top left, no switch and no evacuate bar
        // (the owner's screenshot of a fight at 重度能量淤积点): it was the open world when last seen.
        var tracker = new PartyTracker();
        tracker.Track(Battle(pill: ModePillReading.OutOfBattle));
        tracker.Track(Battle(pill: ModePillReading.OutOfBattle));
        for (int i = 0; i < 240; i++) Assert.Equal(BattleKind.OpenWorld, tracker.Track(Battle()).BattleKind);
    }

    [Fact]
    public void One_misread_changes_nothing()
    {
        var tracker = new PartyTracker();
        tracker.Track(Battle(pill: ModePillReading.Fighting));
        tracker.Track(Battle(pill: ModePillReading.Fighting));
        // Something white over the corner reads as the bar once, between readings of nothing.
        Assert.Equal(BattleKind.OpenWorld, tracker.Track(Battle(evacuate: true)).BattleKind);
        Assert.Equal(BattleKind.OpenWorld, tracker.Track(Battle()).BattleKind);
        Assert.Equal(BattleKind.OpenWorld, tracker.Track(Battle(evacuate: true)).BattleKind);
        // Twice in a row: an instance; and back to the open world the same way.
        Assert.Equal(BattleKind.Instance, tracker.Track(Battle(evacuate: true)).BattleKind);
        Assert.Equal(BattleKind.Instance, tracker.Track(Battle(pill: ModePillReading.OutOfBattle)).BattleKind);
        Assert.Equal(BattleKind.OpenWorld, tracker.Track(Battle(pill: ModePillReading.OutOfBattle)).BattleKind);
    }

    [Fact]
    public void Followed_while_the_squad_panel_is_out_of_sight()
    {
        var tracker = new PartyTracker();
        tracker.Track(Battle());
        // Loading into an instance: no squad panel yet, the evacuate bar already there.
        var loading = new PartyState([], PanelMarks: 0) { EvacuateBar = true };
        tracker.Track(loading);
        var kept = tracker.Track(loading);

        Assert.Equal(2, kept.ReadingsSinceSeen);
        Assert.Equal(BattleKind.Instance, kept.BattleKind);
    }
}
