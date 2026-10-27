using System.Reflection;
using System.Text.Json.Serialization;
using EndHud.Vision.Alerts;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// One condition language for every host (owner, 2026-10-24: "everything that supports condition use
/// the same language and cover the same"): one catalogue of kinds (<see cref="ConditionCatalogue"/>)
/// and one set of checks (<see cref="ConditionRules"/>), whatever the conditions are for: a phase's
/// entry condition, a priority rule, a step's expected state, an alert.
/// </summary>
public class ConditionCatalogueTests
{
    private static readonly string[] Squad = ["Si", "Last_Rite", "Arcane", "Tangtang"];

    /// <summary>Every kind a file can hold is in the catalogue, under the same name, and nothing else is.</summary>
    [Fact]
    public void Every_kind_in_the_files_is_in_the_catalogue()
    {
        var attributes = typeof(StateCondition).GetCustomAttributes<JsonDerivedTypeAttribute>().ToList();
        var subclasses = typeof(StateCondition).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(StateCondition)) && !t.IsAbstract).ToHashSet();

        Assert.Equal(subclasses, attributes.Select(a => a.DerivedType).ToHashSet());
        Assert.Equal(attributes.Count, ConditionCatalogue.Kinds.Count);
        foreach (var attribute in attributes)
        {
            var kind = Assert.Single(ConditionCatalogue.Kinds, k => k.Type == attribute.DerivedType);
            Assert.Equal(attribute.TypeDiscriminator, kind.JsonName);
        }
        Assert.Equal(ConditionCatalogue.Kinds.Count, ConditionCatalogue.Kinds.Select(k => k.Id).Distinct().Count());
    }

    /// <summary>
    /// The same full set is offered wherever conditions are written ("phase shown" in alerts included). Two kinds are
    /// about a phase's own round: "the phase's steps lit", offered in a finish condition only (2026-10-24; elsewhere it
    /// would name another phase's steps, or a condition step's own light), and "time in phase", offered in the phase's own
    /// hosts (its finish condition, a condition step, a step's expected state; owner, 2026-10-27).
    /// </summary>
    [Fact]
    public void Every_host_offers_the_same_full_set_but_the_kinds_about_a_phases_own_round()
    {
        var everything = ConditionCatalogue.EditorKinds;

        Assert.Contains("PhaseShown", everything);
        Assert.Contains("Group", everything);
        Assert.Equal(everything, ConditionCatalogue.OfferedIn(ConditionHostKind.PhaseFinish));
        foreach (var host in new[] { ConditionHostKind.ConditionStep, ConditionHostKind.StepExpect })
            Assert.Equal(everything.Where(k => k != "StepsLit"), ConditionCatalogue.OfferedIn(host));
        foreach (var host in new[] { ConditionHostKind.PhaseGate, ConditionHostKind.PriorityRule, ConditionHostKind.Alert })
            Assert.Equal(everything.Where(k => k is not "StepsLit" and not "TimeInPhase"), ConditionCatalogue.OfferedIn(host));
    }

    [Fact]
    public void Time_in_phase_is_offered_only_in_the_phases_own_hosts()
    {
        foreach (var host in new[] { ConditionHostKind.PhaseFinish, ConditionHostKind.ConditionStep, ConditionHostKind.StepExpect })
            Assert.Contains("TimeInPhase", ConditionCatalogue.OfferedIn(host));
        foreach (var host in new[] { ConditionHostKind.PhaseGate, ConditionHostKind.PriorityRule, ConditionHostKind.Alert })
            Assert.DoesNotContain("TimeInPhase", ConditionCatalogue.OfferedIn(host));
    }

    /// <summary>The catalogue knows whom each kind is about and its comparison, for every kind that has one.</summary>
    [Fact]
    public void The_catalogue_tells_the_operator_and_the_comparison()
    {
        Assert.Equal("Si", ConditionCatalogue.Of(new BattleSkillActiveCondition("Si", true)).OperatorOf(new BattleSkillActiveCondition("Si", true)));
        var counter = new CounterCondition("Si", "damage-stacks", Comparison.Below, 3);
        Assert.Equal("Si", ConditionCatalogue.Of(counter).OperatorOf(counter));
        Assert.Equal(Comparison.Below, ConditionCatalogue.Of(counter).ComparisonOf(counter));
        var points = new SkillPointsCondition(Comparison.AtMost, 1);
        Assert.Null(ConditionCatalogue.Of(points).OperatorOf(points));
        Assert.Equal(Comparison.AtMost, ConditionCatalogue.Of(points).ComparisonOf(points));
        var shown = new PhaseShownCondition("p", Comparison.AtLeast, 1);
        Assert.Equal("p", ConditionCatalogue.Of(shown).PhaseOf(shown));
    }

    private static readonly ConditionHostKind[] RotationHosts = [ConditionHostKind.PhaseGate, ConditionHostKind.PriorityRule, ConditionHostKind.StepExpect, ConditionHostKind.ConditionStep];

    /// <summary>"Battle skill running" for someone whose skill time isn't read is refused alike in every host; alerts let it through before.</summary>
    [Fact]
    public void A_skill_time_nobody_reads_is_refused_in_every_host()
    {
        StateCondition tangtang = new BattleSkillActiveCondition("Tangtang", true);
        foreach (var host in RotationHosts)
            Assert.Equal([ConditionProblemKind.SkillTimeNotRead], ConditionRules.Check([tangtang], new ConditionHost(host, Squad, "p", _ => true)).Select(p => p.Kind));
        Assert.Equal([ConditionProblemKind.SkillTimeNotRead], ConditionRules.Check([new NotCondition(tangtang)], ConditionHost.Alert).Select(p => p.Kind));
        Assert.Equal([AlertProblem.SkillTimeNotRead], AlertRule.New([new AnyCondition([tangtang])], "x").Problems());
    }

    /// <summary>The squad check applies wherever there is a squad (a rotation), never in alerts (no squad).</summary>
    [Fact]
    public void An_operator_outside_the_squad_is_refused_in_rotations_only()
    {
        StateCondition laevatain = new UltimateCondition("Laevatain", true);
        foreach (var host in RotationHosts)
            Assert.Equal([ConditionProblemKind.OperatorNotInSquad], ConditionRules.Check([laevatain], new ConditionHost(host, Squad, "p", _ => true)).Select(p => p.Kind));
        Assert.Empty(ConditionRules.Check([laevatain], ConditionHost.Alert));
    }

    /// <summary>
    /// "Phase shown" in alerts, like "phase played": it names a phase of the rotation followed. An old
    /// build's editor refuses it, so a rule with one is saved in a newer alert format.
    /// </summary>
    [Fact]
    public void Phase_shown_is_allowed_in_alerts_in_a_newer_format()
    {
        var rule = AlertRule.New([new NotCondition(new PhaseShownCondition("p", Comparison.AtLeast, 1))], "x");

        Assert.Empty(rule.Problems());
        Assert.Equal(4, AlertFile.FormatNeeded([rule]));
        Assert.Equal(1, AlertFile.FormatNeeded([AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1)], "x")]));
    }

    /// <summary>The rotation's own rules come out of the shared check, so a rotation says the same as before.</summary>
    [Fact]
    public void The_rotation_problems_come_from_the_shared_check()
    {
        var rotation = new Rotation(Rotation.CurrentFormat, "r", "r", Squad,
        [
            new RotationPhase("a", "A", [new RotationStep("Si", StepAction.BattleSkill, [new ComboCondition("Laevatain", true)])]),
            new RotationPhase("b", "B", [new RotationStep("Si", StepAction.BattleSkill)],
                [new PhaseShownCondition("b", Comparison.AtLeast, 1), new PhasePlayedCondition("gone", Comparison.AtLeast, 1), new AllCondition([])])
            {
                PriorityRules = [new PriorityRule([new BattleSkillActiveCondition("Arcane", true)], 3)],
            },
        ]);

        Assert.Equal(
        [
            new RotationProblem(RotationProblemKind.PhaseShownOwnGate, "B"),
            new RotationProblem(RotationProblemKind.PhasePlayedMissingPhase, "B"),
            new RotationProblem(RotationProblemKind.EmptyConditionGroup, "B"),
            new RotationProblem(RotationProblemKind.SkillTimeNotRead, "B", null, 1),
            new RotationProblem(RotationProblemKind.ConditionOperatorNotInSquad, "A", 1),
        ], rotation.Problems().OrderBy(p => p.Phase == "A").ToList());
    }
}
