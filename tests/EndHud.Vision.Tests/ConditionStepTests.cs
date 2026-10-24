using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// A condition step (owner, 2026-10-24: "add a condition node in rotation, e.g. check whether enemy
/// has 3 ice infliction, and show a highlight when satisfied"): a step that is its conditions, no
/// skill. With the keys read it is the current step until its conditions hold, then done; when they
/// can't be told, a later real cast passes it (as a basic attack is passed). Squad slots: Laevatain
/// (1), Camille (2), Arcane (3), Ardelia (4).
/// </summary>
public class ConditionStepTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];
    private const int Laevatain = 1, Ardelia = 4;

    private static readonly StateCondition CryoThree = new BossStatusCondition("Cryo Infliction", true, 3);

    private static RotationStep Wait(params StateCondition[] until) => new("", StepAction.Condition, until);

    private static Rotation Of(params RotationStep[] steps) => new(Rotation.CurrentFormat, "r", "r", Squad, [new RotationPhase("p", "P", steps)]);

    /// <summary>A reading; <paramref name="cryo"/>: the Cryo stacks read on the enemy (null: the statuses aren't read).</summary>
    private static PartyState Reading(int? cryo)
    {
        var slots = Enumerable.Range(0, 4).Select(i => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Combo: new ComboReading(1, true),
            Ultimate: new UltimateReading(false, 0.2),
            Operator: new OperatorMatch(new OperatorInfo(Squad[i], Squad[i], Squad[i], Squad[i] + ".png"), 1, true))
        {
            BattleSkill = BattleSkillState.Available,
        }).ToList();
        return new PartyState(slots, PanelMarks: 8)
        {
            SkillPoints = 2,
            ActiveSlot = 1,
            BossStatuses = cryo is { } n ? n == 0 ? [] : [new BossStatusReading("Cryo Infliction", 0, 1, n)] : null,
        };
    }

    private sealed class Fight(Rotation rotation)
    {
        public StepFollower Follower { get; } = new(rotation);
        private PartyState? _before;

        public IReadOnlyList<StepMark> Read(PartyState now, params int[] presses)
        {
            var position = Follower.Update(0, _before, now, Casts.Seen(presses), PhaseHistory.None);
            _before = now;
            return position.Marks;
        }
    }

    [Fact]
    public void It_is_current_until_it_holds_then_done()
    {
        var fight = new Fight(Of(Wait(CryoThree), new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill)));

        Assert.Equal([StepMark.Current, StepMark.Upcoming, StepMark.Upcoming], fight.Read(Reading(1)));
        Assert.Equal([StepMark.Current, StepMark.Upcoming, StepMark.Upcoming], fight.Read(Reading(2)));
        Assert.Equal([StepMark.Done, StepMark.Current, StepMark.Upcoming], fight.Read(Reading(3)));
        // Done stays done when it stops holding: the step was reached.
        Assert.Equal([StepMark.Done, StepMark.Current, StepMark.Upcoming], fight.Read(Reading(0)));
    }

    /// <summary>Its conditions seen not holding: a cast of a later step doesn't pass it (it is what the player waits for).</summary>
    [Fact]
    public void Not_holding_it_holds_the_marker()
    {
        var fight = new Fight(Of(Wait(CryoThree), new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill)));

        fight.Read(Reading(1));

        Assert.Equal([StepMark.Current, StepMark.Upcoming, StepMark.Upcoming], fight.Read(Reading(1), Ardelia));
    }

    /// <summary>Owner, 2026-10-24: if its condition is unknown, a later real cast passes it.</summary>
    [Fact]
    public void Unknown_a_later_cast_passes_it()
    {
        var fight = new Fight(Of(Wait(CryoThree), new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill)));

        Assert.Equal([StepMark.Current, StepMark.Upcoming, StepMark.Upcoming], fight.Read(Reading(null)));
        Assert.Equal([StepMark.Done, StepMark.Done, StepMark.Current], fight.Read(Reading(null), Ardelia));
    }

    /// <summary>The step waited on doesn't make a phase "played" by itself, but a phase isn't played through until it is done.</summary>
    [Fact]
    public void A_phase_ending_in_a_condition_step_is_played_through_when_it_holds()
    {
        var fight = new Fight(Of(new RotationStep("Ardelia", StepAction.BattleSkill), Wait(CryoThree)));

        fight.Read(Reading(1));
        fight.Read(Reading(1), Ardelia);
        Assert.Equal(0, fight.Follower.TimesPlayed("p"));
        fight.Read(Reading(3));
        Assert.Equal(1, fight.Follower.TimesPlayed("p"));
        fight.Read(Reading(3));
        Assert.Equal(1, fight.Follower.TimesPlayed("p")); // once per round, not once per reading it holds
    }

    [Fact]
    public void Waiting_on_it_the_reasons_are_its_conditions()
    {
        var fight = new Fight(Of(Wait(CryoThree), new RotationStep("Ardelia", StepAction.BattleSkill)));

        var position = fight.Follower.Update(0, null, Reading(1), [], PhaseHistory.None);

        Assert.False(position.Ready);
        Assert.Contains(position.Checks, c => c is { Kind: CheckKind.Expected, Holds: false } && c.Condition == CryoThree);
        Assert.DoesNotContain(position.Checks, c => c is { Kind: CheckKind.OwnAbility, Holds: not true });
    }

    [Fact]
    public void Its_problems_and_format()
    {
        Assert.Empty(Of(Wait(CryoThree), new RotationStep("Ardelia", StepAction.BattleSkill)).Problems()); // no operator: fine
        Assert.Equal([new RotationProblem(RotationProblemKind.ConditionStepEmpty, "P", 1)], Of(Wait()).Problems());
        Assert.Equal([new RotationProblem(RotationProblemKind.ConditionStepWithPrevious, "P", 2)],
            Of(new RotationStep("Ardelia", StepAction.BattleSkill), Wait(CryoThree) with { WithPrevious = true }).Problems());
        Assert.Equal([new RotationProblem(RotationProblemKind.ConditionStepWithPrevious, "P", 2)],
            Of(Wait(CryoThree), new RotationStep("Ardelia", StepAction.BattleSkill, WithPrevious: true)).Problems());
        // Its conditions are checked like any others'.
        Assert.Equal([new RotationProblem(RotationProblemKind.ConditionOperatorNotInSquad, "P", 1)], Of(Wait(new ComboCondition("Si", true))).Problems());

        var rotation = Of(Wait(CryoThree), new RotationStep("Ardelia", StepAction.BattleSkill));
        Assert.Equal(9, Rotation.FormatNeeded(rotation.Phases, lastMatchWins: false));
        string json = RotationFormat.ToJson(rotation with { Format = 9 });
        Assert.Contains("\"action\": \"Condition\"", json);
        Assert.Equal(rotation.Phases[0].Steps[0], RotationFormat.FromJson(json).Phases[0].Steps[0] with { Expect = rotation.Phases[0].Steps[0].Expect });
        Assert.Equal([CryoThree], RotationFormat.FromJson(json).Phases[0].Steps[0].Expect);
        Assert.True(ConditionWalk.NeedsBossStatuses(rotation)); // its statuses are read
    }
}
