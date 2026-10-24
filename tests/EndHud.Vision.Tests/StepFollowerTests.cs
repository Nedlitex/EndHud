using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Following the step inside the phase the strip shows. Squad slots: Laevatain (1), Camille (2),
/// Arcane (3), Ardelia (4). The charge phase: Ardelia's battle skill, Laevatain's battle skill,
/// Camille's combo, an Arcane basic attack, Ardelia's battle skill, Laevatain's ultimate.
/// </summary>
public sealed class StepFollowerTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];
    private const int Laevatain = 1, Camille = 2, Ardelia = 4;

    private static readonly Rotation Charge = new(Rotation.CurrentFormat, "r", "r", Squad,
    [
        new RotationPhase("charge", "Charge",
        [
            new RotationStep("Ardelia", StepAction.BattleSkill),
            new RotationStep("Laevatain", StepAction.BattleSkill),
            new RotationStep("Camille", StepAction.Combo),
            new RotationStep("Arcane", StepAction.BasicAttack),
            new RotationStep("Ardelia", StepAction.BattleSkill),
            new RotationStep("Laevatain", StepAction.Ultimate),
        ]),
        new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate), new RotationStep("Ardelia", StepAction.BattleSkill)],
            [new UltimateCondition("Laevatain", true)]),
    ]);

    /// <summary>A reading. Defaults: every combo ready, no ultimate ready, every battle skill available, 2 skill points.</summary>
    private sealed record Reading(double? SkillPoints = 2.0)
    {
        public bool[] ComboReady { get; init; } = [true, true, true, true];
        public BattleSkillState?[] Skill { get; init; } = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available];

        public PartyState ToState()
        {
            var slots = Enumerable.Range(0, 4).Select(i => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
                Combo: new ComboReading(ComboReady[i] ? 1 : 0.02, ComboReady[i]),
                Ultimate: new UltimateReading(false, 0.2),
                Operator: new OperatorMatch(new OperatorInfo(Squad[i], Squad[i], Squad[i], Squad[i] + ".png"), 1, true))
            {
                BattleSkill = Skill[i],
            }).ToList();
            return new PartyState(slots, PanelMarks: 8) { SkillPoints = SkillPoints, ActiveSlot = 1 };
        }
    }

    private static readonly PartyState Plain = new Reading().ToState();

    /// <summary>Drives a follower reading by reading, as the overlay does: each reading after the one before.</summary>
    private sealed class Fight(Rotation rotation)
    {
        public StepFollower Follower { get; } = new(rotation);
        private PartyState? _before;

        public StepPosition Read(PartyState now, int phase = 0, params int[] presses)
        {
            var position = Follower.Update(phase, _before, now, Casts.Seen(presses), PhaseHistory.None);
            _before = now;
            return position;
        }

        public StepPosition Press(params int[] slots) => Read(Plain, 0, slots);

        /// <summary>A reading with these casts as the judge decided them.</summary>
        public StepPosition Cast(PartyState now, params SkillCast[] casts)
        {
            var position = Follower.Update(0, _before, now, casts, PhaseHistory.None);
            _before = now;
            return position;
        }
    }

    private const StepMark D = StepMark.Done, C = StepMark.Current, U = StepMark.Upcoming, S = StepMark.Skipped;

    private static SkillCast Certain(int slot, double at = 0) => new(slot, TimeSpan.FromSeconds(at), CastSource.Flash, Paid: true);

    private static PartyState At(double seconds, PartyState? state = null) => (state ?? Plain) with { ReadAt = TimeSpan.FromSeconds(seconds) };

    [Fact]
    public void At_the_start_the_first_step_is_current()
    {
        var fight = new Fight(Charge);
        Assert.Equal([C, U, U, U, U, U], fight.Read(Plain).Marks);
    }

    [Fact]
    public void A_counted_press_marks_its_step_done_and_moves_on()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        Assert.Equal([D, C, U, U, U, U], fight.Press(Ardelia).Marks);
        Assert.Equal([D, D, C, U, U, U], fight.Press(Laevatain).Marks);
    }

    [Fact]
    public void A_press_of_another_slot_moves_nothing()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        Assert.Equal([C, U, U, U, U, U], fight.Press(Camille).Marks); // Camille has no battle-skill step here
    }

    /// <summary>Owner, 2026-10-24: a cast seen for certain of a later step jumps there; the steps passed are skipped.</summary>
    [Fact]
    public void A_certain_cast_of_a_later_step_jumps_there_and_skips_the_steps_passed()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        Assert.Equal([S, D, C, U, U, U], fight.Press(Laevatain).Marks); // step 2 while step 1 (Ardelia) is undone
    }

    [Fact]
    public void A_cast_not_seen_for_certain_only_marks_the_current_group()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        // An empowered cast with no point spent, or a press while the points couldn't be read.
        Assert.Equal([C, U, U, U, U, U], fight.Cast(Plain, new SkillCast(Laevatain, TimeSpan.Zero, CastSource.Empowered, Paid: false)).Marks);
        Assert.Equal([D, C, U, U, U, U], fight.Cast(Plain, new SkillCast(Ardelia, TimeSpan.Zero, CastSource.Press, Paid: false)).Marks);
    }

    /// <summary>The owner's example: the battle skill wasn't tracked, the combo is seen: jump to the combo.</summary>
    [Fact]
    public void A_combo_seen_with_the_battle_skills_before_it_untracked_jumps_to_the_combo()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        var position = fight.Read(new Reading { ComboReady = [true, false, true, true] }.ToState());

        Assert.Equal([S, S, D, C, U, U], position.Marks); // the basic attack after it is current
    }

    [Fact]
    public void A_jump_passes_basic_attacks_as_done_and_goes_to_the_nearest_later_copy()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        fight.Press(Ardelia);

        // Ardelia again: step 5 (the nearest copy after step 1); Laevatain and Camille's combo skipped, the basic attack done.
        Assert.Equal([D, S, S, D, D, C], fight.Press(Ardelia).Marks);
    }

    [Fact]
    public void A_cast_told_late_fills_in_the_step_a_later_use_jumped_past()
    {
        var fight = new Fight(Charge);
        fight.Read(At(0));
        fight.Press(Ardelia);
        // The combo is seen at 1.0 s; Laevatain's cast (at 0.9 s) is only named at the reading after, by its flash.
        Assert.Equal([D, S, D, C, U, U], fight.Read(At(1.0, new Reading { ComboReady = [true, false, true, true] }.ToState())).Marks);
        Assert.Equal([D, D, D, C, U, U], fight.Cast(At(1.25, new Reading { ComboReady = [true, false, true, true] }.ToState()), Certain(Laevatain, at: 0.9)).Marks);
    }

    [Fact]
    public void Points_spent_by_nobody_named_mark_the_current_battle_skill_if_its_operator_could_cast()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        var nobody = new SkillCast(null, TimeSpan.Zero, CastSource.Unattributed, Paid: true);

        Assert.Equal([D, C, U, U, U, U], fight.Cast(Plain, nobody).Marks);
        // Laevatain's button cooling in the reading before: it wasn't her.
        var cooling = new Reading { Skill = [BattleSkillState.Cooling, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available] }.ToState();
        fight.Cast(cooling);
        Assert.Equal([D, C, U, U, U, U], fight.Cast(cooling, nobody).Marks);
    }

    [Fact]
    public void Points_spent_by_nobody_named_never_mark_a_combo_step()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        fight.Press(Ardelia);
        fight.Press(Laevatain);

        Assert.Equal([D, D, C, U, U, U], fight.Cast(Plain, new SkillCast(null, TimeSpan.Zero, CastSource.Unattributed, Paid: true)).Marks);
    }

    [Fact]
    public void A_combo_seen_used_marks_a_combo_step_without_any_press()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        fight.Press(Ardelia);
        fight.Press(Laevatain);

        var position = fight.Read(new Reading { ComboReady = [true, false, true, true] }.ToState());

        Assert.Equal([D, D, D, C, U, U], position.Marks);
    }

    /// <summary>The press is the cast: the button seen cooling after it must not count the same cast again.</summary>
    [Fact]
    public void A_cooldown_seen_on_the_button_does_not_mark_a_battle_skill_step()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        var position = fight.Read(new Reading { Skill = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Cooling] }.ToState());

        Assert.Equal([C, U, U, U, U, U], position.Marks);
    }

    [Fact]
    public void A_basic_attack_step_is_passed_by_the_next_real_cast()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        fight.Press(Ardelia);
        fight.Press(Laevatain);
        var combo = new Reading { ComboReady = [true, false, true, true] }.ToState();
        Assert.Equal([D, D, D, C, U, U], fight.Read(combo).Marks); // on the basic attack
        Assert.True(fight.Read(combo).Ready); // a basic attack never waits

        Assert.Equal([D, D, D, D, D, C], fight.Read(combo, 0, Ardelia).Marks);
    }

    /// <summary>The owner's ask (2026-10-24): a heavy attack is ignored when following, as a basic attack is.</summary>
    [Fact]
    public void A_heavy_attack_step_never_waits_and_is_passed_by_the_next_real_cast()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P",
            [
                new RotationStep("Ardelia", StepAction.BattleSkill),
                new RotationStep("Arcane", StepAction.HeavyAttack),
                new RotationStep("Laevatain", StepAction.BattleSkill),
                new RotationStep("Ardelia", StepAction.BattleSkill),
            ])]);
        var fight = new Fight(rotation);
        fight.Read(Plain);

        var onHeavy = fight.Press(Ardelia);
        Assert.Equal([D, C, U, U], onHeavy.Marks);
        Assert.True(onHeavy.Ready); // a heavy attack never waits
        Assert.Equal([D, D, D, C], fight.Press(Laevatain).Marks);
    }

    [Fact]
    public void A_phase_ending_in_basic_attacks_starts_over_on_its_first_cast()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Arcane", StepAction.BasicAttack)])]);
        var fight = new Fight(rotation);
        fight.Read(Plain);
        Assert.Equal([D, C], fight.Press(Ardelia).Marks);

        Assert.Equal([D, C], fight.Press(Ardelia).Marks); // the next round's first step
    }

    [Fact]
    public void A_phase_played_through_starts_again_at_its_first_step()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill)])]);
        var fight = new Fight(rotation);
        fight.Read(Plain);
        fight.Press(Ardelia);

        Assert.Equal([C, U], fight.Press(Laevatain).Marks);
        Assert.Equal([D, C], fight.Press(Ardelia).Marks);
    }

    [Fact]
    public void Steps_used_together_may_come_in_either_order()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("p", "P",
            [
                new RotationStep("Ardelia", StepAction.BattleSkill),
                new RotationStep("Camille", StepAction.Combo),
                new RotationStep("Arcane", StepAction.Combo, WithPrevious: true),
                new RotationStep("Laevatain", StepAction.BattleSkill),
            ]),
        ]);
        var fight = new Fight(rotation);
        fight.Read(Plain);
        Assert.Equal([D, C, C, U], fight.Press(Ardelia).Marks); // both of the group are current

        var arcaneUsed = new Reading { ComboReady = [true, true, false, true] }.ToState();
        Assert.Equal([D, C, D, U], fight.Read(arcaneUsed).Marks);
        var camilleUsed = new Reading { ComboReady = [true, false, false, true] }.ToState();
        Assert.Equal([D, D, D, C], fight.Read(camilleUsed).Marks);
    }

    [Fact]
    public void A_new_phase_starts_at_its_first_step()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        Assert.Equal([D, C, U, U, U, U], fight.Press(Ardelia).Marks);

        Assert.Equal([C, U], fight.Read(Plain, phase: 1).Marks);
        Assert.Equal(1, fight.Read(Plain, phase: 1).Phase);
        Assert.Equal([C, U, U, U, U, U], fight.Read(Plain, phase: 0).Marks);
    }

    [Fact]
    public void The_current_step_waits_with_its_reason_when_out_of_skill_points()
    {
        var fight = new Fight(Charge);
        var position = fight.Read(new Reading(SkillPoints: 0.4).ToState());

        Assert.False(position.Ready);
        Assert.Contains(position.Checks, c => c.Kind == CheckKind.OwnAbility && c.Holds == false);
        Assert.True(fight.Read(Plain).Ready);
    }

    [Fact]
    public void An_expected_state_that_does_not_hold_keeps_the_step_waiting()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P", [new RotationStep("Ardelia", StepAction.BattleSkill, [new ComboCondition("Camille", false)])])]);
        var position = new Fight(rotation).Read(Plain);

        Assert.False(position.Ready);
        Assert.Contains(position.Checks, c => c.Kind == CheckKind.Expected && c.Holds == false);
    }

    [Fact]
    public void Ending_the_fight_starts_over()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        Assert.Equal([D, C, U, U, U, U], fight.Press(Ardelia).Marks);

        fight.Follower.EndFight();

        Assert.Equal([C, U, U, U, U, U], fight.Read(Plain).Marks);
    }
}
