using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The kinds added on 2026-10-24 (owner: "ult duration should be added"; the 祀 rotation's
/// "当前轴段 是 X 且本次已打完"): how much of a lasting ultimate's time is left, as a % of its bar
/// (<see cref="UltimateTimeLeftCondition"/>), and which phase is on the strip, optionally whether it
/// was played through since it came on (<see cref="PhaseCurrentCondition"/>).
/// </summary>
public class NewConditionKindsTests
{
    // ---- Time left of a lasting ultimate ----

    private static readonly string[] Lasting = ["Laevatain", "Camille", "Arcane", "Tangtang"];

    private static PartyState Reading(IReadOnlyDictionary<string, LastingUltimateView>? running, double? skillPoints = 2)
    {
        var slots = Lasting.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Ultimate: new UltimateReading(false, 0.3),
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: 8) { ActiveSlot = 2, SkillPoints = skillPoints, RunningUltimates = running };
    }

    private static Dictionary<string, LastingUltimateView> Running(string id, double left) => new() { [id] = new LastingUltimateView(id, left, false) };

    [Fact]
    public void Time_left_is_the_share_of_the_bar_left()
    {
        var quarter = Reading(Running("Laevatain", 0.25));

        Assert.Equal(true, RotationTracker.Evaluate(new UltimateTimeLeftCondition("Laevatain", Comparison.AtMost, 30), quarter, PhaseHistory.None));
        Assert.Equal(false, RotationTracker.Evaluate(new UltimateTimeLeftCondition("Laevatain", Comparison.AtLeast, 30), quarter, PhaseHistory.None));
        Assert.Equal(true, RotationTracker.Evaluate(new UltimateTimeLeftCondition("Laevatain", Comparison.Above, 20), quarter, PhaseHistory.None));
    }

    /// <summary>Not running is 0% (Lead, 2026-10-24), as a ready ultimate's charge is 100%.</summary>
    [Fact]
    public void Not_running_is_zero()
    {
        var none = Reading(new Dictionary<string, LastingUltimateView>());

        Assert.Equal(true, RotationTracker.Evaluate(new UltimateTimeLeftCondition("Laevatain", Comparison.AtMost, 0), none, PhaseHistory.None));
        Assert.Equal(false, RotationTracker.Evaluate(new UltimateTimeLeftCondition("Laevatain", Comparison.Above, 0), none, PhaseHistory.None));
    }

    /// <summary>The bottom HUD out of sight (no skill points read): can't be told.</summary>
    [Fact]
    public void Time_left_is_unknown_while_the_hud_is_hidden()
    {
        Assert.Null(RotationTracker.Evaluate(new UltimateTimeLeftCondition("Laevatain", Comparison.AtMost, 30), Reading(null, skillPoints: null), PhaseHistory.None));
    }

    /// <summary>Only the lasting ultimates EndHud reads can be asked about, in rotations and alerts alike.</summary>
    [Fact]
    public void Only_a_lasting_ultimate_that_is_read_can_be_asked_about()
    {
        StateCondition tangtang = new UltimateTimeLeftCondition("Tangtang", Comparison.AtMost, 30);
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Lasting,
            [new RotationPhase("p", "P", [new RotationStep("Laevatain", StepAction.Ultimate, [tangtang])])]);

        Assert.Equal([new RotationProblem(RotationProblemKind.UltimateTimeNotRead, "P", 1)], rotation.Problems());
        Assert.Equal([AlertProblem.UltimateTimeNotRead], AlertRule.New([tangtang], "").Problems());
        Assert.Empty(AlertRule.New([new UltimateTimeLeftCondition("Yvonne", Comparison.AtMost, 30)], "").Problems());
    }

    [Fact]
    public void The_new_kinds_need_rotation_format_9_and_alert_format_4()
    {
        StateCondition left = new UltimateTimeLeftCondition("Laevatain", Comparison.AtMost, 30);
        StateCondition current = new PhaseCurrentCondition("p", PlayedThrough: false);
        foreach (var condition in new[] { left, current })
        {
            var phases = new[] { new RotationPhase("p", "P", [new RotationStep("Laevatain", StepAction.Ultimate)], [new NotCondition(condition)]) };
            Assert.Equal(9, Rotation.FormatNeeded(phases, lastMatchWins: false));
            Assert.Equal(4, AlertFile.FormatNeeded([AlertRule.New([condition], "")]));
            var rotation = new Rotation(9, "r", "r", Lasting, phases);
            Assert.Equal(rotation.Phases[0].When, RotationFormat.FromJson(RotationFormat.ToJson(rotation)).Phases[0].When);
        }
        Assert.True(Rotation.CurrentFormat >= 9);
        Assert.Contains("\"kind\": \"phaseCurrent\"", RotationFormat.ToJson(new Rotation(9, "r", "r", Lasting,
            [new RotationPhase("p", "P", [new RotationStep("Laevatain", StepAction.Ultimate)], [current])])));
    }

    // ---- The phase on the strip now ----

    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];
    private const int Laevatain = 1, Ardelia = 4;

    private static PartyState Plain(double skillPoints = 2.0)
    {
        var slots = Enumerable.Range(0, 4).Select(i => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Combo: new ComboReading(1, true),
            Ultimate: new UltimateReading(false, 0.2),
            Operator: new OperatorMatch(new OperatorInfo(Squad[i], Squad[i], Squad[i], Squad[i] + ".png"), 1, true))
        {
            BattleSkill = BattleSkillState.Available,
        }).ToList();
        return new PartyState(slots, PanelMarks: 8) { SkillPoints = skillPoints, ActiveSlot = 1 };
    }

    /// <summary>
    /// Charge (Ardelia, then Laevatain), the otherwise phase; Basic, entered once Charge is played
    /// through this showing, and held while it is the phase shown, as long as skill points are below 3.
    /// </summary>
    private static Rotation ChargeThenBasic() => new(Rotation.CurrentFormat, "r", "r", Squad,
    [
        new RotationPhase("charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill)]),
        new RotationPhase("basic", "Basic", [new RotationStep("Arcane", StepAction.BasicAttack)],
            [new AnyCondition([new PhaseCurrentCondition("charge", PlayedThrough: true), new PhaseCurrentCondition("basic")]), new SkillPointsCondition(Comparison.Below, 3)]),
    ]);

    private sealed class Fight(Rotation rotation)
    {
        public PhaseSelector Selector { get; } = new(rotation);
        public StepFollower Steps { get; } = new(rotation);
        private PartyState? _before;

        public string Read(PartyState now, params int[] presses)
        {
            var shown = Selector.Update(now, Steps);
            Steps.Update(shown.Step.PhaseIndex, _before, now, Casts.Seen(presses), Selector);
            _before = now;
            return shown.Step.PhaseLabel;
        }
    }

    [Fact]
    public void A_phase_played_through_this_showing_hands_over_and_the_next_holds_itself()
    {
        var fight = new Fight(ChargeThenBasic());

        Assert.Equal("Charge I", fight.Read(Plain()));
        Assert.Equal("Charge I", fight.Read(Plain(), Ardelia));
        Assert.Equal("Charge I", fight.Read(Plain(), Laevatain)); // played through on this reading
        Assert.Equal("Basic II", fight.Read(Plain()));            // the selector sees it on the next
        Assert.Equal("Basic II", fight.Read(Plain()));            // "the phase is Basic" holds it
        Assert.Equal("Charge I", fight.Read(Plain(3)));           // its other part fails: back to the otherwise phase
        // Charge came onto the strip again: not played through this showing, so Basic doesn't come back.
        Assert.Equal("Charge I", fight.Read(Plain()));
        Assert.Equal(false, ((IPhaseHistory)fight.Selector).PlayedThroughThisShowing("charge"));
        Assert.Equal("charge", ((IPhaseHistory)fight.Selector).CurrentPhase);
    }

    private sealed class FakeHistory(string? current, bool? played) : IPhaseHistory
    {
        public int? TimesShown(string phaseId) => null;

        public int? TimesPlayed(string phaseId) => null;

        public string? CurrentPhase => current;

        public bool? PlayedThroughThisShowing(string phaseId) => phaseId == current ? played : false;
    }

    [Fact]
    public void The_phase_and_its_played_through_part_are_told_from_the_history()
    {
        var empty = Plain();
        bool? Holds(StateCondition c, IPhaseHistory h) => RotationTracker.Evaluate(c, empty, h);

        Assert.Equal(true, Holds(new PhaseCurrentCondition("a"), new FakeHistory("a", false)));
        Assert.Equal(false, Holds(new PhaseCurrentCondition("b"), new FakeHistory("a", false)));
        Assert.Equal(true, Holds(new PhaseCurrentCondition("a", false), new FakeHistory("a", false)));
        Assert.Equal(false, Holds(new PhaseCurrentCondition("a", false), new FakeHistory("a", true)));
        Assert.Equal(true, Holds(new PhaseCurrentCondition("a", true), new FakeHistory("a", true)));
        Assert.Null(Holds(new PhaseCurrentCondition("a", true), new FakeHistory("a", null)));
        // No rotation followed (an alert's history then): can't be told.
        Assert.Null(Holds(new PhaseCurrentCondition("a"), PhaseHistory.None));
    }

    [Fact]
    public void A_deleted_phase_is_refused()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P", [new RotationStep("Laevatain", StepAction.Ultimate)], [new PhaseCurrentCondition("gone")])]);

        Assert.Equal([new RotationProblem(RotationProblemKind.PhaseCurrentMissingPhase, "P")], rotation.Problems());
    }
}
