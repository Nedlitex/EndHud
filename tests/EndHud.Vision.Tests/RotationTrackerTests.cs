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
        /// <summary>A combo bar that isn't ready: nearly empty, as just after a use.</summary>
        public double ComboNotReady { get; init; } = 0.02;
        public bool[] UltimateReady { get; init; } = [false, false, false, false];
        public BattleSkillState?[] Skill { get; init; } = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available];
        public int? MeltingFlame { get; init; }
        /// <summary>False: the ultimates can't be read (a cutscene, the HUD hidden).</summary>
        public bool UltimateSeen { get; init; } = true;
        public bool Identified { get; init; } = true;

        public PartyState ToState()
        {
            var slots = Enumerable.Range(0, 4).Select(i => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
                Combo: new ComboReading(ComboReady[i] ? 1 : ComboNotReady, ComboReady[i]),
                Ultimate: UltimateSeen ? new UltimateReading(UltimateReady[i], UltimateReady[i] ? 1 : 0.2) : null,
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

    /// <summary>A bar just under full can read below "ready" for a moment (0.91–0.99 in the recordings): not a use.</summary>
    [Fact]
    public void A_full_combo_bar_dipping_below_ready_is_not_a_use()
    {
        var tracker = Tracker();
        tracker.Update(new Reading().ToState());
        Assert.Equal(1, tracker.Update(new Reading(SkillPoints: 1.4) { Skill = Cooling(3) }.ToState())!.Step.Index); // on Camille and Arcane's combos (used together)

        var position = tracker.Update(new Reading(SkillPoints: 1.4) { Skill = Cooling(3), ComboReady = [true, false, false, true], ComboNotReady = 0.95 }.ToState())!;

        Assert.Equal(1, position.Step.Index);
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

    // ---- Phases with an entry condition ----

    /// <summary>
    /// 充能轴 (0: Ardelia's battle skill, 1: Camille's combo), 充能轴 (2: Arcane's combo, 3: Laevatain's
    /// battle skill), then 爆发轴 (4: Laevatain's ultimate, 5: Ardelia's combo), entered when
    /// <paramref name="gate"/> turns true. Slots: 0 Laevatain, 1 Camille, 2 Arcane, 3 Ardelia.
    /// </summary>
    private static RotationTracker Gated(StateCondition gate, RotationStep? firstChargeStep = null) => new(new Rotation(
        Rotation.CurrentFormat, "gated", "门控轴", Squad,
        [
            new RotationPhase("charge", "充能轴", [firstChargeStep ?? new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Camille", StepAction.Combo)]),
            new RotationPhase("charge-2", "充能轴", [new RotationStep("Arcane", StepAction.Combo), new RotationStep("Laevatain", StepAction.BattleSkill)]),
            new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate), new RotationStep("Ardelia", StepAction.Combo)], [gate]),
        ]));

    private static readonly StateCondition UltReady = new UltimateCondition("Laevatain", true);
    private static bool[] Ult(bool ready) => [ready, false, false, false];
    private static bool[] CombosExcept(int slot)
    {
        bool[] ready = [true, true, true, true];
        ready[slot] = false;
        return ready;
    }

    [Fact]
    public void When_the_ultimate_turns_ready_mid_charge_it_switches_to_burst_right_away()
    {
        var tracker = Gated(UltReady);
        Assert.Equal(0, tracker.Update(new Reading().ToState())!.Step.Index);

        var position = tracker.Update(new Reading { UltimateReady = Ult(true) }.ToState())!;

        Assert.Equal(4, position.Step.Index);
        Assert.Equal("爆发轴 III", position.Step.PhaseLabel);
        Assert.True(position.Ready);
    }

    [Fact]
    public void Burst_plays_to_its_end_after_the_ultimate_is_used_then_charging_starts_again()
    {
        var tracker = Gated(UltReady);
        tracker.Update(new Reading().ToState());
        tracker.Update(new Reading { UltimateReady = Ult(true) }.ToState());

        var afterUlt = tracker.Update(new Reading().ToState())!; // ultimate used: the gate is false now
        Assert.Equal(5, afterUlt.Step.Index); // still in burst: Ardelia's combo

        var afterBurst = tracker.Update(new Reading { ComboReady = CombosExcept(3) }.ToState())!; // Ardelia's combo used
        Assert.Equal(0, afterBurst.Step.Index);
    }

    [Fact]
    public void Without_the_ultimate_it_charges_twice_and_again()
    {
        var tracker = Gated(UltReady);
        var seen = new List<int> { tracker.Update(new Reading().ToState())!.Step.Index };

        // Each ability in turn: used, then ready again.
        seen.Add(tracker.Update(new Reading { Skill = Cooling(3) }.ToState())!.Step.Index);       // Ardelia's battle skill
        seen.Add(tracker.Update(new Reading { ComboReady = CombosExcept(1) }.ToState())!.Step.Index); // Camille's combo
        seen.Add(tracker.Update(new Reading { ComboReady = CombosExcept(2) }.ToState())!.Step.Index); // Arcane's combo
        seen.Add(tracker.Update(new Reading { Skill = Cooling(0) }.ToState())!.Step.Index);       // Laevatain's battle skill

        Assert.Equal([0, 1, 2, 3, 0], seen);
    }

    [Fact]
    public void A_fight_that_starts_with_the_ultimate_ready_starts_in_burst()
    {
        var position = Gated(UltReady).Update(new Reading { UltimateReady = Ult(true) }.ToState())!;

        Assert.Equal(4, position.Step.Index);
    }

    [Fact]
    public void An_unseen_ultimate_neither_opens_nor_closes_the_gate()
    {
        var tracker = Gated(UltReady);
        tracker.Update(new Reading().ToState());

        Assert.Equal(0, tracker.Update(new Reading { UltimateSeen = false }.ToState())!.Step.Index);
        Assert.Equal(4, tracker.Update(new Reading { UltimateReady = Ult(true) }.ToState())!.Step.Index);
    }

    [Fact]
    public void After_burst_a_gate_that_stays_true_does_not_send_it_straight_back()
    {
        // A gate that stays true after the phase: 3 skill points. Unseen readings in between don't count as a new start.
        var tracker = Gated(new SkillPointsCondition(Comparison.AtLeast, 3));
        tracker.Update(new Reading(SkillPoints: 1).ToState());
        Assert.Equal(4, tracker.Update(new Reading(SkillPoints: 3) { UltimateReady = Ult(true) }.ToState())!.Step.Index);
        tracker.Update(new Reading(SkillPoints: 3).ToState()); // ultimate used
        Assert.Equal(0, tracker.Update(new Reading(SkillPoints: 3) { ComboReady = CombosExcept(3) }.ToState())!.Step.Index);

        Assert.Equal(0, tracker.Update(new Reading(SkillPoints: 3).ToState())!.Step.Index);
        Assert.Equal(0, tracker.Update(new Reading(SkillPoints: null).ToState())!.Step.Index);
        Assert.Equal(0, tracker.Update(new Reading(SkillPoints: 3).ToState())!.Step.Index);

        // Seen false, then true again: a new start.
        tracker.Update(new Reading(SkillPoints: 2).ToState());
        Assert.Equal(4, tracker.Update(new Reading(SkillPoints: 3).ToState())!.Step.Index);
    }

    [Theory]
    [InlineData("any", 3.0, false, true)]  // skill points alone open an "any"
    [InlineData("any", 1.0, true, true)]   // the ultimate alone too
    [InlineData("any", 1.0, false, false)]
    [InlineData("all", 3.0, false, false)] // an "all" needs both
    [InlineData("all", 3.0, true, true)]
    public void Entry_conditions_can_be_and_or_or(string group, double skillPoints, bool ult, bool entersBurst)
    {
        StateCondition[] parts = [new SkillPointsCondition(Comparison.AtLeast, 3), UltReady];
        var tracker = Gated(group == "any" ? new AnyCondition(parts) : new AllCondition(parts));
        tracker.Update(new Reading(SkillPoints: 1).ToState());

        var position = tracker.Update(new Reading(SkillPoints: skillPoints) { UltimateReady = Ult(ult) }.ToState())!;

        Assert.Equal(entersBurst, position.Step.Index == 4);
    }

    [Fact]
    public void An_any_inside_an_all_holds_when_one_part_is_seen_true_and_another_unseen()
    {
        // all(any(ultimate ready [unseen], 3 skill points [true]), Camille's combo ready [true])
        var gate = new AllCondition([new AnyCondition([UltReady, new SkillPointsCondition(Comparison.AtLeast, 3)]), new ComboCondition("Camille", true)]);
        var tracker = Gated(gate);
        tracker.Update(new Reading(SkillPoints: 1).ToState());

        Assert.Equal(4, tracker.Update(new Reading(SkillPoints: 3) { UltimateSeen = false }.ToState())!.Step.Index);
    }

    [Fact]
    public void Inside_burst_a_charge_step_that_matches_better_cannot_pull_it_back()
    {
        // The first charge step expects things that all hold, so it outscores a burst step that's waiting.
        var strongCharge = new RotationStep("Ardelia", StepAction.BattleSkill,
            [new SkillPointsCondition(Comparison.AtLeast, 2), new ControllingCondition("Laevatain"), new ComboCondition("Camille", true)]);
        var tracker = Gated(UltReady, strongCharge);
        tracker.Update(new Reading().ToState());
        tracker.Update(new Reading { UltimateReady = Ult(true) }.ToState());
        // Ultimate used, while Ardelia's combo is still cooling: on her combo (step 5), waiting.
        Assert.Equal(5, tracker.Update(new Reading { ComboReady = CombosExcept(3) }.ToState())!.Step.Index);

        // Her combo not ready yet, for a while.
        RotationPosition? position = null;
        for (int i = 0; i < 6; i++) position = tracker.Update(new Reading { ComboReady = CombosExcept(3) }.ToState());

        Assert.Equal(5, position!.Step.Index);
    }

    [Fact]
    public void A_phase_behind_a_condition_in_the_middle_of_the_list_is_never_reached_by_the_flow()
    {
        // 充能轴 A, 爆发轴 (when 3 skill points), 充能轴 B: with the gate true all along, A → B → A.
        var tracker = new RotationTracker(new Rotation(Rotation.CurrentFormat, "mid", "中间", Squad,
        [
            new RotationPhase("a", "充能轴", [new RotationStep("Camille", StepAction.Combo)]),
            new RotationPhase("burst", "爆发轴", [new RotationStep("Laevatain", StepAction.Ultimate)], [new SkillPointsCondition(Comparison.AtLeast, 3)]),
            new RotationPhase("b", "充能轴", [new RotationStep("Arcane", StepAction.Combo)]),
        ]));
        tracker.Update(new Reading(SkillPoints: 1).ToState());
        Assert.Equal(1, tracker.Update(new Reading(SkillPoints: 3) { UltimateReady = Ult(true) }.ToState())!.Step.Index); // entered by its gate
        Assert.Equal(2, tracker.Update(new Reading(SkillPoints: 3).ToState())!.Step.Index);        // ultimate used → B
        Assert.Equal(0, tracker.Update(new Reading(SkillPoints: 3) { ComboReady = CombosExcept(2) }.ToState())!.Step.Index); // → A, not burst
        Assert.Equal(2, tracker.Update(new Reading(SkillPoints: 3) { ComboReady = CombosExcept(1) }.ToState())!.Step.Index); // → B again
    }

    [Fact]
    public void A_rotation_of_one_phase_loops_on_itself()
    {
        var tracker = new RotationTracker(new Rotation(Rotation.CurrentFormat, "one", "单轴", Squad,
            [new RotationPhase("only", "充能轴", [new RotationStep("Camille", StepAction.Combo), new RotationStep("Arcane", StepAction.Combo)])]));
        tracker.Update(new Reading().ToState());

        Assert.Equal(1, tracker.Update(new Reading { ComboReady = CombosExcept(1) }.ToState())!.Step.Index);
        Assert.Equal(0, tracker.Update(new Reading { ComboReady = CombosExcept(2) }.ToState())!.Step.Index);
    }

    /// <summary>"Below" is strictly less: "skill points below 2" holds at 1.95 and not at 2 (format 4).</summary>
    [Fact]
    public void Below_two_skill_points_holds_at_1_95_and_not_at_2()
    {
        var below = new SkillPointsCondition(Comparison.Below, 2);
        var above = new SkillPointsCondition(Comparison.Above, 2);

        Assert.True(RotationTracker.Evaluate(below, new Reading(SkillPoints: 1.95).ToState(), PhaseHistory.None));
        Assert.False(RotationTracker.Evaluate(below, new Reading(SkillPoints: 2).ToState(), PhaseHistory.None));
        Assert.False(RotationTracker.Evaluate(above, new Reading(SkillPoints: 2).ToState(), PhaseHistory.None));
        Assert.True(RotationTracker.Evaluate(above, new Reading(SkillPoints: 2.05).ToState(), PhaseHistory.None));
        Assert.Null(RotationTracker.Evaluate(below, new Reading(SkillPoints: null).ToState(), PhaseHistory.None));
    }

    /// <summary>Outside a phase selector (alerts, explanations) nobody counts phases: "shown" is unknown, never a guessed 0.</summary>
    [Fact]
    public void Phase_shown_is_unknown_without_a_history()
    {
        Assert.Null(RotationTracker.Evaluate(new PhaseShownCondition("charge", Comparison.AtLeast, 1), new Reading().ToState(), PhaseHistory.None));
    }
}
