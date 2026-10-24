using EndHud.Vision;
using EndHud.Vision.Alerts;
using EndHud.Vision.Operators;
using EndHud.Vision.Rotations;
using Xunit;

namespace EndHud.Tests;

/// <summary>
/// What the overlay does with the alert rules on each reading (<see cref="AlertFollower"/>, driven as
/// <c>OverlayWindow.TrackAlerts</c> drives it): a pop-up when a rule turns true, down after 3 s or on
/// the first reading where it stopped holding, all down and re-armed when the fight ends, at most
/// three at once; and the boss's statuses read when only an alert asks about them.
/// </summary>
public class AlertFollowerTests
{
    private static readonly string[] Squad = ["Laevatain", "Camille", "Arcane", "Ardelia"];

    private static readonly AlertRule CastNow = AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, 1.8)], "Cast now");

    private static PartyState Reading(double skillPoints)
    {
        var slots = Squad.Select((id, i) => new PartySlot(i + 1, null, 1.0, "", new Frame(1, 1, new byte[4]),
            Combo: new ComboReading(1, true), Ultimate: new UltimateReading(false, 0.3),
            Operator: new OperatorMatch(new OperatorInfo(id, id, id, id + ".png"), 1, true))).ToList();
        return new PartyState(slots, PanelMarks: 8) { SkillPoints = skillPoints, ActiveSlot = 1 };
    }

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Skill_points_reaching_1_8_show_one_pop_up_and_leaving_battle_takes_it_down()
    {
        var follower = new AlertFollower();

        Assert.Empty(follower.Update(Reading(1.7), inBattle: true, enabled: true, [CastNow], PhaseHistory.None, At(0)).Show);
        Assert.Equal([CastNow], follower.Update(Reading(1.8), true, true, [CastNow], PhaseHistory.None, At(0.5)).Show);
        Assert.Equal([CastNow.Id], follower.Up);
        var outside = follower.Update(null, inBattle: false, enabled: true, [CastNow], PhaseHistory.None, At(1));
        Assert.Equal([CastNow.Id], outside.TakeDown);
        Assert.Empty(follower.Up);
        // A new fight: it pops again for a value that holds from the start.
        Assert.Equal([CastNow], follower.Update(Reading(2), true, true, [CastNow], PhaseHistory.None, At(30)).Show);
    }

    [Fact]
    public void A_pop_up_lasts_3_seconds()
    {
        var follower = new AlertFollower();
        follower.Update(Reading(1.8), true, true, [CastNow], PhaseHistory.None, At(10));

        Assert.Empty(follower.Expire(At(12.9)));
        Assert.Equal([CastNow.Id], follower.Expire(At(13)));
        Assert.Empty(follower.Up);
        // Still holding: it doesn't come back.
        Assert.Empty(follower.Update(Reading(2), true, true, [CastNow], PhaseHistory.None, At(13.5)).Show);
    }

    [Fact]
    public void A_pop_up_comes_down_early_when_it_stops_holding()
    {
        var follower = new AlertFollower();
        follower.Update(Reading(1.8), true, true, [CastNow], PhaseHistory.None, At(0));

        Assert.Equal([CastNow.Id], follower.Update(Reading(0.8), true, true, [CastNow], PhaseHistory.None, At(1)).TakeDown);
        Assert.Equal([CastNow], follower.Update(Reading(1.9), true, true, [CastNow], PhaseHistory.None, At(2)).Show);
    }

    [Fact]
    public void At_most_three_at_once_the_oldest_leaves()
    {
        var rules = Enumerable.Range(0, 4).Select(i => AlertRule.New([new SkillPointsCondition(Comparison.AtLeast, i * 0.5)], $"r{i}")).ToList();
        var follower = new AlertFollower();

        follower.Update(Reading(0), true, true, rules, PhaseHistory.None, At(0));            // r0 holds
        follower.Update(Reading(0.5), true, true, rules, PhaseHistory.None, At(0.5));        // r1
        follower.Update(Reading(1.0), true, true, rules, PhaseHistory.None, At(1));          // r2
        var fourth = follower.Update(Reading(1.5), true, true, rules, PhaseHistory.None, At(1.5)); // r3

        Assert.Equal([rules[3]], fourth.Show);
        Assert.Equal([rules[0].Id], fourth.TakeDown);
        Assert.Equal([rules[1].Id, rules[2].Id, rules[3].Id], follower.Up);
    }

    [Fact]
    public void Alerts_off_show_nothing()
    {
        var follower = new AlertFollower();

        Assert.Empty(follower.Update(Reading(2), true, enabled: false, [CastNow], PhaseHistory.None, At(0)).Show);
        Assert.False(follower.Update(Reading(2), true, enabled: false, [AlertRule.New([new BossStatusCondition("Cryo Infliction", true, 3)], "")], PhaseHistory.None, At(0)).ReadBossStatuses);
    }

    /// <summary>A squad with no rotation and one alert on Cryo Infliction: the statuses are read (in battle only).</summary>
    [Fact]
    public void An_alert_on_a_status_turns_status_reading_on()
    {
        AlertRule[] rules = [CastNow, AlertRule.New([new BossStatusCondition("Cryo Infliction", true, 3)], "Freeze ready")];
        var follower = new AlertFollower();

        Assert.True(follower.Update(Reading(1), inBattle: true, enabled: true, rules, PhaseHistory.None, At(0)).ReadBossStatuses);
        Assert.False(follower.Update(null, inBattle: false, enabled: true, rules, PhaseHistory.None, At(1)).ReadBossStatuses);
        Assert.False(new AlertFollower().Update(Reading(1), true, true, [CastNow], PhaseHistory.None, At(0)).ReadBossStatuses);
    }

    /// <summary>A rule deleted in the editor while its pop-up is up comes down.</summary>
    [Fact]
    public void A_deleted_rules_pop_up_comes_down()
    {
        var follower = new AlertFollower();
        follower.Update(Reading(2), true, true, [CastNow], PhaseHistory.None, At(0));

        Assert.Equal([CastNow.Id], follower.Update(Reading(2), true, true, [], PhaseHistory.None, At(0.5)).TakeDown);
    }
}
