using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// "Time in phase" (a player's request, 2026-10-27: end a phase after a set delay, e.g. a closing phase whose remaining
/// effects land some seconds after the burst): how long the phase on the strip has run this round, from the readings' own
/// times, the squad panel's hidden time left out (owner). Only in the phase's own hosts: its finish condition, a condition
/// step, a step's expected state. Every number of seconds below is a test number, not a game fact.
/// Squad slots: Laevatain (1), Camille (2), Arcane (3), Ardelia (4).
/// </summary>
public sealed class TimeInPhaseConditionTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];

    private static StateCondition AtLeast(double seconds) => new TimeInPhaseCondition(Comparison.AtLeast, seconds);

    /// <summary>A live reading <paramref name="at"/> seconds into the fight, stamped as the overlay stamps them.</summary>
    private static PartyState Reading(double? at, bool laevatainUltimateReady = false, int readingsSinceSeen = 0)
    {
        var slots = Enumerable.Range(0, 4).Select(i => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Combo: new ComboReading(1, true),
            Ultimate: new UltimateReading(i == 0 && laevatainUltimateReady, i == 0 && laevatainUltimateReady ? 1 : 0.2),
            Operator: new OperatorMatch(new OperatorInfo(Squad[i], Squad[i], Squad[i], Squad[i] + ".png"), 1, true))
        {
            BattleSkill = BattleSkillState.Available,
        }).ToList();
        return new PartyState(slots, PanelMarks: 8, ReadingsSinceSeen: readingsSinceSeen)
        {
            SkillPoints = 2,
            ActiveSlot = 1,
            ReadAt = at is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
        };
    }

    /// <summary>One phase of three battle skills, with this finish condition.</summary>
    private static Rotation OnePhase(params StateCondition[] finish) => new(Rotation.CurrentFormat, "r", "r", Squad,
    [
        new RotationPhase("p", "P",
        [
            new RotationStep("Ardelia", StepAction.BattleSkill),
            new RotationStep("Laevatain", StepAction.BattleSkill),
            new RotationStep("Camille", StepAction.BattleSkill),
        ]) { Finish = finish },
    ]);

    private sealed class Clock(double seconds) : IPhaseHistory
    {
        public int? TimesShown(string phaseId) => null;

        public int? TimesPlayed(string phaseId) => null;

        public TimeSpan? TimeInPhase => TimeSpan.FromSeconds(seconds);
    }

    // ---- The file ----

    [Fact]
    public void It_saves_as_format_14_and_reads_back()
    {
        var rotation = OnePhase(AtLeast(4));

        Assert.Equal(14, Rotation.FormatNeeded(rotation.Phases, lastMatchWins: false));
        string json = RotationFormat.ToJson(rotation with { Format = 14 });
        Assert.Contains("\"kind\": \"timeInPhase\"", json);
        Assert.Contains("\"seconds\": 4", json);
        Assert.Equal(rotation.Phases[0].Finish, RotationFormat.FromJson(json).Phases[0].Finish);
    }

    [Fact]
    public void Half_seconds_and_another_comparison_read_back_the_same()
    {
        var rotation = OnePhase(new TimeInPhaseCondition(Comparison.Below, 4.5), new NotCondition(new TimeInPhaseCondition(Comparison.AtMost, 0.5)));

        var back = RotationFormat.FromJson(RotationFormat.ToJson(rotation with { Format = 14 }));

        Assert.Equal(rotation.Phases[0].Finish, back.Phases[0].Finish);
    }

    // ---- Evaluation ----

    [Fact]
    public void Unknown_without_a_clock_and_compared_in_seconds_with_one()
    {
        Assert.Null(RotationTracker.Evaluate(AtLeast(4), Reading(0), PhaseHistory.None));
        Assert.Equal(false, RotationTracker.Evaluate(AtLeast(4), Reading(0), new Clock(3.9)));
        Assert.Equal(true, RotationTracker.Evaluate(new TimeInPhaseCondition(Comparison.Below, 4), Reading(0), new Clock(3.9)));
        Assert.Equal(true, RotationTracker.Evaluate(AtLeast(4), Reading(0), new Clock(4.0)));
        Assert.Equal(false, RotationTracker.Evaluate(new TimeInPhaseCondition(Comparison.Below, 4), Reading(0), new Clock(4.0)));
    }

    // ---- Problems ----

    [Fact]
    public void In_its_own_hosts_it_is_no_problem()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("p", "P",
            [
                new RotationStep("Ardelia", StepAction.BattleSkill, [AtLeast(1)]),
                new RotationStep("", StepAction.Condition, [AtLeast(2)]),
            ]) { Finish = [AtLeast(3)] },
        ]);

        Assert.Empty(rotation.Problems());
    }

    [Fact]
    public void In_an_entry_condition_or_priority_rule_it_is_refused_as_outside_its_phase()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("a", "A", [new RotationStep("Ardelia", StepAction.BattleSkill)]),
            new RotationPhase("b", "B", [new RotationStep("Ardelia", StepAction.BattleSkill)], [new NotCondition(new AnyCondition([AtLeast(2)]))])
            {
                PriorityRules = [new PriorityRule([AtLeast(5)], 2)],
            },
        ]);

        Assert.Equal(
        [
            new RotationProblem(RotationProblemKind.TimeInPhaseOutsideItsPhase, "B"),
            new RotationProblem(RotationProblemKind.TimeInPhaseOutsideItsPhase, "B", null, 1),
        ], rotation.Problems());
    }

    /// <summary>"Steps lit" was the only kind limited to some hosts; its own problem stays its own.</summary>
    [Fact]
    public void Steps_lit_outside_a_finish_is_still_its_own_problem()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("a", "A", [new RotationStep("Ardelia", StepAction.BattleSkill)]),
            new RotationPhase("b", "B", [new RotationStep("Ardelia", StepAction.BattleSkill)], [new StepsLitCondition(StepsLitWhich.All)]),
        ]);

        Assert.Equal([new RotationProblem(RotationProblemKind.StepsLitOutsideFinish, "B")], rotation.Problems());
        Assert.Equal([AlertProblem.NotForAlerts], AlertRule.New([new StepsLitCondition(StepsLitWhich.All)], "x").Problems());
    }

    [Fact]
    public void In_an_alert_it_is_refused_with_its_own_problem()
    {
        Assert.Equal([AlertProblem.TimeInPhaseNotForAlerts], AlertRule.New([new AnyCondition([AtLeast(2)])], "x").Problems());
    }
}
