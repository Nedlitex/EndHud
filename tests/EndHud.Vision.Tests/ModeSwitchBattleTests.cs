using EndHud.Vision;
using EndHud.Vision.Input;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The battle HUD only in an actual battle (owner, 2026-10-24: "the battle HUD should not show in 探索
/// or 工业 mode (see top left), should only show in the actual battle"): while the top-left mode switch
/// shows with its plain icon (<see cref="PartyState.OutOfBattle"/>), every battle overlay's rule says
/// "not in battle", whatever the squad panel shows; the switch with its red fight mark is a battle.
/// A single misread of the switch changes nothing (<see cref="PartyTracker"/>).
/// </summary>
public class ModeSwitchBattleTests
{
    private static readonly Frame NoPortrait = new(1, 1, new byte[4]);

    /// <summary>The squad panel as in a battle: combo bars and the skill-point bar.</summary>
    private static PartyState BattlePanel(ModePillReading pill, ComboReading? combo = null) => new(
        Enumerable.Range(1, 4).Select(n => new PartySlot(n, null, 1, "", NoPortrait, combo ?? new ComboReading(1, true))
        { BattleSkill = BattleSkillState.Available }).ToList(), PanelMarks: 8)
    { SkillPoints = 3, ModePill = pill };

    [Fact]
    public void The_battle_panel_under_the_exploring_switch_is_not_combat()
    {
        Assert.True(BattlePanel(ModePillReading.NotSeen).InCombat);
        Assert.True(BattlePanel(ModePillReading.Fighting).InCombat);
        Assert.False(BattlePanel(ModePillReading.OutOfBattle).InCombat);
    }

    [Fact]
    public void The_strip_gauges_and_alerts_go_down_at_once_when_the_switch_shows()
    {
        var visibility = new StripVisibility();
        Assert.True(visibility.Update(BattlePanel(ModePillReading.NotSeen)));
        // Not after the two seconds a battle's missing signs are waited out: the switch says it is over.
        Assert.False(visibility.Update(BattlePanel(ModePillReading.OutOfBattle)));
        Assert.False(visibility.Update(BattlePanel(ModePillReading.OutOfBattle)));
    }

    [Fact]
    public void A_fight_in_the_open_world_shows_the_strip()
    {
        var visibility = new StripVisibility();
        Assert.False(visibility.Update(BattlePanel(ModePillReading.OutOfBattle)));
        Assert.True(visibility.Update(BattlePanel(ModePillReading.Fighting)));
    }

    [Fact]
    public void No_combo_hint_while_the_switch_shows()
    {
        var hints = new ComboHints { ShowFor = TimeSpan.FromSeconds(3) };
        hints.Update(BattlePanel(ModePillReading.OutOfBattle, new ComboReading(0.2, false)), TimeSpan.FromSeconds(0));
        hints.Update(BattlePanel(ModePillReading.OutOfBattle, new ComboReading(0.6, false)), TimeSpan.FromSeconds(1));

        Assert.Empty(hints.Update(BattlePanel(ModePillReading.OutOfBattle), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Combo_hints_in_an_open_world_fight_as_in_any_battle()
    {
        var hints = new ComboHints { ShowFor = TimeSpan.FromSeconds(3) };
        hints.Update(BattlePanel(ModePillReading.Fighting, new ComboReading(0.2, false)), TimeSpan.FromSeconds(0));
        hints.Update(BattlePanel(ModePillReading.Fighting, new ComboReading(0.6, false)), TimeSpan.FromSeconds(1));

        Assert.Equal([1, 2, 3, 4], hints.Update(BattlePanel(ModePillReading.Fighting), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void A_battle_skill_key_while_the_switch_shows_is_not_a_cast()
    {
        var press = new SkillPress(1, TimeSpan.FromSeconds(1));

        Assert.Equal(PressVerdict.NotInBattle, SkillPressJudge.Judge(BattlePanel(ModePillReading.OutOfBattle), [press]).Single().Verdict);
        Assert.Equal(PressVerdict.Counted, SkillPressJudge.Judge(BattlePanel(ModePillReading.Fighting), [press]).Single().Verdict);
    }

    [Fact]
    public void One_misread_of_the_switch_changes_nothing_two_in_a_row_do()
    {
        var tracker = new PartyTracker();
        Assert.Equal(ModePillReading.NotSeen, tracker.Track(BattlePanel(ModePillReading.NotSeen)).ModePill);
        // An effect over the corner reads as the switch once: still a battle.
        var once = tracker.Track(BattlePanel(ModePillReading.OutOfBattle));
        Assert.Equal(ModePillReading.NotSeen, once.ModePill);
        Assert.True(once.InCombat);
        Assert.Equal(ModePillReading.NotSeen, tracker.Track(BattlePanel(ModePillReading.NotSeen)).ModePill);
        // Two in a row: out of battle.
        tracker.Track(BattlePanel(ModePillReading.OutOfBattle));
        var twice = tracker.Track(BattlePanel(ModePillReading.OutOfBattle));
        Assert.Equal(ModePillReading.OutOfBattle, twice.ModePill);
        Assert.False(twice.InCombat);
        // And back the same way: one reading without it keeps it.
        Assert.Equal(ModePillReading.OutOfBattle, tracker.Track(BattlePanel(ModePillReading.Fighting)).ModePill);
        Assert.Equal(ModePillReading.Fighting, tracker.Track(BattlePanel(ModePillReading.Fighting)).ModePill);
    }

    [Fact]
    public void The_switch_is_followed_while_the_panel_is_out_of_sight()
    {
        var tracker = new PartyTracker();
        tracker.Track(BattlePanel(ModePillReading.NotSeen));
        // The panel hidden (a menu over it), the switch read twice: the kept reading carries it.
        var hidden = new PartyState([], PanelMarks: 0) { ModePill = ModePillReading.OutOfBattle };
        tracker.Track(hidden);
        var kept = tracker.Track(hidden);

        Assert.Equal(2, kept.ReadingsSinceSeen);
        Assert.Equal(ModePillReading.OutOfBattle, kept.ModePill);
        Assert.False(kept.InCombat);
    }
}
