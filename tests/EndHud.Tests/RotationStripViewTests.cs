using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// What the strip shows: the current phase's steps, all alike. EndHud can't see which button the
/// player pressed, so it doesn't claim to know the current step: no marker, nothing greyed out as
/// done, no reasons why "this" step isn't ready.
/// </summary>
public class RotationStripViewTests
{
    private static readonly Rotation Rotation = new(Rotation.CurrentFormat, "r", "r", ["Laevatain", "Camille", "Arcane", "Ardelia"],
    [
        new RotationPhase("charge", "充能轴", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Camille", StepAction.Combo), new RotationStep("Arcane", StepAction.Combo)]),
        new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)], [new UltimateCondition("Laevatain", true)]),
    ]);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_step_looks_the_same_and_no_reasons_are_given(bool ready)
    {
        var played = Rotation.Played();
        var position = new RotationPosition(played[1], ready,
            [new ConditionCheck(CheckKind.OwnAbility, null, ready)]);

        var view = RotationStripView.Build(Rotation, position, null, null, (_, _) => null, _ => null);

        Assert.Equal("充能轴 I", view.Phase);
        Assert.Equal(3 + 2, view.Steps.Count); // the phase, then the first two of the next one in the flow (itself again)
        Assert.All(view.Steps, s => Assert.Equal(StripStepState.Upcoming, s.State));
        Assert.Empty(view.Reasons);
    }

    [Fact]
    public void A_phase_entered_by_its_condition_is_the_one_shown()
    {
        var played = Rotation.Played();
        var view = RotationStripView.Build(Rotation, new RotationPosition(played[3], true, []), null, null, (_, _) => null, _ => null);

        Assert.Equal("爆发轴 II", view.Phase);
        Assert.All(view.Steps, s => Assert.Equal(StripStepState.Upcoming, s.State));
    }
}
