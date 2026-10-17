using System.Windows;
using System.Windows.Media;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// What the strip shows: the current phase's steps, all alike, then the first two of the phase
/// played next, kept apart from them. EndHud can't see which button the player pressed, so it
/// doesn't claim to know the current step: no marker, nothing greyed out as done, no reasons why
/// "this" step isn't ready.
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
        Assert.Equal(3, view.Steps.Count);
        // Then the first two of the next one in the flow (itself again), apart.
        Assert.Equal("充能轴 I", view.Next!.Phase);
        Assert.Equal(2, view.Next.Steps.Count);
        Assert.All(view.Steps.Concat(view.Next.Steps), s => Assert.Equal(StripStepState.Upcoming, s.State));
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

    /// <summary>
    /// A player's report: a phase with a condition and an ordinary phase, both named 充能轴. The strip
    /// showed the first phase's six steps and the second's first two as one row of eight, while the
    /// editor showed six, so the strip looked unlike the rotation they made.
    /// </summary>
    [Fact]
    public void The_next_phases_steps_are_kept_apart_and_named_even_when_the_phases_share_a_name()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", ["Laevatain", "Camille", "Arcane", "Ardelia"],
        [
            new RotationPhase("a", "充能轴", [new RotationStep("Camille", StepAction.BattleSkill), new RotationStep("Arcane", StepAction.Combo)],
                [new UltimateCondition("Laevatain", false)]),
            new RotationPhase("b", "充能轴", [new RotationStep("Laevatain", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.Combo, WithPrevious: true), new RotationStep("Camille", StepAction.Combo)]),
        ]);

        var view = RotationStripView.ForPhase(rotation, 0, null, (_, _) => null, _ => null);

        Assert.Equal("充能轴 I", view.Phase);
        Assert.Equal(["Rotation.Action.BattleSkill", "Rotation.Action.Combo"], view.Steps.Select(s => s.Action));
        Assert.Equal("充能轴 II", view.Next!.Phase);
        Assert.Equal(["Rotation.Action.BattleSkill", "Rotation.Action.Combo"], view.Next.Steps.Select(s => s.Action));
        Assert.False(view.Next.Steps[0].WithPrevious); // starts the next phase: never overlaps this one's last step
        Assert.True(view.Next.Steps[1].WithPrevious);

        // Drawn after a divider naming it, and dimmed.
        var dimmed = OnSta(() =>
        {
            var strip = RotationStrip.Build(view);
            var texts = Descendants(strip).OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Rotation.Next", texts);
            Assert.Contains("充能轴 II", texts);
            return Descendants(strip).OfType<UIElement>().Count(e => e.Opacity == RotationStrip.NextOpacity);
        });
        Assert.Equal(2, dimmed);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
        return result;
    }
}
