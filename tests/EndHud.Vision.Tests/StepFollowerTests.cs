using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Following the steps inside the phase the strip shows, by lighting them up (owner, 2026-10-24: "skip over is bad
/// ... a rotation is fixed order but in reality it can be played in different orders"): each step lights when its
/// use is seen, in any order; nothing is ever skipped. Squad slots: Laevatain (1), Camille (2), Arcane (3),
/// Ardelia (4). The charge phase: Ardelia's battle skill, Laevatain's battle skill, Camille's combo, an Arcane basic
/// attack, Ardelia's battle skill, Laevatain's ultimate.
/// </summary>
public sealed class StepFollowerTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];
    private const int Laevatain = 1, Camille = 2, Arcane = 3, Ardelia = 4;

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
        public bool[] UltimateReady { get; init; } = [false, false, false, false];
        public BattleSkillState?[] Skill { get; init; } = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available];

        public PartyState ToState()
        {
            var slots = Enumerable.Range(0, 4).Select(i => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
                Combo: new ComboReading(ComboReady[i] ? 1 : 0.02, ComboReady[i]),
                Ultimate: new UltimateReading(UltimateReady[i], UltimateReady[i] ? 1 : 0.2),
                Operator: new OperatorMatch(new OperatorInfo(Squad[i], Squad[i], Squad[i], Squad[i] + ".png"), 1, true))
            {
                BattleSkill = Skill[i],
            }).ToList();
            return new PartyState(slots, PanelMarks: 8) { SkillPoints = SkillPoints, ActiveSlot = 1 };
        }
    }

    private static readonly PartyState Plain = new Reading().ToState();

    /// <summary>A reading where this slot's combo was just used (its bar emptied).</summary>
    private static PartyState ComboUsed(int slot) => new Reading { ComboReady = [.. Enumerable.Range(1, 4).Select(s => s != slot)] }.ToState();

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

    private const StepMark L = StepMark.Lit, C = StepMark.Current, U = StepMark.Unlit, N = StepMark.Neutral;

    [Fact]
    public void At_the_start_the_first_step_is_current_and_the_basic_attack_is_neutral()
    {
        var fight = new Fight(Charge);
        Assert.Equal([C, U, U, N, U, U], fight.Read(Plain).Marks);
    }

    [Fact]
    public void A_cast_lights_its_step_and_the_chevron_moves_to_the_first_unlit_one()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        Assert.Equal([L, C, U, N, U, U], fight.Press(Ardelia).Marks);
        Assert.Equal([L, L, C, N, U, U], fight.Press(Laevatain).Marks);
    }

    [Fact]
    public void A_press_of_another_slot_lights_nothing()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        Assert.Equal([C, U, U, N, U, U], fight.Press(Camille).Marks); // Camille has no battle-skill step here
    }

    /// <summary>
    /// The owner (2026-10-24): "skip over is bad". A later step used first lights that step only; the steps before it
    /// stay unlit, the chevron stays on the first of them, and nothing is ever marked skipped.
    /// </summary>
    [Fact]
    public void A_later_step_used_first_lights_only_itself_and_skips_nothing()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        Assert.Equal([C, L, U, N, U, U], fight.Press(Laevatain).Marks);
        Assert.Equal([C, L, L, N, U, U], fight.Read(ComboUsed(Camille)).Marks);
        Assert.Equal([L, L, L, N, C, U], fight.Press(Ardelia).Marks); // the earlier one, now seen
    }

    /// <summary>A use lights the first unlit step of the phase with the same operator and action.</summary>
    [Fact]
    public void The_same_skill_twice_lights_its_first_unlit_step_then_the_next()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        Assert.Equal([L, C, U, N, U, U], fight.Press(Ardelia).Marks);
        Assert.Equal([L, C, U, N, L, U], fight.Press(Ardelia).Marks);
        Assert.Equal([L, C, U, N, L, U], fight.Press(Ardelia).Marks); // no third Ardelia step: nothing
    }

    [Fact]
    public void A_combo_or_an_ultimate_seen_used_lights_its_step_in_any_order()
    {
        var fight = new Fight(Charge);
        fight.Read(new Reading { UltimateReady = [true, false, false, false] }.ToState());

        Assert.Equal([C, U, U, N, U, L], fight.Read(Plain).Marks);         // Laevatain's ultimate spent
        Assert.Equal([C, U, L, N, U, L], fight.Read(ComboUsed(Camille)).Marks);
    }

    /// <summary>A cast not certain (an empowered one with no point spent, a press while the points weren't read) is still a cast of that slot.</summary>
    [Fact]
    public void A_cast_not_certain_still_lights_its_step()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        Assert.Equal([C, L, U, N, U, U], fight.Cast(Plain, new SkillCast(Laevatain, TimeSpan.Zero, CastSource.Empowered, Paid: false)).Marks);
        Assert.Equal([L, L, C, N, U, U], fight.Cast(Plain, new SkillCast(Ardelia, TimeSpan.Zero, CastSource.Press, Paid: false)).Marks);
    }

    [Fact]
    public void Points_spent_by_nobody_named_light_the_chevrons_battle_skill_if_its_operator_could_cast()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        var nobody = new SkillCast(null, TimeSpan.Zero, CastSource.Unattributed, Paid: true);

        Assert.Equal([L, C, U, N, U, U], fight.Cast(Plain, nobody).Marks);
        // Laevatain's button cooling in the reading before: it wasn't her.
        var cooling = new Reading { Skill = [BattleSkillState.Cooling, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available] }.ToState();
        fight.Cast(cooling);
        Assert.Equal([L, C, U, N, U, U], fight.Cast(cooling, nobody).Marks);
    }

    [Fact]
    public void Points_spent_by_nobody_named_never_light_a_combo_step()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        fight.Press(Ardelia);
        fight.Press(Laevatain);

        Assert.Equal([L, L, C, N, U, U], fight.Cast(Plain, new SkillCast(null, TimeSpan.Zero, CastSource.Unattributed, Paid: true)).Marks);
    }

    /// <summary>The press is the cast: the button seen cooling after it must not count the same cast again.</summary>
    [Fact]
    public void A_cooldown_seen_on_the_button_does_not_light_a_battle_skill_step()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);

        var position = fight.Read(new Reading { Skill = [BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Available, BattleSkillState.Cooling] }.ToState());

        Assert.Equal([C, U, U, N, U, U], position.Marks);
    }

    /// <summary>A basic attack is never seen: the chevron passes over it to the next step that can be, and it stays in place, neutral.</summary>
    [Fact]
    public void The_chevron_passes_over_a_basic_attack()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        fight.Press(Ardelia);
        fight.Press(Laevatain);

        var position = fight.Read(ComboUsed(Camille));

        Assert.Equal([L, L, L, N, C, U], position.Marks);
        Assert.True(position.Ready); // Ardelia's battle skill, with 2 points
    }

    /// <summary>The owner's example: four ultimates of a burst, the last one played first. It must not be over.</summary>
    [Fact]
    public void Four_ultimates_in_any_order_finish_the_round_only_when_all_four_are_lit()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("burst", "Burst", [.. Squad.Select(id => new RotationStep(id, StepAction.Ultimate))])]);
        var fight = new Fight(rotation);
        bool[] ready = [true, true, true, true];
        fight.Read(new Reading { UltimateReady = ready }.ToState());

        foreach (int slot in new[] { Ardelia, Arcane, Laevatain })
        {
            ready[slot - 1] = false;
            fight.Read(new Reading { UltimateReady = [.. ready] }.ToState());
            Assert.Equal(0, fight.Follower.TimesPlayed("burst"));
        }
        // Laevatain's, Arcane's and Ardelia's lit, in the reverse of the written order; Camille's still to come.
        Assert.Equal([L, C, L, L], fight.Read(new Reading { UltimateReady = [.. ready] }.ToState()).Marks);

        ready[Camille - 1] = false;
        var last = fight.Read(new Reading { UltimateReady = ready }.ToState());

        Assert.Equal(1, fight.Follower.TimesPlayed("burst"));
        Assert.Equal([C, U, U, U], last.Marks); // played through: the lights go out for a new round
    }

    /// <summary>The default finish: every step that can be seen used lit. Then it counts once and starts a new round.</summary>
    [Fact]
    public void A_round_with_every_step_lit_counts_once_and_the_lights_go_out()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill)])]);
        var fight = new Fight(rotation);
        fight.Read(Plain);

        Assert.Equal([C, L], fight.Press(Laevatain).Marks);
        Assert.Equal(0, fight.Follower.TimesPlayed("p"));
        Assert.Equal([C, U], fight.Press(Ardelia).Marks);
        Assert.Equal(1, fight.Follower.TimesPlayed("p"));
        Assert.Equal([C, U], fight.Read(Plain).Marks);
        Assert.Equal(1, fight.Follower.TimesPlayed("p")); // once per round, not once per reading
    }

    [Fact]
    public void Steps_used_together_are_both_current_and_light_in_either_order()
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
        Assert.Equal([L, C, C, U], fight.Press(Ardelia).Marks); // both of the group are current

        Assert.Equal([L, C, L, U], fight.Read(ComboUsed(Arcane)).Marks);
        Assert.Equal([L, L, L, C], fight.Read(ComboUsed(Camille)).Marks);
    }

    [Fact]
    public void A_new_phase_starts_with_every_step_unlit()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        Assert.Equal([L, C, U, N, U, U], fight.Press(Ardelia).Marks);

        Assert.Equal([C, U], fight.Read(Plain, phase: 1).Marks);
        Assert.Equal(1, fight.Read(Plain, phase: 1).Phase);
        Assert.Equal([C, U, U, N, U, U], fight.Read(Plain, phase: 0).Marks);
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
    public void Ending_the_fight_puts_every_light_out()
    {
        var fight = new Fight(Charge);
        fight.Read(Plain);
        Assert.Equal([L, C, U, N, U, U], fight.Press(Ardelia).Marks);

        fight.Follower.EndFight();

        Assert.Equal([C, U, U, N, U, U], fight.Read(Plain).Marks);
    }
}
