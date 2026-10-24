using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// "Phase X played [cmp] N times" (owner, 2026-10-23): how many times the player finished every step
/// of a phase this fight, as the step follower follows them from the battle-skill presses. Only
/// counted while the player reads their keys; while they don't, the condition is ignored, as if it
/// weren't there, and an alert made of nothing else never fires. Squad slots: Laevatain (1),
/// Camille (2), Arcane (3), Ardelia (4).
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
    private sealed class Fight(Rotation rotation, bool keysRead = true)
    {
        public PhaseSelector Selector { get; } = new(rotation);
        public StepFollower Steps { get; } = new(rotation);
        private PartyState? _before;

        public string Read(PartyState now, params int[] presses)
        {
            var shown = Selector.Update(now, keysRead ? Steps : null);
            if (keysRead) Steps.Update(shown.Step.PhaseIndex, _before, now, Casts.Seen(presses), Selector);
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

        // Charge starts over at Ardelia; Laevatain's cast jumps past her: one missed cast in a round of two still counts.
        fight.Press(Laevatain);
        Assert.Equal(1, fight.Steps.TimesPlayed("charge"));
    }

    /// <summary>Ardelia, Laevatain, Camille's battle skills.</summary>
    private static readonly Rotation Three = new(Rotation.CurrentFormat, "r", "r", Squad,
        [new RotationPhase("p", "P", [new RotationStep("Ardelia", StepAction.BattleSkill), new RotationStep("Laevatain", StepAction.BattleSkill), new RotationStep("Camille", StepAction.BattleSkill)])]);

    private const int Camille = 2;

    /// <summary>Owner, 2026-10-24: "played through" allows one missed cast step per round.</summary>
    [Fact]
    public void A_round_with_one_cast_missed_is_played_through_and_with_two_is_not()
    {
        var one = new Fight(Three);
        one.Read(Plain);
        one.Press(Ardelia);
        one.Press(Camille); // Laevatain's cast not seen: skipped
        Assert.Equal(1, one.Steps.TimesPlayed("p"));

        var two = new Fight(Three);
        two.Read(Plain);
        two.Press(Camille); // Ardelia's and Laevatain's not seen
        Assert.Equal(0, two.Steps.TimesPlayed("p"));
    }

    [Fact]
    public void Starting_the_phase_over_counts_the_round_left_if_it_missed_at_most_one_cast()
    {
        var one = new Fight(Three);
        one.Read(Plain);
        one.Press(Ardelia);
        one.Press(Laevatain);
        one.Press(Ardelia); // Camille's last cast missed: the player started over
        Assert.Equal(1, one.Steps.TimesPlayed("p"));

        var two = new Fight(Three);
        two.Read(Plain);
        two.Press(Ardelia);
        two.Press(Ardelia); // two of three missed
        Assert.Equal(0, two.Steps.TimesPlayed("p"));
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

    // ---- Keys not read: ignored ----

    private static readonly PhasePlayedCondition Played = new("charge", Comparison.AtLeast, 5);
    private static readonly SkillPointsCondition Points = new(Comparison.AtLeast, 1.5);

    [Fact]
    public void Without_the_keys_it_is_dropped_from_an_and()
    {
        Assert.True(RotationTracker.Evaluate(new AllCondition([Played, Points]), Reading(2), PhaseHistory.KeysNotRead));
        Assert.False(RotationTracker.Evaluate(new AllCondition([Played, Points]), Reading(1), PhaseHistory.KeysNotRead));
    }

    [Fact]
    public void Without_the_keys_it_is_dropped_from_an_or()
    {
        Assert.False(RotationTracker.Evaluate(new AnyCondition([Played, Points]), Reading(1), PhaseHistory.KeysNotRead));
        Assert.False(RotationTracker.Evaluate(new AnyCondition([new NotCondition(Played), Points]), Reading(1), PhaseHistory.KeysNotRead));
        // A group left empty is dropped as well: here the "or" is the skill points alone.
        Assert.False(RotationTracker.Evaluate(new AnyCondition([new AllCondition([Played]), Points]), Reading(1), PhaseHistory.KeysNotRead));
    }

    [Fact]
    public void Without_the_keys_a_steps_expected_state_made_only_of_it_holds()
    {
        Assert.True(RotationTracker.Evaluate(Played, Plain, PhaseHistory.KeysNotRead));
        Assert.True(RotationTracker.Evaluate(new NotCondition(Played), Plain, PhaseHistory.KeysNotRead));
        Assert.True(RotationTracker.Ignored(new AnyCondition([Played, new NotCondition(Played)]), PhaseHistory.KeysNotRead));
        Assert.False(RotationTracker.Ignored(Played, PhaseHistory.None)); // keys read (or unknown): never ignored
        Assert.False(RotationTracker.Ignored(new AnyCondition([]), PhaseHistory.KeysNotRead)); // an empty group is a problem of its own
    }

    /// <summary>A gate of nothing but "played" never opens without the keys (opening on every reading would pin the strip to it).</summary>
    [Fact]
    public void Without_the_keys_a_gate_made_only_of_it_never_opens()
    {
        var fight = new Fight(TwoPhases(new PhasePlayedCondition("charge", Comparison.AtLeast, 0)), keysRead: false);

        Assert.Equal("Charge I", fight.Read(Plain));
        Assert.Equal("Charge I", fight.Read(Plain));
        Assert.True(fight.Selector.PlayedIgnored);
    }

    [Fact]
    public void Without_the_keys_a_gate_with_more_opens_on_the_rest()
    {
        var fight = new Fight(TwoPhases(Played, new SkillPointsCondition(Comparison.AtLeast, 3)), keysRead: false);

        Assert.Equal("Charge I", fight.Read(Plain));
        Assert.Equal("Burst II", fight.Read(Reading(3)));
    }

    // ---- Alerts ----

    [Fact]
    public void Without_the_keys_an_alert_made_only_of_it_never_fires()
    {
        var monitor = new AlertMonitor();
        var only = AlertRule.New([new PhasePlayedCondition("charge", Comparison.AtLeast, 0)], "");
        var notOnly = AlertRule.New([new NotCondition(new PhasePlayedCondition("charge", Comparison.AtLeast, 5))], "");

        Assert.Empty(monitor.Update(Plain, [only, notOnly], PhaseHistory.KeysNotRead));
        Assert.Empty(monitor.Update(Plain, [only, notOnly], PhaseHistory.KeysNotRead));
    }

    [Fact]
    public void Without_the_keys_an_alert_with_more_fires_on_the_rest()
    {
        var rule = AlertRule.New([Played, Points], "");

        Assert.Equal([rule], new AlertMonitor().Update(Reading(2), [rule], PhaseHistory.KeysNotRead));
    }

    [Fact]
    public void With_the_keys_an_alert_fires_once_the_phase_was_played_so_often()
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
    /// <summary>A selector that has taken one reading with a step follower (so "played" is read, not ignored).</summary>
    public static PhaseSelector Also(this PhaseSelector selector, PartyState state)
    {
        selector.Update(state, new StepFollower(selector.Rotation));
        return selector;
    }
}
