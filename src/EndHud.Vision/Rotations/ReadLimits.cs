namespace EndHud.Vision.Rotations;

/// <summary>
/// How much a rotation or alert-rule file, or a share code, may hold. Players paste codes from strangers and
/// files can be edited by hand: anything over these is refused as unreadable (the editors' usual sentences),
/// never read until memory or time runs out. Set far above real use: the largest real rotation is 6.5 KB with
/// 5 phases, 18 steps in a phase, 39 conditions in all, nested 5 deep.
/// </summary>
public static class ReadLimits
{
    /// <summary>The most text read: a file, or a share code once unpacked.</summary>
    public const int MaxTextBytes = 1024 * 1024;

    /// <summary>The longest share code (a real one is a few thousand characters).</summary>
    public const int MaxShareCodeChars = 256 * 1024;

    public const int MaxPhases = 100;

    public const int MaxStepsPerPhase = 200;

    /// <summary>Conditions in one rotation, or in one alert rule, groups and their parts all counted.</summary>
    public const int MaxConditions = 5000;

    /// <summary>Groups ("all"/"any") and "not" inside each other: a condition in a list is at depth 1.</summary>
    public const int MaxConditionDepth = 16;

    /// <summary>Format 1's "play this phase N times" (real rotations: 2).</summary>
    public const int MaxRepeats = 20;

    /// <summary>Alert rules read from one file or code (EndHud keeps at most <see cref="Alerts.AlertFile.MaxRules"/>; a file over that still reads).</summary>
    public const int MaxAlertRulesRead = 1000;

    /// <summary>The longest operator id read in an alert rule's squad (real ones are a word or two).</summary>
    public const int MaxOperatorIdChars = 100;

    /// <summary>The most operators an alert rule's squad names: a squad's size.</summary>
    public const int MaxAlertSquad = 4;

    /// <summary>An alert rule's squad: none, or at most <see cref="MaxAlertSquad"/> non-empty ids of at most <see cref="MaxOperatorIdChars"/> characters.</summary>
    public static bool SquadFits(IReadOnlyList<string?>? squad) =>
        squad is null || (squad.Count <= MaxAlertSquad && squad.All(id => !string.IsNullOrEmpty(id) && id.Length <= MaxOperatorIdChars));

    /// <summary>
    /// Every condition present (no null in a list, no empty "not"), nested at most <see cref="MaxConditionDepth"/>,
    /// and no more than <paramref name="budget"/> of them (taken from it as they are counted).
    /// </summary>
    public static bool ConditionsFit(IEnumerable<StateCondition?>? conditions, ref int budget, int depth = 1)
    {
        if (conditions is null) return false;
        foreach (var condition in conditions)
        {
            if (condition is null || depth > MaxConditionDepth || --budget < 0) return false;
            bool fits = condition switch
            {
                AllCondition all => ConditionsFit(all.Of, ref budget, depth + 1),
                AnyCondition any => ConditionsFit(any.Of, ref budget, depth + 1),
                NotCondition not => ConditionsFit(not.Of is null ? null : [not.Of], ref budget, depth + 1),
                _ => true,
            };
            if (!fits) return false;
        }
        return true;
    }

    /// <summary>
    /// The whole of a rotation present and within the limits: phases, their steps, entry, priority and finish
    /// conditions, the squad. A rotation that isn't is "not a rotation".
    /// </summary>
    public static bool Fits(Rotation rotation)
    {
        if (rotation.Id is null || rotation.Name is null || rotation.Squad is null || rotation.Squad.Any(s => s is null)
            || rotation.Phases is null || rotation.Phases.Count > MaxPhases) return false;
        int budget = MaxConditions;
        foreach (var phase in rotation.Phases)
        {
            if (phase is null || phase.Id is null || phase.Name is null || phase.Steps is null || phase.Steps.Count > MaxStepsPerPhase
                || phase.PriorityRules.Any(r => r is null)) return false;
            if (!ConditionsFit(phase.When, ref budget) || !ConditionsFit(phase.Finish, ref budget)) return false;
            foreach (var rule in phase.PriorityRules)
                if (!ConditionsFit(rule.When, ref budget)) return false;
            foreach (var step in phase.Steps)
                if (step is null || step.OperatorId is null || !ConditionsFit(step.Expect, ref budget)) return false;
        }
        return true;
    }
}
