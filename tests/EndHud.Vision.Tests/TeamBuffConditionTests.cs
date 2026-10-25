using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// "连击层数" (owner, 2026-10-25: Laevatain's empowered battle skill "but we also need to add a condition that 连击>=1"): how
/// many stacks of a team buff the game shows left of the HP bar (<see cref="TeamBuffReader"/>, carried on the reading as
/// <see cref="PartyState.Buffs"/>), compared. One kind of the shared catalogue: every host, its own formats; a buff not
/// shown in combat is 0, and anything not read is unknown.
/// </summary>
public class TeamBuffConditionTests
{
    private static readonly string[] Squad = ["Camille", "Laevatain", "Si", "Arcane"];

    private static PartyState Reading(IReadOnlyList<TeamBuff> buffs, bool panel = true, bool combat = true)
    {
        var slots = Squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Combo: combat ? new ComboReading(0.5, false) : null,
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: panel ? 8 : 0) { SkillPoints = panel ? 2 : null, ActiveSlot = panel ? 1 : null, Buffs = buffs };
    }

    private static bool? Holds(StateCondition c, PartyState s) => RotationTracker.Evaluate(c, s, PhaseHistory.None);

    [Fact]
    public void Its_stacks_compare()
    {
        var two = Reading([new TeamBuff("Link", 2)]);
        Assert.Equal(true, Holds(new TeamBuffCondition("Link", Comparison.AtLeast, 1), two));
        Assert.Equal(true, Holds(new TeamBuffCondition("Link", Comparison.Exactly, 2), two));
        Assert.Equal(false, Holds(new TeamBuffCondition("Link", Comparison.AtLeast, 3), two));
        Assert.Equal(true, Holds(new TeamBuffCondition("Link", Comparison.Below, 3), two));
        // One stack shows no number: read as 1.
        Assert.Equal(true, Holds(new TeamBuffCondition("Link", Comparison.Exactly, 1), Reading([new TeamBuff("Link", 1)])));
    }

    [Fact]
    public void Not_shown_in_combat_is_0()
    {
        var none = Reading([]);
        Assert.Equal(false, Holds(new TeamBuffCondition("Link", Comparison.AtLeast, 1), none));
        Assert.Equal(true, Holds(new TeamBuffCondition("Link", Comparison.Exactly, 0), none));
    }

    [Fact]
    public void Unknown_when_it_cant_be_told()
    {
        // A number is there but can't be read (an effect over it).
        Assert.Null(Holds(new TeamBuffCondition("Link", Comparison.AtLeast, 1), Reading([new TeamBuff("Link", null)])));
        // Buffs are read in combat only: out of combat, or the squad panel out of sight, nothing is known.
        Assert.Null(Holds(new TeamBuffCondition("Link", Comparison.AtLeast, 1), Reading([], combat: false)));
        Assert.Null(Holds(new TeamBuffCondition("Link", Comparison.Exactly, 0), Reading([], panel: false)));
    }

    [Fact]
    public void It_is_one_kind_of_the_shared_catalogue_offered_everywhere()
    {
        var kind = ConditionCatalogue.Of(new TeamBuffCondition("Link", Comparison.AtLeast, 1));
        Assert.Equal(("teamBuff", "TeamBuff"), (kind.JsonName, kind.EditorKind));
        Assert.Null(ConditionCatalogue.OperatorOf(new TeamBuffCondition("Link", Comparison.AtLeast, 1)));
        Assert.Equal(Comparison.AtLeast, kind.ComparisonOf(new TeamBuffCondition("Link", Comparison.AtLeast, 1)));
        foreach (var host in Enum.GetValues<ConditionHostKind>())
            Assert.Contains("TeamBuff", ConditionCatalogue.OfferedIn(host));
        Assert.Empty(ConditionRules.Check([new TeamBuffCondition("Link", Comparison.AtLeast, 1)], new ConditionHost(ConditionHostKind.StepExpect, Squad)));
        Assert.Empty(ConditionRules.Check([new TeamBuffCondition("Link", Comparison.AtLeast, 1)], ConditionHost.Alert));
    }

    [Fact]
    public void Its_formats_are_raised_only_when_it_is_used()
    {
        Assert.True(Rotation.CurrentFormat >= 12);
        Assert.True(AlertFile.CurrentFormat >= 6);
        var plain = new RotationPhase("p", "p", [new RotationStep("Laevatain", StepAction.BattleSkill)]);
        var gated = plain with { When = [new NotCondition(new TeamBuffCondition("Link", Comparison.AtLeast, 1))] };
        Assert.Equal(2, Rotation.FormatNeeded([plain], false));
        Assert.Equal(12, Rotation.FormatNeeded([gated], false));
        Assert.Equal(1, AlertFile.FormatNeeded([AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1)], "x")]));
        Assert.Equal(6, AlertFile.FormatNeeded([AlertRule.New([new AnyCondition([new TeamBuffCondition("Link", Comparison.AtLeast, 1)])], "x")]));
    }

    [Fact]
    public void It_round_trips_through_a_rotation_file()
    {
        var phase = new RotationPhase("p", "p", [new RotationStep("Laevatain", StepAction.BattleSkill, Expect: [new TeamBuffCondition("Link", Comparison.AtLeast, 1)])]);
        var rotation = new Rotation(Rotation.FormatNeeded([phase], false), "r", "r", Squad, [phase]);
        string json = RotationFormat.ToJson(rotation);
        Assert.Contains("\"kind\": \"teamBuff\"", json);
        Assert.Contains("\"buffId\": \"Link\"", json);
        var back = RotationFormat.FromJson(json);
        Assert.Equal(new TeamBuffCondition("Link", Comparison.AtLeast, 1), Assert.Single(back.Phases[0].Steps[0].Expect));
    }
}
