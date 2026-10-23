using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// "Ultimate active" (<see cref="UltimateActiveCondition"/>), in rotations and alerts alike (both go
/// through <see cref="RotationTracker.Evaluate"/>). The time bar is not the controlled operator's
/// (Fixtures/lasting-ultimate-background): Zhuang Fangyi's ultimate cast from the background runs
/// under Perlica's HP. So it holds for whoever's lasting ultimate is running
/// (<see cref="PartyState.RunningUltimates"/>, the overlay's <see cref="LastingUltimates"/>), not for
/// the controlled operator.
/// </summary>
public class UltimateActiveConditionTests
{
    private static readonly string[] Squad = ["Zhuang_Fangyi", "Perlica", "Arcane", "Liino"];
    private static readonly BarColour Gold = new(0xF0, 0xD2, 0x33);

    private static readonly StateCondition ZhuangActive = new UltimateActiveCondition("Zhuang_Fangyi", true);
    private static readonly StateCondition ZhuangOver = new UltimateActiveCondition("Zhuang_Fangyi", false);

    /// <summary>Perlica (slot 2) controlled, as on test_zhuang_background1.mp4; Zhuang's ultimate spent.</summary>
    private static PartyState Reading(double? bar, BarColour? colour, IReadOnlyDictionary<string, LastingUltimateView>? running, double? skillPoints = 1.5)
    {
        var slots = Squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Ultimate: id == "Zhuang_Fangyi" ? new UltimateReading(false, 0) : new UltimateReading(true, 1),
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: 8)
        {
            ActiveSlot = 2,
            SkillPoints = skillPoints,
            UltimateTimeLeft = bar,
            UltimateTimeColour = colour,
            RunningUltimates = running,
        };
    }

    private static Dictionary<string, LastingUltimateView> Running(params LastingUltimateView[] views) => views.ToDictionary(v => v.OperatorId);

    [Fact]
    public void Zhuangs_ultimate_is_active_while_it_runs_with_Perlica_controlled()
    {
        var onTheBar = Reading(0.6, Gold, Running(new LastingUltimateView("Zhuang_Fangyi", 0.6, false)));

        Assert.Equal(true, RotationTracker.Evaluate(ZhuangActive, onTheBar, PhaseHistory.None));
        Assert.Equal(false, RotationTracker.Evaluate(ZhuangOver, onTheBar, PhaseHistory.None));
    }

    /// <summary>Carried on in the background, off the bar (another one is shown there, or it's hidden a moment): still running.</summary>
    [Fact]
    public void Carried_on_off_the_bar_it_is_still_active()
    {
        var carried = Reading(null, null, Running(new LastingUltimateView("Zhuang_Fangyi", 0.45, true)));

        Assert.Equal(true, RotationTracker.Evaluate(ZhuangActive, carried, PhaseHistory.None));
    }

    [Fact]
    public void Not_running_it_is_over()
    {
        var none = Reading(null, null, Running());

        Assert.Equal(false, RotationTracker.Evaluate(ZhuangActive, none, PhaseHistory.None));
        Assert.Equal(true, RotationTracker.Evaluate(ZhuangOver, none, PhaseHistory.None));
    }

    /// <summary>A reading nobody worked the running ones out for: from this reading alone, by the bar's colour.</summary>
    [Fact]
    public void Without_the_running_ones_worked_out_the_bars_colour_tells()
    {
        Assert.Equal(true, RotationTracker.Evaluate(ZhuangActive, Reading(0.6, Gold, null), PhaseHistory.None));
        Assert.Equal(false, RotationTracker.Evaluate(ZhuangActive, Reading(null, null, null), PhaseHistory.None));
    }

    /// <summary>With the bottom HUD out of sight (no skill points read) it can't be told.</summary>
    [Fact]
    public void With_the_bottom_HUD_out_of_sight_it_cannot_be_told()
    {
        Assert.Null(RotationTracker.Evaluate(ZhuangActive, Reading(null, null, Running(), skillPoints: null), PhaseHistory.None));
    }

    /// <summary>
    /// An operator whose ultimate isn't known to last with the bar: no bar means not running; a bar
    /// that isn't hers by colour could still be hers (the teal one), so it can't be told.
    /// </summary>
    [Fact]
    public void For_an_operator_not_known_to_have_a_lasting_ultimate_a_bar_cannot_tell()
    {
        StateCondition perlicaActive = new UltimateActiveCondition("Perlica", true);

        Assert.Equal(false, RotationTracker.Evaluate(perlicaActive, Reading(null, null, Running()), PhaseHistory.None));
        Assert.Null(RotationTracker.Evaluate(perlicaActive, Reading(0.6, Gold, Running(new LastingUltimateView("Zhuang_Fangyi", 0.6, false))), PhaseHistory.None));
    }

    /// <summary>An alert on Zhuang's ultimate pops while it runs in the background (alerts go through the same evaluator).</summary>
    [Fact]
    public void An_alert_on_Zhuangs_ultimate_pops_while_Perlica_is_controlled()
    {
        var monitor = new AlertMonitor();
        var rule = new AlertRule("zhuang-on", [ZhuangActive], "");

        Assert.Empty(monitor.Update(Reading(null, null, Running()), [rule]));
        Assert.Equal("zhuang-on", Assert.Single(monitor.Update(Reading(0.98, Gold, Running(new LastingUltimateView("Zhuang_Fangyi", 0.98, false))), [rule])).Id);
    }

    /// <summary>A rotation's phase on Zhuang's ultimate is shown while it runs in the background.</summary>
    [Fact]
    public void A_rotation_phase_on_Zhuangs_ultimate_is_shown_while_Perlica_is_controlled()
    {
        var selector = new PhaseSelector(new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("burst", "Burst", [new RotationStep("Perlica", StepAction.Combo)], [ZhuangActive]),
            new RotationPhase("charge", "Charge", [new RotationStep("Arcane", StepAction.Combo)]),
        ]));

        Assert.Equal("Charge II", selector.Update(Reading(null, null, Running())).Step.PhaseLabel);
        Assert.Equal("Burst I", selector.Update(Reading(0.7, Gold, Running(new LastingUltimateView("Zhuang_Fangyi", 0.7, false)))).Step.PhaseLabel);
    }
}
