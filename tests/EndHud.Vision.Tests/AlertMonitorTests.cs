using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>
/// When an alert pops up (<see cref="AlertMonitor"/>): the moment its condition is seen holding after
/// being seen not holding (or not seen yet this fight). Readings where it can't be told neither pop
/// it nor re-arm it; the end of a fight forgets everything.
/// </summary>
public class AlertMonitorTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];

    private static readonly AlertRule CastNow = AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1.8)], "Cast now");

    /// <summary>A reading in battle: these skill points (null: not read) and boss statuses (null: not read).</summary>
    private static PartyState Reading(double? skillPoints = 1.0, IReadOnlyList<BossStatusReading>? statuses = null)
    {
        var slots = Squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Combo: new ComboReading(1, true), Ultimate: new UltimateReading(false, 0.3),
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: 8) { SkillPoints = skillPoints, ActiveSlot = 1, BossStatuses = statuses };
    }

    /// <summary>The rules that popped on each reading, in turn.</summary>
    private static List<int> Fired(AlertMonitor monitor, IReadOnlyList<AlertRule> rules, params PartyState[] readings) =>
        readings.Select(r => monitor.Update(r, rules).Count).ToList();

    [Fact]
    public void Pops_once_when_skill_points_reach_1_8()
    {
        Assert.Equal([0, 1, 0], Fired(new AlertMonitor(), [CastNow], Reading(1.7), Reading(1.8), Reading(1.9)));
    }

    [Fact]
    public void An_unreadable_moment_neither_pops_nor_re_arms()
    {
        Assert.Equal([1, 0, 0], Fired(new AlertMonitor(), [CastNow], Reading(1.8), Reading(null), Reading(1.8)));
    }

    [Fact]
    public void Pops_again_after_it_stopped_holding()
    {
        var monitor = new AlertMonitor();

        Assert.Equal([1, 0, 1], Fired(monitor, [CastNow], Reading(1.8), Reading(1.5), Reading(1.9)));
        Assert.True(monitor.StillHolds(CastNow.Id));
        monitor.Update(Reading(1.0), [CastNow]);
        Assert.False(monitor.StillHolds(CastNow.Id));
    }

    [Fact]
    public void Holding_on_the_first_reading_pops()
    {
        Assert.Equal([1], Fired(new AlertMonitor(), [CastNow], Reading(2.5)));
    }

    [Fact]
    public void A_new_fight_pops_it_again()
    {
        var monitor = new AlertMonitor();
        monitor.Update(Reading(1.8), [CastNow]);

        monitor.EndFight();

        Assert.False(monitor.StillHolds(CastNow.Id));
        Assert.Equal([1], Fired(monitor, [CastNow], Reading(1.8)));
    }

    [Fact]
    public void Cryo_reaching_three_stacks_pops_and_an_unread_count_does_not()
    {
        var rule = AlertRule.New([new BossStatusCondition("Cryo Infliction", true, 3)], "Freeze ready");
        static IReadOnlyList<BossStatusReading> Cryo(int? stacks) => [new BossStatusReading("Cryo Infliction", BossStatusReading.RingPosition, 1, stacks)];

        Assert.Equal([0, 1], Fired(new AlertMonitor(), [rule], Reading(statuses: Cryo(2)), Reading(statuses: Cryo(3))));
        Assert.Equal([0, 0], Fired(new AlertMonitor(), [rule], Reading(statuses: Cryo(null)), Reading(statuses: Cryo(null))));
        Assert.Equal([0], Fired(new AlertMonitor(), [rule], Reading()));                // statuses not read
    }

    [Fact]
    public void A_rule_on_an_operator_not_in_the_squad_never_pops()
    {
        var rule = AlertRule.New([new UltimateCondition("Typhoeus", false)], "");

        Assert.Equal([0, 0], Fired(new AlertMonitor(), [rule], Reading(), Reading()));
    }

    [Fact]
    public void Each_rule_pops_on_its_own()
    {
        var low = AlertRule.New([new SkillPointsCondition(Comparison.Below, 1)], "Low");
        var monitor = new AlertMonitor();

        Assert.Equal([low], monitor.Update(Reading(0.5), [CastNow, low]));
        Assert.Equal([CastNow], monitor.Update(Reading(2), [CastNow, low]));
    }

    [Fact]
    public void A_rule_without_conditions_never_pops()
    {
        Assert.Equal([0], Fired(new AlertMonitor(), [AlertRule.New([], "Never")], Reading()));
    }

    /// <summary>The boss's statuses are read only when something asks: here, an alert, at any depth.</summary>
    [Fact]
    public void A_status_anywhere_in_a_rule_needs_the_statuses_read()
    {
        Assert.False(AlertMonitor.NeedsBossStatuses([CastNow]));
        Assert.True(AlertMonitor.NeedsBossStatuses([CastNow, AlertRule.New([new AnyCondition([new NotCondition(new BossStatusCondition("Cryo Infliction", true, 3))])], "")]));
    }
}
