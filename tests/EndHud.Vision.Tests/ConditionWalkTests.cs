using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Every condition of a rotation, groups and "not" opened up (<see cref="ConditionWalk"/>): what the
/// format check and the boss-status switch both rely on.
/// </summary>
public class ConditionWalkTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];

    private static Rotation With(IReadOnlyList<StateCondition> gate, IReadOnlyList<StateCondition>? expect = null) => new(
        Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("charge", "充能轴", [new RotationStep("Ardelia", StepAction.BattleSkill, expect)]),
            new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)], gate),
        ]);

    /// <summary>The overlay's rule before 2026-10-22: only a step's expected state turned the boss's statuses on.</summary>
    private static bool Old(Rotation r) => r.Phases.Any(p => p.Steps.Any(s => s.Expect.Any(c => c is BossStatusCondition)));

    [Fact]
    public void A_status_inside_any_inside_not_inside_a_phase_gate_is_found()
    {
        var cryo = new BossStatusCondition("Cryo Infliction", true);
        var rotation = With([new NotCondition(new AnyCondition([new UltimateCondition("Laevatain", true), cryo]))]);

        Assert.Contains(cryo, ConditionWalk.Of(rotation.Phases));
        Assert.True(ConditionWalk.NeedsBossStatuses(rotation));
    }

    [Fact]
    public void A_rotation_without_a_status_condition_does_not_need_the_statuses()
    {
        var rotation = With([new AllCondition([new UltimateCondition("Laevatain", true), new SkillPointsCondition(Comparison.AtLeast, 2)])],
            [new ComboCondition("Camille", true)]);

        Assert.False(ConditionWalk.NeedsBossStatuses(rotation));
        Assert.False(ConditionWalk.NeedsBossStatuses(null));
    }

    [Fact]
    public void A_status_in_a_step_is_found_too()
    {
        Assert.True(ConditionWalk.NeedsBossStatuses(With([], [new BossStatusCondition("Heat Infliction", true)])));
    }

    /// <summary>BUG2: the strip follows phases' entry conditions, but only steps' statuses were read.</summary>
    [Fact]
    public void Today_rule_misses_a_status_in_a_phase_gate()
    {
        var rotation = With([new AnyCondition([new BossStatusCondition("Cryo Infliction", true), new UltimateCondition("Laevatain", true)])]);

        Assert.False(Old(rotation));
        Assert.True(ConditionWalk.NeedsBossStatuses(rotation));
    }

    [Fact]
    public void Groups_and_nots_are_listed_with_what_they_hold()
    {
        var inner = new SkillPointsCondition(Comparison.Below, 2);
        var not = new NotCondition(inner);
        var all = new AllCondition([not]);

        Assert.Equal([all, not, inner], ConditionWalk.Flatten(all));
        Assert.Equal(Comparison.Below, ConditionWalk.ComparisonOf(inner));
        Assert.Null(ConditionWalk.ComparisonOf(all));
    }
}
