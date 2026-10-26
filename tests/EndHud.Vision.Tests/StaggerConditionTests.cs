using EndHud.Vision.Alerts;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// The boss's stagger as conditions (owner, 2026-10-25: "monitor the stagger progress and stagger remaining time; add these
/// as conditions and notifications"), read from its bar (<see cref="StaggerBar"/>, carried on the reading as
/// <see cref="PartyState.Stagger"/>): how far the bar has filled, how many node boxes it has passed (each a small
/// stagger), whether the boss is in the large stagger, and how much of its countdown is left. Four kinds of the shared
/// catalogue: every host, their own formats; unknown whenever the bar isn't read.
/// </summary>
public class StaggerConditionTests
{
    private static readonly StaggerReading Building = new(StaggerState.Building, 0.63, [0.4995], null);
    private static readonly StaggerReading Early = new(StaggerState.Building, 0.30, [0.4995], null);
    private static readonly StaggerReading Countdown = new(StaggerState.Staggered, 1, [0.4995], 0.56);

    private static PartyState Reading(StaggerReading? stagger) =>
        new PartyState([], PanelMarks: 8) { SkillPoints = 2, Stagger = stagger };

    private static bool? Holds(StateCondition c, StaggerReading? s) => RotationTracker.Evaluate(c, Reading(s), PhaseHistory.None);

    [Fact]
    public void Progress_compares_as_a_percentage_full_while_staggered()
    {
        Assert.Equal(true, Holds(new StaggerProgressCondition(Comparison.AtLeast, 60), Building));
        Assert.Equal(false, Holds(new StaggerProgressCondition(Comparison.AtLeast, 70), Building));
        Assert.Equal(true, Holds(new StaggerProgressCondition(Comparison.Below, 50), Early));
        Assert.Equal(true, Holds(new StaggerProgressCondition(Comparison.AtLeast, 100), Countdown));
    }

    [Fact]
    public void Nodes_reached_count_the_boxes_the_fill_has_passed()
    {
        Assert.Equal(true, Holds(new StaggerNodesCondition(Comparison.AtLeast, 1), Building));
        Assert.Equal(false, Holds(new StaggerNodesCondition(Comparison.AtLeast, 1), Early));
        Assert.Equal(true, Holds(new StaggerNodesCondition(Comparison.Exactly, 0), Early));
        // Staggered: the bar was filled through every box.
        Assert.Equal(true, Holds(new StaggerNodesCondition(Comparison.Exactly, 1), Countdown));
    }

    /// <summary>
    /// A boss with no node boxes (owner: "a boss can have 0 nodes", only the large stagger): "nodes reached at least 1" is
    /// false, not unknown, building or staggered; "exactly 0" holds; the large stagger and its countdown still read.
    /// </summary>
    [Fact]
    public void A_boss_without_node_boxes_has_none_reached()
    {
        var building = new StaggerReading(StaggerState.Building, 0.70, [], null);
        var staggered = new StaggerReading(StaggerState.Staggered, 1, [], 0.43);
        foreach (var s in new[] { building, staggered })
        {
            Assert.Equal(false, Holds(new StaggerNodesCondition(Comparison.AtLeast, 1), s));
            Assert.Equal(true, Holds(new StaggerNodesCondition(Comparison.Exactly, 0), s));
        }
        Assert.Equal(true, Holds(new StaggerProgressCondition(Comparison.AtLeast, 70), building));
        Assert.Equal(true, Holds(new StaggeredCondition(true), staggered));
        Assert.Equal(true, Holds(new StaggerTimeLeftCondition(Comparison.AtMost, 50), staggered));
    }

    [Fact]
    public void Staggered_is_the_large_stagger()
    {
        Assert.Equal(true, Holds(new StaggeredCondition(true), Countdown));
        Assert.Equal(false, Holds(new StaggeredCondition(true), Building));
        Assert.Equal(true, Holds(new StaggeredCondition(false), Building));
    }

    [Fact]
    public void Time_left_is_the_countdowns_share_and_0_while_building()
    {
        Assert.Equal(true, Holds(new StaggerTimeLeftCondition(Comparison.AtMost, 60), Countdown));
        Assert.Equal(false, Holds(new StaggerTimeLeftCondition(Comparison.AtMost, 50), Countdown));
        Assert.Equal(true, Holds(new StaggerTimeLeftCondition(Comparison.Exactly, 0), Building));
    }

    [Fact]
    public void Unknown_when_the_bar_isnt_read()
    {
        Assert.Null(Holds(new StaggerProgressCondition(Comparison.AtLeast, 0), null));
        Assert.Null(Holds(new StaggerNodesCondition(Comparison.Exactly, 0), null));
        Assert.Null(Holds(new StaggeredCondition(false), null));
        Assert.Null(Holds(new StaggerTimeLeftCondition(Comparison.Exactly, 0), null));
    }

    public static TheoryData<StateCondition, string, string> Kinds => new()
    {
        { new StaggerProgressCondition(Comparison.AtLeast, 50), "staggerProgress", "StaggerProgress" },
        { new StaggerNodesCondition(Comparison.AtLeast, 1), "staggerNodes", "StaggerNodes" },
        { new StaggeredCondition(true), "staggered", "Staggered" },
        { new StaggerTimeLeftCondition(Comparison.AtMost, 30), "staggerTimeLeft", "StaggerTimeLeft" },
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_is_one_kind_of_the_shared_catalogue_offered_everywhere(StateCondition condition, string json, string editor)
    {
        var kind = ConditionCatalogue.Of(condition);
        Assert.Equal((json, editor), (kind.JsonName, kind.EditorKind));
        Assert.Null(ConditionCatalogue.OperatorOf(condition));
        foreach (var host in Enum.GetValues<ConditionHostKind>())
            Assert.Contains(editor, ConditionCatalogue.OfferedIn(host));
        Assert.Empty(ConditionRules.Check([condition], new ConditionHost(ConditionHostKind.StepExpect, ["Laevatain"])));
        Assert.Empty(ConditionRules.Check([condition], ConditionHost.Alert));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Formats_are_raised_only_when_it_is_used(StateCondition condition, string json, string editor)
    {
        _ = (json, editor);
        Assert.Equal(13, Rotation.CurrentFormat);
        Assert.Equal(7, AlertFile.CurrentFormat);
        var plain = new RotationPhase("p", "p", [new RotationStep("Laevatain", StepAction.BattleSkill)]);
        var gated = plain with { When = [new NotCondition(condition)] };
        Assert.Equal(2, Rotation.FormatNeeded([plain], false));
        Assert.Equal(13, Rotation.FormatNeeded([gated], false));
        Assert.Equal(7, AlertFile.FormatNeeded([AlertRule.New([new AnyCondition([condition])], "x")]));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void It_round_trips_through_a_rotation_file(StateCondition condition, string json, string editor)
    {
        _ = editor;
        var phase = new RotationPhase("p", "p", [new RotationStep("Laevatain", StepAction.BattleSkill, Expect: [condition])]);
        var rotation = new Rotation(Rotation.FormatNeeded([phase], false), "r", "r", ["Laevatain"], [phase]);
        string text = RotationFormat.ToJson(rotation);
        Assert.Contains($"\"kind\": \"{json}\"", text);
        Assert.Equal(condition, Assert.Single(RotationFormat.FromJson(text).Phases[0].Steps[0].Expect));
    }

    /// <summary>An alert on the first node reached fires once as the fill passes it, not again while it stays passed.</summary>
    [Fact]
    public void An_alert_on_a_node_reached_fires_once()
    {
        var monitor = new AlertMonitor();
        var rule = AlertRule.New([new StaggerNodesCondition(Comparison.AtLeast, 1)], "");
        Assert.Empty(monitor.Update(Reading(Early), [rule], PhaseHistory.None));
        Assert.Single(monitor.Update(Reading(Building), [rule], PhaseHistory.None));
        Assert.Empty(monitor.Update(Reading(Building), [rule], PhaseHistory.None));
        // The bar not read (a cutscene) changes nothing.
        Assert.Empty(monitor.Update(Reading(null), [rule], PhaseHistory.None));
        Assert.Empty(monitor.Update(Reading(Countdown), [rule], PhaseHistory.None));
    }
}
