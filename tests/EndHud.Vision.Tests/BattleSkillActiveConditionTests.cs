using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// "Battle skill running" (<see cref="BattleSkillActiveCondition"/>): Si's battle-skill time still
/// running, the purple countdown round her stacks' circle. Read from her battle-skill button
/// (<see cref="SkillRing"/>: the swirl or the crossed-out butterfly while it runs, the butterfly once
/// it is over) and carried across readings the button can't be told by the overlay's
/// <see cref="SkillTimers"/> (<see cref="PartyState.RunningSkills"/>), so the condition and the gauge agree.
/// </summary>
public class BattleSkillActiveConditionTests
{
    private static readonly string[] Squad = ["Si", "Last_Rite", "Arcane", "Tangtang"];

    private static readonly StateCondition SiRunning = new BattleSkillActiveCondition("Si", true);
    private static readonly StateCondition SiOver = new BattleSkillActiveCondition("Si", false);

    /// <summary>Si in slot 1, her button read as <paramref name="icon"/> (null: not read this time).</summary>
    private static PartyState Reading(SkillButtonIcon? icon, IReadOnlyDictionary<string, SkillTimerView>? running = null)
    {
        var slots = Squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Ultimate: new UltimateReading(false, 0.3),
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: 8)
        {
            ActiveSlot = 1,
            SkillPoints = 2,
            SkillRings = icon is { } told ? [new SkillRingReading("Si", told, told is SkillButtonIcon.Changed or SkillButtonIcon.SecondCast ? 0.5 : null)] : [],
            RunningSkills = running,
        };
    }

    private static Dictionary<string, SkillTimerView> Timer(double left) => new() { ["Si"] = new SkillTimerView(left, left * 22, Estimated: true) };

    [Fact]
    public void The_swirl_or_the_crossed_out_butterfly_is_running()
    {
        Assert.Equal(true, RotationTracker.Evaluate(SiRunning, Reading(SkillButtonIcon.Changed), PhaseHistory.None));
        Assert.Equal(true, RotationTracker.Evaluate(SiRunning, Reading(SkillButtonIcon.SecondCast), PhaseHistory.None));
        Assert.Equal(false, RotationTracker.Evaluate(SiOver, Reading(SkillButtonIcon.Changed), PhaseHistory.None));
    }

    [Fact]
    public void The_butterfly_back_is_over()
    {
        Assert.Equal(false, RotationTracker.Evaluate(SiRunning, Reading(SkillButtonIcon.Original), PhaseHistory.None));
        Assert.Equal(true, RotationTracker.Evaluate(SiOver, Reading(SkillButtonIcon.Original), PhaseHistory.None));
        // The timer may not have been told yet: the butterfly on screen is the answer.
        Assert.Equal(false, RotationTracker.Evaluate(SiRunning, Reading(SkillButtonIcon.Original, new Dictionary<string, SkillTimerView>()), PhaseHistory.None));
    }

    /// <summary>The button hidden (an effect, a cutscene, a countdown look, the controller layout) and no timer running: it can't be told.</summary>
    [Fact]
    public void A_button_not_told_cannot_tell()
    {
        Assert.Null(RotationTracker.Evaluate(SiRunning, Reading(null), PhaseHistory.None));
        Assert.Null(RotationTracker.Evaluate(SiRunning, Reading(SkillButtonIcon.Unknown), PhaseHistory.None));
        Assert.Null(RotationTracker.Evaluate(SiRunning, Reading(SkillButtonIcon.Cooling), PhaseHistory.None));
        Assert.Null(RotationTracker.Evaluate(SiRunning, Reading(SkillButtonIcon.Unknown, new Dictionary<string, SkillTimerView>()), PhaseHistory.None));
    }

    /// <summary>The overlay's timer still runs (the estimate, while the button is hidden): still running, as the gauge shows.</summary>
    [Fact]
    public void The_timer_carries_it_while_the_button_is_hidden()
    {
        Assert.Equal(true, RotationTracker.Evaluate(SiRunning, Reading(SkillButtonIcon.Unknown, Timer(0.4)), PhaseHistory.None));
        Assert.Equal(true, RotationTracker.Evaluate(SiRunning, Reading(null, Timer(0)), PhaseHistory.None));
    }

    /// <summary>Only Si's battle-skill time is read; for anyone else the condition could never be told, so the rotation says so.</summary>
    [Fact]
    public void Only_an_operator_whose_skill_time_is_read_can_be_asked_about()
    {
        var rotation = Gated(new BattleSkillActiveCondition("Tangtang", true));

        Assert.Contains(rotation.Problems(), p => p.Kind == RotationProblemKind.SkillTimeNotRead && p.Phase == "Basic");
        Assert.Empty(Gated(SiRunning).Problems());
        Assert.Contains(new Rotation(Rotation.CurrentFormat, "r", "r", ["Laevatain", "Arcane", "Ardelia", "Camille"],
            [new RotationPhase("p", "P", [new RotationStep("Arcane", StepAction.Combo)], [SiRunning])]).Problems(),
            p => p.Kind == RotationProblemKind.ConditionOperatorNotInSquad);
    }

    [Fact]
    public void It_needs_format_7_and_round_trips()
    {
        var rotation = Gated(new NotCondition(SiRunning));

        Assert.True(Rotation.CurrentFormat >= 7);
        Assert.Equal(7, Rotation.FormatNeeded(rotation.Phases, lastMatchWins: false));
        string json = RotationFormat.ToJson(rotation with { Format = 7 });
        Assert.Contains("\"kind\": \"battleSkillActive\"", json);
        Assert.Equal(rotation.Phases[1].When, RotationFormat.FromJson(json).Phases[1].When);
    }

    [Fact]
    public void An_alert_using_it_needs_alert_format_3()
    {
        Assert.True(AlertFile.CurrentFormat >= 3);
        Assert.Equal(3, AlertFile.FormatNeeded([new AlertRule("a", [new AnyCondition([SiOver])], "")]));
        Assert.Equal(1, AlertFile.FormatNeeded([new AlertRule("a", [new SkillPointsCondition(Comparison.AtLeast, 2)], "")]));
    }

    /// <summary>A "keep basic attacking" phase gated on Si's skill time running comes on while it runs and gives way once the butterfly is back.</summary>
    [Fact]
    public void A_phase_gated_on_it_shows_while_her_skill_runs()
    {
        var selector = new PhaseSelector(Gated(SiRunning));

        Assert.Equal("Charge I", selector.Update(Reading(SkillButtonIcon.Original), null).Step.PhaseLabel);
        Assert.Equal("Basic II", selector.Update(Reading(SkillButtonIcon.Changed), null).Step.PhaseLabel);
        Assert.Equal("Basic II", selector.Update(Reading(SkillButtonIcon.Unknown, Timer(0.3)), null).Step.PhaseLabel);
        Assert.Equal("Charge I", selector.Update(Reading(SkillButtonIcon.Original), null).Step.PhaseLabel);
    }

    private static Rotation Gated(StateCondition when) => new(Rotation.CurrentFormat, "r", "r", Squad,
    [
        new RotationPhase("charge", "Charge", [new RotationStep("Si", StepAction.BattleSkill)]),
        new RotationPhase("basic", "Basic", [new RotationStep("Si", StepAction.BasicAttack)], [when]),
    ]);
}
