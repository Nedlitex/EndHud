using EndHud.Vision.Rotations;

namespace EndHud.Vision.Alerts;

/// <summary>
/// Which alerts pop up on a reading. A rule pops when its conditions are seen holding after being
/// seen not holding, or not seen at all yet this fight. A reading where it can't be told (unknown:
/// a cutscene, a count that isn't read, an operator not in the squad) neither pops it nor re-arms it,
/// so a flickering reading can't make it pop again and again. <see cref="EndFight"/> forgets everything.
/// </summary>
public sealed class AlertMonitor
{
    /// <summary>Rule id → whether it was holding the last time it could be told.</summary>
    private readonly Dictionary<string, bool> _lastSeen = new(StringComparer.Ordinal);

    /// <summary>The rules that pop up on this reading (in the rules' order).</summary>
    /// <param name="history">The phases shown and played through, from the rotation the strip follows now; <see cref="PhaseHistory.None"/>
    /// when no rotation is followed.</param>
    public IReadOnlyList<AlertRule> Update(PartyState state, IReadOnlyList<AlertRule> rules, IPhaseHistory history)
    {
        var popped = new List<AlertRule>();
        foreach (var rule in rules)
        {
            if (rule.When.Count == 0) continue;
            var when = new AllCondition(rule.When);
            bool? now = RotationTracker.Evaluate(when, state, history);
            if (now is not { } holds) continue;
            bool was = _lastSeen.TryGetValue(rule.Id, out bool seen) && seen;
            _lastSeen[rule.Id] = holds;
            if (holds && !was) popped.Add(rule);
        }
        return popped;
    }

    /// <summary>Holding when last told (a pop-up comes down early on the first reading where it stopped).</summary>
    public bool StillHolds(string ruleId) => _lastSeen.TryGetValue(ruleId, out bool holds) && holds;

    /// <summary>The fight ended: every rule may pop again in the next one.</summary>
    public void EndFight() => _lastSeen.Clear();

    /// <summary>Does any rule ask about the boss's statuses, at any depth (so they must be read)?</summary>
    public static bool NeedsBossStatuses(IEnumerable<AlertRule> rules) =>
        ConditionWalk.AnyNeedsBossStatuses(ConditionWalk.Flatten(rules.SelectMany(r => r.When)));
}
