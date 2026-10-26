using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// An alert rule's scope (owner, 2026-10-25: "the notification can be set to be either always enabled, or tie to a
/// squad"): no squad (<see cref="AlertRule.Squad"/> null) runs for every squad, the default and every older rule; a squad
/// of up to four operators runs only while the recognised squad contains every one of them, in any slot order. While
/// the squad can't be told, a scoped rule neither fires nor re-arms. The file is at format 8 only when a rule has a squad.
/// </summary>
public class AlertSquadScopeTests
{
    private static readonly string[] Team = ["Laevatain", "Camille", "Arcane", "Ardelia"];

    private static readonly SkillPointsCondition CastNow = new(Comparison.AtLeast, 1.8);

    /// <summary>A reading in battle with these operators by slot (null: not recognised), at these skill points.</summary>
    private static PartyState Reading(double skillPoints, params string?[] squad)
    {
        var slots = squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Combo: new ComboReading(1, true), Ultimate: new UltimateReading(false, 0.3),
            Operator: id is null ? null : new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: 8) { SkillPoints = skillPoints, ActiveSlot = 1 };
    }

    private static int Fires(AlertMonitor monitor, AlertRule rule, PartyState reading) => monitor.Update(reading, [rule], PhaseHistory.None).Count;

    [Fact]
    public void A_rule_with_no_squad_runs_for_every_squad()
    {
        var rule = AlertRule.New([CastNow], "x");

        Assert.Null(rule.Squad);
        Assert.Equal(1, Fires(new AlertMonitor(), rule, Reading(2, "A", "B", "C", "D")));
        Assert.Equal(1, Fires(new AlertMonitor(), rule, Reading(2, Team)));
    }

    [Fact]
    public void A_squad_rule_runs_for_its_four_in_any_slot_order_and_not_for_another_squad()
    {
        var rule = AlertRule.New([CastNow], "x") with { Squad = Team };

        Assert.Equal(1, Fires(new AlertMonitor(), rule, Reading(2, "Ardelia", "Arcane", "Laevatain", "Camille")));
        Assert.Equal(0, Fires(new AlertMonitor(), rule, Reading(2, "Laevatain", "Camille", "Arcane", "Si")));
    }

    /// <summary>Fewer than four named: the squad must contain every one of them (the others can be anyone).</summary>
    [Fact]
    public void A_squad_of_two_runs_for_any_squad_containing_both()
    {
        var rule = AlertRule.New([CastNow], "x") with { Squad = ["Camille", "Laevatain"] };

        Assert.Equal(1, Fires(new AlertMonitor(), rule, Reading(2, Team)));
        Assert.Equal(1, Fires(new AlertMonitor(), rule, Reading(2, "Laevatain", "Si", "Camille", "Tangtang")));
        Assert.Equal(0, Fires(new AlertMonitor(), rule, Reading(2, "Laevatain", "Si", "Arcane", "Tangtang")));
    }

    /// <summary>An operator not recognised yet: whether the squad is the rule's can't be told, unless the named ones are all seen.</summary>
    [Fact]
    public void While_the_squad_is_not_recognised_a_squad_rule_neither_fires_nor_re_arms()
    {
        var rule = AlertRule.New([CastNow], "x") with { Squad = Team };
        var monitor = new AlertMonitor();

        Assert.Equal(0, Fires(monitor, rule, Reading(2, "Laevatain", null, "Arcane", "Ardelia")));
        Assert.Equal(1, Fires(monitor, rule, Reading(2, Team)));
        // Unrecognised again: neither down nor re-armed, so the same holding doesn't pop it twice.
        Assert.Equal(0, Fires(monitor, rule, Reading(2, "Laevatain", null, "Arcane", "Ardelia")));
        Assert.True(monitor.StillHolds(rule.Id));
        Assert.Equal(0, Fires(monitor, rule, Reading(2, Team)));
        // No squad read at all: unknown too.
        Assert.Equal(0, Fires(monitor, rule, Reading(2)));
        Assert.True(monitor.StillHolds(rule.Id));
        // Two named, both seen: told, whoever the unrecognised one is.
        var two = AlertRule.New([CastNow], "y") with { Squad = ["Laevatain", "Arcane"] };
        Assert.Equal(1, Fires(new AlertMonitor(), two, Reading(2, "Laevatain", null, "Arcane", "Ardelia")));
    }

    [Fact]
    public void Another_squad_takes_the_pop_up_down_and_re_arms()
    {
        var rule = AlertRule.New([CastNow], "x") with { Squad = Team };
        var monitor = new AlertMonitor();

        Assert.Equal(1, Fires(monitor, rule, Reading(2, Team)));
        Assert.Equal(0, Fires(monitor, rule, Reading(2, "A", "B", "C", "D")));
        Assert.False(monitor.StillHolds(rule.Id));
        Assert.Equal(1, Fires(monitor, rule, Reading(2, Team)));
    }

    [Fact]
    public void A_squad_rule_names_an_empty_or_repeated_squad_as_a_problem()
    {
        Assert.Equal([AlertProblem.EmptySquad], (AlertRule.New([CastNow], "x") with { Squad = [] }).Problems());
        Assert.Equal([AlertProblem.SquadOperatorRepeated], (AlertRule.New([CastNow], "x") with { Squad = ["Camille", "Si", "Camille"] }).Problems());
        Assert.Empty((AlertRule.New([CastNow], "x") with { Squad = ["Camille"] }).Problems());
    }

    [Fact]
    public void An_empty_squad_never_fires()
    {
        Assert.Equal(0, Fires(new AlertMonitor(), AlertRule.New([CastNow], "x") with { Squad = [] }, Reading(2, Team)));
    }

    [Fact]
    public void A_squad_rule_is_written_at_format_8_and_others_stay_where_they_were()
    {
        var always = AlertRule.New([CastNow], "always");
        var scoped = AlertRule.New([CastNow], "scoped") with { Squad = ["Camille", "Laevatain"] };

        Assert.Contains("\"format\": 1", AlertFormat.ToJson([always]));
        Assert.DoesNotContain("squad", AlertFormat.ToJson([always]));
        string json = AlertFormat.ToJson([always, scoped]);
        Assert.Contains("\"format\": 8", json);
        Assert.Contains("\"squad\"", json);
        Assert.Equal([always, scoped], AlertFormat.FromJson(json));
        Assert.Equal(8, AlertFile.CurrentFormat);
    }

    [Fact]
    public void A_share_code_carries_the_squad()
    {
        var scoped = AlertRule.New([CastNow], "scoped") with { Squad = Team };

        var back = Assert.Single(AlertFormat.FromShareCode(AlertFormat.ToShareCode([scoped])));

        Assert.Equal(Team, back.Squad);
        Assert.Equal(scoped, back);
    }

    /// <summary>A file from before scopes (format 1 to 7) loads every rule as for every squad.</summary>
    [Fact]
    public void An_older_file_loads_as_every_squad()
    {
        const string json = """{ "format": 7, "rules": [ { "id": "a", "when": [ { "kind": "skillPoints", "is": "AtLeast", "value": 1.8 } ], "message": "m" } ] }""";

        Assert.Null(Assert.Single(AlertFormat.FromJson(json)).Squad);
    }

    /// <summary>The same conditions and words for another squad is another rule: an import keeps both.</summary>
    [Fact]
    public void The_same_rule_for_another_squad_is_not_a_duplicate_but_the_same_squad_in_another_order_is()
    {
        var always = AlertRule.New([CastNow], "x");
        var scoped = always with { Id = AlertRule.NewId(), Squad = ["Camille", "Laevatain"] };
        var reordered = scoped with { Id = AlertRule.NewId(), Squad = ["Laevatain", "Camille"] };

        var import = AlertStore.Import([always], [scoped, reordered]);

        Assert.Equal(1, import.Added);
        Assert.Equal(1, import.Duplicates);
    }

    private static string Alerts(string squad) =>
        $$"""{"format":8,"rules":[{"id":"r","when":[{"kind":"skillPoints","is":"AtLeast","value":1.8}],"message":"m","squad":{{squad}}}]}""";

    public static TheoryData<string> BrokenSquads => new()
    {
        Alerts("""["A","B","C","D","E"]"""),
        Alerts("""["A",null]"""),
        Alerts("""["A",""]"""),
        Alerts("\"A\""),
        Alerts($"[\"{new string('x', ReadLimits.MaxOperatorIdChars + 1)}\"]"),
    };

    /// <summary>A squad is four operators at most, each a name: anything else in a file or code isn't alert rules.</summary>
    [Theory]
    [MemberData(nameof(BrokenSquads))]
    public void A_squad_over_the_limits_is_refused(string json)
    {
        Assert.Equal(AlertReadError.NotAlerts, Assert.Throws<AlertReadException>(() => AlertFormat.FromJson(json)).Error);
    }

    [Fact]
    public void A_squad_within_the_limits_reads()
    {
        Assert.Equal(["A", "B", "C", "D"], Assert.Single(AlertFormat.FromJson(Alerts("""["A","B","C","D"]"""))).Squad);
    }
}
