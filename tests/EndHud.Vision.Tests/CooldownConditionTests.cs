using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Cooldowns as conditions: a combo charged to (at least / at most) N% (<see cref="ComboChargeCondition"/>),
/// and a battle skill ready or cooling (<see cref="BattleSkillCondition"/>; its countdown isn't read).
/// </summary>
public class CooldownConditionTests
{
    private static PartyState Reading(ComboReading? combo = null, BattleSkillState? skill = null) => new(
        [new PartySlot(1, null, 1.0, "", new Frame(1, 1, new byte[4]), Combo: combo,
            Operator: new OperatorMatch(new OperatorInfo("Liino", "Liino", "梨诺", "Liino.png"), 1, true)) { BattleSkill = skill }], PanelMarks: 8);

    [Fact]
    public void Combo_charge_compares_the_bar_and_ready_is_100()
    {
        var atLeast = new ComboChargeCondition("Liino", Comparison.AtLeast, 70);
        Assert.True(RotationTracker.Evaluate(atLeast, Reading(new ComboReading(0.75, false))));
        Assert.True(RotationTracker.Evaluate(atLeast, Reading(new ComboReading(1, true))));
        Assert.False(RotationTracker.Evaluate(atLeast, Reading(new ComboReading(0.4, false))));
        Assert.Null(RotationTracker.Evaluate(atLeast, Reading()));

        var below = new ComboChargeCondition("Liino", Comparison.AtMost, 50);
        Assert.True(RotationTracker.Evaluate(below, Reading(new ComboReading(0.4, false))));
        Assert.False(RotationTracker.Evaluate(below, Reading(new ComboReading(1, true))));
    }

    [Fact]
    public void Battle_skill_ready_or_cooling()
    {
        Assert.True(RotationTracker.Evaluate(new BattleSkillCondition("Liino", true), Reading(skill: BattleSkillState.Available)));
        Assert.False(RotationTracker.Evaluate(new BattleSkillCondition("Liino", true), Reading(skill: BattleSkillState.Cooling)));
        Assert.True(RotationTracker.Evaluate(new BattleSkillCondition("Liino", false), Reading(skill: BattleSkillState.Cooling)));
        Assert.Null(RotationTracker.Evaluate(new BattleSkillCondition("Liino", true), Reading()));
    }

    [Fact]
    public void Both_round_trip_through_a_file_and_need_a_squad_operator()
    {
        StateCondition[] when = [new ComboChargeCondition("Liino", Comparison.AtLeast, 70), new BattleSkillCondition("Liino", false)];
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", ["Arcane", "Liino", "Perlica", "Typhoeus"],
            [new RotationPhase("p", "充能轴", [new RotationStep("Liino", StepAction.Combo)], when)]);

        Assert.Equal(when, RotationFormat.FromJson(RotationFormat.ToJson(rotation)).Phases[0].When);
        var outside = rotation with { Squad = ["Arcane", "Perlica", "Typhoeus", "Antal"] };
        Assert.Contains(outside.Problems(), p => p.Kind == RotationProblemKind.ConditionOperatorNotInSquad);
    }
}
