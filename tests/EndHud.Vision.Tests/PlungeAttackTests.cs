using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// 下落攻击 (plunge attack) steps: drawn only, like basic and heavy attacks, and, as those, they may be
/// done by whoever is controlled at the time (<see cref="RotationStep.Controlled"/>, "当前操控"),
/// as the 祀 rotation's "+1冰" plunge is. Squad slots: Laevatain (1), Camille (2), Arcane (3), Ardelia (4).
/// </summary>
public class PlungeAttackTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];
    private const int Ardelia = 4;

    private static Rotation Of(params RotationStep[] steps) => new(Rotation.CurrentFormat, "r", "r", Squad, [new RotationPhase("p", "P", steps)]);

    private static PartyState Plain()
    {
        var slots = Enumerable.Range(0, 4).Select(i => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Combo: new ComboReading(1, true),
            Ultimate: new UltimateReading(false, 0.2),
            Operator: new OperatorMatch(new OperatorInfo(Squad[i], Squad[i], Squad[i], Squad[i] + ".png"), 1, true))
        {
            BattleSkill = BattleSkillState.Available,
        }).ToList();
        return new PartyState(slots, PanelMarks: 8) { SkillPoints = 2, ActiveSlot = 1 };
    }

    [Fact]
    public void A_plunge_attack_is_display_only_and_neutral()
    {
        Assert.True(StepAction.PlungeAttack.IsDisplayOnly());
        var follower = new StepFollower(Of(new RotationStep(RotationStep.Controlled, StepAction.PlungeAttack), new RotationStep("Ardelia", StepAction.BattleSkill),
            new RotationStep("Laevatain", StepAction.BattleSkill)));

        var first = follower.Update(0, null, Plain(), [], PhaseHistory.None);
        // Never seen: neutral, in its place; the chevron goes to the first step that can be seen used.
        Assert.Equal([StepMark.Neutral, StepMark.Current, StepMark.Unlit], first.Marks);
        Assert.True(first.Ready); // Ardelia's battle skill, with 2 points
        Assert.Equal([StepMark.Neutral, StepMark.Lit, StepMark.Current], follower.Update(0, Plain(), Plain(), Casts.Seen([Ardelia]), PhaseHistory.None).Marks);
    }

    [Fact]
    public void Whoever_is_controlled_is_for_attacks_only()
    {
        Assert.Empty(Of(new RotationStep(RotationStep.Controlled, StepAction.PlungeAttack), new RotationStep(RotationStep.Controlled, StepAction.BasicAttack),
            new RotationStep(RotationStep.Controlled, StepAction.HeavyAttack)).Problems());
        Assert.Equal([new RotationProblem(RotationProblemKind.ControlledOperatorOnlyForAttacks, "P", 1)],
            Of(new RotationStep(RotationStep.Controlled, StepAction.BattleSkill)).Problems());
    }

    [Fact]
    public void A_plunge_attack_or_the_controlled_operator_needs_format_9()
    {
        Assert.Equal(9, Rotation.FormatNeeded(Of(new RotationStep("Ardelia", StepAction.PlungeAttack)).Phases, false));
        Assert.Equal(9, Rotation.FormatNeeded(Of(new RotationStep(RotationStep.Controlled, StepAction.BasicAttack)).Phases, false));
        Assert.Equal(4, Rotation.FormatNeeded(Of(new RotationStep("Ardelia", StepAction.BasicAttack)).Phases, false));

        var rotation = Of(new RotationStep(RotationStep.Controlled, StepAction.PlungeAttack, Note: "+1冰")) with { Format = 9 };
        string json = RotationFormat.ToJson(rotation);
        Assert.Contains("\"action\": \"PlungeAttack\"", json);
        Assert.Contains("\"operatorId\": \"controlled\"", json);
        Assert.Equal(rotation.Phases[0].Steps[0], RotationFormat.FromJson(json).Phases[0].Steps[0] with { Expect = [] });
    }
}
