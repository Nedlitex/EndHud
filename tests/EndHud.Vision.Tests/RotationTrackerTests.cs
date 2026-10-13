using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Finding the position in a rotation from readings, with the guide-shaped rotation
/// (<see cref="RotationFormatTests.Guide"/>): steps 0–3 charge (Ardelia's battle skill at 2+ skill
/// points; Camille and Arcane's combos together; Laevatain's battle skill with 熔火 at 3 or less),
/// 4–7 charge again, 8 Laevatain's ultimate, 9 Ardelia's combo. Squad slots: Laevatain, Camille,
/// Arcane, Ardelia.
/// </summary>
public class RotationTrackerTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];

    /// <summary>A reading. Defaults: every combo ready, no ultimate ready, every battle skill available.</summary>
    private sealed record Reading(double? SkillPoints = 2.5, int? Controlled = 1)
    {
        public bool[] ComboReady { get; init; } = [true, true, true, true];
        public bool[] UltimateReady { get; init; } = [false, false, false, false];
        public BattleSkillState?[] Skill { get; init; } = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available];
        public int? MeltingFlame { get; init; }
        public bool Identified { get; init; } = true;

        public PartyState ToState()
        {
            var slots = Enumerable.Range(0, 4).Select(i => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
                Combo: new ComboReading(ComboReady[i] ? 1 : 0.3, ComboReady[i]),
                Ultimate: new UltimateReading(UltimateReady[i], UltimateReady[i] ? 1 : 0.2),
                Operator: Identified ? new OperatorMatch(new OperatorInfo(Squad[i], Squad[i], Squad[i], Squad[i] + ".png"), 1, true) : null)
            {
                BattleSkill = Skill[i],
            }).ToList();
            return new PartyState(slots, PanelMarks: 8)
            {
                SkillPoints = SkillPoints,
                ActiveSlot = Controlled,
                Counters = MeltingFlame is { } m ? [new CounterReading("Laevatain", "melting-flame", m)] : [],
            };
        }
    }

    private static RotationTracker Tracker() => new(RotationFormatTests.Guide());

    private static BattleSkillState?[] Cooling(int slot)
    {
        BattleSkillState?[] skills = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available];
        skills[slot] = BattleSkillState.Cooling;
        return skills;
    }

    [Fact]
    public void At_the_start_of_a_fight_it_is_on_the_first_step()
    {
        var position = Tracker().Update(new Reading().ToState())!;

        Assert.Equal(0, position.Step.Index);
        Assert.True(position.Ready);
    }

    [Fact]
    public void It_waits_on_a_step_that_isnt_ready_and_says_why()
    {
        var tracker = Tracker();
        tracker.Update(new Reading().ToState());

        var position = tracker.Update(new Reading(SkillPoints: 1.4).ToState())!; // Camille's combo is ready, but it's not her turn

        Assert.Equal(0, position.Step.Index);
        Assert.False(position.Ready);
        var unmet = Assert.Single(position.Checks, c => c.Holds == false);
        Assert.Equal(new SkillPointsCondition(Comparison.AtLeast, 2), unmet.Condition);
    }

    [Fact]
    public void Using_the_steps_ability_moves_to_the_next_step()
    {
        var tracker = Tracker();
        tracker.Update(new Reading().ToState());

        var position = tracker.Update(new Reading(SkillPoints: 1.4) { Skill = Cooling(3) }.ToState())!; // Ardelia's battle skill: cooling

        Assert.Equal(1, position.Step.Index);
        Assert.Equal("Camille", position.Step.Step.OperatorId);
        Assert.True(position.Ready);
    }

    [Fact]
    public void Steps_used_together_may_come_in_either_order()
    {
        var tracker = Tracker();
        tracker.Update(new Reading().ToState());
        tracker.Update(new Reading { Skill = Cooling(3) }.ToState()); // step 1: Camille + Arcane together

        var afterArcane = tracker.Update(new Reading { Skill = Cooling(3), ComboReady = [true, true, false, true] }.ToState())!;
        Assert.Equal(1, afterArcane.Step.Index); // Camille's half is still to do

        var afterBoth = tracker.Update(new Reading { Skill = Cooling(3), ComboReady = [true, false, false, true] }.ToState())!;
        Assert.Equal(3, afterBoth.Step.Index);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(4, false)]
    public void A_counter_condition_decides_whether_the_step_is_ready(int meltingFlame, bool ready)
    {
        var tracker = Tracker();
        tracker.Update(new Reading().ToState());
        tracker.Update(new Reading { Skill = Cooling(3) }.ToState());
        tracker.Update(new Reading { Skill = Cooling(3), ComboReady = [true, false, false, true] }.ToState());

        var position = tracker.Update(new Reading { Skill = Cooling(3), ComboReady = [true, false, false, true], MeltingFlame = meltingFlame }.ToState())!;

        Assert.Equal(3, position.Step.Index);
        Assert.Equal(ready, position.Ready);
    }

    [Fact]
    public void After_the_last_step_it_starts_again()
    {
        var tracker = Tracker();
        BattleSkillState?[] dimmed = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, null];

        // Few skill points and Laevatain's ultimate ready: the burst (step 8).
        var burst = tracker.Update(new Reading(SkillPoints: 0.5) { Skill = dimmed, UltimateReady = [true, false, false, false] }.ToState())!;
        // She uses it: the last step, Ardelia's combo.
        var last = tracker.Update(new Reading(SkillPoints: 0.5) { Skill = dimmed }.ToState())!;
        // Ardelia uses it: back to the first step.
        var again = tracker.Update(new Reading(SkillPoints: 0.5) { Skill = dimmed, ComboReady = [true, true, true, false] }.ToState())!;

        Assert.Equal([8, 9, 0], new[] { burst.Step.Index, last.Step.Index, again.Step.Index });
    }

    [Fact]
    public void It_jumps_elsewhere_only_when_clearly_somewhere_else_twice_in_a_row()
    {
        var tracker = Tracker();
        tracker.Update(new Reading().ToState()); // step 0

        // No skill points (Ardelia's button dims), no combo ready, Laevatain's ultimate ready and she's
        // controlled: step 0 can't be done, step 8 (Laevatain's ultimate) fits. Nothing is seen being
        // used, so this is a jump, not a step done.
        var elsewhere = new Reading(SkillPoints: 0.5)
        {
            Skill = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, null],
            ComboReady = [false, false, false, false],
            UltimateReady = [true, false, false, false],
        };
        var once = tracker.Update(elsewhere.ToState())!;
        var twice = tracker.Update(elsewhere.ToState())!;

        Assert.NotEqual(8, once.Step.Index);
        Assert.Equal(8, twice.Step.Index);
    }

    [Fact]
    public void What_was_just_used_tells_it_where_the_player_is()
    {
        var tracker = Tracker();
        tracker.Update(new Reading().ToState()); // step 0

        // Camille and Arcane use their combos (EndHud missed Ardelia's battle skill: her button is
        // dimmed); Laevatain, with 熔火 at 2, is next. Seen twice, the position follows.
        var next = new Reading(SkillPoints: 1.5)
        {
            Skill = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, null],
            ComboReady = [true, false, false, true],
            MeltingFlame = 2,
        };
        tracker.Update(next.ToState());
        var position = tracker.Update(next.ToState())!;

        Assert.Equal(3, position.Step.Index);
    }

    [Fact]
    public void Without_a_recognised_squad_there_is_no_position()
    {
        Assert.Null(Tracker().Update(new Reading { Identified = false }.ToState()));
    }
}
