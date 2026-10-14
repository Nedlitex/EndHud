using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>"An operator's ultimate is charged to at least N%" (<see cref="UltimateChargeCondition"/>): in files, and against readings.</summary>
public class UltimateChargeConditionTests
{
    private static PartyState Reading(UltimateReading? arcane) => new(
        [new PartySlot(1, null, 1.0, "", new Frame(1, 1, new byte[4]), Ultimate: arcane,
            Operator: new OperatorMatch(new OperatorInfo("Arcane", "Arcane", "诀", "Arcane.png"), 1, true))], PanelMarks: 8);

    private static readonly StateCondition AtLeast80 = new UltimateChargeCondition("Arcane", Comparison.AtLeast, 80);

    [Fact]
    public void Holds_at_or_above_the_charge_and_when_ready()
    {
        Assert.True(RotationTracker.Evaluate(AtLeast80, Reading(new UltimateReading(false, 0.85))));
        Assert.True(RotationTracker.Evaluate(AtLeast80, Reading(new UltimateReading(false, 0.80))));
        Assert.True(RotationTracker.Evaluate(AtLeast80, Reading(new UltimateReading(true, 1))));
        Assert.False(RotationTracker.Evaluate(AtLeast80, Reading(new UltimateReading(false, 0.5))));
    }

    [Fact]
    public void Unknown_when_the_ultimate_cant_be_read()
    {
        Assert.Null(RotationTracker.Evaluate(AtLeast80, Reading(null)));
    }

    [Fact]
    public void Below_a_charge_too()
    {
        var below = new UltimateChargeCondition("Arcane", Comparison.AtMost, 79);
        Assert.True(RotationTracker.Evaluate(below, Reading(new UltimateReading(false, 0.5))));
        Assert.False(RotationTracker.Evaluate(below, Reading(new UltimateReading(false, 0.9))));
    }

    [Fact]
    public void Round_trips_through_a_file()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", ["Arcane", "Liino", "Perlica", "Typhoeus"],
            [new RotationPhase("p", "爆发轴", [new RotationStep("Arcane", StepAction.Ultimate)]),
             new RotationPhase("q", "爆发轴", [new RotationStep("Arcane", StepAction.Ultimate)], [AtLeast80])]);

        string json = RotationFormat.ToJson(rotation);
        Assert.Contains("\"ultimateCharge\"", json);
        Assert.Equal(AtLeast80, Assert.Single(RotationFormat.FromJson(json).Phases[1].When));
    }

    [Fact]
    public void An_operator_outside_the_squad_is_a_problem()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", ["Liino", "Perlica", "Typhoeus", "Antal"],
            [new RotationPhase("p", "充能轴", [new RotationStep("Liino", StepAction.Combo)]),
             new RotationPhase("q", "爆发轴", [new RotationStep("Liino", StepAction.Ultimate)], [AtLeast80])]);

        Assert.Contains(rotation.Problems(), p => p.Kind == RotationProblemKind.ConditionOperatorNotInSquad);
    }
}
