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

    /// <summary>The strip as the overlay drives it: the selector picks the phase, then the follower follows it with the selector as history.</summary>
    private sealed class Strip(Rotation rotation)
    {
        public PhaseSelector Selector { get; } = new(rotation);
        public StepFollower Follower { get; } = new(rotation);
        public StepPosition? Position { get; private set; }
        private PartyState? _before;

        /// <summary>One reading; the phase on the strip (its id).</summary>
        public string Read(PartyState now, params int[] presses)
        {
            var shown = Selector.Update(now, Follower);
            Position = Follower.Update(shown.Step.PhaseIndex, _before, now, Casts.Seen(presses), Selector);
            _before = now;
            return rotation.Phases[shown.Step.PhaseIndex].Id;
        }

        /// <summary>Readings every quarter second from <paramref name="fromQuarter"/> to <paramref name="toQuarter"/> (both in).</summary>
        public string Quarters(int fromQuarter, int toQuarter)
        {
            string phase = "";
            for (int q = fromQuarter; q <= toQuarter; q++) phase = Read(Reading(q * 0.25));
            return phase;
        }

        /// <summary>The clock as everything that reads the selector sees it.</summary>
        public TimeSpan? Clock => ((IPhaseHistory)Selector).TimeInPhase;

        public int Played(string phaseId = "p") => Follower.TimesPlayed(phaseId)!.Value;

        public void EndFight()
        {
            Selector.EndFight();
            Follower.EndFight();
            _before = null;
        }
    }

    private const StepMark L = StepMark.Lit, C = StepMark.Current, U = StepMark.Unlit;

    // ---- The clock ----

    /// <summary>Owner, 2026-10-27: time while the HUD is out of sight (a cutscene, a menu, an ultimate's animation) doesn't count.</summary>
    [Fact]
    public void Readings_without_the_panel_do_not_advance_the_clock()
    {
        var strip = new Strip(OnePhase());

        for (int q = 0; q <= 17; q++)
            strip.Read(Reading(q * 0.25, readingsSinceSeen: q is >= 5 and <= 12 ? 1 : 0)); // 8 readings kept from before

        Assert.Equal(TimeSpan.FromSeconds(2.25), strip.Clock); // 4.25 s went by; 2 s of it the panel was out of sight
    }

    /// <summary>A long gap between readings (the strip off, the squad not recognised) is not counted.</summary>
    [Fact]
    public void A_gap_of_more_than_a_second_is_not_counted()
    {
        var strip = new Strip(OnePhase());

        strip.Read(Reading(0));
        strip.Read(Reading(0.25));
        strip.Read(Reading(30));

        Assert.Equal(TimeSpan.FromSeconds(0.25), strip.Clock);
    }

    [Fact]
    public void A_reading_without_a_time_makes_it_unknown()
    {
        var strip = new Strip(OnePhase(AtLeast(1)));
        strip.Read(Reading(0));
        strip.Read(Reading(0.25));

        strip.Read(Reading(null));
        strip.Quarters(2, 12);

        Assert.Null(strip.Clock);
        Assert.Equal(0, strip.Played());
    }

    [Fact]
    public void The_fight_ending_resets_the_clock()
    {
        var strip = new Strip(OnePhase());
        strip.Quarters(0, 20);
        Assert.Equal(TimeSpan.FromSeconds(5), strip.Clock);

        strip.EndFight();
        Assert.Null(strip.Clock);
        strip.Read(Reading(100));
        Assert.Equal(TimeSpan.Zero, strip.Clock);
        strip.Read(Reading(100.25));
        Assert.Equal(TimeSpan.FromSeconds(0.25), strip.Clock);
    }

    /// <summary>The strip's badges of steps other than the chevron's are judged with the selector as history: it reports the follower's clock.</summary>
    [Fact]
    public void The_selector_reports_the_followers_clock()
    {
        var strip = new Strip(OnePhase());
        Assert.Null(strip.Selector.TimeInPhase);

        strip.Quarters(0, 8);

        Assert.Equal(TimeSpan.FromSeconds(2), strip.Follower.TimeInPhase);
        Assert.Equal(strip.Follower.TimeInPhase, strip.Selector.TimeInPhase);
    }

    // ---- Finishing ----

    [Fact]
    public void A_finish_of_at_least_4_seconds_plays_the_phase_through_at_4_seconds_not_before()
    {
        var strip = new Strip(OnePhase(AtLeast(4)));

        strip.Quarters(0, 15);
        Assert.Equal(0, strip.Played()); // 3.75 s
        strip.Quarters(16, 16);
        Assert.Equal(1, strip.Played()); // 4 s
    }

    /// <summary>Played through, a phase's round starts again: so does its clock (else a finish held would never come true again).</summary>
    [Fact]
    public void A_looping_phase_starts_its_clock_again_after_each_finish()
    {
        var strip = new Strip(OnePhase(AtLeast(2)));

        strip.Quarters(0, 7);
        Assert.Equal(0, strip.Played());
        strip.Quarters(8, 8);
        Assert.Equal(1, strip.Played());
        Assert.Equal(TimeSpan.Zero, strip.Clock);
        strip.Quarters(9, 16);
        Assert.Equal(2, strip.Played());
        strip.Quarters(17, 24);
        Assert.Equal(3, strip.Played());
    }

    [Fact]
    public void Two_timed_condition_steps_light_in_time_order_and_the_last_one_finishes_the_phase()
    {
        var strip = new Strip(new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("p", "P", [new RotationStep("", StepAction.Condition, [AtLeast(1.5)]), new RotationStep("", StepAction.Condition, [AtLeast(3)])]),
        ]));

        strip.Quarters(0, 5);
        Assert.Equal([C, U], strip.Position!.Marks);    // 1.25 s
        Assert.Equal(false, strip.Position.Checks.Single(c => c.Condition is not null).Holds); // its badge: known, not met
        strip.Quarters(6, 6);
        Assert.Equal([L, C], strip.Position!.Marks);    // 1.5 s: the first lights
        strip.Quarters(7, 11);
        Assert.Equal(0, strip.Played());
        strip.Quarters(12, 12);                          // 3 s: the last lights, the phase is played through
        Assert.Equal(1, strip.Played());
        Assert.Equal([C, U], strip.Position!.Marks);    // a new round, its clock at 0
    }

    /// <summary>
    /// The player's flow: after the burst is played through the closing phase comes on, its two timed effects light in
    /// time, and when the last one has the charge phase comes back. 1.5 s and 3 s are test numbers, not game facts.
    /// </summary>
    [Fact]
    public void The_players_closing_phase_follows_the_burst_and_gives_way_to_the_charge_phase()
    {
        var strip = new Strip(new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("charge", "Charge", [new RotationStep("Ardelia", StepAction.BattleSkill)]),
            new RotationPhase("burst", "Burst", [new RotationStep("Laevatain", StepAction.Ultimate)], [new UltimateCondition("Laevatain", true)]),
            new RotationPhase("closing", "Closing",
                [new RotationStep("", StepAction.Condition, [AtLeast(1.5)], Label: "first effect"), new RotationStep("", StepAction.Condition, [AtLeast(3)], Label: "second effect")],
                [new AnyCondition([new PhaseCurrentCondition("burst", PlayedThrough: true), new PhaseCurrentCondition("closing", PlayedThrough: false)])]),
        ]));

        Assert.Equal("burst", strip.Read(Reading(0, laevatainUltimateReady: true)));
        Assert.Equal("burst", strip.Read(Reading(0.25)));  // the ultimate used: the burst is played through
        Assert.Equal(1, strip.Played("burst"));
        Assert.Equal("closing", strip.Read(Reading(0.5))); // its round starts here
        Assert.Equal("closing", strip.Quarters(3, 4));     // 1 s: nothing of it lit, it stays
        Assert.Equal("closing", strip.Quarters(5, 13));    // 3.25 s: 2.75 s in
        Assert.Equal(0, strip.Played("closing"));
        Assert.Equal("closing", strip.Quarters(14, 14));   // 3.5 s: 3 s in, the last effect lit, played through
        Assert.Equal(1, strip.Played("closing"));
        Assert.Equal("charge", strip.Quarters(15, 15));
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
