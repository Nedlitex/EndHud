using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// A phase's finish condition (完成条件; owner, 2026-10-24: "define a finish condition for the rotation similar to
/// entry condition"): when it comes true the round is played through (phase played +1, "played through this
/// showing") and every light goes out. Edited like the entry condition, with the full condition set plus "the phase's
/// steps lit" (本轴段步骤已点亮: all / at least N / these steps). No finish condition: every step that lights up is
/// lit. Squad slots: Laevatain (1), Camille (2), Arcane (3), Ardelia (4).
/// </summary>
public sealed class FinishConditionTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];
    private const int Laevatain = 1, Camille = 2, Ardelia = 4;

    /// <summary>Ardelia's, Laevatain's and Camille's battle skills, then an Arcane basic attack.</summary>
    private static Rotation Three(params StateCondition[] finish) => new(Rotation.CurrentFormat, "r", "r", Squad,
    [
        new RotationPhase("p", "P",
        [
            new RotationStep("Ardelia", StepAction.BattleSkill),
            new RotationStep("Laevatain", StepAction.BattleSkill),
            new RotationStep("Camille", StepAction.BattleSkill),
            new RotationStep("Arcane", StepAction.BasicAttack),
        ]) { Finish = finish },
    ]);

    private static PartyState Reading(double skillPoints = 2.0)
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

    private sealed class Fight(Rotation rotation)
    {
        public StepFollower Follower { get; } = new(rotation);
        private PartyState? _before;

        public IReadOnlyList<StepMark> Read(PartyState now, params int[] presses)
        {
            var marks = Follower.Update(0, _before, now, Casts.Seen(presses), PhaseHistory.None).Marks;
            _before = now;
            return marks;
        }

        public IReadOnlyList<StepMark> Press(params int[] slots) => Read(Reading(), slots);

        public int Played => Follower.TimesPlayed("p")!.Value;
    }

    private const StepMark L = StepMark.Lit, C = StepMark.Current, U = StepMark.Unlit, N = StepMark.Neutral;

    // ---- Following ----

    [Fact]
    public void At_least_two_steps_lit_finishes_the_round_at_the_second_in_any_order()
    {
        var fight = new Fight(Three(new StepsLitCondition(StepsLitWhich.AtLeast, Count: 2)));
        fight.Read(Reading());

        Assert.Equal([C, U, L, N], fight.Press(Camille));
        Assert.Equal(0, fight.Played);
        Assert.Equal([C, U, U, N], fight.Press(Laevatain)); // two lit: played through, the lights go out
        Assert.Equal(1, fight.Played);
    }

    [Fact]
    public void These_steps_lit_finishes_when_those_are_lit_whatever_else_is()
    {
        var fight = new Fight(Three(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 3])));
        fight.Read(Reading());

        fight.Press(Camille);   // step 3
        Assert.Equal(0, fight.Played);
        Assert.Equal([C, U, U, N], fight.Press(Ardelia)); // step 1: steps 1 and 3 lit, step 2 never was
        Assert.Equal(1, fight.Played);
        fight.Press(Laevatain); // step 2 alone: not one of them
        Assert.Equal(1, fight.Played);
    }

    [Fact]
    public void All_steps_lit_is_the_same_as_no_finish_condition()
    {
        foreach (var rotation in new[] { Three(new StepsLitCondition(StepsLitWhich.All)), Three() })
        {
            var fight = new Fight(rotation);
            fight.Read(Reading());
            fight.Press(Camille);
            fight.Press(Ardelia);
            Assert.Equal(0, fight.Played);
            fight.Press(Laevatain);
            Assert.Equal(1, fight.Played);
        }
    }

    /// <summary>
    /// Any condition, e.g. on the game's state. It counts on the reading it comes true; while it keeps holding it
    /// doesn't count again, and it counts again once it was seen not holding and came true again.
    /// </summary>
    [Fact]
    public void A_finish_on_the_games_state_counts_when_it_comes_true_and_once_while_it_holds()
    {
        var fight = new Fight(Three(new SkillPointsCondition(Comparison.AtLeast, 3)));
        fight.Read(Reading(1));
        fight.Read(Reading(1), Ardelia);

        Assert.Equal([C, U, U, N], fight.Read(Reading(3))); // came true: played through, the lights out
        Assert.Equal(1, fight.Played);
        Assert.Equal([L, C, U, N], fight.Read(Reading(3), Ardelia));
        Assert.Equal(1, fight.Played);                      // still holding: not again
        fight.Read(Reading(2));
        fight.Read(Reading(3));
        Assert.Equal(2, fight.Played);                      // came true again
    }

    [Fact]
    public void A_finish_that_cannot_be_told_never_counts()
    {
        var fight = new Fight(Three(new SkillPointsCondition(Comparison.AtLeast, 3)));
        fight.Read(Reading(1));

        fight.Read(Reading(1) with { SkillPoints = null }, Ardelia, Laevatain, Camille);

        Assert.Equal(0, fight.Played);
    }

    /// <summary>A phase of nothing but attacks never finishes by default; a finish condition of its own can.</summary>
    [Fact]
    public void A_phase_of_only_attacks_finishes_by_its_own_condition()
    {
        var attacks = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P", [new RotationStep("Arcane", StepAction.BasicAttack)])]);
        var byDefault = new Fight(attacks);
        byDefault.Read(Reading(3));
        byDefault.Read(Reading(3));
        Assert.Equal(0, byDefault.Played);

        var own = new Fight(attacks with { Phases = [attacks.Phases[0] with { Finish = [new SkillPointsCondition(Comparison.AtLeast, 3)] }] });
        own.Read(Reading(3));
        Assert.Equal(1, own.Played);
    }

    /// <summary>"The phase is me, not finished yet" holds it until its finish condition held, as it did until played through.</summary>
    [Fact]
    public void A_gate_on_its_own_phase_not_finished_holds_it_until_the_finish_condition_holds()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill)]),
            new RotationPhase("burst", "Burst",
                [new RotationStep("Laevatain", StepAction.BattleSkill), new RotationStep("Camille", StepAction.BattleSkill), new RotationStep("Ardelia", StepAction.BattleSkill)],
                [new AnyCondition([new SkillPointsCondition(Comparison.AtLeast, 3), new PhaseCurrentCondition("burst", PlayedThrough: false)])])
            {
                Finish = [new StepsLitCondition(StepsLitWhich.AtLeast, Count: 2)],
            },
        ]);
        var selector = new PhaseSelector(rotation);
        var follower = new StepFollower(rotation);
        PartyState? before = null;
        string Read(PartyState now, params int[] presses)
        {
            var shown = selector.Update(now, follower);
            follower.Update(shown.Step.PhaseIndex, before, now, Casts.Seen(presses), selector);
            before = now;
            return shown.Step.PhaseLabel;
        }

        Assert.Equal("Burst II", Read(Reading(3)));
        Assert.Equal("Burst II", Read(Reading(2), Camille));    // held: one step lit
        Assert.Equal("Burst II", Read(Reading(2), Laevatain));  // two lit: finished on this reading
        Assert.Equal("Charge I", Read(Reading(2)));
        Assert.Equal(1, follower.TimesPlayed("burst"));
    }

    [Fact]
    public void Steps_lit_is_unknown_outside_the_step_follower()
    {
        Assert.Null(RotationTracker.Evaluate(new StepsLitCondition(StepsLitWhich.All), Reading(), PhaseHistory.None));
    }

    private sealed class Lights(params bool?[] lit) : IPhaseHistory
    {
        public int? TimesShown(string phaseId) => null;

        public int? TimesPlayed(string phaseId) => null;

        public IReadOnlyList<bool?>? StepsLit => lit;
    }

    /// <summary>A hand-written file's "these steps" with none picked, or "at least 0", is refused; followed anyway, it never holds.</summary>
    [Fact]
    public void Steps_lit_naming_nothing_never_holds()
    {
        var lit = new Lights(true, null, true);

        Assert.Equal(false, RotationTracker.Evaluate(new StepsLitCondition(StepsLitWhich.These, Steps: []), Reading(), lit));
        Assert.Equal(false, RotationTracker.Evaluate(new StepsLitCondition(StepsLitWhich.These), Reading(), lit));
        Assert.Equal(false, RotationTracker.Evaluate(new StepsLitCondition(StepsLitWhich.AtLeast, Count: 0), Reading(), lit));
        Assert.Equal(true, RotationTracker.Evaluate(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 3]), Reading(), lit));
        Assert.Equal(true, RotationTracker.Evaluate(new StepsLitCondition(StepsLitWhich.All), Reading(), lit));
    }

    /// <summary>
    /// Like an entry condition, a finish condition already holding when the phase comes onto the strip counts on that
    /// reading (nothing was seen before it this showing); it counts again only after it was seen not holding.
    /// </summary>
    [Fact]
    public void A_finish_already_holding_when_the_phase_comes_on_counts_on_that_reading()
    {
        var fight = new Fight(Three(new SkillPointsCondition(Comparison.AtLeast, 3)));

        fight.Read(Reading(3));
        Assert.Equal(1, fight.Played);
        fight.Read(Reading(3));
        Assert.Equal(1, fight.Played);
    }

    /// <summary>
    /// Every step lit and the finish condition not holding: no step left to point at, so the strip says what the phase
    /// waits for, the finish condition's needs (never a silent strip).
    /// </summary>
    [Fact]
    public void Every_step_lit_and_the_finish_not_holding_the_checks_are_the_finish_conditions()
    {
        StateCondition three = new SkillPointsCondition(Comparison.AtLeast, 3);
        var fight = new Fight(Three(three));
        fight.Read(Reading(1));
        fight.Press(Ardelia, Laevatain, Camille);

        var position = fight.Follower.Update(0, Reading(1), Reading(1), [], PhaseHistory.None);

        Assert.Equal([L, L, L, N], position.Marks);
        Assert.False(position.Ready);
        Assert.Equal([new ConditionCheck(CheckKind.Expected, three, false)], position.Checks);
    }

    /// <summary>A condition step still met when a round ends lights again in the new round, on that same reading.</summary>
    [Fact]
    public void A_condition_step_still_met_is_lit_again_in_the_new_round()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("", StepAction.Condition, [new SkillPointsCondition(Comparison.AtLeast, 1)])])]);
        var fight = new Fight(rotation);

        Assert.Equal([C, L], fight.Read(Reading(2)));
        Assert.Equal([C, L], fight.Read(Reading(2), Ardelia)); // played through; the condition still holds
        Assert.Equal(1, fight.Played);
        Assert.Equal([C, L], fight.Read(Reading(2)));
        Assert.Equal(1, fight.Played); // not again: Ardelia's step isn't lit in this round
    }

    // ---- The file ----

    [Fact]
    public void A_finish_condition_needs_format_10_and_is_written_as_finish()
    {
        var rotation = Three(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 3]), new SkillPointsCondition(Comparison.AtLeast, 1));

        Assert.True(Rotation.CurrentFormat >= 10);
        Assert.Equal(10, Rotation.FormatNeeded(rotation.Phases, lastMatchWins: false));
        Assert.Equal(10, Rotation.FormatNeeded(Three(new SkillPointsCondition(Comparison.AtLeast, 1)).Phases, lastMatchWins: false));
        Assert.Equal(4, Rotation.FormatNeeded(Three().Phases, lastMatchWins: false)); // a basic attack: as before
        string json = RotationFormat.ToJson(rotation with { Format = 10 });
        Assert.Contains("\"finish\"", json);
        Assert.Contains("\"kind\": \"stepsLit\"", json);
        Assert.Equal(rotation.Phases[0].Finish, RotationFormat.FromJson(json).Phases[0].Finish);
        Assert.DoesNotContain("\"finish\"", RotationFormat.ToJson(Three() with { Format = 4 }));
    }

    [Fact]
    public void A_rotation_from_before_finish_conditions_loads_with_none()
    {
        string file = Path.Combine(OperatorCache.RepositoryRoot(), "rotations", "next-release", "si-last-rite-arcane-tangtang.json");

        var rotation = RotationFormat.FromJson(File.ReadAllText(file));

        Assert.All(rotation.Phases, p => Assert.Empty(p.Finish));
        Assert.Equal(9, Rotation.FormatNeeded(rotation.Phases, rotation.LastMatchWins));
    }

    // ---- Problems ----

    [Fact]
    public void Steps_lit_is_refused_outside_a_finish_condition()
    {
        StateCondition lit = new StepsLitCondition(StepsLitWhich.All);
        var gated = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("a", "A", [new RotationStep("Ardelia", StepAction.BattleSkill)]),
            new RotationPhase("b", "B", [new RotationStep("Ardelia", StepAction.BattleSkill, [lit])], [new NotCondition(lit)])
            {
                PriorityRules = [new PriorityRule([lit], 2)],
            },
        ]);

        Assert.Equal(
        [
            new RotationProblem(RotationProblemKind.StepsLitOutsideFinish, "B"),
            new RotationProblem(RotationProblemKind.StepsLitOutsideFinish, "B", null, 1),
            new RotationProblem(RotationProblemKind.StepsLitOutsideFinish, "B", 1),
        ], gated.Problems());
        Assert.Equal([AlertProblem.NotForAlerts], AlertRule.New([lit], "x").Problems());
        Assert.Empty(Three(lit).Problems());
    }

    [Fact]
    public void Steps_lit_naming_no_step_a_step_that_never_lights_or_too_many_is_refused()
    {
        RotationProblem InFinish(RotationProblemKind kind) => new(kind, "P", Finish: true);

        Assert.Equal([InFinish(RotationProblemKind.StepsLitNoSteps)], Three(new StepsLitCondition(StepsLitWhich.These, Steps: [])).Problems());
        Assert.Equal([InFinish(RotationProblemKind.StepsLitNotCountable)], Three(new StepsLitCondition(StepsLitWhich.These, Steps: [1, 4])).Problems()); // 4: the basic attack
        Assert.Equal([InFinish(RotationProblemKind.StepsLitNotCountable)], Three(new StepsLitCondition(StepsLitWhich.These, Steps: [5])).Problems());
        Assert.Equal([InFinish(RotationProblemKind.StepsLitCountOutOfRange)], Three(new StepsLitCondition(StepsLitWhich.AtLeast, Count: 4)).Problems()); // 3 light up
        Assert.Equal([InFinish(RotationProblemKind.StepsLitCountOutOfRange)], Three(new StepsLitCondition(StepsLitWhich.AtLeast, Count: 0)).Problems());
        Assert.Empty(Three(new StepsLitCondition(StepsLitWhich.AtLeast, Count: 3)).Problems());
    }

    /// <summary>The finish condition is checked like an entry condition, and its problems say they are in it.</summary>
    [Fact]
    public void A_finish_conditions_other_problems_say_they_are_in_the_finish_condition()
    {
        Assert.Equal([new RotationProblem(RotationProblemKind.ConditionOperatorNotInSquad, "P", Finish: true)],
            Three(new UltimateCondition("Si", true)).Problems());
        Assert.Equal([new RotationProblem(RotationProblemKind.EmptyConditionGroup, "P", Finish: true)],
            Three(new AnyCondition([])).Problems());
        Assert.Equal([new RotationProblem(RotationProblemKind.PhasePlayedMissingPhase, "P", Finish: true)],
            Three(new PhasePlayedCondition("gone", Comparison.AtLeast, 1)).Problems());
    }

    [Fact]
    public void The_finish_condition_reads_boss_statuses_when_it_asks_about_them()
    {
        Assert.True(ConditionWalk.NeedsBossStatuses(Three(new BossStatusCondition("Cryo Infliction", true, 3))));
    }
}
