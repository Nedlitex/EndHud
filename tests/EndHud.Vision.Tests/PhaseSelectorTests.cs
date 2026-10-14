using EndHud.Vision;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// Which phase the strip shows (<see cref="PhaseSelector"/>): the first phase whose condition holds
/// in this reading; when none holds, the one already shown; at the start, the first phase without
/// a condition (or the first phase).
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
}
