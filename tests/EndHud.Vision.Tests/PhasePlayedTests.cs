using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// "Phase X played [cmp] N times" (owner, 2026-10-23): how many times the player finished every step
/// of a phase this fight, as the step follower follows them from the screen (battle skills cast, combos
/// and ultimates seen used). Squad slots: Laevatain (1), Camille (2), Arcane (3), Ardelia (4).
/// </summary>
public sealed class PhasePlayedTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];
    private const int Laevatain = 1, Ardelia = 4;

    /// <summary>Charge: Ardelia's battle skill, then Laevatain's. Burst: Laevatain's battle skill, gated.</summary>
    private static Rotation TwoPhases(params StateCondition[] burstGate) => new(Rotation.CurrentFormat, "r", "r", Squad,
    [
        new RotationPhase("charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill)]),
        new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.BattleSkill)], burstGate),
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

    private static readonly PartyState Plain = Reading();

    /// <summary>The overlay's order per reading: the selector (seeing the counts so far), then the step follower inside its phase.</summary>
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

        public string Press(params int[] slots) => Read(Plain, slots);
    }

    // ---- Counting ----

    [Fact]
    public void Each_round_of_a_phase_played_through_counts_once()
    {
        var fight = new Fight(TwoPhases(new UltimateCondition("Laevatain", true)));
        fight.Read(Plain);
        Assert.Equal(0, fight.Steps.TimesPlayed("charge"));

        fight.Press(Ardelia);
        Assert.Equal(0, fight.Steps.TimesPlayed("charge")); // half way
        fight.Press(Laevatain);
        Assert.Equal(1, fight.Steps.TimesPlayed("charge"));
        fight.Press(Ardelia);
        Assert.Equal(1, fight.Steps.TimesPlayed("charge"));
        fight.Press(Laevatain);
        Assert.Equal(2, fight.Steps.TimesPlayed("charge"));
        Assert.Equal(0, fight.Steps.TimesPlayed("burst"));
        Assert.Null(fight.Steps.TimesPlayed("gone"));
    }

    /// <summary>A phase ending in a basic attack (never seen) is played through when its last skill is used; its next round counts again.</summary>
    [Fact]
    public void A_phase_ending_in_a_basic_attack_counts_when_its_last_skill_is_used()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Arcane", StepAction.BasicAttack)])]);
        var fight = new Fight(rotation);
        fight.Read(Plain);

        fight.Press(Ardelia);
        Assert.Equal(1, fight.Steps.TimesPlayed("p"));
        fight.Read(Plain);
        Assert.Equal(1, fight.Steps.TimesPlayed("p")); // waiting on the basic attack: not counted twice
        fight.Press(Ardelia);
        Assert.Equal(2, fight.Steps.TimesPlayed("p"));
    }

    /// <summary>A trailing heavy attack (never seen, as a basic attack) doesn't hold up "phase played".</summary>
    [Fact]
    public void A_phase_ending_in_a_heavy_attack_counts_when_its_last_skill_is_used()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
            [new RotationPhase("p", "P", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Arcane", StepAction.HeavyAttack)])]);
        var fight = new Fight(rotation);
        fight.Read(Plain);

        fight.Press(Ardelia);
        Assert.Equal(1, fight.Steps.TimesPlayed("p"));
        fight.Press(Ardelia);
        Assert.Equal(2, fight.Steps.TimesPlayed("p"));
    }

    [Fact]
    public void Leaving_a_phase_half_way_does_not_count_it()
    {
        var fight = new Fight(TwoPhases(new SkillPointsCondition(Comparison.AtLeast, 3)));
        fight.Read(Plain);
        fight.Press(Ardelia);

        Assert.Equal("Burst II", fight.Read(Reading(3)));
        Assert.Equal("Charge I", fight.Read(Plain));
        Assert.Equal(0, fight.Steps.TimesPlayed("charge")); // the half round left isn't counted

        // Charge came back with every light out: Ardelia's cast from before doesn't count for this round.
        fight.Press(Laevatain);
        Assert.Equal(0, fight.Steps.TimesPlayed("charge"));
        fight.Press(Ardelia);
        Assert.Equal(1, fight.Steps.TimesPlayed("charge"));
    }

    /// <summary>Ardelia, Laevatain, Camille's battle skills.</summary>
    private static readonly Rotation Three = new(Rotation.CurrentFormat, "r", "r", Squad,
        [new RotationPhase("p", "P", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill), new RotationStep("Camille", StepAction.BattleSkill)])]);

    private const int Camille = 2;

    /// <summary>
    /// Owner, 2026-10-24 (the light-up model): a round counts when every step that can be seen used is lit, in any
    /// order; no missed cast is allowed for, and the last step played first doesn't end it.
    /// </summary>
    [Fact]
    public void A_round_counts_only_once_every_cast_step_is_lit_in_any_order()
    {
        var fight = new Fight(Three);
        fight.Read(Plain);
        fight.Press(Camille); // the last step first
        Assert.Equal(0, fight.Steps.TimesPlayed("p"));
        fight.Press(Ardelia);
        Assert.Equal(0, fight.Steps.TimesPlayed("p")); // Laevatain's not seen yet
        fight.Press(Laevatain);
        Assert.Equal(1, fight.Steps.TimesPlayed("p"));
    }

    /// <summary>A phase's first step cast again with a step still unlit doesn't start the phase over or count the round.</summary>
    [Fact]
    public void Casting_the_first_step_again_neither_starts_over_nor_counts()
    {
        var fight = new Fight(Three);
        fight.Read(Plain);
        fight.Press(Ardelia);
        fight.Press(Laevatain);
        fight.Press(Ardelia); // Camille's not seen: no round over
        Assert.Equal(0, fight.Steps.TimesPlayed("p"));
        fight.Press(Camille);
        Assert.Equal(1, fight.Steps.TimesPlayed("p"));
    }

    [Fact]
    public void Counts_go_back_to_0_when_the_fight_ends()
    {
        var fight = new Fight(TwoPhases(new UltimateCondition("Laevatain", true)));
        fight.Read(Plain);
        fight.Press(Ardelia);
        fight.Press(Laevatain);
        Assert.Equal(1, fight.Steps.TimesPlayed("charge"));

        fight.Steps.EndFight();

        Assert.Equal(0, fight.Steps.TimesPlayed("charge"));
    }

    // ---- Holding ----

    /// <summary>The owner's use: Burst once Charge has been played twice.</summary>
    [Fact]
    public void A_gate_on_another_phases_plays_opens_once_it_was_played_so_often()
    {
        var fight = new Fight(TwoPhases(new PhasePlayedCondition("charge", Comparison.AtLeast, 2)));
        fight.Read(Plain);
        fight.Press(Ardelia);
        fight.Press(Laevatain);
        fight.Press(Ardelia);
        Assert.Equal("Charge I", fight.Press(Laevatain)); // the second round ends on this reading

        Assert.Equal("Burst II", fight.Read(Plain));
    }

    [Fact]
    public void Each_comparison_holds_as_it_says()
    {
        var fight = new Fight(TwoPhases());
        fight.Read(Plain);
        fight.Press(Ardelia);
        fight.Press(Laevatain);

        bool? Holds(Comparison c, int n) => RotationTracker.Evaluate(new PhasePlayedCondition("charge", c, n), Plain, fight.Selector);
        Assert.True(Holds(Comparison.AtLeast, 1));
        Assert.False(Holds(Comparison.AtLeast, 2));
        Assert.True(Holds(Comparison.Exactly, 1));
        Assert.True(Holds(Comparison.Below, 2));
        Assert.False(Holds(Comparison.Above, 1));
        Assert.True(Holds(Comparison.AtMost, 1));
    }

    [Fact]
    public void Unknown_outside_a_followed_rotation()
    {
        Assert.Null(RotationTracker.Evaluate(new PhasePlayedCondition("charge", Comparison.AtLeast, 0), Plain, PhaseHistory.None));
        Assert.Null(RotationTracker.Evaluate(new PhasePlayedCondition("gone", Comparison.AtLeast, 0), Plain, new Fight(TwoPhases()).Selector.Also(Plain)));
    }

    // ---- Alerts ----

    [Fact]
    public void An_alert_fires_once_the_phase_was_played_so_often()
    {
        var fight = new Fight(TwoPhases(new UltimateCondition("Laevatain", true)));
        var monitor = new AlertMonitor();
        var rule = AlertRule.New([new PhasePlayedCondition("charge", Comparison.AtLeast, 1)], "");
        fight.Read(Plain);
        Assert.Empty(monitor.Update(Plain, [rule], fight.Selector));
        fight.Press(Ardelia);
        fight.Press(Laevatain);

        Assert.Equal([rule], monitor.Update(Plain, [rule], fight.Selector));
    }
}

internal static class SelectorTestExtensions
{
    /// <summary>A selector that has taken one reading with its step follower (so "played" is known).</summary>
    public static PhaseSelector Also(this PhaseSelector selector, PartyState state)
    {
        selector.Update(state, new StepFollower(selector.Rotation));
        return selector;
    }
}
