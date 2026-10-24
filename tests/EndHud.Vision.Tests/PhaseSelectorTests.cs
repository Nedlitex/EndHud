using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Which phase the strip shows (<see cref="PhaseSelector"/>): the first phase whose condition holds
/// in this reading (the last, if the rotation asks); when none holds, the first phase without a
/// condition (the "otherwise" phase), or, while something can't be read or when every phase has a
/// condition, the one already shown.
/// </summary>
public class PhaseSelectorTests
{
    private static readonly string[] Squad = ["Typhoeus", "Arcane", "Perlica", "Liino"];

    /// <summary>A reading: Typhoeus controlled with these counters, and Arcane's and Liino's ultimate charge (1 = ready).</summary>
    private static PartyState Reading(int? arrows, int? signs, double arcane, double liino)
    {
        var charge = new Dictionary<string, double> { ["Typhoeus"] = 0.1, ["Arcane"] = arcane, ["Perlica"] = 0.1, ["Liino"] = liino };
        var slots = Squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Ultimate: new UltimateReading(charge[id] >= 1, Math.Min(charge[id], 1)),
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: 8)
        {
            ActiveSlot = 1,
            Counters = [new CounterReading("Typhoeus", OperatorCounters.HuntingArrow.Id, arrows), new CounterReading("Typhoeus", OperatorCounters.Sign.Id, signs)],
        };
    }

    private static Rotation Load(string id) =>
        RotationFormat.FromJson(File.ReadAllText(Path.Combine(OperatorCache.RepositoryRoot(), "rotations", id + ".json")));

    private static string Shown(PhaseSelector selector, PartyState state) => selector.Update(state, null).Step.PhaseLabel;

    [Fact]
    public void Typhoeus_charge_one_with_four_arrows_and_no_ultimate_ready()
    {
        var selector = new PhaseSelector(Load("typhoeus-arcane-perlica-liino"));

        Assert.Equal("充能轴 I", Shown(selector, Reading(arrows: 4, signs: 4, arcane: 0.3, liino: 0.5)));
    }

    [Fact]
    public void Typhoeus_charge_two_with_no_arrows_and_eight_signs()
    {
        var selector = new PhaseSelector(Load("typhoeus-arcane-perlica-liino"));

        Assert.Equal("充能轴 II", Shown(selector, Reading(arrows: 0, signs: 8, arcane: 0.3, liino: 0.5)));
    }

    [Fact]
    public void Typhoeus_burst_when_both_ultimates_are_at_80_percent()
    {
        var selector = new PhaseSelector(Load("typhoeus-arcane-perlica-liino"));

        Assert.Equal("爆发轴 III", Shown(selector, Reading(arrows: 0, signs: 8, arcane: 0.85, liino: 0.8)));
        Assert.Equal("爆发轴 III", Shown(selector, Reading(arrows: 0, signs: 8, arcane: 1, liino: 0.9))); // one ready already
        // Only one of them at 80%: still charging.
        Assert.Equal("充能轴 II", Shown(new PhaseSelector(Load("typhoeus-arcane-perlica-liino")), Reading(arrows: 0, signs: 8, arcane: 0.85, liino: 0.5)));
    }

    [Fact]
    public void In_between_states_keep_the_phase_shown()
    {
        var selector = new PhaseSelector(Load("typhoeus-arcane-perlica-liino"));
        Assert.Equal("充能轴 II", Shown(selector, Reading(arrows: 0, signs: 8, arcane: 0.3, liino: 0.5)));

        // Typhoeus's combo turned the signs into arrows; she's shooting them: 2 arrows left.
        Assert.Equal("充能轴 II", Shown(selector, Reading(arrows: 2, signs: 0, arcane: 0.4, liino: 0.6)));
        Assert.Equal("充能轴 II", Shown(selector, Reading(arrows: null, signs: null, arcane: 0.4, liino: 0.6))); // counters covered
        Assert.Equal("充能轴 I", Shown(selector, Reading(arrows: 4, signs: 0, arcane: 0.4, liino: 0.6)));
    }

    [Fact]
    public void Before_any_condition_holds_the_first_phase_is_shown()
    {
        var selector = new PhaseSelector(Load("typhoeus-arcane-perlica-liino"));

        Assert.Equal("充能轴 I", Shown(selector, Reading(arrows: 2, signs: 3, arcane: 0.1, liino: 0.1)));
    }

    [Fact]
    public void The_first_phase_without_a_condition_is_the_start_and_the_first_match_wins()
    {
        StateCondition arcaneReady = new UltimateCondition("Arcane", true);
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("a", "爆发轴", [new RotationStep("Arcane", StepAction.Ultimate)], [arcaneReady]),
            new RotationPhase("b", "充能轴", [new RotationStep("Liino", StepAction.Combo)]),
            new RotationPhase("c", "爆发轴", [new RotationStep("Liino", StepAction.Ultimate)], [arcaneReady]),
        ]);
        var selector = new PhaseSelector(rotation);

        Assert.Equal("充能轴 II", Shown(selector, Reading(4, 4, arcane: 0.5, liino: 0.5)));
        Assert.Equal("爆发轴 I", Shown(selector, Reading(4, 4, arcane: 1, liino: 0.5)));
    }

    /// <summary>Zhuang Fangyi controlled with this many swords (青霆剑) and her ultimate ready or not.</summary>
    private static PartyState Zhuang(int? swords, bool ultimateReady)
    {
        string[] squad = ["Zhuang_Fangyi", "Liino", "Arcane", "Perlica"];
        var slots = squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Ultimate: new UltimateReading(i == 0 && ultimateReady, i == 0 && ultimateReady ? 1 : 0.5),
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: 8) { ActiveSlot = 1, Counters = [new CounterReading("Zhuang_Fangyi", OperatorCounters.Sunderblade.Id, swords)] };
    }

    [Fact]
    public void Zhuang_fangyi_charges_on_swords_and_bursts_at_nine_with_her_ultimate()
    {
        var selector = new PhaseSelector(Load("zhuang-fangyi-liino-arcane-perlica"));

        Assert.Equal("充能轴 I", Shown(selector, Zhuang(3, false)));
        Assert.Equal("充能轴 II", Shown(selector, Zhuang(5, false)));
        Assert.Equal("充能轴 II", Shown(selector, Zhuang(9, false))); // nine, ultimate still charging
        Assert.Equal("爆发轴 III", Shown(selector, Zhuang(9, true)));
        Assert.Equal("爆发轴 III", Shown(selector, Zhuang(7, true))); // spending: nothing else holds yet
        Assert.Equal("充能轴 I", Shown(selector, Zhuang(0, false))); // swords spent: charging again
    }

    /// <summary>
    /// A player's report (2026-10-18): 充能轴 I "while Laevatain's ultimate is charged to at most 50%",
    /// 充能轴 II with no condition. Above 50% the strip stayed on I; only re-saving the rotation (which
    /// starts the selector afresh) showed II. Here with Arcane's ultimate.
    /// </summary>
    [Fact]
    public void A_phase_without_a_condition_is_shown_when_no_other_holds()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("a", "充能轴", [new RotationStep("Arcane", StepAction.Combo)], [new UltimateChargeCondition("Arcane", Comparison.AtMost, 50)]),
            new RotationPhase("b", "充能轴", [new RotationStep("Liino", StepAction.Combo)]),
        ]);
        var selector = new PhaseSelector(rotation);

        Assert.Equal("充能轴 I", Shown(selector, Reading(4, 4, arcane: 0.3, liino: 0.5)));
        Assert.Equal("充能轴 II", Shown(selector, Reading(4, 4, arcane: 0.7, liino: 0.5)));
        Assert.Equal("充能轴 II", Shown(selector, Reading(4, 4, arcane: 1, liino: 0.5))); // ready counts as 100%
        Assert.Equal("充能轴 I", Shown(selector, Reading(4, 4, arcane: 0, liino: 0.5))); // the ultimate used
    }

    /// <summary>A condition that can't be read (a cutscene) is no reason to switch: the phase shown stays.</summary>
    [Fact]
    public void While_a_condition_cant_be_read_the_phase_shown_stays()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("a", "充能轴", [new RotationStep("Typhoeus", StepAction.Combo)], [new CounterCondition("Typhoeus", OperatorCounters.HuntingArrow.Id, Comparison.AtLeast, 2)]),
            new RotationPhase("b", "充能轴", [new RotationStep("Liino", StepAction.Combo)]),
        ]);
        var selector = new PhaseSelector(rotation);

        Assert.Equal("充能轴 I", Shown(selector, Reading(arrows: 3, signs: 0, arcane: 0.5, liino: 0.5)));
        Assert.Equal("充能轴 I", Shown(selector, Reading(arrows: null, signs: null, arcane: 0.5, liino: 0.5)));
        Assert.Equal("充能轴 II", Shown(selector, Reading(arrows: 0, signs: 0, arcane: 0.5, liino: 0.5)));
    }

    [Fact]
    public void With_last_match_wins_the_last_phase_that_holds_is_shown()
    {
        PhaseSelector Selector(bool lastMatchWins) => new(new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("a", "充能轴", [new RotationStep("Arcane", StepAction.Combo)], [new UltimateChargeCondition("Arcane", Comparison.AtLeast, 20)]),
            new RotationPhase("b", "爆发轴", [new RotationStep("Arcane", StepAction.Ultimate)], [new UltimateCondition("Arcane", true)]),
        ], lastMatchWins));
        var ready = Reading(4, 4, arcane: 1, liino: 0.5);

        Assert.Equal("充能轴 I", Shown(Selector(false), ready));
        Assert.Equal("爆发轴 II", Shown(Selector(true), ready));
    }

    /// <summary>"Not" turns any condition around; what can't be read stays unknown.</summary>
    [Fact]
    public void A_condition_can_be_turned_around()
    {
        StateCondition atMostHalf = new UltimateChargeCondition("Arcane", Comparison.AtMost, 50);
        var notAtMostHalf = new NotCondition(atMostHalf);

        Assert.Equal(true, RotationTracker.Evaluate(notAtMostHalf, Reading(4, 4, arcane: 0.7, liino: 0.5), PhaseHistory.None));
        Assert.Equal(false, RotationTracker.Evaluate(notAtMostHalf, Reading(4, 4, arcane: 0.3, liino: 0.5), PhaseHistory.None));
        Assert.Null(RotationTracker.Evaluate(new NotCondition(new CounterCondition("Typhoeus", OperatorCounters.HuntingArrow.Id, Comparison.AtLeast, 2)),
            Reading(arrows: null, signs: 0, arcane: 0.5, liino: 0.5), PhaseHistory.None));
    }

    /// <summary>"At most 0%": the ultimate just used.</summary>
    [Fact]
    public void An_ultimate_charged_to_at_most_0_percent_is_one_just_used()
    {
        StateCondition justUsed = new UltimateChargeCondition("Arcane", Comparison.AtMost, 0);

        Assert.Equal(true, RotationTracker.Evaluate(justUsed, Reading(4, 4, arcane: 0, liino: 0.5), PhaseHistory.None));
        Assert.Equal(false, RotationTracker.Evaluate(justUsed, Reading(4, 4, arcane: 0.04, liino: 0.5), PhaseHistory.None));
    }

    // "Ultimate active": see UltimateActiveConditionTests (the bar isn't the controlled operator's).

    // ---- "Phase shown N times this fight" (format 4): counted when a phase comes onto the strip ----

    /// <summary>A: Burst while Arcane's ultimate is ready; B: Charge otherwise.</summary>
    private static PhaseSelector ChargeAndBurst() => new(new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
    [
        new RotationPhase("a", "Burst", [new RotationStep("Arcane", StepAction.Ultimate)], [new UltimateCondition("Arcane", true)]),
        new RotationPhase("b", "Charge", [new RotationStep("Liino", StepAction.Combo)]),
    ]));

    private static PartyState Charging => Reading(4, 4, arcane: 0.5, liino: 0.5);
    private static PartyState Bursting => Reading(4, 4, arcane: 1, liino: 0.5);

    [Fact]
    public void Counts_each_time_a_phase_comes_onto_the_strip()
    {
        var selector = ChargeAndBurst();

        foreach (var reading in new[] { Charging, Bursting, Charging, Bursting }) selector.Update(reading, null);

        Assert.Equal(2, selector.TimesShown("a"));
        Assert.Equal(2, selector.TimesShown("b"));
        Assert.Null(selector.TimesShown("deleted"));
    }

    [Fact]
    public void Staying_on_a_phase_does_not_count_again()
    {
        var selector = ChargeAndBurst();

        for (int i = 0; i < 10; i++) selector.Update(Charging, null);

        Assert.Equal(1, selector.TimesShown("b"));
        Assert.Equal(0, selector.TimesShown("a"));
    }

    [Fact]
    public void End_of_fight_forgets_the_counts_and_the_phase()
    {
        var selector = ChargeAndBurst();
        selector.Update(Charging, null);
        selector.Update(Bursting, null);
        Assert.Equal(1, selector.TimesShown("a"));

        selector.EndFight();

        Assert.Equal(0, selector.TimesShown("a"));
        Assert.Equal(0, selector.TimesShown("b"));
        // Burst isn't remembered as shown: with Arcane's ultimate unreadable (it would keep the phase
        // shown), the next fight starts on the otherwise phase.
        Assert.Equal("Charge II", Shown(selector, Bursting with { Slots = [] }));
        Assert.Equal(1, selector.TimesShown("b"));
    }

    /// <summary>The owner's ask (2026-10-22), "has this phase been played yet": Burst waits until Charge has been shown once.</summary>
    [Fact]
    public void A_gate_on_another_phase_count_opens_after_it_was_shown()
    {
        var selector = new PhaseSelector(new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("burst", "Burst", [new RotationStep("Arcane", StepAction.Ultimate)], [new PhaseShownCondition("charge", Comparison.AtLeast, 1)]),
            new RotationPhase("charge", "Charge", [new RotationStep("Liino", StepAction.Combo)]),
        ]));

        Assert.Equal("Charge II", Shown(selector, Charging));
        Assert.Equal("Burst I", Shown(selector, Charging));
        selector.EndFight();
        Assert.Equal("Charge II", Shown(selector, Charging)); // a new fight: not shown yet
    }

    // ---- Priorities (format 6): among the phases whose entry condition holds, the biggest priority wins ----

    private static readonly StateCondition ArcaneAtLeast20 = new UltimateChargeCondition("Arcane", Comparison.AtLeast, 20);
    private static readonly StateCondition ArcaneReady = new UltimateCondition("Arcane", true);
    private static readonly StateCondition LiinoReady = new UltimateCondition("Liino", true);
    private static readonly StateCondition ArrowsAtLeast2 = new CounterCondition("Typhoeus", OperatorCounters.HuntingArrow.Id, Comparison.AtLeast, 2);

    /// <summary>Charge (gated on Arcane at 20% or more) above Burst (gated on Arcane ready), then an otherwise phase.</summary>
    private static PhaseSelector Ranked(RotationPhase charge, RotationPhase burst, bool lastMatchWins = false) =>
        new(new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            charge,
            burst,
            new RotationPhase("rest", "Rest", [new RotationStep("Perlica", StepAction.Combo)]),
        ], lastMatchWins));

    private static RotationPhase Charge(int priority = 0, params PriorityRule[] rules) =>
        new("charge", "Charge", [new RotationStep("Arcane", StepAction.Combo)], [ArcaneAtLeast20]) { Priority = priority, PriorityRules = rules };

    private static RotationPhase Burst(int priority = 0, params PriorityRule[] rules) =>
        new("burst", "Burst", [new RotationStep("Arcane", StepAction.Ultimate)], [ArcaneReady]) { Priority = priority, PriorityRules = rules };

    /// <summary>Both entry conditions hold (Arcane ready); Liino's ultimate ready or at half; Typhoeus's arrows read or covered.</summary>
    private static PartyState BothHold(bool liinoReady = false, bool arrowsCovered = false) =>
        Reading(arrows: arrowsCovered ? null : 4, signs: 4, arcane: 1, liino: liinoReady ? 1 : 0.5);

    [Fact]
    public void A_bigger_priority_wins_over_list_order()
    {
        Assert.Equal("Burst II", Shown(Ranked(Charge(0), Burst(5)), BothHold()));
        Assert.Equal("Charge I", Shown(Ranked(Charge(5), Burst(0), lastMatchWins: true), BothHold()));
        Assert.Equal("Burst II", Shown(Ranked(Charge(-3), Burst(-1)), BothHold())); // negatives too: -1 is bigger
    }

    /// <summary>The owner's rule 3: equal priorities go by list order, the first (or the last, with "last match wins").</summary>
    [Fact]
    public void Equal_priorities_go_by_list_order()
    {
        Assert.Equal("Charge I", Shown(Ranked(Charge(3), Burst(3)), BothHold()));
        Assert.Equal("Burst II", Shown(Ranked(Charge(3), Burst(3), lastMatchWins: true), BothHold()));
    }

    /// <summary>Every existing rotation sets no priority: all 0, exactly today's choice (the first, or the last, that holds).</summary>
    [Fact]
    public void All_priorities_0_is_list_order_as_before()
    {
        Assert.Equal("Charge I", Shown(Ranked(Charge(), Burst()), BothHold()));
        Assert.Equal("Burst II", Shown(Ranked(Charge(), Burst(), lastMatchWins: true), BothHold()));
        // Only one holds: that one, whatever the order.
        Assert.Equal("Charge I", Shown(Ranked(Charge(), Burst()), Reading(4, 4, arcane: 0.5, liino: 0.5)));
        Assert.Equal("Rest III", Shown(Ranked(Charge(), Burst()), Reading(4, 4, arcane: 0.1, liino: 0.5)));
    }

    /// <summary>A priority only ranks phases whose entry condition holds: a lower one that holds beats a higher one that doesn't.</summary>
    [Fact]
    public void A_priority_only_ranks_phases_whose_condition_holds()
    {
        Assert.Equal("Charge I", Shown(Ranked(Charge(0), Burst(9)), Reading(4, 4, arcane: 0.5, liino: 0.5)));
    }

    /// <summary>The "otherwise" phase keeps its role: a priority on it doesn't put it above a phase whose condition holds.</summary>
    [Fact]
    public void The_otherwise_phase_is_not_ranked()
    {
        var selector = new PhaseSelector(new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("rest", "Rest", [new RotationStep("Perlica", StepAction.Combo)]) { Priority = 100 },
            Burst(0),
        ]));

        Assert.Equal("Rest I", Shown(selector, Reading(4, 4, arcane: 0.5, liino: 0.5)));
        Assert.Equal("Burst II", Shown(selector, BothHold()));
    }

    /// <summary>"If Liino's ultimate is ready, the priority is 10": the rule decides only while it holds.</summary>
    [Fact]
    public void A_rule_that_holds_sets_the_priority()
    {
        var rule = new PriorityRule([LiinoReady], 10);

        Assert.Equal("Charge I", Shown(Ranked(Charge(0), Burst(0, rule)), BothHold(liinoReady: false)));
        Assert.Equal("Burst II", Shown(Ranked(Charge(0), Burst(0, rule)), BothHold(liinoReady: true)));
    }

    /// <summary>The first rule that holds sets the priority; the later ones aren't asked.</summary>
    [Fact]
    public void The_first_rule_that_holds_wins()
    {
        var burst = Burst(0, new PriorityRule([LiinoReady], 3), new PriorityRule([ArcaneReady], 9));

        Assert.Equal("Charge I", Shown(Ranked(Charge(5), burst), BothHold(liinoReady: true)));  // the first rule: 3 < 5
        Assert.Equal("Burst II", Shown(Ranked(Charge(5), burst), BothHold(liinoReady: false))); // the second: 9 > 5
    }

    /// <summary>A rule that can't be read (the arrows covered) doesn't hold: the next rule applies, or the base priority.</summary>
    [Fact]
    public void A_rule_that_cant_be_read_is_skipped()
    {
        var thenReady = Burst(0, new PriorityRule([ArrowsAtLeast2], 9), new PriorityRule([ArcaneReady], 1));
        var thenBase = Burst(7, new PriorityRule([ArrowsAtLeast2], 1));

        Assert.Equal("Charge I", Shown(Ranked(Charge(5), thenReady), BothHold(arrowsCovered: true))); // 1, not 9
        Assert.Equal("Burst II", Shown(Ranked(Charge(5), thenReady), BothHold()));                     // read: 9
        Assert.Equal("Burst II", Shown(Ranked(Charge(5), thenBase), BothHold(arrowsCovered: true)));  // the base, 7
        Assert.Equal("Charge I", Shown(Ranked(Charge(5), thenBase), BothHold()));                     // read: 1
    }

    /// <summary>No rule holds: the base priority.</summary>
    [Fact]
    public void Without_a_rule_holding_the_base_priority_applies()
    {
        Assert.Equal("Burst II", Shown(Ranked(Charge(5), Burst(7, new PriorityRule([LiinoReady], 1))), BothHold(liinoReady: false)));
    }

}
