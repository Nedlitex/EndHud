using EndHud.Vision;
using EndHud.Vision.Input;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Did a press cast the skill? Judged against the reading taken before it: in battle, the slot's
/// battle-skill button not cooling, a skill point left once the presses already counted have spent
/// theirs, and one cast per slot between two readings (a mashed key casts once).
/// </summary>
public sealed class SkillPressJudgeTests
{
    /// <summary>A reading in battle: skill points (null = out of battle), each slot's battle-skill button.</summary>
    private sealed record Reading(double? SkillPoints = 2.0)
    {
        public BattleSkillState?[] Skill { get; init; } = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available];

        public PartyState ToState()
        {
            var slots = Enumerable.Range(0, 4).Select(i => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
                Combo: new ComboReading(0.5, false), Ultimate: new UltimateReading(false, 0.2))
            {
                BattleSkill = Skill[i],
            }).ToList();
            return new PartyState(slots, PanelMarks: 8) { SkillPoints = SkillPoints, ActiveSlot = 1 };
        }
    }

    private static SkillPress Press(int slot, double seconds = 1) => new(slot, TimeSpan.FromSeconds(seconds));

    private static PressVerdict[] Verdicts(PartyState? before, params SkillPress[] presses) =>
        SkillPressJudge.Judge(before, presses).Select(j => j.Verdict).ToArray();

    [Fact]
    public void A_press_with_a_point_and_the_button_ready_counts() =>
        Assert.Equal([PressVerdict.Counted], Verdicts(new Reading().ToState(), Press(2)));

    [Fact]
    public void A_press_while_the_button_cools_does_not_count()
    {
        var before = new Reading { Skill = [BattleSkillState.Available, BattleSkillState.Cooling, BattleSkillState.Available, BattleSkillState.Available] }.ToState();
        Assert.Equal([PressVerdict.Cooling], Verdicts(before, Press(2)));
    }

    [Fact]
    public void A_press_without_a_skill_point_does_not_count() =>
        Assert.Equal([PressVerdict.NoSkillPoints], Verdicts(new Reading(SkillPoints: 0.6).ToState(), Press(2)));

    [Fact]
    public void Mashing_one_key_between_two_readings_casts_once()
    {
        var verdicts = Verdicts(new Reading(SkillPoints: 3).ToState(), Press(2, 1.0), Press(2, 1.05), Press(2, 1.1), Press(2, 1.15), Press(2, 1.2));
        Assert.Equal([PressVerdict.Counted, PressVerdict.Repeat, PressVerdict.Repeat, PressVerdict.Repeat, PressVerdict.Repeat], verdicts);
    }

    [Fact]
    public void Two_slots_share_the_points_left() =>
        Assert.Equal([PressVerdict.Counted, PressVerdict.NoSkillPoints], Verdicts(new Reading(SkillPoints: 1.4).ToState(), Press(1, 1.0), Press(2, 1.1)));

    /// <summary>Also what typing digits in a chat window or a menu comes to: no skill-point bar, no battle.</summary>
    [Fact]
    public void A_press_out_of_battle_does_not_count()
    {
        Assert.Equal([PressVerdict.NotInBattle], Verdicts(new Reading(SkillPoints: null).ToState(), Press(2)));
        Assert.Equal([PressVerdict.NotInBattle], Verdicts(null, Press(2)));
    }

    [Fact]
    public void With_a_controller_the_button_is_unknown_and_the_points_decide()
    {
        var before = new Reading { Skill = [null, null, null, null] }.ToState();
        Assert.Equal([PressVerdict.Counted], Verdicts(before, Press(3)));
    }

    [Fact]
    public void A_press_for_a_slot_the_squad_lacks_does_not_count()
    {
        var state = new Reading().ToState();
        var three = state with { Slots = [.. state.Slots.Take(3), state.Slots[3] with { Empty = true }] };
        Assert.Equal([PressVerdict.EmptySlot], Verdicts(three, Press(4)));
    }

    [Fact]
    public void Presses_are_judged_oldest_first()
    {
        var judged = SkillPressJudge.Judge(new Reading(SkillPoints: 1.2).ToState(), [Press(2, 1.1), Press(1, 1.0)]);
        Assert.Equal([(1, PressVerdict.Counted), (2, PressVerdict.NoSkillPoints)], judged.Select(j => (j.Press.Slot, j.Verdict)));
    }
}
