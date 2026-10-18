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

    private static string Shown(PhaseSelector selector, PartyState state) => selector.Update(state).Step.PhaseLabel;

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

        Assert.Equal(true, RotationTracker.Evaluate(notAtMostHalf, Reading(4, 4, arcane: 0.7, liino: 0.5)));
        Assert.Equal(false, RotationTracker.Evaluate(notAtMostHalf, Reading(4, 4, arcane: 0.3, liino: 0.5)));
        Assert.Null(RotationTracker.Evaluate(new NotCondition(new CounterCondition("Typhoeus", OperatorCounters.HuntingArrow.Id, Comparison.AtLeast, 2)),
            Reading(arrows: null, signs: 0, arcane: 0.5, liino: 0.5)));
    }

    /// <summary>"At most 0%": the ultimate just used.</summary>
    [Fact]
    public void An_ultimate_charged_to_at_most_0_percent_is_one_just_used()
    {
        StateCondition justUsed = new UltimateChargeCondition("Arcane", Comparison.AtMost, 0);

        Assert.Equal(true, RotationTracker.Evaluate(justUsed, Reading(4, 4, arcane: 0, liino: 0.5)));
        Assert.Equal(false, RotationTracker.Evaluate(justUsed, Reading(4, 4, arcane: 0.04, liino: 0.5)));
    }

    /// <summary>
    /// "Ultimate active": the time bar is the controlled operator's, so it's Arcane's only while Arcane
    /// is controlled; with the bottom HUD out of sight (no skill points read) it can't be told.
    /// </summary>
    [Fact]
    public void An_ultimate_is_active_while_its_time_bar_shows_for_the_controlled_operator()
    {
        StateCondition arcaneActive = new UltimateActiveCondition("Arcane", true);
        PartyState With(int controlled, double? left, double? skillPoints = 1.5) =>
            Reading(4, 4, arcane: 0, liino: 0.5) with { ActiveSlot = controlled, UltimateTimeLeft = left, SkillPoints = skillPoints };

        Assert.Equal(true, RotationTracker.Evaluate(arcaneActive, With(controlled: 2, left: 0.7)));
        Assert.Equal(false, RotationTracker.Evaluate(arcaneActive, With(controlled: 2, left: null)));
        Assert.Equal(false, RotationTracker.Evaluate(arcaneActive, With(controlled: 1, left: 0.7))); // Typhoeus's bar
        Assert.Null(RotationTracker.Evaluate(arcaneActive, With(controlled: 2, left: null, skillPoints: null)));
        Assert.Equal(true, RotationTracker.Evaluate(new UltimateActiveCondition("Arcane", false), With(controlled: 2, left: null)));
    }
}
